using Gtk;
using Application = Gtk.Application;

namespace SlideLibrary.Demo
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        static void Main(string[] args)
        {
            Application.Init();
            SlideView view = SlideView.Create();
            view.Show();
            Application.Run();
        }
    }
}