using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

// Compiled against the .NET Framework included with Windows 10/11, before .NET 9 is available.
internal static class Program
{
    private const string DownloadUrl = "https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0";
    private const int HostLibraryMissing = unchecked((int)0x80008083);
    private const int FrameworkMissing = unchecked((int)0x80008096);

    [STAThread]
    private static int Main()
    {
        var appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CatFish");
        if (!File.Exists(appPath + ".dll") || !File.Exists(appPath + ".runtimeconfig.json") || !File.Exists(appPath + ".deps.json"))
        {
            ShowError("CatFish 缺少运行文件。请完整解压发布包后重试。");
            return 1;
        }

        var dotnetPath = FindDotnetPath();
        if (!File.Exists(dotnetPath))
        {
            OfferRuntimeDownload();
            return HostLibraryMissing;
        }

        var start = new ProcessStartInfo(dotnetPath, "exec \"" + appPath + ".dll\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        int exitCode;
        try
        {
            using (var app = Process.Start(start))
            {
                app.WaitForExit();
                exitCode = app.ExitCode;
            }
        }
        catch (Win32Exception ex)
        {
            ShowError("无法启动 CatFish。请确认发布文件完整后重试。\n\n" + ex.Message);
            return 1;
        }

        if (exitCode == HostLibraryMissing || exitCode == FrameworkMissing)
            OfferRuntimeDownload();
        else if (exitCode != 0)
            ShowError("CatFish 未能正常运行。请确认发布文件完整后重试。\n\n错误代码：0x" + exitCode.ToString("X8"));
        return exitCode;
    }

    private static string FindDotnetPath()
    {
        var customRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64") ?? Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (Directory.Exists(customRoot)) return Path.Combine(customRoot, "dotnet.exe");

        // The .NET installer records x64 installations in the 32-bit registry view.
        using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        using (var install = machine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"))
        {
            var registeredRoot = install == null ? null : install.GetValue("InstallLocation") as string;
            if (Directory.Exists(registeredRoot)) return Path.Combine(registeredRoot, "dotnet.exe");
        }

        var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        var emulatedHost = Path.Combine(defaultRoot, "x64", "dotnet.exe");
        return File.Exists(emulatedHost) ? emulatedHost : Path.Combine(defaultRoot, "dotnet.exe");
    }

    private static void OfferRuntimeDownload()
    {
        var answer = MessageBox.Show("未检测到 CatFish 所需的 .NET 9 桌面运行时（x64）。\n\n是否前往微软官方下载页面？",
            "CatFish", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        try { using (Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true })) { } }
        catch (Win32Exception) { ShowError("无法打开浏览器，请手动访问：\n" + DownloadUrl); }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "CatFish", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
