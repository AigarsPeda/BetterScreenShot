using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BetterScreenShot;

internal static class ImageClipboardService
{
    internal static async Task<System.Windows.DataObject> CreateDataObjectAsync(BitmapSource image)
    {
        var png = await Task.Run(() => EncodePng(image));
        return CreateDataObject(image, png);
    }

    internal static System.Windows.DataObject CreateDataObject(BitmapSource image) => CreateDataObject(image, EncodePng(image));

    private static MemoryStream EncodePng(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        // The data object owns this managed stream until the clipboard has copied it.
        var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;
        return png;
    }

    private static System.Windows.DataObject CreateDataObject(BitmapSource image, MemoryStream png)
    {
        var data = new System.Windows.DataObject();
        data.SetImage(image);
        data.SetData("PNG", png, autoConvert: false);
        return data;
    }
}
