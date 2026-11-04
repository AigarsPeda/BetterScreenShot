using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace BetterScreenShot;

internal enum ScrollFrameResult { Unchanged, Added, NoOverlap, LimitReached }
internal enum ScrollDirection { Vertical, Horizontal }

internal sealed class ScrollFrame(int width, int height, byte[] pixels)
{
    internal int Width { get; } = width;
    internal int Height { get; } = height;
    internal byte[] Pixels { get; } = pixels;
    internal int Stride => Width * 4;

    internal ScrollFrame Transpose()
    {
        var pixels = new byte[Pixels.Length];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            Pixels.AsSpan((y * Width + x) * 4, 4).CopyTo(pixels.AsSpan((x * Height + y) * 4, 4));
        return new ScrollFrame(Height, Width, pixels);
    }

    internal static ScrollFrame Capture(Rectangle bounds)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bounds.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
            if (data.Stride != bounds.Width * 4) throw new InvalidOperationException("Unsupported screen pixel layout.");
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new ScrollFrame(bounds.Width, bounds.Height, pixels);
        }
        finally { bitmap.UnlockBits(data); }
    }
}

internal sealed class ScrollingStitcher : IDisposable
{
    private const int MaxPixels = 30_000_000;
    private readonly FileStream spool;
    private ScrollFrame last;
    private ulong[]? lastFingerprints;
    private Dictionary<ulong, int>? lastUniqueRows;
    private int fixedFooterHeight;
    private int length;
    private byte[]? outputRows;
    private int outputRowsStart = -1;
    private int outputRowCount;
    internal ScrollDirection Direction { get; }
    internal int Width => Direction == ScrollDirection.Horizontal ? length : last.Width;
    internal int Height => Direction == ScrollDirection.Horizontal ? last.Width : length;
    internal int AddedFrames { get; private set; }
    internal string SpoolPath => spool.Name;
    internal long SpoolBytes => spool.Length;

