using BruTile;
using Gdk;
using Mapsui;
using Mapsui.Fetcher;
using Mapsui.Layers;
using Mapsui.Providers;
using Mapsui.Styles;
using OpenSlideSharp.BruTile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AForge;
namespace SlideLibrary.Demo
{
    /// <summary>
    /// Slide slice layer
    /// </summary>
    public class SlideSliceLayer : BaseLayer
    {
        private ISlideSource _slideSource;
        private double _lastResolution = 0;
        private List<IFeature> _lastFeatures = new List<IFeature>();// = new Features(new[] { new Feature() });
        private Extent _lastExtent;
        public Pixbuf buffer;
        //Random random = new Random();
        public SlideSliceLayer(ISlideSource slideSource) : base()
        {
            _slideSource = slideSource;
            Name = "SliceLayer";
            //Envelope = slideSource.Schema.Extent.ToBoundingBox();
            this.Extent = new MRect(slideSource.Schema.Extent.MinX, slideSource.Schema.Extent.MinY, slideSource.Schema.Extent.MaxX, slideSource.Schema.Extent.MaxY);
        }

        public override IEnumerable<IFeature> GetFeatures(MRect box, double resolution)
        {
            if (box is null)
            {
                Console.WriteLine("MRect is null for GetFeatures.");
                return Enumerable.Empty<IFeature>();
            }
            _lastExtent = new Extent(box.MinX, box.MinY, box.MaxX, box.MaxY);//box.ToExtent();
            _lastResolution = resolution;
            MRect box2 = box.Grow(SymbolStyle.DefaultWidth * 2.0 * resolution, SymbolStyle.DefaultHeight * 2.0 * resolution);
            var sliceInfo = new SliceInfo() { Extent = new Extent(box2.MinX, box2.MinY, box2.MaxX, box2.MaxY), Resolution = resolution };
            var bytes = _slideSource.GetSlice(sliceInfo);
            buffer = new Pixbuf(bytes,false,8,(int)sliceInfo.Extent.Width,(int)sliceInfo.Extent.Height, (int)sliceInfo.Extent.Width*3);
            return _lastFeatures;
        }
        /*
        public override void RefreshData(FetchInfo info)
        {
            OnDataChanged(new DataChangedEventArgs());
        }
        */
    }
}
