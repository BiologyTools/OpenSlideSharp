using Gtk;
using Gdk;
using System;
using System.Collections.Generic;
using System.Linq;
using AForge;
using Point = Mapsui.MPoint;
using Color = AForge.Color;
using Rectangle = AForge.Rectangle;
using System.IO;
using Mapsui;
using OpenSlideSharp.BruTile;
using SlideLibrary.Demo;
using System.Collections.ObjectModel;
using System.Reflection;
using Mapsui.UI;
using Mapsui.Extensions;
using Mapsui.Rendering.Skia;
using OpenCvSharp;

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

        /// The function creates an ImageView object using a BioImage object and returns it.
        /// 
        /// @param BioImage The BioImage parameter is an object that represents an image in a biological
        /// context. It likely contains information about the image file, such as the filename, and
        /// possibly additional metadata related to the image.
        /// 
        /// @return The method is returning an instance of the ImageView class.
        public static SlideView Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/SlideView.glade", FileMode.Open));
            SlideView v = new SlideView(builder, builder.GetObject("slideView").Handle);
            return v;
        }


        /* The above code is a constructor for the ImageView class in C#. It takes in a Builder object,
        a handle, and a BioImage object as parameters. */
        protected SlideView(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
            Initialize("F:\\TESTIMAGES\\CZI\\Whole-Slide\\CMU-2.svs");
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

        bool initialized = false;
        /// If the image is pyramidal, update the image. If the image is not initialized, go to the
       /// image. Update the view
       /// 
       /// @param o The object that the event is being called on.
       /// @param SizeAllocatedArgs
       /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-size-allocation.html.en
        private void PictureBox_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            MainMap.Navigator.SetSize(pictureBox.WidthRequest, pictureBox.HeightRequest);
        }

        /// The function is called when the picturebox is drawn. It checks if the bitmaps are up to
        /// date, and if not, it updates them. It then draws the image, and then draws the annotations. 
        /// 
        /// The annotations are drawn by looping through the list of annotations, and then drawing them
        /// based on their type. 
        /// 
        /// The annotations are drawn in the view space, which is the space of the picturebox. 
        /// 
        /// The annotations are drawn in the view space by converting the coordinates of the annotation
        /// to the view space. 
        /// 
        /// The coordinates of the annotation are converted to the view space by multiplying the
        /// coordinates by the scale of the image. 
        /// 
        /// The scale of the image is the ratio of the size of the image to the size of the picturebox. 
        /// 
        /// The size of the image is the size of the image in pixels. 
        /// 
        /// The size of the picturebox is the size of the picturebox in pixels.
        /// 
        /// @param o The object that is being drawn
        /// @param DrawnArgs This is a class that contains the Cairo context and the allocated width and
        /// height of the picturebox.
        private void PictureBox_Drawn(object o, DrawnArgs e)
        {
            Title = MainMap.Navigator.Viewport.ToString();
            MemoryStream ms = rend.RenderToBitmapStream(MainMap.Navigator.Viewport, MainMap.Layers, MainMap.BackColor);
            byte[] bts = ms.GetBuffer();
            Pixbuf pf = new Pixbuf(bts);
            Gdk.CairoHelper.SetSourcePixbuf(e.Cr,pf, 0, 0);
            e.Cr.Paint();
            pf.Dispose();
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
            /*
            if (e.Event.Key == Gdk.Key.w)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new Point(PyramidalOrigin.X, PyramidalOrigin.Y + 150);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y + moveAmount);
            }
            if (e.Event.Key == Gdk.Key.s)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new Point(PyramidalOrigin.X, PyramidalOrigin.Y - 150);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y - moveAmount);
            }
            if (e.Event.Key == Gdk.Key.a)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new Point(PyramidalOrigin.X - 150, PyramidalOrigin.Y);
                }
                else
                    Origin = new PointD(Origin.X + moveAmount, Origin.Y);
            }
            if (e.Event.Key == Gdk.Key.d)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new Point(PyramidalOrigin.X + 150, PyramidalOrigin.Y);
                }
                else
                    Origin = new PointD(Origin.X - moveAmount, Origin.Y);
            }
            */
            pictureBox.QueueDraw();
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
        /// The function is called when the user scrolls the mouse wheel. If the user is holding down
        /// the control key, the function will change the resolution of the image. If the user is not
        /// holding down the control key, the function will change the z-slice of the image
        /// 
        /// @param o the object that the event is being called on
        /// @param ScrollEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-scroll-events.html.en
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
            pictureBox.QueueDraw();
        }

        /* Setting the resolution of the image. */
        double resolution = 10;
        public double Resolution
        {
            get { return resolution; }
            set
            {
                resolution = value;
                MainMap.Navigator.SetViewport(new Mapsui.Viewport(0,0, resolution, 0, pictureBox.AllocatedWidth, pictureBox.AllocatedHeight));
            }
        }

        /// This function is called when the mouse is moved over the image. It updates the mouse
        /// position, and if the user is drawing a brush stroke, it draws the stroke on the image
        /// 
        /// @param o the object that the event is being called from
        /// @param MotionNotifyEventArgs 
        private void ImageView_MotionNotifyEvent(object o, MotionNotifyEventArgs e)
        {
            if(e.Event.State.HasFlag(ModifierType.Button2Mask))
            {
                MainMap.Navigator.SetViewport(new Mapsui.Viewport(e.Event.X, e.Event.Y, resolution, 0, pictureBox.AllocatedWidth, pictureBox.AllocatedHeight));
                pictureBox.QueueDraw();
            }
        }
        
        /// The function is called when the mouse button is released. It checks if the mouse button is
        /// the left button, and if it is, it sets the mouseLeftState to false. It then sets the viewer
        /// to the current viewer, and converts the mouse coordinates to view space. It then sets the
        /// mouseUp variable to the pointer variable. It then checks if the mouse button is the middle
        /// button, and if it is, it checks if the selected image is pyramidal. If it is, it sets the
        /// pyramidal origin to the mouse coordinates. If it isn't, it sets the origin to the mouse
        /// coordinates. It then updates the image and the view. It then checks if the selected image is
        /// null, and if it is, it returns. It then calls the ToolUp function in the tools class,
        /// passing in the pointer and the event
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonReleaseEventArgs 
        /// 
        /// @return The image is being returned.
        private void ImageView_ButtonReleaseEvent(object o, ButtonReleaseEventArgs e)
        {

        }

        /// The function is called when the user clicks on the image. It checks if the user clicked on
        /// an annotation, and if so, it selects the annotation
        /// 
        /// @param o the object that the event is being called on
        /// @param ButtonPressEventArgs e.Event.State
        /// 
        /// @return The return value is a tuple of the form (x,y,z,c,t) where x,y,z,c,t are the
        /// coordinates of the pixel in the image.
        private void ImageView_ButtonPressEvent(object o, ButtonPressEventArgs e)
        {

        }
        private ISlideSource _slideSource;
        private Point centerPixel = new Point(0, 0);
        public Point CenterPixel
        {
            get { return centerPixel; }
            set { centerPixel = value; }
        }


        private Point centerWorld = new Point(0, 0);
        public Point CenterWorld
        {
            get { return centerWorld; }
            set { centerWorld = value; }
        }
        /// <summary>
        /// Open slide file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Initialize(string file)
        {
            MainMap = new Map();
            PreviewMap = new Map();
            rend = new MapRenderer();
            if (_slideSource != null) (_slideSource as IDisposable).Dispose();
            _slideSource = SlideSourceBase.Create(file);
            if (_slideSource == null) return;
            InitMain(_slideSource);
            InitPreview(_slideSource);
        }
        Map MainMap;
        Map PreviewMap;
        MapRenderer rend;
        RasterStyleRenderer renderer = new RasterStyleRenderer();
        /// <summary>
        /// Init main map
        /// </summary>
        /// <param name="_slideSource"></param>
        private void InitMain(ISlideSource _slideSource)
        {
            MainMap.Layers.Clear();
            MainMap.Layers.Add(new SlideTileLayer(_slideSource));
            MainMap.Layers.Add(new SlideSliceLayer(_slideSource) { Enabled = false, Opacity = 0.5 });

            Resolution = MainMap.Navigator.Viewport.Resolution;
            CenterWorld = new Point(MainMap.Navigator.Viewport.CenterX, -MainMap.Navigator.Viewport.CenterY);
            CenterPixel = new Point(MainMap.Navigator.Viewport.CenterX / Resolution, -MainMap.Navigator.Viewport.CenterY / Resolution);

        }

        /// <summary>
        /// Init hawkeye map
        /// </summary>
        /// <param name="source"></param>
        private void InitPreview(ISlideSource source)
        {
            PreviewMap.Navigator.PanLock = false;
            PreviewMap.Navigator.RotationLock = false;
            PreviewMap.Navigator.ZoomLock = false;
            PreviewMap.Layers.Clear();
            PreviewMap.Layers.Add(new SlideTileLayer(source));
            PreviewMap.Navigator.PanLock = true;
            PreviewMap.Navigator.RotationLock = true;
            PreviewMap.Navigator.ZoomLock = true;
        }
    }
}
