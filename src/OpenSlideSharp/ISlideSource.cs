using AForge;
using Atk;
using BruTile;
using Gdk;
using Gtk;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using static OpenSlideGTK.OpenSlideImage;
using PixelFormat = AForge.PixelFormat;
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

        public TValue Get(Info ke)
        {
            foreach (LinkedListNode<(Info key, TValue value)> item in cacheMap.Values)
            {
                if (ke.Coordinate == item.Value.key.Coordinate && ke.Index == item.Value.key.Index)
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
        private Stitch stitch;
        SlideSourceBase source = null;
        public TileCache(SlideSourceBase source, int capacity = 100)
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
        public bool HasTile(Info inf)
        {
            byte[] data = cache.Get(inf);
            if (data != null)
            {
                return true;
            }
            return false;
        }
        public byte[] GetTileSync(Info inf)
        {
            byte[] data = cache.Get(inf);
            if (data != null)
            {
                return data;
            }
            byte[] tile = LoadTileSync(inf);
            if (tile != null)
                AddTile(inf, tile);
            return tile;
        }

        public void AddTile(Info tileId, byte[] tile)
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
                return await source.GetTileAsync(tf, tileId.Coordinate);
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
                if (!string.IsNullOrEmpty(OpenSlideBase.DetectVendor(source)))
                {
                    var osb = new OpenSlideBase(source, enableCache);
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
        public async Task<byte[]> GetTileAsync(BruTile.TileInfo tileInfo, ZCT coord)
        {
            if (tileInfo == null)
                return null;
            if (cache == null)
                cache = new TileCache(this);
            if (cache.HasTile(new Info(coord, tileInfo.Index, tileInfo.Extent, tileInfo.Index.Level)))
            {
                return await cache.GetTile(new Info(coord, tileInfo.Index, tileInfo.Extent, tileInfo.Index.Level));
            }
            var r = Schema.Resolutions[tileInfo.Index.Level].UnitsPerPixel;
            var tileWidth = Schema.Resolutions[tileInfo.Index.Level].TileWidth;
            var tileHeight = Schema.Resolutions[tileInfo.Index.Level].TileHeight;
            var curLevelOffsetXPixel = tileInfo.Extent.MinX / Schema.Resolutions[tileInfo.Index.Level].UnitsPerPixel;
            var curLevelOffsetYPixel = -tileInfo.Extent.MaxY / Schema.Resolutions[tileInfo.Index.Level].UnitsPerPixel;
            var curTileWidth = (int)(tileInfo.Extent.MaxX > Schema.Extent.Width ? tileWidth - (tileInfo.Extent.MaxX - Schema.Extent.Width) / r : tileWidth);
            var curTileHeight = (int)(-tileInfo.Extent.MinY > Schema.Extent.Height ? tileHeight - (-tileInfo.Extent.MinY - Schema.Extent.Height) / r : tileHeight);

            var bgraData = Image.ReadRegion(tileInfo.Index.Level, (long)curLevelOffsetXPixel, (long)curLevelOffsetYPixel, curTileWidth, curTileHeight);
            cache.AddTile(new Info(coord, tileInfo.Index, tileInfo.Extent, tileInfo.Index.Level), bgraData);
            return bgraData;
        }
        public double MinUnitsPerPixel { get; protected set; }
        public static byte[] LastSlice;
        public static Extent destExtent;
        public static Extent sourceExtent;
        public static double curUnitsPerPixel = 1;
        public OpenSlideImage Image;
        //private int level;
        //private ZCT coord;
        public static bool UseVips = true;
        public static bool useGPU = true;
        public Stitch stitch;
        private PixelFormat px;
        public AForge.Size PyramidalSize;
        public PointD PyramidalOrigin;
        public PixelFormat PixelFormat
        {
            get { return px; }
        }

        private int GetOptimalBatchSize()
        {

            // Small viewport (< 800x600): fetch all tiles at once
            if (PyramidalSize.Width < 800 && PyramidalSize.Height < 600)
                return 100;

            // Medium viewport (< 1920x1080): moderate batching
            if (PyramidalSize.Width < 1920 && PyramidalSize.Height < 1080)
                return 50;

            // Large viewport (fullscreen): aggressive batching
            // Process visible center tiles first, then outer tiles
            return 25;
        }

        private async Task<byte[]> FetchSingleTileAsync(TileInfo tile, int level)
        {
            // Your existing tile fetch logic here
            // This should use the cache and only fetch if not cached
            try
            {
                byte[] tileData = this.GetTile(tile);
                // Process tile data...
                return tileData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching tile: {ex.Message}");
            }
            return null;
        }

        // ============================================================================
        // OPTIMIZATION 4: Smarter pyramid level selection
        // ============================================================================

        private int GetOptimalPyramidLevel(double resolution, IDictionary<int, Resolution> ress)
        {
            // This should use your existing TileUtil.GetLevel() logic
            // but with awareness of viewport size
            // For small viewports, can use higher detail levels
            // For large viewports, might need to drop to lower detail to maintain performance
            int level = TileUtil.GetLevel(ress, resolution);
            // Adaptive level adjustment based on viewport size
            if (PyramidalSize.Width > 2560 || PyramidalSize.Height > 1440)
            {
                // 4K or larger - consider dropping one level for performance
                level = Math.Min(level + 1, ress.Count - 1);
            }
            return level;
        }

        /// <summary>
        /// Fetches tiles in priority order: center viewport first, then edges.
        /// Ensures visible content appears before prefetch content.
        /// </summary>
        public async Task FetchTilesAsync(List<BruTile.TileInfo> tiles, int level, ZCT coordinate)
        {
            if (tiles == null || tiles.Count == 0)
                return;

            // --------------------------------------------------------------------
            // 1. Calculate viewport center (base-resolution coordinates)
            // --------------------------------------------------------------------
            double centerX = PyramidalOrigin.X + (PyramidalSize.Width * 0.5);
            double centerY = PyramidalOrigin.Y + (PyramidalSize.Height * 0.5);

            // --------------------------------------------------------------------
            // 2. Sort tiles by squared distance (avoid sqrt)
            // --------------------------------------------------------------------
            var prioritizedTiles = tiles
                .OrderBy(tile =>
                {
                    double dx = tile.Extent.CenterX - centerX;
                    double dy = tile.Extent.CenterY - centerY;
                    return (dx * dx) + (dy * dy);
                })
                .ToList();

            // --------------------------------------------------------------------
            // 3. Fetch tiles in batches
            // --------------------------------------------------------------------
            int batchSize = GetOptimalBatchSize();

            for (int i = 0; i < prioritizedTiles.Count; i += batchSize)
            {
                var batch = prioritizedTiles.Skip(i).Take(batchSize).ToList();

                // Parallel fetch within batch
                byte[][] results = await Task.WhenAll(
                    batch.Select(tile => FetchSingleTileAsync(tile, level))
                );

                // ----------------------------------------------------------------
                // 4. Insert into appropriate cache
                // ----------------------------------------------------------------
                for (int j = 0; j < batch.Count; j++)
                {
                    var tile = batch[j];
                    var data = results[j];

                    if (data == null)
                        continue;
                    var info = new Info(
                        coordinate,
                        tile.Index,
                        this.Schema.Extent,
                        level
                    );
                    TileInfo tf = new TileInfo();
                    tf.Index = tile.Index;
                    tf.Extent = tile.Extent;
                    stitch.AddTile(new Stitch.GpuTile(tf, data));
                }
            }
        }
        public async Task<byte[]> GetSliceAsync(SliceInfo sliceInfo, int level, ZCT coordinate)
        {
            if (stitch == null)
                stitch = new Stitch();
            if (cache == null)
                cache = new TileCache(this, 200);
            var curLevel = this.Schema.Resolutions[level];
            var curUnitsPerPixel = sliceInfo.Resolution;
            var tileInfos = Schema.GetTileInfos(sliceInfo.Extent.WorldToPixelInvertedY(curUnitsPerPixel), curLevel.Level);
            if(tileInfos.Count()==0)
                tileInfos = Schema.GetTileInfos(sliceInfo.Extent, curLevel.Level);
            List<Tuple<Extent, byte[]>> tiles = new List<Tuple<Extent, byte[]>>();
            await this.FetchTilesAsync(tileInfos.ToList(), level, coordinate);
            foreach (BruTile.TileInfo t in tileInfos)
            {
                byte[] c = cache.GetTileSync(new Info(coordinate, t.Index, t.Extent, level));
                if (c != null)
                {
                    if (useGPU)
                    {
                        TileInfo tileInfo = new TileInfo();
                        tileInfo.Extent = t.Extent;
                        tileInfo.Index = t.Index;
                        stitch.AddTile(new Stitch.GpuTile(tileInfo, c));
                        tiles.Add(Tuple.Create(t.Extent, c));
                    }
                    else
                        tiles.Add(Tuple.Create(t.Extent.WorldToPixelInvertedY(curUnitsPerPixel), c));
                }
            }

            var srcPixelExtent = sliceInfo.Extent;
            var dstPixelExtent = sliceInfo.Extent.WorldToPixelInvertedY(sliceInfo.Resolution);
            var dstPixelHeight = sliceInfo.Parame.DstPixelHeight > 0 ? sliceInfo.Parame.DstPixelHeight : dstPixelExtent.Height;
            var dstPixelWidth = sliceInfo.Parame.DstPixelWidth > 0 ? sliceInfo.Parame.DstPixelWidth : dstPixelExtent.Width;
            destExtent = new Extent(0, 0, dstPixelWidth, dstPixelHeight);
            sourceExtent = srcPixelExtent;
            if (useGPU && stitch.gpuTiles.Count > 0)
            {
                try
                {
                    if (tileInfos.Count() > 0 && stitch.initialized)
                        return stitch.StitchImages(tileInfos.ToList(), (int)Math.Round(dstPixelWidth), (int)Math.Round(dstPixelHeight), Math.Round(srcPixelExtent.MinX), Math.Round(srcPixelExtent.MinY), curUnitsPerPixel);
                    else
                    {
                        return null;
                    }
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
                    if (px == PixelFormat.Format32bppArgb)
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
            return null;
        }

        public byte[] GetSlice(SliceInfo sliceInfo, ZCT coord, int level)
        {
            if (stitch == null)
                stitch = new Stitch();
            if (cache == null)
                cache = new TileCache(this, 200);
            var curLevel = this.Schema.Resolutions[level];
            var curUnitsPerPixel = sliceInfo.Resolution;
            var tileInfos = Schema.GetTileInfos(sliceInfo.Extent, curLevel.Level);
            List<Tuple<Extent, byte[]>> tiles = new List<Tuple<Extent, byte[]>>();
            this.FetchTilesAsync(tileInfos.ToList(), level, coord).Wait();
            foreach (BruTile.TileInfo t in tileInfos)
            {
                byte[] c = cache.GetTileSync(new Info(coord, t.Index, t.Extent, level));
                if (c != null)
                {
                    if (useGPU)
                    {
                        TileInfo tileInfo = new TileInfo();
                        tileInfo.Extent = t.Extent.WorldToPixelInvertedY(curUnitsPerPixel);
                        tileInfo.Index = t.Index;
                        stitch.AddTile(new Stitch.GpuTile(tileInfo, c));
                        tiles.Add(Tuple.Create(t.Extent, c));
                    }
                    else
                        tiles.Add(Tuple.Create(t.Extent.WorldToPixelInvertedY(curUnitsPerPixel), c));
                }
            }

            var srcPixelExtent = sliceInfo.Extent;
            var dstPixelExtent = sliceInfo.Extent.WorldToPixelInvertedY(sliceInfo.Resolution);
            var dstPixelHeight = sliceInfo.Parame.DstPixelHeight > 0 ? sliceInfo.Parame.DstPixelHeight : dstPixelExtent.Height;
            var dstPixelWidth = sliceInfo.Parame.DstPixelWidth > 0 ? sliceInfo.Parame.DstPixelWidth : dstPixelExtent.Width;
            destExtent = new Extent(0, 0, dstPixelWidth, dstPixelHeight);
            sourceExtent = srcPixelExtent;
            if (useGPU && stitch.gpuTiles.Count > 0)
            {
                try
                {
                    if (tileInfos.Count() > 0 && stitch.initialized)
                        return stitch.StitchImages(tileInfos.ToList(), (int)Math.Round(dstPixelWidth), (int)Math.Round(dstPixelHeight), Math.Round(srcPixelExtent.MinX), Math.Round(srcPixelExtent.MinY), curUnitsPerPixel);
                    else
                    {
                        return null;
                    }
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
                    if (px == PixelFormat.Format32bppArgb)
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
            return null;
        }

        /*
        public byte[] GetSlice(SliceInfo sliceInfo)
        {
            if (stitch == null)
                stitch = new Stitch();
            if (cache == null)
                cache = new TileCache(this, 200);
            var curLevel = this.Schema.Resolutions[this.level];
            var curUnitsPerPixel = sliceInfo.Resolution;
            var tileInfos = Schema.GetTileInfos(sliceInfo.Extent.WorldToPixelInvertedY(curUnitsPerPixel), curLevel.Level);
            List<Tuple<Extent, byte[]>> tiles = new List<Tuple<Extent, byte[]>>();
            this.FetchTilesAsync(tileInfos.ToList(), this.level, coord).Wait();
            foreach (BruTile.TileInfo t in tileInfos)
            {
                byte[] c = cache.GetTileSync(new Info(coord, t.Index, t.Extent, level));
                if (c != null)
                {
                    if (useGPU)
                    {
                        TileInfo tileInfo = new TileInfo();
                        tileInfo.Extent = t.Extent.WorldToPixelInvertedY(curUnitsPerPixel);
                        tileInfo.Index = t.Index;
                        stitch.AddTile(new Stitch.GpuTile(tileInfo, c));
                        tiles.Add(Tuple.Create(t.Extent, c));
                    }
                    else
                        tiles.Add(Tuple.Create(t.Extent.WorldToPixelInvertedY(curUnitsPerPixel), c));
                }
            }

            // Upload tiles to GPU stitch before rendering
            foreach (BruTile.TileInfo t in tileInfos)
            {
                if (!stitch.HasTile(t))
                {
                    TileInformation tf = new TileInformation(t.Index, t.Extent, coord);
                    byte[] tileData = cache.GetTile(new Info(coord, t.Index,t.Extent, t.Index.Level)).Result;
                    if (tileData != null)
                    {
                        var gpuTile = new Stitch.GpuTile(t, tileData);
                        stitch.AddTile(gpuTile);
                    }
                }
            }

            var srcPixelExtent = sliceInfo.Extent;
            var dstPixelExtent = sliceInfo.Extent.WorldToPixelInvertedY(sliceInfo.Resolution);
            var dstPixelHeight = sliceInfo.Parame.DstPixelHeight > 0 ? sliceInfo.Parame.DstPixelHeight : dstPixelExtent.Height;
            var dstPixelWidth = sliceInfo.Parame.DstPixelWidth > 0 ? sliceInfo.Parame.DstPixelWidth : dstPixelExtent.Width;
            destExtent = new Extent(0, 0, dstPixelWidth, dstPixelHeight);
            sourceExtent = srcPixelExtent;
            if (useGPU && stitch.gpuTiles.Count > 0)
            {
                try
                {
                    stitch.Initialize(stitch.tileCopy);
                    if (tileInfos.Count() > 0 && stitch.initialized)
                        return stitch.StitchImages(tileInfos.ToList(), (int)Math.Round(dstPixelWidth), (int)Math.Round(dstPixelHeight), Math.Round(srcPixelExtent.MinX), Math.Round(srcPixelExtent.MinY), curUnitsPerPixel, stitch.tileCopy);
                    else
                    {
                        return null;
                    }
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
                    if (px == PixelFormat.Format32bppArgb)
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
            return null;
        }
        */
        public byte[] GetRgb24Bytes(Image<Rgb24> image)
        {
            if (image == null)
                return new byte[1];
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
        public TileCache cache;

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
        byte[] GetSlice(SliceInfo sliceInfo, ZCT coord, int level);
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