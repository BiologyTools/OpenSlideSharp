﻿using BruTile;
using BruTile.Cache;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using AForge;
using NetVips;

namespace OpenSlideGTK
{
    public class LruCache<TKey, TValue>
    {
        private readonly int capacity;
        public Dictionary<Info, LinkedListNode<(Info key, TValue value)>> cacheMap = new Dictionary<Info, LinkedListNode<(Info key, TValue value)>>();
        private LinkedList<(Info key, TValue value)> lruList = new LinkedList<(Info key, TValue value)>();

        public LruCache(int capacity)
        {
            this.capacity = capacity;
        }

        public TValue Get(Info key)
        {
            foreach (LinkedListNode<(Info key, TValue value)> item in cacheMap.Values)
            {
                Info k = item.Value.key;
                if (k.Coordinate == key.Coordinate && k.Index == key.Index)
                {
                    lruList.Remove(item);
                    lruList.AddLast(item);
                    return item.Value.value;
                }
            }
            return default(TValue);
        }

        public void Add(Info key, TValue value)
        {
            if (cacheMap.Count >= capacity)
            {
                var oldest = lruList.First;
                if (oldest != null)
                {
                    lruList.RemoveFirst();
                    cacheMap.Remove(oldest.Value.key);
                }
            }

            if (cacheMap.ContainsKey(key))
            {
                lruList.Remove(cacheMap[key]);
            }

            var newNode = new LinkedListNode<(Info key, TValue value)>((key, value));
            lruList.AddLast(newNode);
            cacheMap[key] = newNode;
        }
        public void Dispose()
        {
            foreach (LinkedListNode<(Info key, TValue value)> item in cacheMap.Values)
            {
                lruList.Remove(item);
            }
        }
    }
    public class Info
    {
        public int Level { get; set; }
        public ZCT Coordinate { get; set; }
        public TileIndex Index { get; set; }
        public Extent Extent { get; set; }
        public Info(ZCT coordinate, TileIndex index, Extent extent, int level)
        {
            Coordinate = coordinate;
            Index = index;
            Extent = extent;
            Level = level;
        }
    }
    public class TileCache
    {
        public LruCache<Info, byte[]> cache;
        private int capacity;
        private Stitch stitch = new Stitch();
        SlideSourceBase source = null;
        public TileCache(SlideSourceBase source, int capacity = 1000)
        {
            this.source = source;
            this.capacity = capacity;
            this.cache = new LruCache<Info, byte[]>(capacity);
        }

        public async Task<byte[]> GetTile(Info inf)
        {
            byte[] data = cache.Get(inf);
            if (data != null)
            {
                return data;
            }
            byte[] tile = await LoadTile(inf);
            if (tile != null)
                AddTile(inf, tile);
            return tile;
        }

        public byte[] GetTileSync(Info inf, double unitsPerPixel)
        {
        A:
            try
            {
                if (SlideSourceBase.useGPU)
                {
                    if (stitch.HasTile(inf.Extent.WorldToPixelInvertedY(unitsPerPixel)))
                        return null;
                }
            }
            catch (Exception)
            {
                SlideSourceBase.useGPU = false;
                goto A;
            }

            byte[] data = cache.Get(inf);
            if (data != null)
            {
                return data;
            }
            byte[] tile = LoadTileSync(inf);
            if (tile != null && !SlideSourceBase.useGPU)
                AddTile(inf, tile);
            return tile;
        }

        private void AddTile(Info tileId, byte[] tile)
        {
            cache.Add(tileId, tile);
        }

        private async Task<byte[]> LoadTile(Info tileId)
        {
            try
            {
                TileInfo tf = new TileInfo();
                tf.Index = tileId.Index;
                tf.Extent = tileId.Extent;
                return await source.GetTileAsync(tf);
            }
            catch (Exception e)
            {
                return null;
            }
        }
        private byte[] LoadTileSync(Info tileId)
        {
            try
            {
                TileInfo tf = new TileInfo();
                tf.Index = tileId.Index;
                tf.Extent = tileId.Extent;
                return source.GetTile(tf);
            }
            catch (Exception e)
            {
                return null;
            }
        }
        public void Dispose()
        {
            cache.Dispose();
        }
    }
    public abstract class SlideSourceBase : ISlideSource, IDisposable
    {
        #region Static
        public static bool UseRealResolution { get; set; } = true;

        private static IDictionary<string, Func<string, bool, ISlideSource>> keyValuePairs = new Dictionary<string, Func<string, bool, ISlideSource>>();

