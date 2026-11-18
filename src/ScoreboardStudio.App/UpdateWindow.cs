using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

internal sealed class UpdateWindow : Window
{
    private readonly GitHubUpdates client=new();
    private readonly CancellationTokenSource cancellation=new();
    private readonly TextBlock status=new() {TextWrapping=TextWrapping.Wrap,Margin=new(0,16,0,12)};
    private readonly ProgressBar progress=new() {Height=12,Minimum=0,Maximum=100,Margin=new(0,0,0,18)};
    private readonly Button action=new() {Content="Checking…",IsEnabled=false,Padding=new(16,9,16,9),Margin=new(0,0,10,0)};
    private readonly Button close=new() {Content="Close",Padding=new(16,9,16,9)};
    private UpdateOffer? offer;
    private bool busy,handedOff;
    internal UpdateSession? Prepared {get;private set;}
    internal bool IsChecking=>busy;
    internal string StatusText=>status.Text;
    public UpdateWindow()
    {
        Title="TMS Scoreboard Studio — Updates";Width=580;MinHeight=300;SizeToContent=SizeToContent.Height;MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-40);ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(16,21,30));Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=14;
        if(Application.Current.MainWindow is {} main)
        {Resources.Add(typeof(Button),main.FindResource(typeof(Button)));action.Style=(Style)main.FindResource("Primary");}
        var panel=new StackPanel {Margin=new Thickness(26)};
        panel.Children.Add(new TextBlock {Text="SOFTWARE UPDATES",FontWeight=FontWeights.Bold,FontSize=22,Foreground=new SolidColorBrush(Color.FromRgb(139,199,255))});
        panel.Children.Add(new TextBlock {Text="Installed version: "+AppUpdate.CurrentVersion,Margin=new(0,8,0,0)});
        panel.Children.Add(status);panel.Children.Add(progress);
        var buttons=new WrapPanel();buttons.Children.Add(action);buttons.Children.Add(close);panel.Children.Add(buttons);
        var releases=new Button {Content="GitHub downloads and release notes",Margin=new(0,18,0,0),Padding=new(8),HorizontalAlignment=HorizontalAlignment.Left};
        releases.Click+=(_,_)=> {try {Process.Start(new ProcessStartInfo(offer?.ReleaseUrl ?? "https://github.com/BitFaced2/scoreboard-studio/releases") {UseShellExecute=true});}catch(Exception ex){ShowError("Could not open your browser: "+ex.Message);}};
        panel.Children.Add(releases);Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Loaded+=async (_,_)=>await CheckAsync();
        action.Click+=async (_,_)=>await ActAsync();
        close.Click+=(_,_)=>Close();
        Closing+=(_,_)=>cancellation.Cancel();
        Closed+=(_,_)=> {client.Dispose();if(!handedOff&&Prepared is {} session)_=Task.Run(()=>AppUpdate.DeleteTree(AppUpdate.Folder(session)));};
    }
    private async Task CheckAsync()
    {
        busy=true;status.Text="Checking the project's GitHub releases…";progress.IsIndeterminate=true;
        try
        {
            offer=await client.CheckAsync(AppUpdate.CurrentVersion,cancellation.Token);
            status.Text=offer is null ? "You're up to date. Updates are checked only when you open this window." :
                $"Version {offer.Version}{(offer.Preview?" (preview)":"")} is available ({offer.Size/1048576.0:0.0} MB). Download it first, then choose Install and restart. Your saved setup files and media stay in place.";
            action.Content=offer is null?"Check again":"Download update";action.IsEnabled=true;
        }
        catch(OperationCanceledException) {if(!cancellation.IsCancellationRequested)ShowError("The update check timed out. Try again.");}
        catch(Exception ex) {ShowError(ex.Message);}
        finally {busy=false;progress.IsIndeterminate=false;}
    }
    private async Task ActAsync()
    {
        if(busy)return;
        if(Prepared is not null) {handedOff=true;DialogResult=true;return;}
        if(offer is null) {await CheckAsync();return;}
        busy=true;action.IsEnabled=false;close.Content="Cancel";status.Text="Downloading and verifying the update…";
        try
        {
            Prepared=await AppUpdate.PrepareAsync(offer,client,new Progress<double>(value=>progress.Value=value),cancellation.Token);
            status.Text=$"Version {offer.Version} is verified and ready. Install and restart will close this app, replace its program files, then reopen your saved setup with outputs stopped.";
            action.Content="Install and restart";action.IsEnabled=true;close.Content="Close";
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) {Diagnostics.Log("Download update",ex);ShowError(ex.Message);}
        finally {busy=false;}
    }
    private void ShowError(string message)
    {
        status.Text=message+" Your installed app has not been changed.";
        action.Content=offer is null?"Try again":"Retry download";action.IsEnabled=true;close.Content="Close";
    }
}
