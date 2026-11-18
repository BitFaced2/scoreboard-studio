using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ScoreboardStudio.Core;

namespace ScoreboardStudio.App;

internal sealed class UpdateSession
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Version {get;set;}="";
    public string Sha256 {get;set;}="";
    public string TargetDirectory {get;set;}="";
    public int ParentPid {get;set;}
    public long ParentStarted {get;set;}
    public string? RestoreSetup {get;set;}
    public bool Success {get;set;}
    public string? Error {get;set;}
    public bool TestExitAfterRestart {get;set;}
}

internal static class AppUpdate
{
    internal static string CacheRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TMS Scoreboard Studio","Updates");
    internal static Version CurrentVersion {get {var v=typeof(App).Assembly.GetName().Version!;return new(v.Major,v.Minor,v.Build);}}
    internal static string Folder(UpdateSession session)
    {
        if(!Guid.TryParseExact(session.Id,"N",out _))throw new InvalidDataException("Invalid update session.");
        return Path.Combine(CacheRoot,session.Id);
    }
    internal static string StatePath(UpdateSession session)=>Path.Combine(Folder(session),"session.json");
    internal static UpdateSession Read(string path)
    {
        if(new FileInfo(path).Length>16384)throw new InvalidDataException("Invalid update session size.");
        var session=JsonSerializer.Deserialize<UpdateSession>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing update session.");
        if(!Path.GetFullPath(path).Equals(StatePath(session),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update session is outside the update cache.");
        UpdatePackage.NoLinks(Folder(session));return session;
    }
    internal static void Save(UpdateSession session)=>File.WriteAllText(StatePath(session),JsonSerializer.Serialize(session));
    internal static async Task<UpdateSession> PrepareAsync(UpdateOffer offer,GitHubUpdates client,IProgress<double>? progress,CancellationToken cancellation)
    {
        var session=new UpdateSession {Version=offer.Version,Sha256=offer.Sha256};
        string folder=Folder(session);Directory.CreateDirectory(folder);
        try
        {
            string zip=Path.Combine(folder,"package.zip");
            await client.DownloadAsync(offer,zip,progress,cancellation);
            await Task.Run(()=>UpdatePackage.Extract(zip,session.Sha256,session.Version,Path.Combine(folder,"payload"),cancellation),cancellation);
            cancellation.ThrowIfCancellationRequested();
            return session;
        }
        catch {DeleteTree(folder);throw;}
    }
    internal static async Task StartInstallerAsync(UpdateSession session,string? setup)
    {
        string exe=Environment.ProcessPath ?? throw new IOException("Cannot locate the running application.");
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"ScoreboardStudio.dll")))
            throw new IOException("In-app installation is supported by the portable GitHub release. This is a development build.");
        session.TargetDirectory=Path.GetDirectoryName(exe)!;
        session.ParentPid=Environment.ProcessId;
        session.ParentStarted=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        session.RestoreSetup=setup is null?null:Path.GetFullPath(setup);
        if(RunningIn(session.TargetDirectory,Environment.ProcessId))throw new IOException("Close the other copy of this app, then retry the update.");
        UpdatePackage.NoLinks(session.TargetDirectory);
        // Check write access before closing the application.
        string probe=Path.Combine(session.TargetDirectory,".update-access-"+session.Id);
        using(new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { }
        File.Delete(probe);
        string helper=Path.Combine(Folder(session),"ScoreboardStudio.Update.exe");
        await Task.Run(()=>File.Copy(exe,helper,false));
        Save(session);
        StartHelper(session,helper).Dispose();
    }
    internal static Process StartHelper(UpdateSession session,string helper)
    {
        var start=new ProcessStartInfo(helper) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Folder(session)};
        start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(StatePath(session));
        return Process.Start(start) ?? throw new IOException("Could not start the update installer.");
    }
    internal static async Task<int> ApplyAsync(string statePath)
    {
        UpdateSession session=Read(statePath);
        try
        {
            try
            {
                using var parent=Process.GetProcessById(session.ParentPid);
                if(parent.StartTime.ToUniversalTime().Ticks==session.ParentStarted)
                {
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    await parent.WaitForExitAsync(timeout.Token);
                }
            }
            catch(ArgumentException) { /* Parent has already closed. */ }
            if(RunningIn(session.TargetDirectory))throw new IOException("Close the other copy of Scoreboard Studio before installing the update.");
            string folder=Folder(session),payload=Path.Combine(folder,"install-payload");
            // Reverify and re-extract after shutdown, before touching installed files.
            UpdatePackage.Extract(Path.Combine(folder,"package.zip"),session.Sha256,session.Version,payload);
            UpdatePackage.Install(payload,session.TargetDirectory,session.Version,session.Id);
            session.Success=true;session.Error=null;
        }
        catch(Exception ex) {session.Success=false;session.Error=ex.Message;Diagnostics.Log("Update installation",ex);}
        Save(session);
        try
        {
            if(!RunningIn(session.TargetDirectory))
            {
                var start=new ProcessStartInfo(Path.Combine(session.TargetDirectory,"ScoreboardStudio.exe")) {UseShellExecute=false,WorkingDirectory=session.TargetDirectory};
                start.ArgumentList.Add("--update-result");start.ArgumentList.Add(statePath);
                if(session.TestExitAfterRestart)start.ArgumentList.Add("--update-test-exit");
                Process.Start(start)?.Dispose();
            }
        }
        catch(Exception ex) {Diagnostics.Log("Restart after update",ex);return 1;}
        return session.Success?0:1;
    }
    private static bool RunningIn(string directory,int exceptPid=0)
    {
        string target=Path.GetFullPath(Path.Combine(directory,"ScoreboardStudio.exe"));
        foreach(var process in Process.GetProcessesByName("ScoreboardStudio"))
        {
            using(process)
            {
                if(process.Id==exceptPid)continue;
                try {if(process.MainModule?.FileName?.Equals(target,StringComparison.OrdinalIgnoreCase)==true)return true;}
                catch(System.ComponentModel.Win32Exception) { }
                catch(InvalidOperationException) { }
            }
        }
        return false;
    }
    internal static async Task CleanupAsync(UpdateSession session)
    {
        if(session.Success)
        {
            string actual=Path.GetDirectoryName(Environment.ProcessPath!)!;
            if(!actual.Equals(session.TargetDirectory,StringComparison.OrdinalIgnoreCase))return;
            string backup=Path.Combine(actual,".update-backup-"+session.Id);
            DeleteTree(backup);
        }
        for(int attempt=0;attempt<10;attempt++)
        {
            if(DeleteTree(Folder(session)))return;
            await Task.Delay(500);
        }
    }
    internal static bool DeleteTree(string folder)
    {
        try
        {
            if(!Directory.Exists(folder))return true;
            UpdatePackage.NoLinks(folder);
            foreach(string item in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes=File.GetAttributes(item);
                if(attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("An update cache entry is a link.");
                if(attributes.HasFlag(FileAttributes.Directory)) {if(!DeleteTree(item))return false;}
                else File.Delete(item);
            }
            Directory.Delete(folder);return true;
        }
        catch(Exception ex) {Diagnostics.Log("Update cleanup",ex);return false;}
    }
}
