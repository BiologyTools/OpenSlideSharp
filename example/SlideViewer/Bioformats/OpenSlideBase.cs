using BruTile;
using BruTile.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenSlideGTK;
using BioGTK;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SlideViewer
{
    public class OpenSlideBase : SlideSourceBase
    {
        //public readonly OpenSlideImage SlideImage;
        private readonly bool _enableCache;
        private readonly MemoryCache<Image> _tileCache = new MemoryCache<Image>();
        private BioImage image;
        public OpenSlideBase(string source, bool enableCache = true)
        {
            Source = source;
            _enableCache = enableCache;
            image = BioImage.OpenFile(source);
            double minUnitsPerPixel;
            if (image.PhysicalSizeX < image.PhysicalSizeY) minUnitsPerPixel = image.PhysicalSizeX; else minUnitsPerPixel = image.PhysicalSizeY;
            MinUnitsPerPixel = UseRealResolution ? minUnitsPerPixel : 1;
            if (MinUnitsPerPixel <= 0) MinUnitsPerPixel = 1;
            var height = image.Resolutions[0].SizeY * MinUnitsPerPixel;
            var width = image.Resolutions[0].SizeX * MinUnitsPerPixel;
            //ExternInfo = GetInfo();
            Schema = new TileSchema
            {
                YAxis = YAxis.OSM,
                Format = "jpg",
                Extent = new Extent(0, -height, width, 0),
                OriginX = 0,
                OriginY = 0,
            };
            InitResolutions(Schema.Resolutions, 256, 256);
        }

        public static string DetectVendor(string source)
        {
            return OpenSlideImage.DetectVendor(source);
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
        public override Image<Rgb24> GetTile(TileInfo tileInfo)
        {
            if (tileInfo == null)
                return null;
            if (_enableCache && _tileCache.Find(tileInfo.Index) is Image<Rgb24> output)
                return output;
            var r = Schema.Resolutions[tileInfo.Index.Level].UnitsPerPixel;
            var tileWidth = Schema.Resolutions[tileInfo.Index.Level].TileWidth;
            var tileHeight = Schema.Resolutions[tileInfo.Index.Level].TileHeight;
            var curLevelOffsetXPixel = tileInfo.Extent.MinX / MinUnitsPerPixel;
            var curLevelOffsetYPixel = -tileInfo.Extent.MaxY / MinUnitsPerPixel;
            var curTileWidth = (int)(tileInfo.Extent.MaxX > Schema.Extent.Width ? tileWidth - (tileInfo.Extent.MaxX - Schema.Extent.Width) / r : tileWidth);
            var curTileHeight = (int)(-tileInfo.Extent.MinY > Schema.Extent.Height ? tileHeight - (-tileInfo.Extent.MinY - Schema.Extent.Height) / r : tileHeight);
            byte[] bgraData;
            try
            {
                //bgraData = BioImage.OpenOME(image.file, tileInfo.Index.Level,false,false,true,(int)curLevelOffsetXPixel,(int)curLevelOffsetYPixel,curTileWidth,curTileHeight).Buffers[0].RGBBytes;
                bgraData = BioImage.GetTile(image, new AForge.ZCT(), tileInfo.Index.Level, (int)curLevelOffsetXPixel, (int)curLevelOffsetYPixel, curTileWidth, curTileHeight).RGBBytes;
            }
            catch (Exception e)
            {
                throw;
            }
            Image<Rgb24> bm = CreateImageFromRgbaData(bgraData,curTileWidth,curTileHeight);
            if (_enableCache && bgraData != null)
                _tileCache.Add(tileInfo.Index, bm);
            return bm;
        }

        public override async Task<byte[]> GetTileAsync(TileInfo tileInfo)
        {
            return null;
        }
        /*
        protected IReadOnlyDictionary<string, object> GetInfo()
        {
            Dictionary<string, object> keys = SlideImage.GetFieldsProperties().ToDictionary(_ => _.Key, _ => _.Value);
            foreach (var item in SlideImage.GetProperties())
            {
                keys.Add(item.Key, item.Value);
            }
            return keys;
        }
        */
        protected void InitResolutions(IDictionary<int, BruTile.Resolution> resolutions, int tileWidth, int tileHeight)
        {
            for (int i = 0; i < image.Resolutions.Count; i++)
            {
                /*
                bool useInternalWidth = int.TryParse(ExternInfo.TryGetValue($"openslide.level[{i}].tile-width", out var _w) ? (string)_w : null, out var w) && w >= tileWidth;
                bool useInternalHeight = int.TryParse(ExternInfo.TryGetValue($"openslide.level[{i}].tile-height", out var _h) ? (string)_h : null, out var h) && h >= tileHeight;

                bool useInternalSize = useInternalHeight && useInternalWidth;
                */
                //resolutions.Add(i, new BruTile.Resolution(i, MinUnitsPerPixel * SlideImage.GetLevelDownsample(i), tw, th));
                resolutions.Add(i, new BruTile.Resolution(i, MinUnitsPerPixel * i, 256, 256));
            }
        }

        #region IDisposable
        private bool disposedValue;
        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    image.Dispose();
                }
                disposedValue = true;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
