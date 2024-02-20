using BruTile;
using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;
using NetVips;
using static System.Net.Mime.MediaTypeNames;
using System.Runtime.InteropServices;
using System.Reflection;
using static System.Net.WebRequestMethods;

namespace OpenSlideGTK
{
    public class ImageUtil
    {
        /// <summary>
        /// Join by <paramref name="srcPixelTiles"/> and cut by <paramref name="srcPixelExtent"/> then scale to <paramref name="dstPixelExtent"/>(only height an width is useful).
        /// </summary>
        /// <param name="srcPixelTiles">tile with tile extent collection</param>
        /// <param name="srcPixelExtent">canvas extent</param>
        /// <param name="dstPixelExtent">jpeg output size</param>
        /// <returns></returns>
        public static Image<Rgb24> Join(IEnumerable<Tuple<Extent, byte[]>> srcPixelTiles, Extent srcPixelExtent, Extent dstPixelExtent)
        {
            if (srcPixelTiles == null || srcPixelTiles.Count() == 0)
                return null;
            srcPixelExtent = srcPixelExtent.ToIntegerExtent();
            dstPixelExtent = dstPixelExtent.ToIntegerExtent();
            int canvasWidth = (int)srcPixelExtent.Width;
            int canvasHeight = (int)srcPixelExtent.Height;
            var dstWidth = (int)dstPixelExtent.Width;
            var dstHeight = (int)dstPixelExtent.Height;
            Image<Rgb24> canvas = new Image<Rgb24>(canvasWidth, canvasHeight);
            foreach (var tile in srcPixelTiles)
            {
                var tileExtent = tile.Item1.ToIntegerExtent();
                var intersect = srcPixelExtent.Intersect(tileExtent);
                if (intersect.Width == 0 || intersect.Height == 0)
                    continue;
                if(tile.Item2 == null)
                    continue;
                Image<Rgb24> tileRawData = CreateImageFromBytes(tile.Item2, (int)tileExtent.Width, (int)tileExtent.Height);
                var tileOffsetPixelX = (int)Math.Ceiling(intersect.MinX - tileExtent.MinX);
                var tileOffsetPixelY = (int)Math.Ceiling(intersect.MinY - tileExtent.MinY);
                var canvasOffsetPixelX = (int)Math.Ceiling(intersect.MinX - srcPixelExtent.MinX);
                var canvasOffsetPixelY = (int)Math.Ceiling(intersect.MinY - srcPixelExtent.MinY);
                
                try
                {
                    //We copy the tile region to the canvas.
                    for (int y = 0; y < intersect.Height; y++)
                    {
                        for (int x = 0; x < intersect.Width; x++)
                        {
                            int indx = canvasOffsetPixelX + x;
                            int indy = canvasOffsetPixelY + y;
                            int tindx = tileOffsetPixelX + x;
                            int tindy = tileOffsetPixelY + y;
                            canvas[indx, indy] = tileRawData[tindx, tindy];
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
                tileRawData.Dispose();
            }
            if (dstWidth != canvasWidth || dstHeight != canvasHeight)
            {
                try
                {
                    canvas.Mutate(x => x.Resize(dstWidth, dstHeight));
                    return canvas;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }

            }
            return canvas;
        }

        /// <summary>
        /// Join by <paramref name="srcPixelTiles"/> and cut by <paramref name="srcPixelExtent"/> then scale to <paramref name="dstPixelExtent"/>(only height an width is useful).
        /// </summary>
        /// <param name="srcPixelTiles">tile with tile extent collection</param>
        /// <param name="srcPixelExtent">canvas extent</param>
        /// <param name="dstPixelExtent">jpeg output size</param>
        /// <returns></returns>
        public static unsafe NetVips.Image JoinVips(IEnumerable<Tuple<Extent, byte[]>> srcPixelTiles, Extent srcPixelExtent, Extent dstPixelExtent)
        {
            if (srcPixelTiles == null || !srcPixelTiles.Any())
                return null;

            srcPixelExtent = srcPixelExtent.ToIntegerExtent();
            dstPixelExtent = dstPixelExtent.ToIntegerExtent();
            int canvasWidth = (int)srcPixelExtent.Width;
            int canvasHeight = (int)srcPixelExtent.Height;

            // Create a base canvas. Adjust as necessary, for example, using a transparent image if needed.
            NetVips.Image canvas = NetVips.Image.Black(canvasWidth, canvasHeight, bands: 3);

            foreach (var tile in srcPixelTiles)
            {
                if (tile.Item2 == null)
                    continue;

                fixed (byte* pTileData = tile.Item2)
                {
                    var tileExtent = tile.Item1.ToIntegerExtent();
                    NetVips.Image tileImage = NetVips.Image.NewFromMemory((IntPtr)pTileData, (ulong)tile.Item2.Length, (int)tileExtent.Width, (int)tileExtent.Height, 3, Enums.BandFormat.Uchar);

                    // Calculate positions and sizes for cropping and inserting
                    var intersect = srcPixelExtent.Intersect(tileExtent);
                    if (intersect.Width == 0 || intersect.Height == 0)
                        continue;

                    int tileOffsetPixelX = (int)Math.Ceiling(intersect.MinX - tileExtent.MinX);
                    int tileOffsetPixelY = (int)Math.Ceiling(intersect.MinY - tileExtent.MinY);
                    int canvasOffsetPixelX = (int)Math.Ceiling(intersect.MinX - srcPixelExtent.MinX);
                    int canvasOffsetPixelY = (int)Math.Ceiling(intersect.MinY - srcPixelExtent.MinY);

                    using (var croppedTile = tileImage.Crop(tileOffsetPixelX, tileOffsetPixelY, (int)intersect.Width, (int)intersect.Height))
                    {
                        // Instead of inserting directly, we composite over the base canvas
                        canvas = canvas.Composite2(croppedTile, Enums.BlendMode.Over, canvasOffsetPixelX, canvasOffsetPixelY);
                    }
                }
            }

            // Resize if the destination extent differs from the source canvas size
            if ((int)dstPixelExtent.Width != canvasWidth || (int)dstPixelExtent.Height != canvasHeight)
            {
                double scaleX = (double)dstPixelExtent.Width / canvasWidth;
                double scaleY = (double)dstPixelExtent.Height / canvasHeight;
                canvas = canvas.Resize(scaleX, vscale: scaleY, kernel: Enums.Kernel.Nearest);
            }

            return canvas;
        }

        public static Image<Rgb24> CreateImageFromBytes(byte[] rgbBytes, int width, int height)
        {
            if (rgbBytes.Length != width * height * 3)
            {
                throw new ArgumentException("Byte array size does not match the dimensions of the image");
            }

            // Create a new image of the specified size
            Image<Rgb24> image = new Image<Rgb24>(width, height);

            // Index for the byte array
            int byteIndex = 0;

            // Iterate over the image pixels
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Create a color from the next three bytes
                    Rgb24 color = new Rgb24(rgbBytes[byteIndex], rgbBytes[byteIndex + 1], rgbBytes[byteIndex + 2]);
                    byteIndex += 3;
                    // Set the pixel
                    image[x, y] = color;
                }
            }

            return image;
        }

    }


