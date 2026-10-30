using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterScreenShot;

internal sealed record RecognizedWord(string Text, System.Windows.Rect Bounds, int TextStart, float Confidence = 0, bool FromDictionary = false);
internal sealed record TextExtractionResult(string Text, IReadOnlyList<RecognizedWord> Words, int Confidence = 0);
internal readonly record struct OcrTiming(string Stage, string Language, int Width, int Height, double Milliseconds);

internal static class TextExtractionService
{
    internal const string AutomaticLanguage = "auto";
    private static readonly Lazy<string> assetDirectory = new(PrepareAssets);
    private static readonly SemaphoreSlim recognitionGate = new(1, 1);
    internal static IReadOnlyDictionary<string, string> Languages { get; } = new Dictionary<string, string>
    {
        ["eng"] = "English", ["lav"] = "Latvian", ["lit"] = "Lithuanian", ["est"] = "Estonian",
        ["swe"] = "Swedish", ["fin"] = "Finnish", ["rus"] = "Russian"
    };

    internal static async Task<string> ExtractAsync(BitmapSource image, string language, CancellationToken cancellationToken = default)
        => (await RecognizeAsync(image, language, cancellationToken).ConfigureAwait(false)).Text;

    internal static Task<TextExtractionResult> RecognizeAsync(BitmapSource image, string language, CancellationToken cancellationToken = default, Action<OcrTiming>? reportTiming = null)
    {
        if (!image.IsFrozen) throw new ArgumentException("OCR requires a frozen image.", nameof(image));
        if (language != AutomaticLanguage && (string.IsNullOrEmpty(language) || language.Split('+').Any(code => !Languages.ContainsKey(code))))
            throw new ArgumentException("Unsupported OCR language.", nameof(language));
        return RunRecognitionAsync(image, language, cancellationToken, reportTiming);
    }