        /// <summary>
        /// resister decode for Specific format
        /// </summary>
        /// <param name="extensionUpper">dot and extension upper</param>
        /// <param name="factory">file path,enable cache,decoder</param>
        public static void Resister(string extensionUpper, Func<string, bool, ISlideSource> factory)
        {
            keyValuePairs.Add(extensionUpper, factory);
        }

        public static ISlideSource Create(string source, bool enableCache = true)
        {
            var ext = Path.GetExtension(source).ToUpper();
            
            try
            {
                if (keyValuePairs.TryGetValue(ext, out var factory) && factory != null)
                    return factory.Invoke(source, enableCache);

                if (!string.IsNullOrEmpty(OpenSlideBase.DetectVendor(source)))
                {
                    var osb = new OpenSlideBase(source, enableCache);
                    osb.cache = new TileCache(osb);
                    return osb;
                }
            }
            catch (Exception e) 
            { 
                Console.WriteLine(e.Message); 
            }
            return null;
        }
        #endregion

        public abstract byte[] GetTile(TileInfo tileInfo);

        public abstract Task<byte[]> GetTileAsync(TileInfo tileInfo);

        public double MinUnitsPerPixel { get; protected set; }

        public Dictionary<TileIndex, byte[]> _bgraCache = new Dictionary<TileIndex, byte[]>();
        public static byte[] LastSlice;
        public static Extent destExtent;
        public static Extent sourceExtent;
        public static double curUnitsPerPixel = 1;
        private int level;
        private ZCT coord;
        public static bool UseVips = true;
        public static bool useGPU = false;
        public TileCache cache;
        public Stitch stitch = new Stitch();
        private PixelFormat px;
        public PixelFormat PixelFormat
        {
            get { return px; }
        }
       
        public static byte[] SwapRedBlueChannels(byte[] imageData)
        {
            int bytesPerPixel = 3;
            if (bytesPerPixel < 3)
                throw new ArgumentException("The image must have at least 3 bytes per pixel (RGB).");

            // Create a new array to store the swapped channel data
            byte[] swappedData = new byte[imageData.Length];

            for (int i = 0; i < imageData.Length; i += bytesPerPixel)
            {
                // Copy the green channel as is
                swappedData[i + 1] = imageData[i + 1]; // Green channel stays the same

                // Swap the red and blue channels
                swappedData[i] = imageData[i + 2];     // Red channel gets Blue's value
                swappedData[i + 2] = imageData[i];     // Blue channel gets Red's value

                // If there are more bytes per pixel (like an alpha channel), copy the rest as is
                for (int j = 3; j < bytesPerPixel; j++)
                {
                    swappedData[i + j] = imageData[i + j]; // Copy other channels if any (like alpha)
                }
            }

            return swappedData;
        }

