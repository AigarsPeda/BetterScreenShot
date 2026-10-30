using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterScreenShot;

internal sealed record RecognizedWord(string Text, System.Windows.Rect Bounds, int TextStart, float Confidence = 0, bool FromDictionary = false);
internal sealed record TextExtractionResult(string Text, IReadOnlyList<RecognizedWord> Words, int Confidence = 0);

internal static class TextExtractionService
{
    internal const string AutomaticLanguage = "auto";
    private static readonly Lazy<string> assetDirectory = new(PrepareAssets);
    internal static IReadOnlyDictionary<string, string> Languages { get; } = new Dictionary<string, string>
    {
        ["eng"] = "English", ["lav"] = "Latvian", ["lit"] = "Lithuanian", ["est"] = "Estonian",
        ["swe"] = "Swedish", ["fin"] = "Finnish", ["rus"] = "Russian"
    };

    internal static async Task<string> ExtractAsync(BitmapSource image, string language, CancellationToken cancellationToken = default)
        => (await RecognizeAsync(image, language, cancellationToken).ConfigureAwait(false)).Text;

    internal static Task<TextExtractionResult> RecognizeAsync(BitmapSource image, string language, CancellationToken cancellationToken = default)
    {
        if (!image.IsFrozen) throw new ArgumentException("OCR requires a frozen image.", nameof(image));
        if (language != AutomaticLanguage && (string.IsNullOrEmpty(language) || language.Split('+').Any(code => !Languages.ContainsKey(code))))
            throw new ArgumentException("Unsupported OCR language.", nameof(language));
        return Task.Run(() => Extract(image, language, cancellationToken), cancellationToken);
    }

