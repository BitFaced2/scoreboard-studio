using System.IO;
using System.Windows;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private bool restartingForUpdate;
    private async void CheckUpdatesClick(object sender,RoutedEventArgs e)
    {
        if(output||locked||EditingPerspective) {Notice.Text="Stop NDI outputs, unlock editing, and finish straightening before updating.";return;}
        var window=new UpdateWindow {Owner=this};
        if(window.ShowDialog()!=true || window.Prepared is not {} session)return;
        if(!ConfirmDiscard()) {_=Task.Run(()=>AppUpdate.DeleteTree(AppUpdate.Folder(session)));return;}
        try
        {
            IsEnabled=false;
            await AppUpdate.StartInstallerAsync(session,savePath);
            restartingForUpdate=true;
            Close();
        }
        catch(Exception ex)
        {
            IsEnabled=true;Diagnostics.Log("Start update",ex);
            Notice.Text="Could not start the update: "+ex.Message+" The existing app is unchanged.";
            _=Task.Run(()=>AppUpdate.DeleteTree(AppUpdate.Folder(session)));
        }
    }
    private void CompleteUpdateStartup(string[] args)
    {
        int i=Array.IndexOf(args,"--update-result");
        if(i<0||i+1>=args.Length)return;
        try
        {
            var session=AppUpdate.Read(args[i+1]);
            if(session.Success && session.Version!=AppUpdate.CurrentVersion.ToString())throw new IOException("The restarted version does not match the update. The backup has been retained.");
            if(session.RestoreSetup is {} path && File.Exists(path))OpenLayout(path);
            Notice.Text=session.Success ? $"Updated to {AppUpdate.CurrentVersion}. Review your setup before starting outputs."
                : "The update could not be installed: "+session.Error;
            if(session.TestExitAfterRestart)
            {
                File.WriteAllText(Path.Combine(AppUpdate.Folder(session),"launch-test.txt"),System.Text.Json.JsonSerializer.Serialize(new {
                    version=AppUpdate.CurrentVersion.ToString(),restored=savePath==session.RestoreSetup,fields=fields.Count,output,session.Success
                }));
            }
            else if(session.Success)_=AppUpdate.CleanupAsync(session);
        }
        catch(Exception ex) {Diagnostics.Log("Restore after update",ex);Notice.Text="App started. Load your saved setup to continue. Update details: "+ex.Message;}
    }
}
