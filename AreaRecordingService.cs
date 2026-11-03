using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using Rectangle = System.Drawing.Rectangle;

namespace BetterScreenShot;

internal enum RecordingFormat { Video, Gif }
internal enum VideoQuality { Standard, High, Maximum }
internal readonly record struct VideoEncodingOptions(int FramesPerSecond, int Bitrate);

internal static class AreaRecordingService
{
    internal const int GifSecondsLimit = 20;
    private const int GifFps = 8;

    internal static (string FilePath, BitmapSource Preview) Record(
        Rectangle bounds, RecordingFormat format, CancellationToken stop, CancellationToken cancel,
        VideoQuality videoQuality = VideoQuality.High)
        => Record(bounds, format, stop, cancel, Capture, videoQuality);

    internal static (string FilePath, BitmapSource Preview) Record(
        Rectangle bounds, RecordingFormat format, CancellationToken stop, CancellationToken cancel,
        Func<Rectangle, int, int, byte[]> captureFrame, VideoQuality videoQuality = VideoQuality.High)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Recording dimensions must be positive.");
        if (format is not (RecordingFormat.Video or RecordingFormat.Gif))
            throw new ArgumentOutOfRangeException(nameof(format));
        if (videoQuality is not (VideoQuality.Standard or VideoQuality.High or VideoQuality.Maximum))
            throw new ArgumentOutOfRangeException(nameof(videoQuality));
        var folder = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
        Directory.CreateDirectory(folder);
        var extension = format == RecordingFormat.Video ? ".mp4" : ".gif";
        var path = Path.Combine(folder, $"recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid():N}{extension}");
        try
        {
            return format == RecordingFormat.Video
                ? RecordVideo(bounds, path, stop, cancel, captureFrame, videoQuality)
                : RecordGif(bounds, path, stop, cancel, captureFrame);
        }
        catch
        {
            ScreenshotFileService.TryDelete(path);
            throw;
        }
    }

    internal static VideoEncodingOptions GetVideoEncodingOptions(int width, int height, VideoQuality quality)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var (fps, bitrateDivisor, maxBitrate) = quality switch
        {
            VideoQuality.Standard => (15, 8, 20_000_000),
            VideoQuality.High => (30, 4, 40_000_000),
            VideoQuality.Maximum => (30, 2, 80_000_000),
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };
        var bitrate = (int)Math.Clamp((double)width * height * fps / bitrateDivisor,
            1_000_000, maxBitrate);
        return new VideoEncodingOptions(fps, bitrate);
    }

    private static (string, BitmapSource) RecordVideo(Rectangle bounds, string path,
        CancellationToken stop, CancellationToken cancel, Func<Rectangle, int, int, byte[]> captureFrame,
        VideoQuality quality)
    {
        var width = (bounds.Width + 1) & ~1;
        var height = (bounds.Height + 1) & ~1;
        var options = GetVideoEncodingOptions(width, height, quality);
        using var writer = new Mp4Writer(path, width, height, options.FramesPerSecond, options.Bitrate);
        BitmapSource? preview = null;
        var clock = Stopwatch.StartNew();
        var frame = 0;
        var frameDuration = TimeSpan.TicksPerSecond / options.FramesPerSecond;
        byte[]? previousPixels = null;
        var previousTime = 0L;
        do
        {
            cancel.ThrowIfCancellationRequested();
            var pixels = captureFrame(bounds, width, height);
            preview ??= Preview(pixels, width, height);
            var timestamp = frame == 0 ? 0 : clock.Elapsed.Ticks;
            if (previousPixels is not null)
                writer.WriteFrame(previousPixels, previousTime, Math.Max(1, timestamp - previousTime));
            previousPixels = pixels;
            previousTime = timestamp;
            frame++;
            WaitForNextFrame(clock, frame, options.FramesPerSecond, stop, cancel);
        } while (!stop.IsCancellationRequested);
        cancel.ThrowIfCancellationRequested();
        writer.WriteFrame(previousPixels!, previousTime,
            Math.Max(frameDuration, clock.Elapsed.Ticks - previousTime));
        writer.Finish();
        cancel.ThrowIfCancellationRequested();
        return (path, preview!);
    }

    private static (string, BitmapSource) RecordGif(Rectangle bounds, string path,
        CancellationToken stop, CancellationToken cancel, Func<Rectangle, int, int, byte[]> captureFrame)
    {
        var scale = Math.Min(1d, Math.Min(640d / bounds.Width, 480d / bounds.Height));
        var width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        SixLabors.ImageSharp.Image<Bgra32>? animation = null;
        BitmapSource? preview = null;
        var clock = Stopwatch.StartNew();
        var frame = 0;
        try
        {
            do
            {
                cancel.ThrowIfCancellationRequested();
                var pixels = captureFrame(bounds, width, height);
                preview ??= Preview(pixels, width, height);
                using var image = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(pixels, width, height);
                if (animation is null)
                {
                    animation = image.Clone();
                    animation.Metadata.GetGifMetadata().RepeatCount = 0;
                    animation.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = 12;
                }
                else
                {
                    animation.Frames.AddFrame(image.Frames.RootFrame)
                        .Metadata.GetGifMetadata().FrameDelay = frame % 2 == 1 ? 13 : 12;
                }
                frame++;
                if (frame >= GifSecondsLimit * GifFps) break;
                WaitForNextFrame(clock, frame, GifFps, stop, cancel);
            } while (!stop.IsCancellationRequested);
            cancel.ThrowIfCancellationRequested();
            animation!.Save(path, new GifEncoder());
            cancel.ThrowIfCancellationRequested();
            return (path, preview!);
        }
        finally { animation?.Dispose(); }
    }

    private static byte[] Capture(Rectangle bounds, int width, int height)
    {
        using var source = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(source))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        if (width == bounds.Width && height == bounds.Height) return CopyPixels(source);
        using var output = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(output))
        {
            if (width < bounds.Width || height < bounds.Height)
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(source, 0, 0, width, height);
            }
            else
            {
                graphics.DrawImageUnscaled(source, 0, 0);
                if (width > bounds.Width)
                    graphics.DrawImage(source, new Rectangle(width - 1, 0, 1, bounds.Height),
                        new Rectangle(bounds.Width - 1, 0, 1, bounds.Height), GraphicsUnit.Pixel);
                if (height > bounds.Height)
                    graphics.DrawImage(source, new Rectangle(0, height - 1, bounds.Width, 1),
                        new Rectangle(0, bounds.Height - 1, bounds.Width, 1), GraphicsUnit.Pixel);
                if (width > bounds.Width && height > bounds.Height)
                    graphics.DrawImage(source, new Rectangle(width - 1, height - 1, 1, 1),
                        new Rectangle(bounds.Width - 1, bounds.Height - 1, 1, 1), GraphicsUnit.Pixel);
            }
        }
        return CopyPixels(output);
    }

    private static byte[] CopyPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * bitmap.Width * 4, bitmap.Width * 4);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static BitmapSource Preview(byte[] pixels, int width, int height)
    {
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private static void WaitForNextFrame(Stopwatch clock, int frame, int fps,
        CancellationToken stop, CancellationToken cancel)
    {
        while (!stop.IsCancellationRequested)
        {
            cancel.ThrowIfCancellationRequested();
            var remaining = TimeSpan.FromSeconds((double)frame / fps) - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            Thread.Sleep(Math.Min(10, Math.Max(1, (int)remaining.TotalMilliseconds)));
        }
    }
}
