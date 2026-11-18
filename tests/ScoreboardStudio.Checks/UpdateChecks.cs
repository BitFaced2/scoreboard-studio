using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ScoreboardStudio.Core;

internal static class UpdateChecks
{
    internal static void Register(Action<string,Action> check,string? executable)
    {
        check("Updates select the newest published compatible release, including previews, without downgrading",()=> {
            string json=JsonSerializer.Serialize(new[]{Release("v0.4.1"),Release("v0.5.0",preview:true),Release("v9.0.0",draft:true)});
            Require(GitHubUpdates.SelectRelease(json,new Version(0,4,1))?.Version=="0.5.0","Newest preview was not selected.");
            Require(GitHubUpdates.SelectRelease(json,new Version(0,5,0)) is null,"Updater tried to downgrade or reinstall.");
        });
        check("Updates reject releases with untrusted asset URLs or missing checksums",()=> {
            Reject(()=>GitHubUpdates.SelectRelease(JsonSerializer.Serialize(new[]{Release("v0.5.0",url:"https://example.com/app.zip")}),new Version(0,4,1)));
            Reject(()=>GitHubUpdates.SelectRelease(JsonSerializer.Serialize(new[]{Release("v0.5.0",hash:"")}),new Version(0,4,1)));
        });
        check("Update downloads follow GitHub redirects and reject incomplete or corrupted bytes",()=>InTemp(folder=> {
            byte[] bytes=[1,2,3,4];string hash=Convert.ToHexString(SHA256.HashData(bytes));
            var offer=new UpdateOffer("0.5.0","v0.5.0",new Uri("https://github.com/BitFaced2/scoreboard-studio/releases/download/v0.5.0/ScoreboardStudio-0.5.0-win-x64.zip"),4,hash,true);
            int calls=0;
            using(var client=new GitHubUpdates(new FakeHandler((_,_)=>Task.FromResult(++calls==1?
                Redirect("https://release-assets.githubusercontent.com/package") : Bytes(bytes)))))
                client.DownloadAsync(offer,Path.Combine(folder,"good.zip"),null,default).GetAwaiter().GetResult();
            Require(File.ReadAllBytes(Path.Combine(folder,"good.zip")).SequenceEqual(bytes),"Download changed bytes.");
            using(var client=new GitHubUpdates(new FakeHandler((_,_)=>Task.FromResult(Bytes([4,3,2,1])))))
                Reject(()=>client.DownloadAsync(offer,Path.Combine(folder,"bad.zip"),null,default).GetAwaiter().GetResult());
            Require(!File.Exists(Path.Combine(folder,"bad.zip")),"Corrupt download was retained.");
            using(var client=new GitHubUpdates(new FakeHandler((_,_)=>Task.FromResult(Bytes([1,2])))))
                Reject(()=>client.DownloadAsync(offer,Path.Combine(folder,"short.zip"),null,default).GetAwaiter().GetResult());
            using(var client=new GitHubUpdates(new FakeHandler((_,_)=>Task.FromResult(Redirect("https://example.com/package")))))
                Reject(()=>client.DownloadAsync(offer,Path.Combine(folder,"external.zip"),null,default).GetAwaiter().GetResult());
            using(var client=new GitHubUpdates(new FakeHandler((_,_)=>Task.FromResult(Bytes(bytes)))))
                Reject(()=>client.DownloadAsync(offer,Path.Combine(folder,"good.zip"),null,default).GetAwaiter().GetResult());
            Require(File.ReadAllBytes(Path.Combine(folder,"good.zip")).SequenceEqual(bytes),"Download failure deleted an existing file.");
        }));
        check("Update network failures and cancellation leave installed files untouched",()=>InTemp(folder=> {
            string sentinel=Path.Combine(folder,"ScoreboardStudio.exe");File.WriteAllText(sentinel,"installed app");
            using(var client=new GitHubUpdates(new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
                Reject(()=>client.CheckAsync(new Version(0,4,1),default).GetAwaiter().GetResult());
            using(var client=new GitHubUpdates(new FakeHandler(async (_,token)=>{await Task.Delay(Timeout.Infinite,token);return Bytes([]);})))
            using(var cancel=new CancellationTokenSource(50))
                Reject(()=>client.CheckAsync(new Version(0,4,1),cancel.Token).GetAwaiter().GetResult());
            Require(File.ReadAllText(sentinel)=="installed app","Network check modified installed files.");
        }));
        if(executable is null)return;
        string version=FileVersionInfo.GetVersionInfo(executable).ProductVersion!.Split('+')[0];
        byte[] exe=File.ReadAllBytes(executable);
        check("Update ZIP validation rejects traversal, duplicate names, links, wrong version and checksum",()=>InTemp(folder=> {
            string prefix=$"ScoreboardStudio-{version}-win-x64/";
            foreach(string bad in new[]{prefix+"../../outside.txt",prefix+"README.md",prefix+"settings.scoreboard.json",prefix+"C:/outside.txt"})
            {
                string zip=Archive(folder,version,exe,bad);
                string target=Path.Combine(folder,Guid.NewGuid().ToString("N"));
                Reject(()=>UpdatePackage.Extract(zip,Hash(zip),version,target));
                Require(!Directory.Exists(target),"Invalid ZIP created an extraction directory.");
            }
            string linked=Archive(folder,version,exe,prefix+"START-HERE.txt",link:true);
            Reject(()=>UpdatePackage.Extract(linked,Hash(linked),version,Path.Combine(folder,"link")));
            string good=Archive(folder,version,exe);
            Reject(()=>UpdatePackage.Extract(good,new string('0',64),version,Path.Combine(folder,"corrupt")));
            Reject(()=>UpdatePackage.VerifyExecutable(executable,"99.0.0"));
        }));
        check("Update installation preserves layouts and footage and retains a rollback copy",()=>InTemp(folder=> {
            string zip=Archive(folder,version,exe,windowsPaths:true),payload=Path.Combine(folder,"payload"),target=Path.Combine(folder,"app");
            UpdatePackage.Extract(zip,Hash(zip),version,payload);Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target,"ScoreboardStudio.exe"),exe);File.WriteAllText(Path.Combine(target,"README.md"),"old readme");
            File.WriteAllText(Path.Combine(target,"venue.scoreboard.json"),"saved setup");File.WriteAllText(Path.Combine(target,"clip.mp4"),"recording");
            string backup=UpdatePackage.Install(payload,target,version,Guid.NewGuid().ToString("N"));
            Require(File.ReadAllText(Path.Combine(target,"README.md"))=="new readme","Program file was not updated.");
            Require(File.ReadAllText(Path.Combine(backup,"README.md"))=="old readme","Backup was not retained.");
            Require(File.ReadAllText(Path.Combine(target,"venue.scoreboard.json"))=="saved setup"&&File.ReadAllText(Path.Combine(target,"clip.mp4"))=="recording","User files changed.");
        }));
        check("Failed update installation restores original files and removes newly introduced files",()=>InTemp(folder=> {
            string zip=Archive(folder,version,exe),payload=Path.Combine(folder,"payload"),target=Path.Combine(folder,"app");
            UpdatePackage.Extract(zip,Hash(zip),version,payload);Directory.CreateDirectory(target);
            File.WriteAllBytes(Path.Combine(target,"ScoreboardStudio.exe"),exe);File.WriteAllText(Path.Combine(target,"README.md"),"old readme");
            File.WriteAllText(Path.Combine(target,"venue.scoreboard.json"),"saved setup");
            Reject(()=>UpdatePackage.Install(payload,target,version,Guid.NewGuid().ToString("N"),i=>{if(i==2)throw new IOException("Simulated write failure");}));
            Require(File.ReadAllText(Path.Combine(target,"README.md"))=="old readme","Rollback did not restore the previous file.");
            Require(!File.Exists(Path.Combine(target,"BUILD-INFO.txt")),"Rollback retained a newly introduced file.");
            Require(File.ReadAllBytes(Path.Combine(target,"ScoreboardStudio.exe")).SequenceEqual(exe)&&File.ReadAllText(Path.Combine(target,"venue.scoreboard.json"))=="saved setup","Rollback damaged installed data.");
        }));
    }
    private static object Release(string tag,bool draft=false,bool preview=false,string? url=null,string? hash=null)=>new {
        tag_name=tag,draft,prerelease=preview,assets=new[]{new {name=$"ScoreboardStudio-{tag[1..]}-win-x64.zip",state="uploaded",size=1024,
            digest=hash ?? "sha256:"+new string('a',64),browser_download_url=url ?? $"https://github.com/BitFaced2/scoreboard-studio/releases/download/{tag}/ScoreboardStudio-{tag[1..]}-win-x64.zip"}}
    };
    private static HttpResponseMessage Bytes(byte[] data)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
    private static HttpResponseMessage Redirect(string url) {var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri(url);return response;}
    private sealed class FakeHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>action(request,cancellation);}
    private static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
    private static void Reject(Action action) {try {action();}catch(Exception){return;}throw new Exception("Unsafe or invalid update was accepted.");}
    private static void InTemp(Action<string> action)
    {
        string folder=Path.Combine(Path.GetTempPath(),"TMS-Update-Checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try {action(folder);}finally {Directory.Delete(folder,true);}
    }
    private static string Hash(string path) {using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    private static string Archive(string folder,string version,byte[] exe,string? extra=null,bool link=false,bool windowsPaths=false)
    {
        string path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".zip");
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);string root=$"ScoreboardStudio-{version}-win-x64/";
        if(windowsPaths)root=root.Replace('/','\\');
        using(var writer=zip.CreateEntry(root+"ScoreboardStudio.exe").Open())writer.Write(exe);
        using(var writer=new StreamWriter(zip.CreateEntry(root+"README.md").Open()))writer.Write("new readme");
        using(var writer=new StreamWriter(zip.CreateEntry(root+"BUILD-INFO.txt").Open()))writer.Write(version);
        if(extra is not null) {var entry=zip.CreateEntry(extra);if(link)entry.ExternalAttributes=0xA000<<16;using var writer=new StreamWriter(entry.Open());writer.Write("unexpected");}
        return path;
    }
}
