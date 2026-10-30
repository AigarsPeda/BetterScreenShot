using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterScreenShot;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using FlowDirection = System.Windows.FlowDirection;

internal static class OcrLayoutTests
{
    internal static int Demo()
    {
        var app = new System.Windows.Application();
        app.Run(new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(Path.GetTempPath(), "ocr-file-list-unused.png")), FileListFixture())
            { Title = "Small filename selection test" });
        return 0;
    }

    internal static void Run(Action<string, Action> check, string directory)
    {
        check("OCR keeps filenames beside icons and table columns in small dark screenshots", () =>
        {
            foreach (var language in new[] { "eng", TextExtractionService.AutomaticLanguage })
            {
                var result = TextExtractionService.RecognizeAsync(FileListFixture(), language).GetAwaiter().GetResult();
                foreach (var name in Names)
                    Require(result.Words.Any(word => word.Text == name), $"{language} missed {name}: {result.Text}");
                foreach (var word in result.Words)
                    Require(result.Text.Substring(word.TextStart, word.Text.Length) == word.Text, "Word offsets must match the final text.");
                var filename = result.Words.Single(word => word.Text == Names[2]);
                Require(filename.Bounds.Left is >= 50 and <= 56 && filename.Bounds.Top is >= 146 and <= 157
                    && filename.Bounds.Height < 20, $"Scaled OCR must return source coordinates: {filename.Bounds}");
            }
        });
        check("OCR prefers a complete layout over a high-confidence dates-only result", () =>
        {
            var date = new RecognizedWord("30.10.2026", new Rect(540, 60, 70, 12), 0, 99);
            var name = new RecognizedWord(Names[2], new Rect(52, 60, 170, 12), 0, 80);
            var fullText = name.Text + Environment.NewLine + date.Text;
            var result = TextExtractionService.MergeResults([
                new TextExtractionResult(date.Text, [date], 99),
                new TextExtractionResult(fullText, [name, date with { TextStart = name.Text.Length + Environment.NewLine.Length, Confidence = 85 }], 82)
            ]);
            Require(result.Text == fullText && result.Words.Count == 2, "A confident date must not discard the filename region.");
        });
        check("Recovered small filenames can be selected on the image and in the text panel", () =>
        {
            var previousContext = SynchronizationContext.Current;
            var window = new ScreenshotViewerWindow(new TemporaryScreenshot(Path.Combine(directory, "small-file-list.png")), FileListFixture());
            try
            {
                window.Show();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                TextExtractionTests.Complete(window.SelectImageWordAsync(new Point(150, 153)));
                var box = (System.Windows.Controls.TextBox)window.FindName("ExtractedTextBox");
                Require(box.SelectedText == Names[2], $"Image selection missed the filename: {box.SelectedText}");
                var zip = box.Text.IndexOf(Names[5], StringComparison.Ordinal);
                box.Select(zip, Names[5].Length);
                var highlight = (System.Windows.Shapes.Path)window.FindName("OcrSelectionHighlight");
                Require(highlight.Visibility == Visibility.Visible && highlight.Data.Bounds.Top is >= 272 and <= 283,
                    $"Panel selection must highlight the original zip filename: {highlight.Data.Bounds}");
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
    }

    private static readonly string[] Names = ["BetterScreenShot-Portable-win-x64", "BetterScreenShot-Portable-win-x64-FrameworkDependent", "BetterScreenShot-win-x64", "ocr-portable-probe", "ocr-relocated-probe", "BetterScreenShot-win-x64.zip"];

    internal static BitmapSource FileListFixture()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, new Rect(0, 0, 1000, 360));
            void Text(string text, double x, double y) => drawing.DrawText(new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, 1), new Point(x, y));
            Text("Name", 52, 18);
            Text("Date modified", 540, 18);
            Text("Type", 740, 18);
            Text("Size", 920, 18);
            for (var row = 0; row < Names.Length; row++)
            {
                var y = 62 + row * 42;
                drawing.DrawRectangle(Brushes.Goldenrod, null, new Rect(24, y + 3, 17, 13));
                Text(Names[row], 52, y);
                Text("30.10.2026 22:04", 540, y);
                Text(row == 5 ? "Compressed (zipped) Folder" : "File folder", 740, y);
            }
        }
        var image = new RenderTargetBitmap(1000, 360, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual);
        image.Freeze();
        return image;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
