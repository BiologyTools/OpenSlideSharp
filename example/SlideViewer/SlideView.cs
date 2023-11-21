using Gtk;
using Gdk;
using System;
using System.Collections.Generic;
using System.Linq;
using Point = Mapsui.MPoint;
using System.IO;
using Mapsui;
using OpenSlideSharp.BruTile;
using Mapsui.Rendering.Skia;
using Mapsui.Extensions;

namespace SlideLibrary.Demo
{
    public class SlideView : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
       
#pragma warning disable 649
        [Builder.Object]
        private Gtk.DrawingArea pictureBox;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        public static SlideView Create(string file)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/SlideView.glade", FileMode.Open));
            SlideView v = new SlideView(builder, builder.GetObject("slideView").Handle,file);
            return v;
        }
        public static SlideView Create()
        {
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

        private void PictureBox_Drawn(object o, DrawnArgs e)
        {
            Title = MainMap.Navigator.Viewport.ToString();
            MRect rect = MainMap.Navigator.Viewport.ToExtent();
            slice.GetFeatures(rect, resolution);
            Pixbuf pf = slice.buffer;
            if (pf == null) { Console.WriteLine("No Image"); return; }
            Gdk.CairoHelper.SetSourcePixbuf(e.Cr,pf, 0, 0);
            e.Cr.Paint();
            pf.Dispose();
        }
        Point pp = new Point(0, 0);
        Point Origin
        {
            get { return pp; }
            set 
            {
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
            double moveAmount = 10*resolution;
            keyDown = e.Event.Key;
            //double moveAmount = 5 * Scale.Width;
            if (e.Event.Key == Gdk.Key.e)
            {
                    Resolution--;
            }
            if (e.Event.Key == Gdk.Key.q)
            {
                    Resolution++;
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
            MainMap.Navigator.CenterOn(Origin.X, Origin.Y);
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
                    Resolution--;
            }
            else
            {
                    Resolution++;
            }
        }

        /* Setting the resolution of the image. */
        double resolution = 10;
        public double Resolution
        {
            get { return resolution; }
            set
            {
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
                MainMap.Navigator.CenterOn(Origin.X, Origin.Y);
            }
            p = new Point(e.Event.X, e.Event.Y);
        }

        private void ImageView_ButtonReleaseEvent(object o, ButtonReleaseEventArgs e)
        {

        }

        private void ImageView_ButtonPressEvent(object o, ButtonPressEventArgs e)
        {

        }
        private ISlideSource _slideSource;
        private SlideTileLayer tile;
        private SlideSliceLayer slice;
        /// <summary>
        /// Open slide file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Initialize(string file)
        {
            MainMap = new Map();
            MainMap.BackColor = new Mapsui.Styles.Color(0, 0, 0);
            if (_slideSource != null) (_slideSource as IDisposable).Dispose();
            _slideSource = SlideSourceBase.Create(file);
            if (_slideSource == null)
            {
                Console.WriteLine("Failed to load image with OpenSlide.");
                return;
            }
            InitMain(_slideSource);
            Console.WriteLine("Initialization Complete.");
        }
        Map MainMap;
        /// <summary>
        /// Init main map
        /// </summary>
        /// <param name="_slideSource"></param>
        private void InitMain(ISlideSource _slideSource)
        {
            MainMap.Layers.Clear();
            tile = new SlideTileLayer(_slideSource);
            tile.Enabled = true;
            slice = new SlideSliceLayer(_slideSource);
            MainMap.Layers.Add(tile);
            MainMap.Layers.Add(slice);
            Resolution = 1;
        }

    }
}
