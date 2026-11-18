using System.IO;

namespace ScoreboardStudio.App;

internal static class Diagnostics
{
    internal static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TMS Scoreboard Studio","Diagnostics");
    private static readonly object Gate=new();
    internal static void Log(string context,Exception error)
    {
        try
        {
            lock(Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                string path=Path.Combine(DirectoryPath,"app.log");
                if(File.Exists(path)&&new FileInfo(path).Length>2_000_000)File.Move(path,path+".previous",true);
                File.AppendAllText(path,$"{DateTimeOffset.Now:O} [{typeof(App).Assembly.GetName().Version}] {context}\n{error}\n\n");
            }
        }
        catch { /* A logging failure must not hide the original error. */ }
    }
}
