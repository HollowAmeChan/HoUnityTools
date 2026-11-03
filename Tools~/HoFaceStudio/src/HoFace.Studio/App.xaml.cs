using System;
using System.IO;
using System.Windows;

namespace HoFace.Studio
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (sender, args) =>
            {
                string directory = Path.Combine(AppContext.BaseDirectory, ".user");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "last-error.log"), args.Exception.ToString());
                MessageBox.Show(args.Exception.Message, "HoFaceStudio", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
