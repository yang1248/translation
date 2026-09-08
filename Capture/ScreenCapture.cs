using System.Drawing;
using System.Drawing.Imaging;
using RealtimeTranslator.Models;

namespace RealtimeTranslator.Capture;

public static class ScreenCapture
{
    public static Bitmap? CaptureRegion(RegionInfo region)
    {
        if (!region.IsValid)
        {
            return null;
        }

        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                region.X,
                region.Y,
                0,
                0,
                new Size(region.Width, region.Height),
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