    private static TextExtractionResult Extract(BitmapSource image, string language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = assetDirectory.Value;
        var converted = new FormatConvertedBitmap(image, PixelFormats.Rgb24, null, 0);
        var stride = checked(image.PixelWidth * 3);
        var pixels = new byte[checked(stride * image.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        cancellationToken.ThrowIfCancellationRequested();

        if (language != AutomaticLanguage)
            return RecognizePixels(pixels, image.PixelWidth, image.PixelHeight, stride, directory, language, cancellationToken);
        var candidates = new List<TextExtractionResult>();
        foreach (var code in Languages.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(RecognizePixels(pixels, image.PixelWidth, image.PixelHeight, stride, directory, code, cancellationToken));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return MergeAutomaticResults(candidates);
    }

    private static TextExtractionResult RecognizePixels(byte[] pixels, int width, int height, int stride, string directory, string language, CancellationToken cancellationToken)
    {
        var engine = Native.Create();
        if (engine == IntPtr.Zero) throw new InvalidOperationException("Could not create the text recognition engine.");
        try
        {
            if (Native.Init(engine, directory, language) != 0)
                throw new InvalidOperationException("Could not load the text recognition language data.");
            // Automatic layout preserves paragraphs. No orientation model is required for screenshots.
            Native.SetPageSegMode(engine, 3);
            var pinnedPixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                Native.SetImage(engine, pinnedPixels.AddrOfPinnedObject(), width, height, 3, stride);
                Native.SetSourceResolution(engine, 96);
                cancellationToken.ThrowIfCancellationRequested();
                var text = Native.GetText(engine);
                if (text == IntPtr.Zero) throw new InvalidOperationException("Text recognition did not complete.");
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var recognized = (Marshal.PtrToStringUTF8(text) ?? "").Replace("\r\n", "\n").Trim().Replace("\n", Environment.NewLine);
                    return new TextExtractionResult(recognized, ReadWords(engine, recognized, cancellationToken), Native.GetMeanConfidence(engine));
                }
                finally { Native.DeleteText(text); }
            }
            finally { pinnedPixels.Free(); }
        }
        finally { Native.Delete(engine); }
    }

    private static TextExtractionResult MergeAutomaticResults(IReadOnlyList<TextExtractionResult> candidates)
    {
        var baseline = candidates.MaxBy(result => result.Confidence)!;
        if (baseline.Words.Count == 0) return baseline;
        // Match candidates by their image positions, independently of the recognized strings.
        // Row buckets avoid comparing every word with every other word on large captures.
        const int rowHeight = 64;
        var rows = candidates.SelectMany(result => result.Words)
            .ToLookup(word => (int)((word.Bounds.Top + word.Bounds.Height / 2) / rowHeight));
        var text = new StringBuilder();
        var words = new List<RecognizedWord>();
        var offset = 0;
        foreach (var original in baseline.Words)
        {
            var best = original;
            var bestScore = WordScore(original);
            var firstRow = (int)Math.Floor((original.Bounds.Top - original.Bounds.Height) / rowHeight);
            var lastRow = (int)Math.Floor((original.Bounds.Bottom + original.Bounds.Height) / rowHeight);
            for (var row = firstRow; row <= lastRow; row++)
            foreach (var candidate in rows[row])
            {
                var overlap = System.Windows.Rect.Intersect(original.Bounds, candidate.Bounds);
                if (overlap.IsEmpty) continue;
                var unionArea = original.Bounds.Width * original.Bounds.Height + candidate.Bounds.Width * candidate.Bounds.Height - overlap.Width * overlap.Height;
                if (overlap.Width * overlap.Height / unionArea < .6) continue;
                var score = WordScore(candidate);
                // Near-ties must not turn ordinary Latin letters into Cyrillic lookalikes.
                var scriptMargin = HasCyrillic(best.Text) != HasCyrillic(candidate.Text) ? 3 : 0;
                if (score <= bestScore + scriptMargin) continue;
                best = candidate;
                bestScore = score;
            }
            text.Append(baseline.Text, offset, original.TextStart - offset);
            words.Add(best with { TextStart = text.Length });
            text.Append(best.Text);
            offset = original.TextStart + original.Text.Length;
        }
        text.Append(baseline.Text, offset, baseline.Text.Length - offset);
        return new TextExtractionResult(text.ToString(), words.AsReadOnly(), baseline.Confidence);
    }

    private static float WordScore(RecognizedWord word)
    {
        // Dictionary evidence helps resolve model disagreements. A small accent tie-break
        // preserves diacritics when an unaccented spelling has nearly identical confidence.
        var accented = word.Text.Normalize(NormalizationForm.FormD).Any(character => CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark);
        return word.Confidence + (word.FromDictionary ? 3 : 0) + (accented ? .5f : 0);
    }

    private static bool HasCyrillic(string text) => text.Any(character => character is >= '\u0400' and <= '\u052f');

    private static IReadOnlyList<RecognizedWord> ReadWords(IntPtr engine, string text, CancellationToken cancellationToken)
    {
        var words = new List<RecognizedWord>();
        var iterator = Native.GetIterator(engine);
        if (iterator == IntPtr.Zero) return words;
        try
        {
            var page = Native.GetPageIterator(iterator);
            var offset = 0;
            const int wordLevel = 3;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wordText = Native.GetWordText(iterator, wordLevel);
                if (wordText == IntPtr.Zero) continue;
                string word;
                try { word = (Marshal.PtrToStringUTF8(wordText) ?? "").Trim(); }
                finally { Native.DeleteText(wordText); }
                if (word.Length == 0 || Native.GetBoundingBox(page, wordLevel, out var left, out var top, out var right, out var bottom) == 0
                    || right <= left || bottom <= top) continue;
                var start = text.IndexOf(word, offset, StringComparison.Ordinal);
                if (start < 0) continue;
                words.Add(new RecognizedWord(word, new System.Windows.Rect(left, top, right - left, bottom - top), start,
                    Native.GetWordConfidence(iterator, wordLevel), Native.WordFromDictionary(iterator) != 0));
                offset = start + word.Length;
            } while (Native.NextWord(iterator, wordLevel) != 0);
        }
        finally { Native.DeleteIterator(iterator); }
        return words.AsReadOnly();
    }

    private static string PrepareAssets()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Text extraction is currently available in the Windows x64 build. On an ARM64 PC, use the x64 build with Windows emulation.");

