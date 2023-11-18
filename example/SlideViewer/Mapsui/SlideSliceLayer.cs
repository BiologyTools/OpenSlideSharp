using BruTile;
using Mapsui;
using Mapsui.Fetcher;
using Mapsui.Layers;
using Mapsui.Providers;
using Mapsui.Styles;
using OpenSlideSharp.BruTile;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SlideLibrary.Demo
{
    /// <summary>
    /// Slide slice layer
    /// </summary>
    public class SlideSliceLayer : BaseLayer
    {
        private ISlideSource _slideSource;
        private double _lastResolution = 0;
        private IEnumerable<IFeature> _lastFeatures;
        private Extent _lastExtent;

        public SlideSliceLayer(ISlideSource slideSource) : base()
        {
            _slideSource = slideSource;
            Name = "SliceLayer";
            //Envelope = slideSource.Schema.Extent.ToBoundingBox();
            this.Extent = new MRect(slideSource.Schema.Extent.MinX, slideSource.Schema.Extent.MinY, slideSource.Schema.Extent.MaxX, slideSource.Schema.Extent.MaxY);
        }

        public override IEnumerable<IFeature> GetFeatures(MRect box, double resolution)
        {
            if (box is null) return Enumerable.Empty<IFeature>();
            // Repaint on debouncing, resolution changed(zoom map) or box changed(resize control) .
            MRect rect = new MRect(_lastExtent.MinX, _lastExtent.MinY, _lastExtent.MaxX,_lastExtent.MaxY);
            if (rect.Centroid.Distance(box.Centroid) > 2 * resolution || _lastResolution != resolution || _lastExtent.Width != box.Width || _lastExtent.Height != box.Height)
            {
                _lastExtent = new Extent(box.MinX, box.MinY, box.MaxX, box.MaxY);//box.ToExtent();
                _lastResolution = resolution;
                MRect box2 = box.Grow(SymbolStyle.DefaultWidth * 2.0 * resolution, SymbolStyle.DefaultHeight * 2.0 * resolution);
                var sliceInfo = new SliceInfo() { Extent = new Extent(box2.MinX,box2.MinY,box2.MaxX,box2.MaxY), Resolution = resolution };
                var bytes = _slideSource.GetSlice(sliceInfo);
                if (bytes != null && _lastFeatures.FirstOrDefault() is IFeature feature)
                {
                    feature = new RasterFeature(new MRaster(bytes, box2));
                }
            }
            return _lastFeatures;
        }
        /*
        public override void RefreshData(MRect extent, double resolution, ChangeType changeType)
        {
            OnDataChanged(new DataChangedEventArgs());
        }
        */
    }
}