        public void SetSliceInfo(int level,PixelFormat px,ZCT coord)
        {
            this.px = px;
            this.level = level;
            this.coord = coord;
        }
        public byte[] GetSlice(SliceInfo sliceInfo)
        {
            if (cache == null)
                cache = new TileCache(this);
            var curLevel = level;
            var curUnitsPerPixel = sliceInfo.Resolution;
            var tileInfos = Schema.GetTileInfos(sliceInfo.Extent, curLevel);
            List<Tuple<Extent, byte[]>> tiles = new List<Tuple<Extent, byte[]>>();

            foreach (BruTile.TileInfo t in tileInfos)
            {
                Info tf = new Info(coord, t.Index, t.Extent, curLevel);
                byte[] c = cache.GetTileSync(tf, curUnitsPerPixel);
                if (c != null)
                {
                    if (c.Length == 4 * 256 * 256)
                    {
                        c = Bitmap.Convert32BitARGBTo24BitRGB(c);
                    }
                    else
                    if (px == PixelFormat.Format16bppGrayScale)
                    {
                        c = Bitmap.Convert16BitGrayscaleTo24BitRGB(c);
                    }
                    else
                    if (px == PixelFormat.Format48bppRgb)
                    {
                        c = Bitmap.Convert48BitTo24BitRGB(c);
                    }
                    if (useGPU)
                    {
                        TileInfo tileInfo = new TileInfo();
                        tileInfo.Extent = t.Extent.WorldToPixelInvertedY(curUnitsPerPixel);
                        tileInfo.Index = t.Index;
                        stitch.AddTile(Tuple.Create(tileInfo, c));
                    }
                    else
                        tiles.Add(Tuple.Create(t.Extent.WorldToPixelInvertedY(curUnitsPerPixel), c));
                }
            }
            var srcPixelExtent = sliceInfo.Extent.WorldToPixelInvertedY(curUnitsPerPixel);
            var dstPixelExtent = sliceInfo.Extent.WorldToPixelInvertedY(sliceInfo.Resolution);
            var dstPixelHeight = sliceInfo.Parame.DstPixelHeight > 0 ? sliceInfo.Parame.DstPixelHeight : dstPixelExtent.Height;
            var dstPixelWidth = sliceInfo.Parame.DstPixelWidth > 0 ? sliceInfo.Parame.DstPixelWidth : dstPixelExtent.Width;
            destExtent = new Extent(0, 0, dstPixelWidth, dstPixelHeight);
            sourceExtent = srcPixelExtent;
            if (useGPU)
            {
                try
                {
                    return stitch.StitchImages(tileInfos.ToList(), (int)Math.Round(dstPixelWidth), (int)Math.Round(dstPixelHeight), Math.Round(srcPixelExtent.MinX), Math.Round(srcPixelExtent.MinY), curUnitsPerPixel);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message.ToString());
                    UseVips = true;
                    useGPU = false;
                }
            }
            if (UseVips)
            {
                try
                {
                    NetVips.Image im;
                    if (px == PixelFormat.Format24bppRgb)
                        im = ImageUtil.JoinVipsRGB24(tiles, srcPixelExtent, new Extent(0, 0, dstPixelWidth, dstPixelHeight));
                    else
                        im = ImageUtil.JoinVips16(tiles, srcPixelExtent, new Extent(0, 0, dstPixelWidth, dstPixelHeight));
                    return im.WriteToMemory();
                }
                catch (Exception e)
                {
                    UseVips = false;
                    Console.WriteLine("Failed to use LibVips please install Libvips for your platform.");
                    Console.WriteLine(e.Message);
                }
            }
            try
            {
                
                if (px == PixelFormat.Format16bppGrayScale)
                {
                    SixLabors.ImageSharp.Image im = ImageUtil.Join16(tiles, srcPixelExtent, new Extent(0, 0, dstPixelWidth, dstPixelHeight));
                    byte[] bts = Get16Bytes((Image<L16>)im);
                    im.Dispose();
                    return bts;
                }
                else if (px == PixelFormat.Format24bppRgb)
                {
                    SixLabors.ImageSharp.Image im = ImageUtil.JoinRGB24(tiles, srcPixelExtent, new Extent(0, 0, dstPixelWidth, dstPixelHeight));
                    byte[] bts = GetRgb24Bytes((Image<Rgb24>)im);
                    im.Dispose();
                    return bts;
                }
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
                return null;
            }
            return null;
        }
        
