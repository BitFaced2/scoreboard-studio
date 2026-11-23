using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

public partial class MainWindow
{
    private async Task UpdateSmokeTest(string directory)
    {
        var report=new List<string>();
        try
        {
            Directory.CreateDirectory(directory);
            var args=Environment.GetCommandLineArgs();int packageIndex=Array.IndexOf(args,"--update-package");
            string package=packageIndex>=0&&packageIndex+1<args.Length?Path.GetFullPath(args[packageIndex+1]):throw new ArgumentException("Supply --update-package.");
            using var client=new GitHubUpdates();
            var offer=await client.CheckAsync(new Version(0,0,0),default) ?? throw new Exception("No public update release found.");
            var previous=await AppUpdate.PrepareAsync(offer,client,null,default);
            report.Add($"PASS: public GitHub release discovery, download, checksum and executable validation ({offer.Version}).");
            string target=Path.Combine(directory,"portable-app");Directory.CreateDirectory(target);
            string oldPayload=Path.Combine(AppUpdate.Folder(previous),"payload");
            foreach(string file in Directory.GetFiles(oldPayload,"*",SearchOption.AllDirectories))
            {
                string destination=Path.Combine(target,Path.GetRelativePath(oldPayload,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination);
            }
            AppUpdate.DeleteTree(AppUpdate.Folder(previous));
            string setup=Path.Combine(target,"venue.scoreboard.json"),media=Path.Combine(target,"operator-recording.mp4");
            new Layout {Name="Update restore check",Fields=[new ScoreField {Name="Preserved field"}]}.Save(setup);
            File.WriteAllText(media,"operator media must remain unchanged");
            string originalSetup=File.ReadAllText(setup);
            async Task<UpdateSession> RunHelper(bool forceFailure)
            {
                var session=new UpdateSession {Version=AppUpdate.CurrentVersion.ToString(),TargetDirectory=target,RestoreSetup=setup,TestExitAfterRestart=true};
                string folder=AppUpdate.Folder(session);Directory.CreateDirectory(folder);File.Copy(package,Path.Combine(folder,"package.zip"));
                using(var input=File.OpenRead(package))session.Sha256=Convert.ToHexString(SHA256.HashData(input));
                // Exercise the updater shipped in the installed release, not
                // the implementation from the incoming package.
                string helper=Path.Combine(folder,"ScoreboardStudio.Update.exe");File.Copy(Path.Combine(target,"ScoreboardStudio.exe"),helper);
                var waitStart=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};waitStart.ArgumentList.Add("--update-test-wait");
                using var parent=Process.Start(waitStart)!;
                session.ParentPid=parent.Id;session.ParentStarted=parent.StartTime.ToUniversalTime().Ticks;
                AppUpdate.Save(session);
                using var lockedFile=forceFailure?new FileStream(Path.Combine(target,"README.md"),FileMode.Open,FileAccess.Read,FileShare.None):null;
                using var installer=AppUpdate.StartHelper(session,helper);
                await Task.Delay(200);
                if(parent.HasExited)throw new Exception("Parent wait fixture ended too soon.");
                if(AppUpdate.Read(AppUpdate.StatePath(session)).Success)throw new Exception("Installer ran before the original app exited.");
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));await installer.WaitForExitAsync(deadline.Token);
                if(installer.ExitCode!=(forceFailure?1:0))throw new Exception("Unexpected installer result: "+AppUpdate.Read(AppUpdate.StatePath(session)).Error);
                string marker=Path.Combine(folder,"launch-test.txt");
                for(int i=0;i<100&&!File.Exists(marker);i++)await Task.Delay(100);
                if(!File.Exists(marker))throw new Exception("The installed application did not restart.");
                using var restarted=JsonDocument.Parse(File.ReadAllText(marker));
                if(restarted.RootElement.GetProperty("version").GetString()!=AppUpdate.CurrentVersion.ToString() || !restarted.RootElement.GetProperty("restored").GetBoolean()
                    || restarted.RootElement.GetProperty("fields").GetInt32()!=1 || restarted.RootElement.GetProperty("output").GetBoolean())
                    throw new Exception("Restart did not restore the saved setup with outputs stopped.");
                if(File.ReadAllText(setup)!=originalSetup||File.ReadAllText(media)!="operator media must remain unchanged")throw new Exception("Update modified user files.");
                return AppUpdate.Read(AppUpdate.StatePath(session));
            }
            var success=await RunHelper(false);
            report.Add("PASS: installer waits for app exit, replaces portable files, restarts the new app, restores the saved layout, and leaves outputs stopped.");
            var failure=await RunHelper(true);
            report.Add("PASS: locked-file installation failure rolls back and restarts the previous app; layouts and recordings remain unchanged.");
            File.WriteAllLines(Path.Combine(directory,"update-checks.txt"),report);
            await Task.Delay(300);
            AppUpdate.DeleteTree(AppUpdate.Folder(success));AppUpdate.DeleteTree(AppUpdate.Folder(failure));
        }
        catch(Exception ex) {File.WriteAllText(Path.Combine(directory,"update-error.txt"),ex.ToString());Environment.ExitCode=1;}
        finally {dirty=false;Close();}
    }
}