    public class TileUtil
    {
        /// <summary>
        /// To ensure image quality, try to use high-resolution level downsampling to low-resolution level 
        /// </summary>
        /// <param name="resolutions"></param>
        /// <param name="unitsPerPixel"></param>
        /// <param name="sampleMode"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        public static int GetLevel(IDictionary<int, Resolution> resolutions, double unitsPerPixel, SampleMode sampleMode = SampleMode.Nearest)
        {
            if (resolutions.Count == 0)
            {
                throw new ArgumentException("No tile resolutions");
            }

            IOrderedEnumerable<KeyValuePair<int, Resolution>> orderedEnumerable = resolutions.OrderByDescending((KeyValuePair<int, Resolution> r) => r.Value.UnitsPerPixel);
            if (orderedEnumerable.Last().Value.UnitsPerPixel > unitsPerPixel)
            {
                return orderedEnumerable.Last().Key;
            }

            if (orderedEnumerable.First().Value.UnitsPerPixel < unitsPerPixel)
            {
                return orderedEnumerable.First().Key;
            }

            switch (sampleMode)
            {
                case SampleMode.Nearest:
                    {
                        int id = -1;
                        double num = double.MaxValue;
                        foreach (KeyValuePair<int, Resolution> item in orderedEnumerable)
                        {
                            double num2 = Math.Abs(item.Value.UnitsPerPixel - unitsPerPixel);
                            if (num2 < num)
                            {
                                id = item.Key;
                                num = num2;
                            }
                        }

                        if (id == -1)
                        {
                            throw new Exception("Unexpected error when calculating nearest level");
                        }

                        return id;
                    }
                case SampleMode.NearestUp:
                    return orderedEnumerable.Last(_ => _.Value.UnitsPerPixel >= unitsPerPixel).Key;
                case SampleMode.NearestDwon:
                    return orderedEnumerable.First(_ => _.Value.UnitsPerPixel <= unitsPerPixel).Key;
                case SampleMode.Top:
                    return orderedEnumerable.First().Key;
                case SampleMode.Bottom:
                    return orderedEnumerable.Last().Key;
                default:
                    throw new Exception($"Unexpected error {nameof(sampleMode)}");
            }
        }
    }
}
