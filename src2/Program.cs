using System;
using System.Windows;

namespace AlanWakeTracker
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Repo.Load();
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow w = new MainWindow();
            app.Run(w);
        }
    }
}
