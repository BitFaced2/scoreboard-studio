using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScoreboardStudio.Core;

public sealed record UpdateOffer(string Version, string Tag, Uri Download, long Size, string Sha256, bool Preview)
{
    public string ReleaseUrl => "https://github.com/BitFaced2/scoreboard-studio/releases/tag/"+Tag;
}

public sealed class GitHubUpdates : IDisposable
{
    public const string Repository = "BitFaced2/scoreboard-studio";
    public const long MaximumZipBytes = 256L*1024*1024;
    private readonly HttpClient client;
    public GitHubUpdates(HttpMessageHandler? handler=null)
    {
        client=new(handler ?? new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromMinutes(10),MaxResponseContentBufferSize=4_000_000};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TMS-Scoreboard-Studio-Updater");
    }
    public static UpdateOffer? SelectRelease(string json,Version current)
    {
        using var document=JsonDocument.Parse(json);
        var offers=new List<UpdateOffer>();
        foreach(var release in document.RootElement.EnumerateArray())
        {
            if(release.GetProperty("draft").GetBoolean())continue;
            string tag=release.GetProperty("tag_name").GetString() ?? "";
            if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..],out var version) || version<=current)continue;
            string name=$"ScoreboardStudio-{version}-win-x64.zip";
            foreach(var asset in release.GetProperty("assets").EnumerateArray())
            {
                if(asset.GetProperty("name").GetString()!=name || asset.GetProperty("state").GetString()!="uploaded")continue;
                long size=asset.GetProperty("size").GetInt64();
                string digest=asset.TryGetProperty("digest",out var hash)?hash.GetString() ?? "":"";
                string url=asset.GetProperty("browser_download_url").GetString() ?? "";
                string expected=$"https://github.com/{Repository}/releases/download/{tag}/{name}";
                if(size<1||size>MaximumZipBytes||!Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$")||url!=expected)
                    throw new InvalidDataException("The newer release is not ready for verified in-app installation. Try again later or use GitHub Downloads.");
                offers.Add(new(version.ToString(),tag,new Uri(url),size,digest[7..],release.GetProperty("prerelease").GetBoolean()));
            }
        }
        return offers.OrderByDescending(o=>Version.Parse(o.Version)).FirstOrDefault();
    }
    public async Task<UpdateOffer?> CheckAsync(Version current,CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://api.github.com/repos/{Repository}/releases?per_page=100");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version","2026-03-10");
        using var response=await client.SendAsync(request,timeout.Token);
        CheckStatus(response);
        return SelectRelease(await response.Content.ReadAsStringAsync(timeout.Token),current);
    }
    public async Task DownloadAsync(UpdateOffer offer,string destination,IProgress<double>? progress,CancellationToken cancellation)
    {
        if(offer.Size<1||offer.Size>MaximumZipBytes)throw new InvalidDataException("Unexpected update size.");
        Uri address=offer.Download;
        bool created=false;
        try
        {
            for(int redirects=0;redirects<6;redirects++)
            {
                if(address.Scheme!="https" || !(address.Host=="github.com"||address.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Update download was redirected outside GitHub.");
                using var response=await client.GetAsync(address,HttpCompletionOption.ResponseHeadersRead,cancellation);
                if((int)response.StatusCode is >=300 and <400 && response.Headers.Location is {} next)
                {address=next.IsAbsoluteUri?next:new Uri(address,next);continue;}
                CheckStatus(response);
                if(response.Content.Headers.ContentLength is {} length && length!=offer.Size)throw new InvalidDataException("The update size does not match its release.");
                await using(var input=await response.Content.ReadAsStreamAsync(cancellation))
                await using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true))
                {
                    created=true;
                    byte[] buffer=new byte[131072];long total=0;int count;
                    while((count=await input.ReadAsync(buffer,cancellation))>0)
                    {
                        total+=count;if(total>offer.Size)throw new InvalidDataException("Update download exceeded the expected size.");
                        await output.WriteAsync(buffer.AsMemory(0,count),cancellation);progress?.Report(total*100.0/offer.Size);
                    }
                    if(total!=offer.Size)throw new InvalidDataException("The update download is incomplete.");
                }
                UpdatePackage.VerifyHash(destination,offer.Sha256);return;
            }
            throw new IOException("Too many redirects while downloading the update.");
        }
        catch {if(created&&File.Exists(destination))File.Delete(destination);throw;}
    }
    private static void CheckStatus(HttpResponseMessage response)
    {
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new IOException("GitHub is limiting update checks. Try again later, or download the release from GitHub.");
        response.EnsureSuccessStatusCode();
    }
    public void Dispose()=>client.Dispose();
}

