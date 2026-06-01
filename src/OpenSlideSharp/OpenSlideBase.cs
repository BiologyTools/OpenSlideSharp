using BruTile;
using BruTile.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenSlideGTK
{
    public class OpenSlideBase : SlideSourceBase
    {
        public readonly OpenSlideImage SlideImage;
        private readonly bool _enableCache;
        private readonly MemoryCache<byte[]> _tileCache = new MemoryCache<byte[]>();
        private readonly int _nativeLevelCount;

        public OpenSlideBase(string source, bool enableCache = true)
        {
            Source = source;
            _enableCache = enableCache;
            SlideImage = OpenSlideImage.Open(source);
            _nativeLevelCount = SlideImage.LevelCount;
            var minUnitsPerPixel = SlideImage.MicronsPerPixelX ?? SlideImage.MicronsPerPixelY ?? 1;
            MinUnitsPerPixel = UseRealResolution ? minUnitsPerPixel : 1;
            if (MinUnitsPerPixel <= 0) MinUnitsPerPixel = 1;
            var height = SlideImage.Dimensions.Height;
            var width = SlideImage.Dimensions.Width;
            ExternInfo = GetInfo();
            Schema = new TileSchema
            {
                YAxis = YAxis.OSM,
                Format = "jpg",
                Extent = new Extent(0, -height * MinUnitsPerPixel, width * MinUnitsPerPixel, 0),
                OriginX = 0,
                OriginY = 0,
            };
            InitResolutions(Schema.Resolutions, 256, 256);
            AppendSyntheticPyramidLevels(256, 256);
        }

        public static string DetectVendor(string source)
        {
            return OpenSlideImage.DetectVendor(source);
        }

        private (int Width, int Height) GetSyntheticLevelDimension(int level)
        {
            if (level < _nativeLevelCount)
            {
                var dim = SlideImage.GetLevelDimension(level);
                return ((int)dim.Width, (int)dim.Height);
            }

            double downsample = GetSyntheticLevelDownsample(level);
            int width = Math.Max(1, (int)Math.Ceiling(SlideImage.Dimensions.Width / downsample));
            int height = Math.Max(1, (int)Math.Ceiling(SlideImage.Dimensions.Height / downsample));
            return (width, height);
        }

        private double GetSyntheticLevelDownsample(int level)
        {
            if (level < _nativeLevelCount)
                return SlideImage.GetLevelDownsample(level);

            double nativeDownsample = SlideImage.GetLevelDownsample(_nativeLevelCount - 1);
            int syntheticStep = level - _nativeLevelCount + 1;
            return nativeDownsample * Math.Pow(2.0, syntheticStep);
        }

        
        public override IReadOnlyDictionary<string, byte[]> GetExternImages()
        {
            throw new NotImplementedException();
            /*
            Dictionary<string, byte[]> images = new Dictionary<string, byte[]>();
            var r = Math.Max(Schema.Extent.Height, Schema.Extent.Width) / 512;
            images.Add("preview", GetSlice(new SliceInfo { Extent = Schema.Extent, Resolution = r }));
            foreach (var item in SlideImage.GetAssociatedImages())
            {
                var dim = item.Value.Dimensions;
                images.Add(item.Key, ImageUtil.GetJpeg(item.Value.Data, 4, 4 * (int)dim.Width, (int)dim.Width, (int)dim.Height));
            }
            return images;
            */
        }
        private static Image<Rgb24> CreateImageFromRgbaData(byte[] rgbaData, int width, int height)
        {
            Image<Rgb24> image = new Image<Rgb24>(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width + x) * 4;
                    byte r = rgbaData[index];
                    byte g = rgbaData[index + 1];
                    byte b = rgbaData[index + 2];
                    // byte a = rgbaData[index + 3]; // Alpha channel, not used in Rgb24

                    image[x, y] = new Rgb24(r, g, b);
                }
            }

            return image;
        }
        public override byte[] GetTile(TileInfo tileInfo)
        {
            if (tileInfo == null)
                return null;
            if (_enableCache && _tileCache.Find(tileInfo.Index) is byte[] output)
                return output;
            
            var tileWidth = Schema.Resolutions[tileInfo.Index.Level].TileWidth;
            var tileHeight = Schema.Resolutions[tileInfo.Index.Level].TileHeight;

            if (_nativeLevelCount > 0 && tileInfo.Index.Level >= _nativeLevelCount)
            {
                int sourceLevel = _nativeLevelCount - 1;
                var sourceDim = SlideImage.GetLevelDimension(sourceLevel);
                var targetDim = GetSyntheticLevelDimension(tileInfo.Index.Level);
                double sourceDownsample = SlideImage.GetLevelDownsample(sourceLevel);
                double targetDownsample = GetSyntheticLevelDownsample(tileInfo.Index.Level);
                double sourceScale = targetDownsample / Math.Max(1e-9, sourceDownsample);
                int targetX = tileInfo.Index.Col * tileWidth;
                int targetY = tileInfo.Index.Row * tileHeight;
                int overlapW = Math.Max(0, Math.Min(tileWidth, targetDim.Width - targetX));
                int overlapH = Math.Max(0, Math.Min(tileHeight, targetDim.Height - targetY));
                byte[] syntheticTile = new byte[tileWidth * tileHeight * 4];

                if (overlapW <= 0 || overlapH <= 0)
                    return syntheticTile;

                int targetX2 = Math.Min(targetDim.Width, targetX + tileWidth);
                int targetY2 = Math.Min(targetDim.Height, targetY + tileHeight);
                long sourceX = Math.Max(0, (long)Math.Floor(targetX * sourceScale));
                long sourceY = Math.Max(0, (long)Math.Floor(targetY * sourceScale));
                long sourceX2 = Math.Max(sourceX + 1, (long)Math.Ceiling(targetX2 * sourceScale));
                long sourceY2 = Math.Max(sourceY + 1, (long)Math.Ceiling(targetY2 * sourceScale));
                int sourceTileWidth = Math.Max(1, (int)(sourceX2 - sourceX));
                int sourceTileHeight = Math.Max(1, (int)(sourceY2 - sourceY));
                long level0SourceX = (long)Math.Round(sourceX * sourceDownsample);
                long level0SourceY = (long)Math.Round(sourceY * sourceDownsample);

                if (sourceX >= sourceDim.Width || sourceY >= sourceDim.Height)
                    return syntheticTile;

                if (sourceX + sourceTileWidth > sourceDim.Width)
                    sourceTileWidth = Math.Max(1, (int)(sourceDim.Width - sourceX));
                if (sourceY + sourceTileHeight > sourceDim.Height)
                    sourceTileHeight = Math.Max(1, (int)(sourceDim.Height - sourceY));

                var sourceData = SlideImage.ReadRegion(
                    sourceLevel,
                    level0SourceX,
                    level0SourceY,
                    sourceTileWidth,
                    sourceTileHeight);

                if (sourceData == null || sourceData.Length == 0)
                    return syntheticTile;

                try
                {
                    var loaded = global::SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(sourceData, sourceTileWidth, sourceTileHeight);
                    loaded.Mutate(x => x.Resize(overlapW, overlapH));
                    byte[] resized = new byte[overlapW * overlapH * 4];
                    loaded.CopyPixelDataTo(resized);
                    for (int row = 0; row < overlapH; row++)
                    {
                        int srcOffset = row * overlapW * 4;
                        int dstOffset = row * tileWidth * 4;
                        Buffer.BlockCopy(resized, srcOffset, syntheticTile, dstOffset, overlapW * 4);
                    }
                    return syntheticTile;
                }
                catch
                {
                    return syntheticTile;
                }
            }

            // OpenSlide.ReadRegion expects level-0 reference coordinates. Derive
            // the origin from the tile grid index so the read position stays aligned
            // with the requested tile regardless of extent representation.
            var downsample = SlideImage.GetLevelDownsample(tileInfo.Index.Level);
            var curLevelOffsetXPixel = (long)Math.Round(tileInfo.Index.Col * tileWidth * downsample);
            var curLevelOffsetYPixel = (long)Math.Round(tileInfo.Index.Row * tileHeight * downsample);

            var bgraData = SlideImage.ReadRegion(
                tileInfo.Index.Level,
                curLevelOffsetXPixel,
                curLevelOffsetYPixel,
                tileWidth,
                tileHeight);
            //We check to see if the data is valid.
            if (bgraData.Length != tileWidth * tileHeight * 4)
                return null;
            if (_enableCache && bgraData != null)
                _tileCache.Add(tileInfo.Index, bgraData);
            return bgraData;
        }
        public static byte[] ConvertRgbaToRgb(byte[] rgbaArray)
        {
            // Initialize a new byte array for RGB24 format
            byte[] rgbArray = new byte[(rgbaArray.Length / 4) * 3];

            for (int i = 0, j = 0; i < rgbaArray.Length; i += 4, j += 3)
            {
                // Copy the R, G, B values, skip the A value
                rgbArray[j] = rgbaArray[i + 2];     // B
                rgbArray[j + 1] = rgbaArray[i + 1]; // G
                rgbArray[j + 2] = rgbaArray[i]; // R
            }

            return rgbArray;
        }

        public async Task<byte[]> GetTileAsync(TileInfo tileInfo)
        {
            return GetTile(tileInfo);
        }

        protected IReadOnlyDictionary<string, object> GetInfo()
        {
            Dictionary<string, object> keys = SlideImage.GetFieldsProperties().ToDictionary(_ => _.Key, _ => _.Value);
            foreach (var item in SlideImage.GetProperties())
            {
                keys.Add(item.Key, item.Value);
            }
            return keys;
        }

        protected void InitResolutions(IDictionary<int, Resolution> resolutions, int tileWidth, int tileHeight)
        {
            for (int i = 0; i < SlideImage.LevelCount; i++)
            {
                bool useInternalWidth = int.TryParse(ExternInfo.TryGetValue($"openslide.level[{i}].tile-width", out var _w) ? (string)_w : null, out var w) && w >= tileWidth;
                bool useInternalHeight = int.TryParse(ExternInfo.TryGetValue($"openslide.level[{i}].tile-height", out var _h) ? (string)_h : null, out var h) && h >= tileHeight;

                bool useInternalSize = useInternalHeight && useInternalWidth;
                var tw = useInternalSize ? w : tileWidth;
                var th = useInternalSize ? h : tileHeight;
                resolutions.Add(i, new Resolution(i, MinUnitsPerPixel * SlideImage.GetLevelDownsample(i), tw, th));
            }
        }

        private void AppendSyntheticPyramidLevels(int tileWidth, int tileHeight)
        {
            if (_nativeLevelCount <= 0)
                return;

            int level = _nativeLevelCount;
            while (true)
            {
                var dim = GetSyntheticLevelDimension(level);
                if (dim.Width <= 1 && dim.Height <= 1)
                {
                    double unitsPerPixel = MinUnitsPerPixel * GetSyntheticLevelDownsample(level);
                    Schema.Resolutions[level] = new BruTile.Resolution(level, unitsPerPixel, tileWidth, tileHeight);
                    break;
                }

                double levelDownsample = MinUnitsPerPixel * GetSyntheticLevelDownsample(level);
                Schema.Resolutions[level] = new BruTile.Resolution(level, levelDownsample, tileWidth, tileHeight);
                level++;
            }
        }

        private void AppendSyntheticPyramidLevelsLegacy(int tileWidth, int tileHeight)
        {
            if (_nativeLevelCount <= 0)
                return;

            int level = _nativeLevelCount;
            int width = (int)SlideImage.GetLevelDimension(_nativeLevelCount - 1).Width;
            int height = (int)SlideImage.GetLevelDimension(_nativeLevelCount - 1).Height;
            double unitsPerPixel = Schema.Resolutions[_nativeLevelCount - 1].UnitsPerPixel;

            while (width > 1 || height > 1)
            {
                width = Math.Max(1, (int)Math.Ceiling(width / 2.0));
                height = Math.Max(1, (int)Math.Ceiling(height / 2.0));
                unitsPerPixel *= 2.0;
                Schema.Resolutions[level] = new BruTile.Resolution(level, unitsPerPixel, tileWidth, tileHeight);
                level++;
                if (width == 1 && height == 1)
                    break;
            }
        }

        #region IDisposable
        private bool disposedValue;
        public TileCache cache;

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    SlideImage.Dispose();
                }
                disposedValue = true;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}

