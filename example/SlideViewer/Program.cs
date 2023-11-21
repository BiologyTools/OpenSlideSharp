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
            if (args.Length == 0)
            {
                SlideView view = SlideView.Create();
                view.Show();
            }
            else
            {
                SlideView view = SlideView.Create(args[0]);
                view.Show();
            }
            Application.Run();
        }
    }
}