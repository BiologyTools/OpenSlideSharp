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

namespace BioGTK
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
            
        }
        #endregion
        
        bool showOverview;
        Rectangle overview;
        Pixbuf overviewBitmap;
        /* A property that is used to set the value of the showOverview variable. */
        public bool ShowOverview
        {
            get { return showOverview; }
            set
            {
                showOverview = value;
                UpdateView();
            }
        }
        /// We will find the first Resolution small enough in bytes to use as a preview image
        /// 
        /// @return The index of the resolution that is small enough to use as a preview image.
        private int GetPreviewResolution()
        {
            //We will find the first Resolution small enough in bytes to use as a preview image.
            int i = 0;
            foreach (Resolution res in SelectedImage.Resolutions)
            {
                if (res.SizeInBytes < 1e+9 * 0.05)
                    return i;
                i++;
            }
            return 0;
        }
        /// It takes a large image, resizes it to a small image, and then displays it in a Gtk.Image
        public void InitPreview()
        {
            if (SelectedImage.Resolutions.Count == 1)
                return;
            overview = new Rectangle(0, 0, 200, 80);
            int r = GetPreviewResolution();
            Bitmap bm;
            ResizeBilinear re = new ResizeBilinear(200, 80);
            bm = re.Apply(BioImage.GetTile(Images[0], GetCoordinate(), r, 0, 0, Images[0].SizeX, Images[0].SizeY));
            overviewBitmap = new Pixbuf(bm.RGBBytes, true, 8, bm.Width, bm.Height, bm.Stride);
            showOverview = true;
        }
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
        

        /// When the user clicks the "Go to Origin" button, the viewer's origin is set to the negative
        /// of the image's location
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void GoToOriginMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.viewer.Origin = new PointD(-SelectedImage.Volume.Location.X, -SelectedImage.Volume.Location.Y);
        }

        /// This function is called when the user clicks on the "Go to Image" button in the menu
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk3/stable/GtkButton.html#GtkButton-clicked
        private void GoToImageMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.viewer.GoToImage();
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
            if (!initialized)
            {
                GoToImage();
                initialized = true;
            }
            UpdateView();
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
            if ((Bitmaps.Count == 0 || Bitmaps.Count != Images.Count))
                UpdateImages();
            e.Cr.Scale(Scale.Width, Scale.Height);
            e.Cr.Translate(pictureBox.AllocatedWidth / 2, pictureBox.AllocatedHeight / 2);
            RectangleD rr = ToViewSpace(PointD.MinX, PointD.MinY, PointD.MaxX - PointD.MinX, PointD.MaxY - PointD.MinY);
            e.Cr.Rectangle(rr.X, rr.Y,Math.Abs(rr.W),Math.Abs(rr.H));
            e.Cr.Stroke();
            int i = 0;
            
            //These ensure we always ask for min 2x2px pixels since Bioformats throws an error if we try to get 1x1px.
            if (!SelectedImage.isPyramidal && (pictureBox.AllocatedWidth <= 1 || pictureBox.AllocatedHeight <= 1))
                return;
            if (SelectedImage.isPyramidal && (imageBox.AllocatedWidth <= 1 || imageBox.AllocatedHeight <= 1))
                return;
            foreach (BioImage im in Images)
            {
                RectangleD r = ToViewSpace(im.Volume.Location.X, im.Volume.Location.Y, im.Volume.Width, im.Volume.Height);
                e.Cr.LineWidth = 1;
                if (SelectedImage.isPyramidal)
                {
                    e.Cr.Restore(); //g.ResetTransform();   
                    Gdk.CairoHelper.SetSourcePixbuf(e.Cr, Bitmaps[i], 0, 0);
                    e.Cr.Paint();
                    if(ShowOverview)
                    {
                        Pixbuf pf = overviewBitmap.ScaleSimple((int)overview.Width, (int)overview.Height, InterpType.Bilinear);
                        Gdk.CairoHelper.SetSourcePixbuf(e.Cr, pf, 0, 0);
                        
                        e.Cr.Paint();
                        e.Cr.SetSourceColor(FromColor(Color.Gray));
                        e.Cr.Rectangle(overview.X, overview.Y, overview.Width, overview.Height);
                        e.Cr.Stroke();
                        Resolution rs = SelectedImage.Resolutions[Resolution];
                        double dx = ((double)PyramidalOrigin.X / rs.SizeX) * overview.Width;
                        double dy = ((double)PyramidalOrigin.Y / rs.SizeY) * overview.Height;
                        double dw = Math.Ceiling((double)((double)imageBox.AllocatedWidth / rs.SizeX) * overview.Width);
                        double dh = Math.Ceiling((double)((double)imageBox.AllocatedHeight / rs.SizeY) * overview.Height);
                        e.Cr.SetSourceColor(FromColor(Color.Red));
                        e.Cr.Rectangle((int)dx, (int)dy, (int)dw, (int)dh);
                        e.Cr.Stroke();
                    }
                }
                else
                {
                    Pixbuf pf = Bitmaps[i].ScaleSimple((int)r.W,(int)r.H, InterpType.Bilinear);
                    Gdk.CairoHelper.SetSourcePixbuf(e.Cr, pf, (int)r.X, (int)r.Y);
                    e.Cr.Paint();
                    foreach (ROI an in im.Annotations)
                    {
                        if (an.type == ROI.Type.Mask || an.mask != null)
                        {
                            Pixbuf p = an.mask.ScaleSimple((int)r.W, (int)r.H, InterpType.Bilinear);
                            Gdk.CairoHelper.SetSourcePixbuf(e.Cr, p, r.X, r.Y);
                            e.Cr.Paint();
                        }
                    }
                }
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
            keyDown = e.Event.Key;
            double moveAmount = 5 * Scale.Width;
            if (e.Event.Key == Gdk.Key.c && e.Event.State == ModifierType.ControlMask)
            {
                CopySelection();
                return;
            }
            if (e.Event.Key == Gdk.Key.v && e.Event.State == ModifierType.ControlMask)
            {
                PasteSelection();
                return;
            }
            if (e.Event.Key == Gdk.Key.e)
            {
                if (SelectedImage.isPyramidal)
                {
                    Resolution--;
                }
                else
                    Scale = new SizeF(Scale.Width - 0.1f, Scale.Height - 0.1f);
            }
            if (e.Event.Key == Gdk.Key.q)
            {
                if (SelectedImage.isPyramidal)
                {
                    Resolution++;
                }
                else
                    Scale = new SizeF(Scale.Width + 0.1f, Scale.Height + 0.1f);
            }
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
            UpdateView();
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
            float dx = Scale.Width / 50;
            float dy = Scale.Height / 50;
            if (!SelectedImage.isPyramidal)
                if (args.Event.State == ModifierType.ControlMask)
                {
                    if (args.Event.Direction == ScrollDirection.Up)
                    {
                        pxWmicron -= 0.01f;
                        pxHmicron -= 0.01f;
                    }
                    else
                    {
                        pxWmicron += 0.01f;
                        pxHmicron += 0.01f;
                    }
                    UpdateView();
                }
              
            if (args.Event.State.HasFlag(ModifierType.ControlMask) && SelectedImage.isPyramidal)
                if (args.Event.Direction == ScrollDirection.Up)
                {
                        Resolution--;
                }
                else
                {
                        Resolution++;
                }
        }

        /// It updates the GUI to reflect the current state of the image
        public void UpdateGUI()
        {
            
        }

        /* Defining an enum. */
        public enum ViewMode
        {
            Raw,
            Filtered,
            RGBImage,
            Emission,
        }
        static BioImage selectedImage;
        public static BioImage SelectedImage
        {
            get
            {
                return selectedImage;
            }
            set
            {
                selectedImage = value;
                App.viewer.Images[App.viewer.SelectedIndex] = value;
            }
        }
        private ViewMode viewMode = ViewMode.Filtered;
        /* Setting the view mode of the application. */
        public ViewMode Mode
        {
            get
            {
                return viewMode;
            }
            set
            {
                viewMode = value;
                if (viewMode == ViewMode.RGBImage)
                {
                    rgbStack.VisibleChild = rgbStack.Children[1];
                }
                else
                if (viewMode == ViewMode.Filtered)
                {
                    rgbStack.VisibleChild = rgbStack.Children[0];
                }
                else
                if (viewMode == ViewMode.Raw)
                {
                    rgbStack.VisibleChild = rgbStack.Children[0];
                }
                else
                {
                    rgbStack.VisibleChild = rgbStack.Children[0];
                }
                UpdateImage();
                UpdateView();
            }
        }
        /* A property that returns the R channel of the selected image. */
        public Channel RChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[0]];
            }
        }
        /* A property that returns the G channel of the selected image. */
        public Channel GChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[1]];
            }
        }
        /* A property that returns the B channel of the selected image. */
        public Channel BChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[2]];
            }
        }
        PointD origin = new PointD(0, 0);
        Point pyramidalOrigin = new Point(0, 0);
        /* Origin of the viewer in microns */
        public PointD Origin
        {
            get { return origin; }
            set
            {
                if(AllowNavigation)
                origin = value;
            }
        }
        /* Origin of the viewer in microns */
        public PointD TopRightOrigin
        {
            get {
                return new PointD((Origin.X - ((pictureBox.AllocatedWidth / 2) * pxWmicron)), (Origin.Y - ((pictureBox.AllocatedHeight / 2) * pxHmicron)));
            }
        }
        /* Setting the origin of a pyramidal image. */
        public Point PyramidalOrigin
        {
            get { return pyramidalOrigin; }
            set
            {
                if (!AllowNavigation)
                    return;
                if (scrollH.Adjustment.Upper > value.X && value.X > -1)
                {
                    scrollH.Adjustment.Value = value.X;
                    pyramidalOrigin = value;
                }
                if (scrollV.Adjustment.Upper > value.Y && value.Y > -1)
                {
                    scrollV.Adjustment.Value = value.Y;
                    pyramidalOrigin = value;
                }
                UpdateImage();
                UpdateView();
            }
        }
        int resolution = 0;
        /* Setting the resolution of the image. */
        public int Resolution
        {
            get { return resolution; }
            set
            {
                if (SelectedImage.Resolutions.Count <= value || value < 0)
                    return;
                double x, y;
                if(value > resolution)
                {
                    //++resolution zoom out
                    x = ((double)PyramidalOrigin.X / (double)SelectedImage.Resolutions[resolution].SizeX) * (double)SelectedImage.Resolutions[value].SizeX;
                    y = ((double)PyramidalOrigin.Y / (double)SelectedImage.Resolutions[resolution].SizeY) * (double)SelectedImage.Resolutions[value].SizeY;
                    scrollH.Adjustment.Upper = SelectedImage.Resolutions[value].SizeX;
                    scrollV.Adjustment.Upper = SelectedImage.Resolutions[value].SizeY;
                    float w = AllocatedWidth / 4;
                    float h = AllocatedHeight / 4;
                    resolution = value;
                    PyramidalOrigin = new Point((int)x - w, (int)y - h);
                }
                else
                {
                    //--resolution zoom in
                    x = ((double)PyramidalOrigin.X / (double)SelectedImage.Resolutions[resolution].SizeX) * (double)SelectedImage.Resolutions[value].SizeX;
                    y = ((double)PyramidalOrigin.Y / (double)SelectedImage.Resolutions[resolution].SizeY) * (double)SelectedImage.Resolutions[value].SizeY;
                    scrollH.Adjustment.Upper = SelectedImage.Resolutions[value].SizeX;
                    scrollV.Adjustment.Upper = SelectedImage.Resolutions[value].SizeY;
                    float w = AllocatedWidth / 2;
                    float h = AllocatedHeight / 2;
                    resolution = value;
                    PyramidalOrigin = new Point((int)x + w, (int)y + h);
                }
                UpdateImage();
                UpdateView();
            }
        }
        SizeF scale = new SizeF(1, 1);
        /* A property that is used to set the scale of the view. */
        public new SizeF Scale
        {
            get
            {
                return scale;
            }
            set
            {
                scale = value;
                UpdateView();
            }
        }
        /// It updates the status of the user.
        public void UpdateStatus()
        {
            statusLabel.Text = (zBar.Value + 1) + "/" + (zBar.Adjustment.Upper + 1) + ", " + (cBar.Value + 1) + "/" + (cBar.Adjustment.Upper + 1) + ", " + (tBar.Value + 1) + "/" + (tBar.Adjustment.Upper + 1) + ", " +
                mousePoint + mouseColor + ", " + SelectedImage.Buffers[0].PixelFormat.ToString() + ", (" + SelectedImage.Volume.Location.X.ToString("N2") + ", " + SelectedImage.Volume.Location.Y.ToString("N2") + ") " + Origin.ToString();
        }
        /// It updates the view.
        public void UpdateView()
        {
            if (SelectedImage.isPyramidal)
                imageBox.QueueDraw();
            else
                pictureBox.QueueDraw();
        }
        private string mousePoint = "";
        private string mouseColor = "";

        public double GetScale()
        {
            return ToViewSizeW(ROI.selectBoxSize / Scale.Width);
        }
        public static bool x1State;
        public static bool x2State;
        public static bool mouseLeftState;
        public static ModifierType Modifiers;
        PointD mouseD = new PointD(0, 0);

        /// This function is called when the mouse is moved over the image. It updates the mouse
        /// position, and if the user is drawing a brush stroke, it draws the stroke on the image
        /// 
        /// @param o the object that the event is being called from
        /// @param MotionNotifyEventArgs 
        private void ImageView_MotionNotifyEvent(object o, MotionNotifyEventArgs e)
        {
            Modifiers = e.Event.State;
            MouseMoveInt = new PointD((int)e.Event.X, (int)e.Event.Y);

            PointD p = ImageToViewSpace(e.Event.X, e.Event.Y);
            PointD ip = new PointD((p.X - TopRightOrigin.X) / pxWmicron, (p.Y - TopRightOrigin.Y) / pxHmicron);
            App.tools.ToolMove(p, e);
            Tools.currentTool.Rectangle = new RectangleD(mouseDown.X, mouseDown.Y, p.X - mouseDown.X, p.Y - mouseDown.Y);
            mousePoint = "(" + (p.X.ToString("F")) + ", " + (p.Y.ToString("F")) + ")";
            
            //If point selection tool is clicked we  
            if (Tools.currentTool.type == Tools.Tool.Type.pointSel && e.Event.State.HasFlag(ModifierType.Button1Mask))
            {
                foreach (ROI an in SelectedImage.Annotations)
                {
                    if(an.selected)
                    if (an.selectedPoints.Count > 0 && an.selectedPoints.Count < an.GetPointCount())
                    {
                        //If the selection is rectangle or ellipse we resize the annotation based on corners
                        if (an.type == ROI.Type.Rectangle || an.type == ROI.Type.Ellipse)
                        {
                            RectangleD d = an.Rect;
                            if (an.selectedPoints[0] == 0)
                            {
                                double dw = d.X - p.X;
                                double dh = d.Y - p.Y;
                                d.X = p.X;
                                d.Y = p.Y;
                                d.W += dw;
                                d.H += dh;
                            }
                            else
                            if (an.selectedPoints[0] == 1)
                            {
                                double dw = p.X - (d.W + d.X);
                                double dh = d.Y - p.Y;
                                d.W += dw;
                                d.H += dh;
                                d.Y -= dh;
                            }
                            else
                            if (an.selectedPoints[0] == 2)
                            {
                                double dw = d.X - p.X;
                                double dh = p.Y - (d.Y + d.H);
                                d.W += dw;
                                d.H += dh;
                                d.X -= dw;
                            }
                            else
                            if (an.selectedPoints[0] == 3)
                            {
                                double dw = d.X - p.X;
                                double dh = d.Y - p.Y;
                                d.W = p.X - an.X;
                                d.H = p.Y - an.Y;
                            }
                            an.Rect = d;
                        }
                        else
                        {
                            PointD pod = new PointD(p.X - pd.X, p.Y - pd.Y);
                            //PointD dif = new PointD((e.Event.X - ed.X) * PxWmicron, (e.Event.Y - ed.Y) * PxHmicron);
                            for (int i = 0; i < an.selectedPoints.Count; i++)
                            {
                                PointD poid = an.GetPoint(an.selectedPoints[i]);
                                an.UpdatePoint(new PointD(poid.X + pod.X, poid.Y + pod.Y), an.selectedPoints[i]);
                            }
                        }
                    }
                    else
                    {
                        PointD pod = new PointD(p.X - pd.X, p.Y - pd.Y);
                        for (int i = 0; i < an.GetPointCount(); i++)
                        {
                            PointD poid = an.PointsD[i];
                            an.UpdatePoint(new PointD(poid.X + pod.X, poid.Y + pod.Y), i);
                        }
                    }
                }
            }

            UpdateStatus();
            pd = p;
            UpdateView();
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
            Modifiers = e.Event.State;
            if (e.Event.Button == 1)
                mouseLeftState = false;
            PointD pointer = ImageToViewSpace(e.Event.X, e.Event.Y);
            mouseUp = pointer;
            if (e.Event.State.HasFlag(ModifierType.Button2Mask))
            {
                if (SelectedImage != null && !SelectedImage.isPyramidal)
                {
                    PointD pd = new PointD(pointer.X - mouseDown.X, pointer.Y - mouseDown.Y);
                    origin = new PointD(origin.X + pd.X, origin.Y + pd.Y);
                }
                UpdateView();
            }
            if (SelectedImage == null)
                return;
        }
        PointD pd;
        PointD mouseDownInt = new PointD(0, 0);
        PointD mouseMoveInt = new PointD(0, 0);
        PointD mouseMove = new PointD(0, 0);
        public static PointD mouseDown;
        public static PointD mouseUp;
        /* A property that returns the value of the mouseDownInt variable. */
        public PointD MouseDownInt
        {
            get { return mouseDownInt; }
            set { mouseDownInt = value; }
        }
        public PointD MouseMoveInt
        {
            get { return mouseMoveInt; }
            set { mouseMoveInt = value; }
        }
        public PointD MouseDown
        {
            get { return mouseDown; }
            set { mouseDown = value; }
        }
        public PointD MouseMove
        {
            get { return mouseMove; }
            set { mouseMove = value; }
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
            Modifiers = e.Event.State;
            if (e.Event.Button == 1)
                mouseLeftState = true;
            else
                mouseLeftState = false;
            PointD pointer = ImageToViewSpace(e.Event.X, e.Event.Y);
            MouseDownInt = new PointD(e.Event.X, e.Event.Y);
            pd = pointer;
            mouseDown = pd;
            mouseD = new PointD(((pointer.X - TopRightOrigin.X) / SelectedImage.Volume.Width)*SelectedImage.SizeX,((pointer.Y - TopRightOrigin.Y) / SelectedImage.Volume.Height) * SelectedImage.SizeY);
            
            if (SelectedImage == null)
                return;
            PointD ip = pointer; // SelectedImage.ToImageSpace(pointer);
            int ind = 0;
            //if (e.Event.Button == 3)
            //    contextMenu.Popup();
            
        }
        List<ROI> copys = new List<ROI>();
        /// It takes a point in the image space and returns the point in the view space
        /// 
        /// @param x the x coordinate of the point in the image
        /// @param y the y coordinate of the point in the image
        /// 
        /// @return The point in the image space that corresponds to the point in the view space.
        public PointD ImageToViewSpace(double x,double y)
        {
            if(SelectedImage.isPyramidal)
            {
                return new PointD(PyramidalOrigin.X + x, PyramidalOrigin.Y + y);
            }
            double dx = ToViewW(SelectedImage.Volume.Width);
            double dy = ToViewH(SelectedImage.Volume.Height);
            //The origin is the middle of the screen we want the top left corner
            PointD torig = new PointD((Origin.X - ((pictureBox.AllocatedWidth / 2) * pxWmicron)), (Origin.Y - ((pictureBox.AllocatedHeight / 2) * pxHmicron)));
            PointD orig = new PointD(torig.X - SelectedImage.Volume.Location.X, torig.Y - SelectedImage.Volume.Location.Y);
            PointD diff = new PointD(ToViewW(orig.X), ToViewH(orig.Y));
            PointD f = new PointD((((x + diff.X)/ dx) * SelectedImage.Volume.Width),(((y + diff.Y) / dy) * SelectedImage.Volume.Height));
            PointD ff = new PointD(SelectedImage.Volume.Location.X + f.X, SelectedImage.Volume.Location.Y + f.Y);
            return ff;
        }
        /// The function converts a rectangle from world space to view space.
        /// 
        /// @param RectangleD The RectangleD is a custom data type that represents a rectangle in 2D
        /// space. It has four properties: X (the x-coordinate of the top-left corner), Y (the
        /// y-coordinate of the top-left corner), W (the width of the rectangle), and H (the height of
        /// the
        /// 
        /// @return The method is returning a RectangleD object.
        public RectangleD ToViewSpace(RectangleD p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            double dx = ToScreenScaleW(p.W);
            double dy = ToScreenScaleH(p.H);
            return new RectangleD((float)d.X, (float)d.Y, (float)dx, (float)dy);
        }
        /// The function converts a Point object to PointF object in view space.
        /// 
        /// @param Point The Point class represents an ordered pair of integer x and y coordinates that
        /// define a point in a two-dimensional plane.
        /// 
        /// @return The method is returning a PointF object, which represents a point in 2D space with
        /// floating-point coordinates.
        public PointF ToViewSpace(Point p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            return new PointF((float)d.X, (float)d.Y);
        }
        /// The function converts a PointF object from world space to view space.
        /// 
        /// @param PointF PointF is a structure in C# that represents a point in a two-dimensional
        /// space. It consists of two float values, X and Y, which represent the coordinates of the
        /// point.
        /// 
        /// @return The method is returning a PointF object.
        public PointF ToViewSpace(PointF p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            return new PointF((float)d.X, (float)d.Y);
        }
        /// The function converts a point from a coordinate system to view space.
        /// 
        /// @param PointD The PointD class represents a point in a two-dimensional space. It typically
        /// has two properties: X and Y, which represent the coordinates of the point.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToViewSpace(PointD p)
        {
            return ToViewSpace(p.X, p.Y); ;
        }
        /// The function converts coordinates from a given space to view space.
        /// 
        /// @param x The x-coordinate in the original coordinate system.
        /// @param y The parameter "y" represents the y-coordinate in the original coordinate system.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToViewSpace(double x, double y)
        {
            if (SelectedImage.isPyramidal)
            {
                return new PointD(x, y);
            }
            double dx = (ToViewSizeW(Origin.X - x)) * Scale.Width;
            double dy = (ToViewSizeH(Origin.Y - y)) * Scale.Height;
            return new PointD(dx, dy);
        }
        /// The function converts coordinates and sizes from a given space to a view space.
        /// 
        /// @param x The x-coordinate of the rectangle's top-left corner in world space.
        /// @param y The parameter "y" represents the y-coordinate of the rectangle in the original
        /// coordinate space.
        /// @param w The width of the rectangle in world space.
        /// @param h The parameter "h" represents the height of the rectangle in the original coordinate
        /// space.
        /// 
        /// @return The method is returning a RectangleD object.
        public RectangleD ToViewSpace(double x, double y, double w, double h)
        {
            PointD d = ToViewSpace(x, y);
            double dw = ToViewSizeW(w);
            double dh = ToViewSizeH(h);
            if (SelectedImage.isPyramidal)
            {
                return new RectangleD(d.X - PyramidalOrigin.X, d.Y - PyramidalOrigin.Y, dw, dh);
            }
            return new RectangleD(-d.X, -d.Y, dw, dh);
        }
        /// The function converts a given value to a view size width based on certain conditions.
        /// 
        /// @param d The parameter "d" represents a size value that needs to be converted to a view
        /// size.
        /// 
        /// @return The method is returning a double value.
        private double ToViewSizeW(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        /// The function converts a given value to a view size in the horizontal direction.
        /// 
        /// @param d The parameter "d" represents a value that needs to be converted to a view size.
        /// 
        /// @return The method is returning a double value.
        public double ToViewSizeH(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
            double y = (double)(d / PxHmicron) * Scale.Width;
            return y;
        }
        /// The function converts a given value from microns to view width units, taking into account the
       /// scale and whether the image is pyramidal.
       /// 
       /// @param d The parameter "d" represents a value that needs to be converted to a view width.
       /// 
       /// @return The method is returning a double value.
        public double ToViewW(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        /// The function converts a given value from a specific unit to a view height value.
        /// 
        /// @param d The parameter "d" represents a value that needs to be converted to a different unit
        /// of measurement.
        /// 
        /// @return The method is returning a double value.
        public double ToViewH(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
            double y = (double)(d / PxHmicron) * Scale.Height;
            return y;
        }
        /// The function converts coordinates from a Cartesian plane to screen space.
        /// 
        /// @param x The x-coordinate of the point in the coordinate system.
        /// @param y The parameter "y" represents the y-coordinate of a point in a coordinate system.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToScreenSpace(double x, double y)
        {
            double fx = ToScreenScaleW(Origin.X - x);
            double fy = ToScreenScaleH(Origin.Y - y);
            return new PointD(fx, fy);
        }
        /// The function converts a point from a coordinate system to screen space.
        /// 
        /// @param PointD The PointD class represents a point in a two-dimensional space. It typically
        /// has two properties, X and Y, which represent the coordinates of the point.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToScreenSpace(PointD p)
        {
            return ToScreenSpace(p.X, p.Y);
        }
        /// The function converts a PointF object from world space to screen space.
        /// 
        /// @param PointF PointF is a structure in C# that represents a point in a two-dimensional
        /// space. It consists of two float values, X and Y, which represent the coordinates of the
        /// point.
        /// 
        /// @return The method is returning a PointF object.
        public PointF ToScreenSpace(PointF p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        /// The function takes an array of PointF objects and converts them to screen space coordinates.
        /// 
        /// @param p An array of PointF objects representing points in some coordinate system.
        /// 
        /// @return The method is returning an array of PointF objects in screen space.
        public PointF[] ToScreenSpace(PointF[] p)
        {
            PointF[] pf = new PointF[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                pf[i] = ToScreenSpace(p[i]);
            }
            return pf;
        }
        /// The function converts a 3D point to screen space and returns it as a PointF object.
        /// 
        /// @param Point3D The Point3D parameter represents a point in a three-dimensional space. It
        /// typically consists of three coordinates: X, Y, and Z.
        /// 
        /// @return The method is returning a PointF object.
        public PointF ToScreenSpace(Point3D p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        /// The function converts a given value to screen scale width based on the selected image's
        /// properties.
        /// 
        /// @param x The parameter "x" represents a value that needs to be converted to screen scale
        /// width.
        /// 
        /// @return The method is returning a double value.
        public double ToScreenScaleW(double x)
        {
            if (SelectedImage.isPyramidal)
            {
                return (double)x;
            }
            return (x * PxWmicron) * Scale.Width;
        }
        /// The function converts a given value to screen scale height based on the selected image's
        /// properties.
        /// 
        /// @param y The parameter "y" represents the vertical coordinate value that needs to be
        /// converted to screen scale.
        /// 
        /// @return The method is returning a double value.
        public double ToScreenScaleH(double y)
        {
            if (SelectedImage.isPyramidal)
            {
                return (double)y;
            }
            return (y * PxHmicron) * Scale.Height;
        }
        /// The function takes a PointD object and returns a PointF object with the coordinates scaled
        /// to the screen.
        /// 
        /// @param PointD PointD is a custom data type that represents a point in a two-dimensional
        /// space. It consists of two double values, X and Y, which represent the coordinates of the
        /// point.
        /// 
        /// @return The method is returning a PointF object, which represents a point in a
        /// two-dimensional plane with floating-point coordinates.
        public PointF ToScreenScale(PointD p)
        {
            double x = ToScreenScaleW((float)p.X);
            double y = ToScreenScaleH((float)p.Y);
            return new PointF((float)x, (float)y);
        }
        /// The function converts a set of coordinates and dimensions from a mathematical coordinate
       /// system to a screen coordinate system and returns a rectangle with the converted values.
       /// 
       /// @param x The x-coordinate of the rectangle's top-left corner in world space.
       /// @param y The parameter "y" represents the y-coordinate of the top-left corner of the
       /// rectangle in the coordinate system of the screen.
       /// @param w The width of the rectangle in world space.
       /// @param h The parameter "h" represents the height of the rectangle.
       /// 
       /// @return The method is returning a RectangleD object.
        public RectangleD ToScreenRectF(double x, double y, double w, double h)
        {
            PointD pf = ToScreenSpace(x, y);
            RectangleD rf = new RectangleD((float)pf.X, (float)pf.Y, (float)ToViewW(w), (float)ToViewH(h));
            return rf;
        }
        /// The function converts a RectangleD object to screen space.
        /// 
        /// @param RectangleD The RectangleD is a custom data type that represents a rectangle in 2D
        /// space. It typically has four properties: X (the x-coordinate of the top-left corner), Y (the
        /// y-coordinate of the top-left corner), W (the width of the rectangle), and H (the height of
        /// 
        /// @return The method is returning a RectangleD object.
        public RectangleD ToScreenSpace(RectangleD p)
        {
            return ToScreenRectF(p.X, p.Y, p.W, p.H);
        }
        /// The function takes an array of RectangleD objects and converts them to screen space.
        /// 
        /// @param p An array of RectangleD objects representing rectangles in some coordinate space.
        /// 
        /// @return The method is returning an array of RectangleD objects.
        public RectangleD[] ToScreenSpace(RectangleD[] p)
        {
            RectangleD[] rs = new RectangleD[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                rs[i] = ToScreenSpace(p[i]);
            }
            return rs;
        }
        /// The function takes an array of PointD objects and converts them to an array of PointF
        /// objects in screen space.
        /// 
        /// @param p An array of PointD objects representing points in some coordinate system.
        /// 
        /// @return The method is returning an array of PointF objects.
        public PointF[] ToScreenSpace(PointD[] p)
        {
            PointF[] rs = new PointF[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                PointD pd = ToScreenSpace(p[i]);
                rs[i] = new PointF((float)pd.X, (float)pd.Y);
            }
            return rs;
        }
        /// This function is used to go to the image at the specified index
        public void GoToImage()
        {
            GoToImage(0);
        }
        /// It takes an image index and sets the origin and physical size of the image to the values of
        /// the image at that index
        /// 
        /// @param i the index of the image in the list
        /// 
        /// @return The method is returning the value of the variable "i"
        public void GoToImage(int i)
        {
            if (Images.Count <= i)
                return;
            double dx = Images[i].Volume.Width / 2;
            double dy = Images[i].Volume.Height / 2;
            Origin = new PointD((Images[i].Volume.Location.X)+dx, (Images[i].Volume.Location.Y)+dy);
            PxWmicron = Images[i].PhysicalSizeX;
            PxHmicron = Images[i].PhysicalSizeY;
            if (Images[i].SizeX > 1080)
            {
                double w = (double)SelectedImage.SizeX / (double)pictureBox.AllocatedWidth;
                double h = (double)SelectedImage.SizeY / (double)pictureBox.AllocatedHeight;
                PxWmicron *= h;
                PxHmicron *= h;
            }
            UpdateView();
        }
        private Point centerPixel = new Point(0, 0);
        public Point CenterPixel
        {
            get { return centerPixel; }
            set { SetProperty(ref centerPixel, value); }
        }


        private Point centerWorld = new Point(0, 0);
        public Point CenterWorld
        {
            get { return centerWorld; }
            set { SetProperty(ref centerWorld, value); }
        }


        private double resolution = 1;
        public double Resolution
        {
            get { return resolution; }
            set { SetProperty(ref resolution, value); }
        }

        public ObservableCollection<KeyValuePair<string, object>> Infos { get; } = new ObservableCollection<KeyValuePair<string, object>>();
        public ObservableCollection<KeyValuePair<string, ImageSource>> Images { get; } = new ObservableCollection<KeyValuePair<string, ImageSource>>();

        private ISlideSource _slideSource;
        private Random random = new Random();

        /// <summary>
        /// Open slide file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                var file = openFileDialog.FileName;
                if (_slideSource != null) (_slideSource as IDisposable).Dispose();
                _slideSource = SlideSourceBase.Create(file);
                if (_slideSource == null) return;
                InitMain(_slideSource);
                InitPreview(_slideSource);
                InitImage(_slideSource);
                InitInfo(_slideSource);
            }
        }

        /// <summary>
        /// Init main map
        /// </summary>
        /// <param name="_slideSource"></param>
        private void InitMain(ISlideSource _slideSource)
        {
            MainMap.Map.Layers.Clear();
            MainMap.Map.Layers.Add(new SlideTileLayer(_slideSource, dataFetchStrategy: new MinimalDataFetchStrategy()));
            MainMap.Map.Layers.Add(new SlideSliceLayer(_slideSource) { Enabled = false, Opacity = 0.5 });

            var center = MainMap.Viewport.Center;
            Resolution = MainMap.Viewport.Resolution;
            CenterWorld = new Point(center.X, -center.Y);
            CenterPixel = new Point(center.X / Resolution, -center.Y / Resolution);

        }

        /// <summary>
        /// Init hawkeye map
        /// </summary>
        /// <param name="source"></param>
        private void InitPreview(ISlideSource source)
        {
            PreviewMap.Map.PanLock = false;
            PreviewMap.Map.RotationLock = false;
            PreviewMap.Map.ZoomLock = false;
            PreviewMap.Map.Layers.Clear();
            PreviewMap.Map.Layers.Add(new SlideTileLayer(source, dataFetchStrategy: new MinimalDataFetchStrategy()));
            PreviewMap.Navigator.NavigateToFullEnvelope(Mapsui.Utilities.ScaleMethod.Fit);
            PreviewMap.Map.PanLock = true;
            PreviewMap.Map.RotationLock = true;
            PreviewMap.Map.ZoomLock = true;
        }

        /// <summary>
        /// Init label etc.
        /// </summary>
        /// <param name="slideSource"></param>
        private void InitImage(ISlideSource slideSource)
        {
            Images.Clear();
            foreach (var item in slideSource.GetExternImages())
            {
                Images.Add(new KeyValuePair<string, ImageSource>(item.Key, (ImageSource)new ImageSourceConverter().ConvertFrom(item.Value)));
            }
        }

        /// <summary>
        /// Init info list.
        /// </summary>
        /// <param name="slideSource"></param>
        private void InitInfo(ISlideSource slideSource)
        {
            Title = slideSource.Source;
            Infos.Clear();
            foreach (var item in slideSource.ExternInfo)
            {
                Infos.Add(item);
            }
            Infos.Add(new KeyValuePair<string, object>("--Layer--", "-Resolution(um/pixel)-"));
            foreach (var item in slideSource.Schema.Resolutions)
            {
                Infos.Add(new KeyValuePair<string, object>(item.Key.ToString(), item.Value.UnitsPerPixel));
            }
            LayerList.ItemsSource = MainMap.Map.Layers.Reverse();
        }

        /// <summary>
        /// Random goto.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Random_Click(object sender, RoutedEventArgs e)
        {
            var w = random.Next(0, (int)_slideSource.Schema.Extent.Width);
            var h = random.Next(-(int)_slideSource.Schema.Extent.Height, 0);
            MainMap.Navigator.NavigateTo(new Point(w, h), MainMap.Viewport.Resolution);
        }

        /// <summary>
        /// Open process directory.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Explorer_Click(object sender, RoutedEventArgs e)
        {
            var assemblyLocation = Assembly.GetCallingAssembly().Location;
            var assemblyDirectory = Directory.GetParent(assemblyLocation)?.FullName;
            Process.Start("explorer.exe", assemblyDirectory);
        }


        private void CenterOnPixel_Click(object sender, RoutedEventArgs e)
        {
            var resolution = MainMap.Viewport.Resolution;
            MainMap.Navigator.CenterOn(new Point(CenterPixel.X * resolution, -CenterPixel.Y * resolution));
        }

        private void CenterOnWorld_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Navigator.CenterOn(new Point(CenterWorld.X, -CenterWorld.Y));
        }

        private void ZoomTo_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Navigator.ZoomTo(Resolution);
        }
    }
}
}