    private static async Task<TextExtractionResult> RunRecognitionAsync(BitmapSource image, string language, CancellationToken cancellationToken, Action<OcrTiming>? reportTiming)
    {
        var started = Stopwatch.GetTimestamp();
        await recognitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Report("queue", language, image.PixelWidth, image.PixelHeight, started, reportTiming);
            return await Task.Run(() => Extract(image, language, cancellationToken, reportTiming), cancellationToken).ConfigureAwait(false);
        }
        finally { recognitionGate.Release(); }
    }

    private static TextExtractionResult Extract(BitmapSource image, string language, CancellationToken cancellationToken, Action<OcrTiming>? reportTiming)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var directory = assetDirectory.Value;
        Report("assets", language, image.PixelWidth, image.PixelHeight, started, reportTiming);
        var codes = language == AutomaticLanguage ? Languages.Keys.ToArray() : new[] { language };
        var candidates = new List<TextExtractionResult>();
        BitmapSource source = image;
        var (pixels, stride) = CopyPixels();
        var initial = RecognizeCode(codes[0], 1);
        candidates.Add(initial);
        // Small UI text and folder icons can confuse document segmentation. A larger
        // working image recovers those regions; keep selections in source coordinates.
        // Bound the extra pixel buffer to 48 MB, even for very large captures.
        var heights = initial.Words.Select(word => word.Bounds.Height).Order().ToArray();
        var scale = 1;
        var remainingCodes = codes.Skip(1).ToArray();
        if ((heights.Length == 0 || heights[heights.Length / 2] < 20)
            && (long)image.PixelWidth * image.PixelHeight <= 4_000_000)
        {
            source = new TransformedBitmap(image, new ScaleTransform(2, 2));
            source.Freeze();
            (pixels, stride) = CopyPixels();
            scale = 2;
            remainingCodes = codes;
        }
        // Independent engines can share the immutable pixels. Keep at most two active
        // and serialize requests so rapid toggles cannot accumulate CPU-heavy jobs.
        var results = new TextExtractionResult[remainingCodes.Length];
        // Short snippets are mostly model-loading work and gain little from parallelism.
        var parallel = Environment.ProcessorCount >= 4 && (scale == 2 || initial.Words.Count >= 12);
        var options = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = parallel ? 2 : 1 };
        Parallel.For(0, remainingCodes.Length, options, index => results[index] = RecognizeCode(remainingCodes[index], scale));
        candidates.AddRange(results);
        cancellationToken.ThrowIfCancellationRequested();
        started = Stopwatch.GetTimestamp();
        var merged = MergeResults(candidates);
        Report("merge", language, image.PixelWidth, image.PixelHeight, started, reportTiming);
        return merged;

        (byte[] Pixels, int Stride) CopyPixels()
        {
            var copyStarted = Stopwatch.GetTimestamp();
            var converted = new FormatConvertedBitmap(source, PixelFormats.Rgb24, null, 0);
            var stride = checked(source.PixelWidth * 3);
            var pixels = new byte[checked(stride * source.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            Report("pixels", language, source.PixelWidth, source.PixelHeight, copyStarted, reportTiming);
            return (pixels, stride);
        }

        TextExtractionResult RecognizeCode(string code, int recognitionScale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = RecognizePixels(pixels, source.PixelWidth, source.PixelHeight, stride, directory, code, cancellationToken, reportTiming);
            if (recognitionScale != 1)
                result = result with { Words = result.Words.Select(word => word with
                {
                    Bounds = new System.Windows.Rect(word.Bounds.X / recognitionScale, word.Bounds.Y / recognitionScale,
                        word.Bounds.Width / recognitionScale, word.Bounds.Height / recognitionScale)
                }).ToArray() };
            return result;
        }
    }

    private static TextExtractionResult RecognizePixels(byte[] pixels, int width, int height, int stride, string directory, string language, CancellationToken cancellationToken, Action<OcrTiming>? reportTiming)
    {
        var engine = Native.Create();
        if (engine == IntPtr.Zero) throw new InvalidOperationException("Could not create the text recognition engine.");
        try
        {
            var started = Stopwatch.GetTimestamp();
            if (Native.Init(engine, directory, language) != 0)
                throw new InvalidOperationException("Could not load the text recognition language data.");
            Report("init", language, width, height, started, reportTiming);
            // Automatic layout preserves paragraphs. No orientation model is required for screenshots.
            Native.SetPageSegMode(engine, 3);
            var pinnedPixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                Native.SetImage(engine, pinnedPixels.AddrOfPinnedObject(), width, height, 3, stride);
                Native.SetSourceResolution(engine, 96);
                cancellationToken.ThrowIfCancellationRequested();
                started = Stopwatch.GetTimestamp();
                var text = Native.GetText(engine);
                Report("recognize", language, width, height, started, reportTiming);
                if (text == IntPtr.Zero) throw new InvalidOperationException("Text recognition did not complete.");
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var recognized = (Marshal.PtrToStringUTF8(text) ?? "").Replace("\r\n", "\n").Trim().Replace("\n", Environment.NewLine);
                    started = Stopwatch.GetTimestamp();
                    var words = ReadWords(engine, recognized, cancellationToken);
                    Report("words", language, width, height, started, reportTiming);
                    return new TextExtractionResult(recognized, words, Native.GetMeanConfidence(engine));
                }
                finally { Native.DeleteText(text); }
            }
            finally { pinnedPixels.Free(); }
        }
        finally { Native.Delete(engine); }
    }

    private static void Report(string stage, string language, int width, int height, long started, Action<OcrTiming>? reportTiming)
        => reportTiming?.Invoke(new OcrTiming(stage, language, width, height, Stopwatch.GetElapsedTime(started).TotalMilliseconds));

    internal static TextExtractionResult MergeResults(IReadOnlyList<TextExtractionResult> candidates)
    {
        // Mean confidence alone favors partial results containing only easy dates or
        // headings. Count confidently recognized characters so missing regions matter.
        var baseline = candidates.MaxBy(result => result.Words.Sum(word =>
            word.Text.Count(char.IsLetterOrDigit) * Math.Max(0, word.Confidence - 30)))!;
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
