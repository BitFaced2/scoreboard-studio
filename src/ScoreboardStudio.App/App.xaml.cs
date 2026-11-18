using System.Configuration;
using System.Data;
using System.Windows;

namespace ScoreboardStudio.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    internal bool FatalError { get; private set; }
    public App()
    {
        DispatcherUnhandledException += (_, e) => {
            Diagnostics.Log("UI failure",e.Exception);
            FatalError=true;
            string? recovery=(MainWindow as MainWindow)?.SaveRecovery();
            e.Handled=true;
            MessageBox.Show("The app encountered an error and needs to close.\n\n"+
                (recovery is null ? "" : "Your setup was recovered to:\n"+recovery+"\n\n")+
                "Error details: "+Diagnostics.DirectoryPath,"TMS Scoreboard Studio",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Diagnostics.Log("Unhandled failure",e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { Diagnostics.Log("Background failure",e.Exception);e.SetObserved(); };
    }
}