public static class UpdatePackage
{
    public static bool IsManagedFile(string path) => path is "ScoreboardStudio.exe" or "README.md" or "START-HERE.txt" or "BUILD-INFO.txt" or "TMS Scoreboard Studio - User Guide.pdf"
        || Regex.IsMatch(path,@"^notices/(Microsoft\.NETCore\.App|Microsoft\.WindowsDesktop\.App)/(LICENSE|LICENSE\.TXT|THIRD-PARTY-NOTICES\.TXT)$");
    public static void VerifyHash(string path,string expected)
    {
        if(!Regex.IsMatch(expected,@"^[0-9a-fA-F]{64}$"))throw new InvalidDataException("Missing release checksum.");
        using var input=File.OpenRead(path);
        if(!Convert.ToHexString(SHA256.HashData(input)).Equals(expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update verification failed. The downloaded file does not match GitHub's SHA-256 checksum.");
    }
    public static void Extract(string archive,string sha256,string version,string destination,CancellationToken cancellation=default)
    {
        if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+$"))throw new InvalidDataException("Invalid update version.");
        VerifyHash(archive,sha256);
        if(Directory.Exists(destination))throw new IOException("Update staging folder already exists.");
        NoLinks(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        string prefix=$"ScoreboardStudio-{version}-win-x64/";
        using var zip=ZipFile.OpenRead(archive);
        if(zip.Entries.Count>128)throw new InvalidDataException("Unexpected update package contents.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long size=0;
        foreach(var entry in zip.Entries)
        {
            string name=entry.FullName.Replace('\\','/');
            if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Update package contains a link.");
            if(!name.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidDataException("Unexpected package folder.");
            var relative=name[prefix.Length..];
            if(relative.EndsWith('/')||relative.Length==0)
            {
                if(relative is not ("" or "notices/" or "notices/Microsoft.NETCore.App/" or "notices/Microsoft.WindowsDesktop.App/"))
                    throw new InvalidDataException("Unexpected package directory.");
                continue;
            }
            if(!IsManagedFile(relative)||!names.Add(relative))throw new InvalidDataException("Unexpected or duplicate update file: "+relative);
            size+=entry.Length;if(size>512L*1024*1024)throw new InvalidDataException("Expanded update is too large.");
        }
        if(!names.Contains("ScoreboardStudio.exe")||!names.Contains("BUILD-INFO.txt"))throw new InvalidDataException("Update is missing required files.");
        Directory.CreateDirectory(destination);
        foreach(var entry in zip.Entries.Where(e=>!e.FullName.Replace('\\','/').EndsWith('/')))
        {
            cancellation.ThrowIfCancellationRequested();
            string target=Path.Combine(destination,entry.FullName.Replace('\\','/')[prefix.Length..].Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target,false);
        }
        VerifyExecutable(Path.Combine(destination,"ScoreboardStudio.exe"),version);
    }
    public static void VerifyExecutable(string path,string version)
    {
        var info=FileVersionInfo.GetVersionInfo(path);
        if(info.ProductName!="TMS Scoreboard Studio" || info.ProductVersion?.Split('+')[0]!=version)
            throw new InvalidDataException("The downloaded executable is not the expected TMS Scoreboard Studio version.");
    }
    public static void NoLinks(string path)
    {
        for(var directory=new DirectoryInfo(Path.GetFullPath(path));directory is not null;directory=directory.Parent)
            if(directory.Exists&&directory.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Update folders cannot be symbolic links or junctions.");
    }
    // All replacements are app-owned files. Media, saved layouts, shortcuts and
    // every other file in the portable directory remain untouched.
    public static string Install(string payload,string target,string version,string id,Action<int>? beforeReplace=null)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new ArgumentException("Invalid update identifier.");
        target=Path.GetFullPath(target);payload=Path.GetFullPath(payload);
        NoLinks(target);NoLinks(payload);
        if(!File.Exists(Path.Combine(target,"ScoreboardStudio.exe")))throw new IOException("The installed application could not be found.");
        VerifyExecutable(Path.Combine(payload,"ScoreboardStudio.exe"),version);
        var files=Directory.GetFiles(payload,"*",SearchOption.AllDirectories)
            .Select(p=>Path.GetRelativePath(payload,p).Replace('\\','/')).OrderBy(p=>p=="ScoreboardStudio.exe"?1:0).ThenBy(p=>p).ToArray();
        if(files.Any(p=>!IsManagedFile(p)))throw new InvalidDataException("Unexpected staged update files.");
        string staging=Path.Combine(target,".update-staging-"+id),backup=Path.Combine(target,".update-backup-"+id);
        if(Directory.Exists(staging)||Directory.Exists(backup))throw new IOException("Update staging or backup already exists.");
        Directory.CreateDirectory(staging);Directory.CreateDirectory(backup);
        var changed=new List<(string Name,bool Existed)>();
        try
        {
            foreach(string file in files)
            {
                NoLinks(Path.GetDirectoryName(Path.Combine(target,file))!);
                if(File.Exists(Path.Combine(target,file))&&File.GetAttributes(Path.Combine(target,file)).HasFlag(FileAttributes.ReparsePoint))throw new IOException("An installed file is a link.");
                string staged=Path.Combine(staging,file);Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                File.Copy(Path.Combine(payload,file),staged);
            }
            for(int i=0;i<files.Length;i++)
            {
                string name=files[i];beforeReplace?.Invoke(i);
                string live=Path.Combine(target,name),old=Path.Combine(backup,name);
                Directory.CreateDirectory(Path.GetDirectoryName(live)!);Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                bool existed=File.Exists(live);
                if(existed)File.Move(live,old);
                changed.Add((name,existed));
                File.Move(Path.Combine(staging,name),live);
            }
            Directory.Delete(staging,true);return backup;
        }
        catch(Exception failure)
        {
            var rollbackErrors=new List<Exception>();
            foreach(var change in changed.AsEnumerable().Reverse())
            {
                try
                {
                    string live=Path.Combine(target,change.Name);if(File.Exists(live))File.Delete(live);
                    if(change.Existed)File.Move(Path.Combine(backup,change.Name),live);
                }
                catch(Exception error){rollbackErrors.Add(error);}
            }
            if(rollbackErrors.Count>0)throw new AggregateException("Update failed; restore the previous files from "+backup,new[]{failure}.Concat(rollbackErrors));
            throw new IOException("Update was not installed. The previous application files were restored.",failure);
        }
    }
}
