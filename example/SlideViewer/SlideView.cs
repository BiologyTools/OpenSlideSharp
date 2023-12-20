using Gtk;
using Gdk;
using System;
using System.Collections.Generic;
using System.Linq;
using Point = Mapsui.MPoint;
using System.IO;
using Mapsui;
using Mapsui.Extensions;
using BruTile.Predefined;
using Mapsui.Tiling.Layers;
using Mapsui.Tiling.Fetcher;
using OpenSlideGTK;
using BioGTK;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

namespace SlideViewer
{
    public class SlideView : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private DrawingArea pictureBox;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        public static SlideView Create(string file)
        {
            BioImage.Initialize();
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/SlideView.glade", FileMode.Open));
            SlideView v = new SlideView(builder, builder.GetObject("slideView").Handle,file);
            return v;
        }
        public static SlideView Create()
        {
            BioImage.Initialize();
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/SlideView.glade", FileMode.Open));
            SlideView v = new SlideView(builder, builder.GetObject("slideView").Handle, "");
            return v;
        }

        protected SlideView(Builder builder, IntPtr handle,string file) : base(handle)
        {
            if (file == "")
            {
                Gtk.FileChooserDialog filechooser =
            new Gtk.FileChooserDialog("Pick Slide Image to Open with OpenSlide",
                this,
                FileChooserAction.Open,
                "Cancel", ResponseType.Cancel,
                "Save", ResponseType.Accept);
                if (filechooser.Run() != (int)ResponseType.Accept)
                    return;
                filechooser.Hide();
                file = filechooser.Filename;
            }
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
            Initialize(file);
        }
        #endregion

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            pictureBox.MotionNotifyEvent += ImageView_MotionNotifyEvent;
            pictureBox.ButtonPressEvent += ImageView_ButtonPressEvent;
            pictureBox.ButtonReleaseEvent += ImageView_ButtonReleaseEvent;
            pictureBox.ScrollEvent += ImageView_ScrollEvent;
            pictureBox.Drawn += PictureBox_Drawn;
            pictureBox.SizeAllocated += PictureBox_SizeAllocated;
            pictureBox.AddEvents((int)
            (EventMask.ButtonPressMask
            | EventMask.ButtonReleaseMask
            | EventMask.KeyPressMask
            | EventMask.PointerMotionMask | EventMask.ScrollMask));
            this.KeyPressEvent += ImageView_KeyPressEvent;
        }

        #endregion

        private void PictureBox_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            MainMap.Navigator.SetSize(pictureBox.AllocatedWidth,pictureBox.AllocatedHeight);
        }
        static byte[] ImageToByteArray(Image<Rgb24> image)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                // Save the image to the MemoryStream using PNG format
                // You can change the format to JPEG or any other supported format
                image.Save(memoryStream, new PngEncoder());
                return memoryStream.ToArray();
            }
        }
        private void PictureBox_Drawn(object o, DrawnArgs e)
        {
            Title = MainMap.Navigator.Viewport.ToString();
            try
            {
                if (!openSlide)
                {
                    _slideSource.GetSlice(new SliceInfo(MainMap.Navigator.Viewport.CenterX, MainMap.Navigator.Viewport.CenterY, MainMap.Navigator.Viewport.Width, MainMap.Navigator.Viewport.Height, resolution));
                    byte[] bts = OpenSlideBase.LastSlice;
                    Pixbuf pf = new Pixbuf(bts,false,8,(int)OpenSlideBase.destExtent.Width, (int)OpenSlideBase.destExtent.Height, (int)OpenSlideBase.destExtent.Width*3);
                    Gdk.CairoHelper.SetSourcePixbuf(e.Cr, pf, 0, 0);
                }
                else
                {
                    _openSlideBase.GetSlice(new OpenSlideGTK.SliceInfo(MainMap.Navigator.Viewport.CenterX, MainMap.Navigator.Viewport.CenterY, MainMap.Navigator.Viewport.Width, MainMap.Navigator.Viewport.Height, resolution));
                    byte[] bts = OpenSlideGTK.OpenSlideBase.LastSlice;
                    Pixbuf pf = new Pixbuf(bts, false, 8, (int)OpenSlideGTK.OpenSlideBase.destExtent.Width, (int)OpenSlideGTK.OpenSlideBase.destExtent.Height, (int)OpenSlideGTK.OpenSlideBase.destExtent.Width * 3);
                    Gdk.CairoHelper.SetSourcePixbuf(e.Cr, pf, 0, 0);
                }
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
            }
            
            e.Cr.Paint();
        }
        Point pp = new Point(0, 0);
        Point Origin
        {
            get { return pp; }
            set 
            {
                MainMap.Navigator.CenterOn(Origin.X, Origin.Y);
                pp = value;
                pictureBox.QueueDraw();
            }
        }
        public static Gdk.Key keyDown = Gdk.Key.Key_3270_Test;
        /// This function is called when a key is pressed. It checks if the key is a function key, and
        /// if so, it performs the function
        /// 
        /// @param o The object that the event is being called on
        /// @param KeyPressEventArgs
        /// https://developer.gnome.org/gtk-sharp/stable/Gtk.KeyPressEventArgs.html
        /// 
        /// @return The key that was pressed.
        private void ImageView_KeyPressEvent(object o, KeyPressEventArgs e)
        {
            double moveAmount = 50*resolution;
            keyDown = e.Event.Key;
            //double moveAmount = 5 * Scale.Width;
            if (e.Event.Key == Gdk.Key.e)
            {
                    Resolution -= 0.5f;
            }
            if (e.Event.Key == Gdk.Key.q)
            {
                    Resolution += 0.5f;
            }
            if (e.Event.Key == Gdk.Key.w)
            {
                    Origin = new Point(Origin.X, Origin.Y + moveAmount);
            }
            if (e.Event.Key == Gdk.Key.s)
            {
                    Origin = new Point(Origin.X, Origin.Y - moveAmount);
            }
            if (e.Event.Key == Gdk.Key.a)
            {
                    Origin = new Point(Origin.X - moveAmount, Origin.Y);
            }
            if (e.Event.Key == Gdk.Key.d)
            {
                    Origin = new Point(Origin.X + moveAmount, Origin.Y);
            }
        }
        /// The function is called when the user presses a key on the keyboard
        /// 
        /// @param o The object that the event is being called from.
        /// @param KeyPressEventArgs
        /// https://developer.gnome.org/gtk-sharp/stable/Gtk.KeyPressEventArgs.html
        private void ImageView_KeyUpEvent(object o, KeyPressEventArgs e)
        {
            keyDown = Gdk.Key.Key_3270_Test;
        }

        private void ImageView_ScrollEvent(object o, ScrollEventArgs args)
        {  
            if (args.Event.State.HasFlag(ModifierType.ControlMask))
            if (args.Event.Direction == ScrollDirection.Up)
            {
                    Resolution -=0.3;
            }
            else
            {
                    Resolution += 0.3;
            }
        }

        /* Setting the resolution of the image. */
        double resolution = 1;
        public double Resolution
        {
            get { return resolution; }
            set
            {
                if (value < 0)
                    return;
                resolution = value;
                MainMap.Navigator.ZoomTo(resolution);
                pictureBox.QueueDraw();
            }
        }

        Point p;
        private void ImageView_MotionNotifyEvent(object o, MotionNotifyEventArgs e)
        {
            if(e.Event.State.HasFlag(ModifierType.Button2Mask))
            {
                double x = Origin.X - ((p.X - e.Event.X)*resolution);
                double y = Origin.Y + ((p.Y - e.Event.Y)*resolution);
                Origin = new Point(x, y);
            }
            p = new Point(e.Event.X, e.Event.Y);
        }
        Point up = new Point(0,0);
        private void ImageView_ButtonReleaseEvent(object o, ButtonReleaseEventArgs e)
        {
            up = new Point(e.Event.X, e.Event.Y);
            Point p = new Point(up.X - down.X,up.Y - down.Y);
        }
        Point down = new Point(0, 0);
        private void ImageView_ButtonPressEvent(object o, ButtonPressEventArgs e)
        {
            down = new Point(e.Event.X, e.Event.Y);
        }
        private Map MainMap = new Map();
        private OpenSlideBase _slideSource;
        private OpenSlideGTK.OpenSlideBase _openSlideBase;
        private bool openSlide = false;
        /// <summary>
        /// Open slide file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Initialize(string file)
        {
            MainMap.Navigator.CenterOn(0, 0);

            if (_openSlideBase != null) (_openSlideBase as IDisposable).Dispose();
            _openSlideBase = (OpenSlideGTK.OpenSlideBase)OpenSlideGTK.SlideSourceBase.Create(file);
            if (_openSlideBase == null)
            {
                openSlide = false;
                Console.WriteLine("Failed to load image with OpenSlide.");
                if (_slideSource != null) (_slideSource as IDisposable).Dispose();
                _slideSource = (OpenSlideBase)SlideSourceBase.Create(file);
                if (_slideSource == null)
                {
                    Console.WriteLine("Failed to load image with Bioformats.");
                    return;
                }
                MainMap.Navigator.ZoomToBox(new MRect(0, 0, 256, 256));
                InitMainBioformats(_slideSource);
            }
            else
            {
                openSlide = true;
                MainMap.Navigator.SetViewport(new Mapsui.Viewport(0, 0, resolution, 0, 256, 256));
                //MainMap.Navigator.Limiter.Limit(MainMap.Navigator.Viewport,new MRect(-_openSlideBase.SlideImage.Dimensions.Width, -_openSlideBase.SlideImage.Dimensions.Height, _openSlideBase.SlideImage.Dimensions.Width, _openSlideBase.SlideImage.Dimensions.Height), null);
                //MainMap.Navigator.ZoomToBox(new MRect(0, 0, _openSlideBase.SlideImage.Dimensions.Width, _openSlideBase.SlideImage.Dimensions.Height));
                InitMainOpenSlide(_openSlideBase);
            }
            Console.WriteLine("Initialization Complete.");
        }
        /// <summary>
        /// Init main map
        /// </summary>
        /// <param name="_slideSource"></param>
        private void InitMainBioformats(ISlideSource _slideSource)
        {
            MainMap.Layers.Clear();
            MainMap.Layers.Add(new SlideViewer.SlideTileLayer(_slideSource, dataFetchStrategy: new MinimalDataFetchStrategy()));
            //MainMap.Layers.Add(new SlideSliceLayer(_slideSource) { Enabled = false, Opacity = 0.5 });
            //MainMap.Layers.Add(slice);
        }

        /// <summary>
        /// Init main map
        /// </summary>
        /// <param name="_slideSource"></param>
        private void InitMainOpenSlide(OpenSlideGTK.ISlideSource _slideSource)
        {
            MainMap.Layers.Clear();
            MainMap.Layers.Add(new OpenSlideGTK.SlideTileLayer(_slideSource, dataFetchStrategy: new MinimalDataFetchStrategy()));
            //MainMap.Layers.Add(new SlideSliceLayer(_slideSource) { Enabled = false, Opacity = 0.5 });
            //MainMap.Layers.Add(slice);
        }

    }
}
