using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace BetterScreenShot;

internal static class PngStreamWriter
{
    private static readonly byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] crcTable = BuildCrcTable();

    internal static void Write(Stream output, int width, int height, IEnumerable<ReadOnlyMemory<byte>> rows)
    {
        output.Write(signature);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // 8-bit RGB, no interlace.
        header[9] = 2;
        WriteChunk(output, "IHDR"u8, header);

        using (var chunks = new IdatStream(output))
        {
            using (var compressed = new ZLibStream(chunks, CompressionLevel.Optimal, leaveOpen: true))
            {
                var rgb = new byte[checked(width * 3)];
                var count = 0;
                foreach (var row in rows)
                {
                    if (row.Length != width * 4) throw new InvalidOperationException("Invalid scrolling capture row.");
                    var source = row.Span;
                    for (var x = 0; x < width; x++)
                    {
                        rgb[x * 3] = source[x * 4 + 2];
                        rgb[x * 3 + 1] = source[x * 4 + 1];
                        rgb[x * 3 + 2] = source[x * 4];
                    }
                    compressed.WriteByte(0); // PNG filter None.
                    compressed.Write(rgb);
                    count++;
                }
                if (count != height) throw new InvalidOperationException("Scrolling capture row count changed.");
            }
            chunks.Flush();
        }
        WriteChunk(output, "IEND"u8, []);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        var crc = UpdateCrc(UpdateCrc(uint.MaxValue, type), data) ^ uint.MaxValue;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data) crc = crcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            table[index] = crc;
        }
        return table;
    }

    private sealed class IdatStream(Stream output) : Stream
    {
        private readonly byte[] buffer = new byte[64 * 1024];
        private int used;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override int Read(byte[] target, int offset, int count) => throw new NotSupportedException();

        public override void Write(byte[] source, int offset, int count) => Write(source.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> source)
        {
            while (!source.IsEmpty)
            {
                var count = Math.Min(source.Length, buffer.Length - used);
                source[..count].CopyTo(buffer.AsSpan(used));
                source = source[count..];
                used += count;
                if (used == buffer.Length) Flush();
            }
        }

        public override void Flush()
        {
            if (used == 0) return;
            WriteChunk(output, "IDAT"u8, buffer.AsSpan(0, used));
            used = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Flush();
            base.Dispose(disposing);
        }
    }
}
