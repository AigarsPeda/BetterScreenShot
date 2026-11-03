using System.Runtime.InteropServices;

namespace BetterScreenShot;

// Minimal Media Foundation sink-writer interop for RGB screen frames -> H.264 MP4.
internal sealed class Mp4Writer : IDisposable
{
    private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid AverageBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");

    private IntPtr writer;
    private int stream;

    internal Mp4Writer(string path, int width, int height, int framesPerSecond, int bitrate)
    {
        if (bitrate <= 0) throw new ArgumentOutOfRangeException(nameof(bitrate));
        Marshal.ThrowExceptionForHR(MFStartup(0x00020070, 0));
        try
        {
            Marshal.ThrowExceptionForHR(MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out writer));
            var output = CreateType(H264, width, height, framesPerSecond);
            try
            {
                Set32(output, AverageBitrate, bitrate);
                Marshal.ThrowExceptionForHR(Method<AddStream>(writer, 3)(writer, output, out stream));
            }
            finally { Marshal.Release(output); }
            var input = CreateType(Rgb32, width, height, framesPerSecond);
            try
            {
                Set32(input, DefaultStride, checked(width * 4));
                Marshal.ThrowExceptionForHR(Method<SetInputMediaType>(writer, 4)(writer, stream, input, IntPtr.Zero));
            }
            finally { Marshal.Release(input); }
            Marshal.ThrowExceptionForHR(Method<NoArgs>(writer, 5)(writer));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void WriteFrame(byte[] pixels, long timestamp, long duration)
    {
        Marshal.ThrowExceptionForHR(MFCreateMemoryBuffer(pixels.Length, out var buffer));
        try
        {
            Marshal.ThrowExceptionForHR(Method<LockBuffer>(buffer, 3)(buffer, out var destination, IntPtr.Zero, IntPtr.Zero));
            try { Marshal.Copy(pixels, 0, destination, pixels.Length); }
            finally { Marshal.ThrowExceptionForHR(Method<NoArgs>(buffer, 4)(buffer)); }
            Marshal.ThrowExceptionForHR(Method<SetLength>(buffer, 6)(buffer, pixels.Length));
            Marshal.ThrowExceptionForHR(MFCreateSample(out var sample));
            try
            {
                Marshal.ThrowExceptionForHR(Method<AddBuffer>(sample, 42)(sample, buffer));
                Marshal.ThrowExceptionForHR(Method<SetLong>(sample, 36)(sample, timestamp));
                Marshal.ThrowExceptionForHR(Method<SetLong>(sample, 38)(sample, duration));
                Marshal.ThrowExceptionForHR(Method<WriteSample>(writer, 6)(writer, stream, sample));
            }
            finally { Marshal.Release(sample); }
        }
        finally { Marshal.Release(buffer); }
    }

    internal void Finish()
    {
        Marshal.ThrowExceptionForHR(Method<NoArgs>(writer, 11)(writer));
    }

    public void Dispose()
    {
        if (writer != IntPtr.Zero)
        {
            Marshal.Release(writer);
            writer = IntPtr.Zero;
        }
        MFShutdown();
    }

    private static IntPtr CreateType(Guid format, int width, int height, int fps)
    {
        Marshal.ThrowExceptionForHR(MFCreateMediaType(out var type));
        try
        {
            SetGuid(type, MajorType, Video);
            SetGuid(type, Subtype, format);
            Set32(type, InterlaceMode, 2);
            Set64(type, FrameSize, ((long)width << 32) | (uint)height);
            Set64(type, FrameRate, ((long)fps << 32) | 1);
            Set64(type, PixelAspectRatio, (1L << 32) | 1);
            return type;
        }
        catch { Marshal.Release(type); throw; }
    }

    private static void SetGuid(IntPtr type, Guid key, Guid value) =>
        Marshal.ThrowExceptionForHR(Method<SetGuidAttribute>(type, 24)(type, ref key, ref value));
    private static void Set32(IntPtr type, Guid key, int value) =>
        Marshal.ThrowExceptionForHR(Method<SetIntAttribute>(type, 21)(type, ref key, value));
    private static void Set64(IntPtr type, Guid key, long value) =>
        Marshal.ThrowExceptionForHR(Method<SetLongAttribute>(type, 22)(type, ref key, value));
    private static T Method<T>(IntPtr instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgs(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddStream(IntPtr self, IntPtr type, out int index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetInputMediaType(IntPtr self, int index, IntPtr type, IntPtr parameters);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WriteSample(IntPtr self, int index, IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockBuffer(IntPtr self, out IntPtr data, IntPtr capacity, IntPtr length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetLength(IntPtr self, int length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddBuffer(IntPtr self, IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetLong(IntPtr self, long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidAttribute(IntPtr self, ref Guid key, ref Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetIntAttribute(IntPtr self, ref Guid key, int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetLongAttribute(IntPtr self, ref Guid key, long value);

    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMemoryBuffer(int size, out IntPtr buffer);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateSample(out IntPtr sample);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSinkWriterFromURL(string path, IntPtr byteStream, IntPtr attributes, out IntPtr writer);
}
