using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AForge;
namespace OpenSlideSharp.BitmapExtensions
{
    /// <summary>
    /// 
    /// </summary>
    public static class AssociatedImageExtensions
    {
        /// <summary>
        /// To bitmap.
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public unsafe static Bitmap ToBitmap(this AssociatedImage image)
        {
            if (image == null) throw new NullReferenceException();
            fixed (byte* scan0 = image.Data)
            {
                return new Bitmap((int)image.Dimensions.Width, (int)image.Dimensions.Height, (int)image.Dimensions.Width * 4, PixelFormat.Format32bppArgb, (IntPtr)scan0);
            }
        }

    }


}
