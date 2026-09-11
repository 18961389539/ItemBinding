using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageViewer.Controls
{
    internal static class ImageViewerImageSourceUtilities
    {
        public static bool TryGetSourceImageSize(ImageSource? source, out Size imageSize)
        {
            switch (source)
            {
                case BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } bitmap:
                    imageSize = new Size(bitmap.PixelWidth, bitmap.PixelHeight);
                    return true;
                case ImageSource { Width: > 0, Height: > 0 } image:
                    imageSize = new Size(image.Width, image.Height);
                    return true;
                default:
                    imageSize = default;
                    return false;
            }
        }
    }
}