    internal ScrollingStitcher(ScrollFrame first, ScrollDirection direction = ScrollDirection.Vertical)
    {
        ValidateFrame(first);
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if ((long)first.Width * first.Height > MaxPixels) throw new InvalidOperationException("The selected area is too large for a scrolling capture.");
        Direction = direction;
        if (direction == ScrollDirection.Horizontal) first = first.Transpose();
        var folder = Path.Combine(Path.GetTempPath(), "BetterScreenShot");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"scroll_{Guid.NewGuid():N}.tmp");
        spool = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            64 * 1024, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        try { spool.Write(first.Pixels); }
        catch { spool.Dispose(); throw; }
        last = first;
        length = first.Height;
    }

    internal ScrollFrameResult Add(ScrollFrame next)
    {
        ValidateFrame(next);
        var expectedWidth = Direction == ScrollDirection.Horizontal ? last.Height : last.Width;
        var expectedHeight = Direction == ScrollDirection.Horizontal ? last.Width : last.Height;
        if (next.Width != expectedWidth || next.Height != expectedHeight) throw new ArgumentException("The capture area changed size.", nameof(next));
        if (Direction == ScrollDirection.Horizontal) next = next.Transpose();
        var shift = FindShift(next, out var detectedFooter, out var nextFingerprints, out var nextUniqueRows);
        if (shift == 0) return ScrollFrameResult.Unchanged;
        if (shift < 0) return ScrollFrameResult.NoOverlap;
        var footer = AddedFrames == 0 ? detectedFooter : fixedFooterHeight;
        if (shift >= next.Height - footer - 32) return ScrollFrameResult.NoOverlap;
        if ((long)last.Width * (length + shift) > MaxPixels || length + shift > 32_000) return ScrollFrameResult.LimitReached;
        if (AddedFrames == 0 && footer > 0)
        {
            fixedFooterHeight = footer;
            spool.SetLength((long)(last.Height - footer) * last.Stride);
        }
        spool.Position = spool.Length;
        spool.Write(next.Pixels, (next.Height - fixedFooterHeight - shift) * next.Stride, shift * next.Stride);
        last = next;
        lastFingerprints = nextFingerprints;
        lastUniqueRows = nextUniqueRows;
        length += shift;
        outputRowsStart = -1;
        AddedFrames++;
        return ScrollFrameResult.Added;
    }

    internal Bitmap BuildBitmap()
    {
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                if (data.Stride != Width * 4) throw new InvalidOperationException("Unsupported output pixel layout.");
                var offset = 0;
                ForEachRow((pixels, sourceOffset) =>
                {
                    Marshal.Copy(pixels, sourceOffset, IntPtr.Add(data.Scan0, offset), Width * 4);
                    offset += Width * 4;
                });
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal void ForEachRow(Action<byte[], int> useRow)
    {
        if (Direction == ScrollDirection.Horizontal)
        {
            for (var row = 0; row < Height; row++)
            {
                LoadOutputRows(row);
                useRow(outputRows!, (row - outputRowsStart) * Width * 4);
            }
            return;
        }
        var stride = Width * 4;
        var buffer = new byte[stride];
        spool.Flush();
        spool.Position = 0;
        for (var row = 0; row < Height - fixedFooterHeight; row++)
        {
            spool.ReadExactly(buffer);
            useRow(buffer, 0);
        }
        for (var row = last.Height - fixedFooterHeight; row < last.Height; row++)
            useRow(last.Pixels, row * stride);
    }

    internal void CopyRow(int row, Span<byte> destination)
    {
        if (row < 0 || row >= Height) throw new ArgumentOutOfRangeException(nameof(row));
        var stride = Width * 4;
        if (destination.Length < stride) throw new ArgumentException("Row buffer is too small.", nameof(destination));
        if (Direction == ScrollDirection.Horizontal)
        {
            LoadOutputRows(row);
            outputRows.AsSpan((row - outputRowsStart) * stride, stride).CopyTo(destination);
            return;
        }
        var storedHeight = Height - fixedFooterHeight;
        if (row < storedHeight)
        {
            spool.Position = (long)row * stride;
            spool.ReadExactly(destination[..stride]);
        }
        else
        {
            var footerRow = last.Height - fixedFooterHeight + row - storedHeight;
            last.Pixels.AsSpan(footerRow * stride, stride).CopyTo(destination);
        }
    }

    public void Dispose() => spool.Dispose();

    private static void ValidateFrame(ScrollFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels is null
            || frame.Pixels.Length != (long)frame.Width * frame.Height * 4)
            throw new ArgumentException("Invalid scroll capture frame.", nameof(frame));
    }

    private void LoadOutputRows(int row)
    {
        if (outputRowsStart >= 0 && row >= outputRowsStart && row < outputRowsStart + outputRowCount) return;
        // Store horizontal captures as columns so adding a frame remains append-only.
        // Transpose a bounded batch for row-based PNG export and preview sampling.
        var rowsPerBatch = Math.Max(1, 4_000_000 / (Width * 4));
        outputRowsStart = -1;
        var start = row / rowsPerBatch * rowsPerBatch;
        outputRowCount = Math.Min(rowsPerBatch, Height - start);
        var bytes = outputRowCount * Width * 4;
        if (outputRows is null || outputRows.Length < bytes) outputRows = new byte[bytes];
        var column = new byte[outputRowCount * 4];
        for (var x = 0; x < Width; x++)
        {
            if (x < length - fixedFooterHeight)
            {
                spool.Position = (long)x * last.Stride + start * 4;
                spool.ReadExactly(column);
            }
            else
            {
                var sourceColumn = last.Height - fixedFooterHeight + x - (length - fixedFooterHeight);
                last.Pixels.AsSpan(sourceColumn * last.Stride + start * 4, column.Length).CopyTo(column);
            }
            for (var y = 0; y < outputRowCount; y++)
                column.AsSpan(y * 4, 4).CopyTo(outputRows.AsSpan((y * Width + x) * 4, 4));
        }
        outputRowsStart = start;
    }

    private int FindShift(ScrollFrame current, out int fixedFooter,
        out ulong[]? currentFingerprints, out Dictionary<ulong, int>? currentUniqueRows)
    {
        fixedFooter = fixedFooterHeight;
        currentFingerprints = null;
        currentUniqueRows = null;
        if (last.Pixels.AsSpan().SequenceEqual(current.Pixels)) return 0;
        var height = last.Height;
        var maxShift = height - Math.Max(32, height / 5);
        if (maxShift < 4) return -1;
        // Match distinctive three-row fingerprints. Flat backgrounds and repeated buttons
        // cannot vote for a scroll distance, so they cannot create a false seam.
        var before = lastFingerprints ??= Fingerprints(last);
        var uniqueBefore = lastUniqueRows ??= UniqueRows(before);
        currentFingerprints = Fingerprints(current);
        currentUniqueRows = UniqueRows(currentFingerprints);
        var votes = new Dictionary<int, List<int>>();
        foreach (var (fingerprint, currentRow) in currentUniqueRows)
        {
            if (!uniqueBefore.TryGetValue(fingerprint, out var previousRow)) continue;
            var shift = previousRow - currentRow;
            if (shift < 3 || shift > maxShift) continue;
            if (!votes.TryGetValue(shift, out var rows)) votes[shift] = rows = [];
            rows.Add(currentRow);
        }
        var ranked = votes.OrderByDescending(item => item.Value.Count).ToArray();
        if (ranked.Length == 0) return Score(last, current, 0) < 0.8 ? 0 : -1;
        var best = ranked[0];
        var runnerUp = ranked.Skip(1).Where(item => Math.Abs(item.Key - best.Key) > 2)
            .Select(item => item.Value.Count).DefaultIfEmpty(0).Max();
        var requiredVotes = Math.Max(3, Math.Min(8, height / 100));
        var span = best.Value.Max() - best.Value.Min();
        if (best.Value.Count < requiredVotes || runnerUp * 2 >= best.Value.Count
            || span < Math.Min(24, (height - best.Key) / 4)) return -1;
        if (fixedFooter == 0) fixedFooter = DetectFixedFooter(last, current, before, currentFingerprints);
        var contentHeight = height - fixedFooter;
        if (best.Key >= contentHeight - 32) return -1;
        var aligned = Score(last, current, best.Key, contentHeight);
        var stationary = Score(last, current, 0, contentHeight);
        if (aligned > 18 || aligned >= stationary * 0.9) return -1;
        return best.Key;
    }

    private static int DetectFixedFooter(ScrollFrame previous, ScrollFrame current, ulong[] before, ulong[] after)
    {
        var bottom = previous.Height;
        var minimum = bottom - bottom / 3;
        var start = bottom;
        var changedRun = 0;
        for (var y = bottom - 1; y >= minimum; y--)
        {
            if (SameRow(previous, current, y))
            {
                start = y;
                changedRun = 0;
            }
            else if (++changedRun >= 5) break;
        }
        if (bottom - start < 12) return 0;
        var distinctive = 0;
        for (var y = start; y < bottom; y++)
            if (before[y] != 0 && before[y] == after[y]) distinctive++;
        return distinctive >= 3 ? bottom - start : 0;
    }

    private static bool SameRow(ScrollFrame previous, ScrollFrame current, int y)
    {
        var changed = 0;
        var samples = 0;
        var step = Math.Max(1, previous.Width / 192);
        for (var x = 0; x < previous.Width; x += step)
        {
            var pixel = y * previous.Stride + x * 4;
            if (Math.Abs(previous.Pixels[pixel] - current.Pixels[pixel]) > 12
                || Math.Abs(previous.Pixels[pixel + 1] - current.Pixels[pixel + 1]) > 12
                || Math.Abs(previous.Pixels[pixel + 2] - current.Pixels[pixel + 2]) > 12) changed++;
            samples++;
        }
        return changed <= Math.Max(2, samples / 100);
    }

    private static ulong[] Fingerprints(ScrollFrame frame)
    {
        var result = new ulong[frame.Height];
        var left = frame.Width / 16;
        var right = frame.Width - left;
        var step = Math.Max(1, (right - left) / 192);
        for (var y = 2; y < frame.Height - 2; y++)
        {
            ulong hash = 14695981039346656037;
            var first = y * frame.Stride + left * 4;
            var colorful = 0;
            for (var row = y - 2; row <= y + 2; row += 2)
            for (var x = left; x < right; x += step)
            {
                var pixel = row * frame.Stride + x * 4;
                if (Math.Abs(frame.Pixels[pixel] - frame.Pixels[first]) > 12
                    || Math.Abs(frame.Pixels[pixel + 1] - frame.Pixels[first + 1]) > 12
                    || Math.Abs(frame.Pixels[pixel + 2] - frame.Pixels[first + 2]) > 12) colorful++;
                for (var channel = 0; channel < 3; channel++)
                {
                    hash ^= frame.Pixels[pixel + channel];
                    hash *= 1099511628211;
                }
            }
            if (colorful >= 3) result[y] = hash;
        }
        return result;
    }

    private static Dictionary<ulong, int> UniqueRows(ulong[] fingerprints)
    {
        var rows = new Dictionary<ulong, int>();
        for (var y = 0; y < fingerprints.Length; y++)
        {
            var fingerprint = fingerprints[y];
            if (fingerprint == 0) continue;
            if (!rows.TryAdd(fingerprint, y)) rows[fingerprint] = -1;
        }
        foreach (var repeated in rows.Where(item => item.Value < 0).Select(item => item.Key).ToArray())
            rows.Remove(repeated);
        return rows;
    }

    private static double Score(ScrollFrame previous, ScrollFrame current, int shift, int? contentHeight = null)
    {
        var overlap = (contentHeight ?? previous.Height) - shift;
        var margin = Math.Min(previous.Height / 12, overlap / 5);
        var stepY = Math.Max(2, previous.Height / 100);
        var stepX = Math.Max(2, previous.Width / 48);
        var left = previous.Width / 12;
        var right = previous.Width - left;
        long difference = 0;
        var samples = 0;
        for (var y = margin; y < overlap - margin; y += stepY)
        for (var x = left; x < right; x += stepX)
        {
            var before = (y + shift) * previous.Stride + x * 4;
            var after = y * current.Stride + x * 4;
            difference += Math.Abs(previous.Pixels[before] - current.Pixels[after]);
            difference += Math.Abs(previous.Pixels[before + 1] - current.Pixels[after + 1]);
            difference += Math.Abs(previous.Pixels[before + 2] - current.Pixels[after + 2]);
            samples += 3;
        }
        return samples == 0 ? double.MaxValue : (double)difference / samples;
    }
}