        public byte[] GetRgb24Bytes(Image<Rgb24> image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgbBytes = new byte[width * height * 3]; // 3 bytes per pixel (RGB)

            int byteIndex = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Rgb24 pixel = image[x, y];
                    rgbBytes[byteIndex++] = pixel.R;
                    rgbBytes[byteIndex++] = pixel.G;
                    rgbBytes[byteIndex++] = pixel.B;
                }
            }

            return rgbBytes;
        }
        public byte[] Get16Bytes(Image<L16> image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] bytes = new byte[width * height * 2];

            int byteIndex = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    L16 pixel = image[x, y];
                    byte[] bts = BitConverter.GetBytes(pixel.PackedValue);
                    bytes[byteIndex++] = bts[0];
                    bytes[byteIndex++] = bts[1];
                }
            }

            return bytes;
        }

        public ITileSchema Schema { get; protected set; }

        public string Name { get; protected set; }

        public Attribution Attribution { get; protected set; }

        public IReadOnlyDictionary<string, object> ExternInfo { get; protected set; }

        public string Source { get; protected set; }

        public abstract IReadOnlyDictionary<string, byte[]> GetExternImages();

        #region IDisposable
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //_bgraCache.Dispose();
                }
                disposedValue = true;
            }
        }

        ~SlideSourceBase()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public interface ISlideSource : ITileSource, ISliceProvider, ISlideExternInfo
    {

    }

    /// <summary>
    /// </summary>
    public interface ISlideExternInfo
    {
        /// <summary>
        /// File path.
        /// </summary>
        string Source { get; }

        /// <summary>
        /// Extern info.
        /// </summary>
        IReadOnlyDictionary<string, object> ExternInfo { get; }

        /// <summary>
        /// Extern image.
        /// </summary>
        /// <returns></returns>
        IReadOnlyDictionary<string, byte[]> GetExternImages();
    }

    /// <summary>
    /// </summary>
    public interface ISliceProvider
    {
        /// <summary>
        /// um/pixel
        /// </summary>
        double MinUnitsPerPixel { get; }

        /// <summary>
        /// Get slice.
        /// </summary>
        /// <param name="sliceInfo">Slice info</param>
        /// <returns></returns>
        byte[] GetSlice(SliceInfo sliceInfo);
    }

    /// <summary>
    /// Slice info.
    /// </summary>
    public class SliceInfo
    {
        public SliceInfo() { }

        /// <summary>
        /// Create a world extent by pixel and resolution.
        /// </summary>
        /// <param name="xPixel">pixel x</param>
        /// <param name="yPixel">pixel y</param>
        /// <param name="widthPixel">pixel width</param>
        /// <param name="heightPixel">pixel height</param>
        /// <param name="unitsPerPixel">um/pixel</param>
        public SliceInfo(double xPixel, double yPixel, double widthPixel, double heightPixel, double unitsPerPixel)
        {
            Extent = new Extent(xPixel, yPixel, xPixel + widthPixel, yPixel + heightPixel).PixelToWorldInvertedY(unitsPerPixel);
            Resolution = unitsPerPixel;
        }

        /// <summary>
        /// um/pixel
        /// </summary>
        public double Resolution
        {
            get;
            set;
        } = 1;

        /// <summary>
        /// World extent.
        /// </summary>
        public Extent Extent
        {
            get;
            set;
        }
        public SliceParame Parame
        {
            get;
            set;
        } = new SliceParame();
    }

    public class SliceParame
    {
        /// <summary>
        /// Scale to width,default 0(no scale)
        /// /// </summary>
        public int DstPixelWidth { get; set; } = 0;

        /// <summary>
        /// Scale to height,default 0(no scale)
        /// </summary>
        public int DstPixelHeight { get; set; } = 0;

        /// <summary>
        /// Sample mode.
        /// </summary>
        public SampleMode SampleMode { get; set; } = SampleMode.Nearest;

        /// <summary>
        /// Image quality.
        /// </summary>
        public int? Quality { get; set; }
    }


    public enum SampleMode
    {
        /// <summary>
        /// Nearest.
        /// </summary>
        Nearest = 0,
        /// <summary>
        /// Nearest up.
        /// </summary>
        NearestUp,
        /// <summary>
        /// Nearest dwon.
        /// </summary>
        NearestDwon,
        /// <summary>
        /// Top.
        /// </summary>
        Top,
        /// <summary>
        /// Bottom.
        /// </summary>
        /// <remarks>
        /// maybe very slow, just for clearer images.
        /// </remarks>
        Bottom,
    }

    /// <summary>
    /// Image type.
    /// </summary>
    public enum ImageType : int
    {
        /// <summary>
        /// </summary>
        Label,

        /// <summary>
        /// </summary>
        Title,

        /// <summary>
        /// </summary>
        Preview,
    }

    public static class ExtentEx
    {
        /// <summary>
        /// Convert OSM world to pixel
        /// </summary>
        /// <param name="extent">world extent</param>
        /// <param name="unitsPerPixel">resolution,um/pixel</param>
        /// <returns></returns>
        public static Extent WorldToPixelInvertedY(this Extent extent, double unitsPerPixel)
        {
            return new Extent(extent.MinX / unitsPerPixel, -extent.MaxY / unitsPerPixel, extent.MaxX / unitsPerPixel, -extent.MinY / unitsPerPixel);
        }


        /// <summary>
        /// Convert pixel to OSM world.
        /// </summary>
        /// <param name="extent">pixel extent</param>
        /// <param name="unitsPerPixel">resolution,um/pixel</param>
        /// <returns></returns>
        public static Extent PixelToWorldInvertedY(this Extent extent, double unitsPerPixel)
        {
            return new Extent(extent.MinX * unitsPerPixel, -extent.MaxY * unitsPerPixel, extent.MaxX * unitsPerPixel, -extent.MinY * unitsPerPixel);
        }

        /// <summary>
        /// Convert double to int.
        /// </summary>
        /// <param name="extent"></param>
        /// <returns></returns>
        public static Extent ToIntegerExtent(this Extent extent)
        {
            return new Extent((int)Math.Round(extent.MinX), (int)Math.Round(extent.MinY), (int)Math.Round(extent.MaxX), (int)Math.Round(extent.MaxY));
        }
    }

    public static class ObjectEx
    {
        /// <summary>
        /// Get fields and properties
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static Dictionary<string, object> GetFieldsProperties(this object obj)
        {
            Dictionary<string, object> keys = new Dictionary<string, object>();
            foreach (var item in obj.GetType().GetFields())
            {
                keys.Add(item.Name, item.GetValue(obj));
            }
            foreach (var item in obj.GetType().GetProperties())
            {
                try
                {
                    if (item.GetIndexParameters().Any()) continue;
                    keys.Add(item.Name, item.GetValue(obj));
                }
                catch (Exception) { }
            }
            return keys;
        }
    }
}