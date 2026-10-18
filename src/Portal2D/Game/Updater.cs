using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Portal2D.Game;

/// <summary>
/// Self-updater. Checks the latest GitHub release, downloads the build for this platform
/// (asset "Portal2D-&lt;rid&gt;.zip" containing the flat published files), and swaps the files in after
/// the game exits using a tiny helper script, then relaunches.
/// </summary>
public sealed class Updater
{
    /// <summary>Public repository that hosts release builds.</summary>
    public const string ReleaseRepo = "xandertaylorx/portal-2d-releases";

    public enum State { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed, Disabled }

    public volatile State Status = State.Idle;
    public string LatestVersion = "";
    public string Notes = "";
    public string Error = "";
    public volatile float Progress;
    string? assetUrl;
    string? stagedDir;

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string Rid
    {
        get
        {
            string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            if (OperatingSystem.IsMacOS()) return $"osx-{arch}";
            if (OperatingSystem.IsWindows()) return $"win-{arch}";
            return $"linux-{arch}";
        }
    }

    static string InstallDir => AppContext.BaseDirectory;

    /// <summary>Running from a development build (dotnet run / bin folder): don't self-update.</summary>
    public static bool IsDevBuild
    {
        get
        {
            var d = InstallDir.Replace('\\', '/');
            return d.Contains("/bin/Debug/") || d.Contains("/bin/Release/") || !File.Exists(Path.Combine(InstallDir, ExeName));
        }
    }

    static string ExeName => OperatingSystem.IsWindows() ? "Portal2D.exe" : "Portal2D";

    static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Portal2D/{CurrentVersion}");
        return c;
    }

    public void CheckAsync()
    {
        if (IsDevBuild) { Status = State.Disabled; return; }
        if (Status is State.Checking or State.Downloading) return;
        Status = State.Checking;
        Task.Run(async () =>
        {
            try
            {
                using var http = NewClient();
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                var json = await http.GetStringAsync($"https://api.github.com/repos/{ReleaseRepo}/releases/latest");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                LatestVersion = tag.TrimStart('v', 'V');
                Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                assetUrl = null;
                foreach (var a in root.GetProperty("assets").EnumerateArray())
                    if (a.GetProperty("name").GetString() == $"Portal2D-{Rid}.zip")
                        assetUrl = a.GetProperty("browser_download_url").GetString();
                Status = IsNewer(LatestVersion, CurrentVersion) && assetUrl != null ? State.Available : State.UpToDate;
            }
            catch (Exception e)
            {
                Error = e.Message;
                Status = State.Failed;
            }
        });
    }

    public static bool IsNewer(string a, string b)
    {
        static int[] Parse(string s) => s.Split('.', '-').Take(3).Select(p => int.TryParse(p, out int n) ? n : 0)
            .Concat([0, 0, 0]).Take(3).ToArray();
        var x = Parse(a); var y = Parse(b);
        for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] > y[i];
        return false;
    }

    public void DownloadAsync()
    {
        if (Status != State.Available || assetUrl == null) return;
        Status = State.Downloading;
        Progress = 0;
        Task.Run(async () =>
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), $"portal2d-update-{LatestVersion}");
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                string zip = Path.Combine(tmp, "update.zip");

                using (var http = NewClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(10);
                    using var resp = await http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1;
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    await using var dst = File.Create(zip);
                    var buf = new byte[81920];
                    long read = 0;
                    int n;
                    while ((n = await src.ReadAsync(buf)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n));
                        read += n;
                        if (total > 0) Progress = read / (float)total;
                    }
                }

                string files = Path.Combine(tmp, "files");
                ZipFile.ExtractToDirectory(zip, files);
                if (!File.Exists(Path.Combine(files, ExeName))) throw new InvalidDataException("update package is missing the game executable");
                stagedDir = files;
                Progress = 1;
                Status = State.Ready;
            }
            catch (Exception e)
            {
                Error = e.Message;
                Status = State.Failed;
            }
        });
    }

    /// <summary>Test hook: stage an update from a local zip instead of downloading it.</summary>
    public void StageLocal(string zip)
    {
        string files = Path.Combine(Path.GetTempPath(), "portal2d-update-local", "files");
        if (Directory.Exists(files)) Directory.Delete(files, true);
        ZipFile.ExtractToDirectory(zip, files);
        stagedDir = files;
        Status = State.Ready;
    }

    /// <summary>Launch the helper that copies the staged files over the install once we exit, then relaunches.</summary>
    public bool LaunchSwapAndQuit()
    {
        if (Status != State.Ready || stagedDir == null) return false;
        int pid = Environment.ProcessId;
        string dest = InstallDir.TrimEnd('/', '\\');
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string script = Path.Combine(Path.GetTempPath(), "portal2d-update.cmd");
                File.WriteAllText(script,
                    "@echo off\r\n" +
                    ":wait\r\n" +
                    $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                    $"xcopy /E /Y /Q \"{stagedDir}\\*\" \"{dest}\\\" >nul\r\n" +
                    $"start \"\" \"{Path.Combine(dest, ExeName)}\"\r\n");
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            else
            {
                string? bundle = null;
                int idx = dest.IndexOf(".app/Contents/MacOS", StringComparison.Ordinal);
                if (idx > 0) bundle = dest[..(idx + 4)];
                string relaunch = bundle != null ? $"open \"{bundle}\"" : $"\"{Path.Combine(dest, ExeName)}\" >/dev/null 2>&1 &";
                string script = Path.Combine(Path.GetTempPath(), "portal2d-update.sh");
                File.WriteAllText(script,
                    "#!/bin/sh\n" +
                    $"while kill -0 {pid} 2>/dev/null; do sleep 0.2; done\n" +
                    $"cp -R \"{stagedDir}/.\" \"{dest}/\"\n" +
                    $"chmod +x \"{Path.Combine(dest, ExeName)}\"\n" +
                    relaunch + "\n");
                Process.Start(new ProcessStartInfo("/bin/sh", $"\"{script}\"") { UseShellExecute = false });
            }
            return true;
        }
        catch (Exception e)
        {
            Error = e.Message;
            Status = State.Failed;
            return false;
        }
    }
}