        var assembly = typeof(TextExtractionService).Assembly;
        var directory = Path.Combine(Path.GetTempPath(), "BetterScreenShot", "ocr", assembly.ManifestModule.ModuleVersionId.ToString("N"));
        Directory.CreateDirectory(directory);
        var resources = Languages.Keys.Select(code => code + ".traineddata")
            .Concat(["leptonica-1.85.0.dll", "tesseract55.dll", "LICENSE", "NOTICES"]);
        foreach (var name in resources)
        {
            using var resource = assembly.GetManifestResourceStream("BetterScreenShot.ocr." + name)
                ?? throw new InvalidOperationException($"Missing text recognition resource: {name}");
            var path = Path.Combine(directory, name);
            ExtractAsset(resource, path);
        }

        try
        {
            // Load the dependency first, then keep both handles for the lifetime of the process.
            NativeLibrary.Load(Path.Combine(directory, "leptonica-1.85.0.dll"));
            var library = NativeLibrary.Load(Path.Combine(directory, "tesseract55.dll"));
            NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == Native.Library ? library : IntPtr.Zero);
        }
        catch (DllNotFoundException error)
        {
            throw new InvalidOperationException("Text extraction needs the Microsoft Visual C++ 2015–2022 Redistributable (x64). Install it and restart BetterScreenShot.", error);
        }
        return directory;
    }

    internal static void ExtractAsset(Stream resource, string path)
    {
        // Embedded resource streams are seekable. Hash and copy them directly to
        // avoid holding both a growing MemoryStream and a second full-size array.
        if (File.Exists(path))
        {
            using var existing = File.OpenRead(path);
            if (existing.Length == resource.Length && SHA256.HashData(existing).AsSpan().SequenceEqual(SHA256.HashData(resource))) return;
            resource.Position = 0;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) resource.CopyTo(output);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static class Native
    {
        internal const string Library = "BetterScreenShot.Tesseract";
        [DllImport(Library, EntryPoint = "TessBaseAPICreate", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create();
        [DllImport(Library, EntryPoint = "TessBaseAPIInit3", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Init(IntPtr engine, [MarshalAs(UnmanagedType.LPUTF8Str)] string dataPath, [MarshalAs(UnmanagedType.LPUTF8Str)] string language);
        [DllImport(Library, EntryPoint = "TessBaseAPISetPageSegMode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetPageSegMode(IntPtr engine, int mode);
        [DllImport(Library, EntryPoint = "TessBaseAPISetImage", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetImage(IntPtr engine, IntPtr pixels, int width, int height, int bytesPerPixel, int bytesPerLine);
        [DllImport(Library, EntryPoint = "TessBaseAPISetSourceResolution", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetSourceResolution(IntPtr engine, int resolution);
        [DllImport(Library, EntryPoint = "TessBaseAPIGetUTF8Text", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetText(IntPtr engine);
        [DllImport(Library, EntryPoint = "TessBaseAPIMeanTextConf", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetMeanConfidence(IntPtr engine);
        [DllImport(Library, EntryPoint = "TessBaseAPIGetIterator", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetIterator(IntPtr engine);
        [DllImport(Library, EntryPoint = "TessResultIteratorGetPageIterator", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetPageIterator(IntPtr iterator);
        [DllImport(Library, EntryPoint = "TessResultIteratorGetUTF8Text", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr GetWordText(IntPtr iterator, int level);
        [DllImport(Library, EntryPoint = "TessResultIteratorConfidence", CallingConvention = CallingConvention.Cdecl)]
        internal static extern float GetWordConfidence(IntPtr iterator, int level);
        [DllImport(Library, EntryPoint = "TessResultIteratorWordIsFromDictionary", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int WordFromDictionary(IntPtr iterator);
        [DllImport(Library, EntryPoint = "TessResultIteratorNext", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NextWord(IntPtr iterator, int level);
        [DllImport(Library, EntryPoint = "TessPageIteratorBoundingBox", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetBoundingBox(IntPtr iterator, int level, out int left, out int top, out int right, out int bottom);
        [DllImport(Library, EntryPoint = "TessResultIteratorDelete", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DeleteIterator(IntPtr iterator);
        [DllImport(Library, EntryPoint = "TessDeleteText", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DeleteText(IntPtr text);
        [DllImport(Library, EntryPoint = "TessBaseAPIDelete", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Delete(IntPtr engine);
    }
}
