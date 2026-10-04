using System.Diagnostics;
using Microsoft.Win32;

namespace PolarAd.Core.Startup;

/// <summary>
/// Registers/removes PolarAd's Windows autostart. Two pieces work together:
///
///  1. A Scheduled Task ("PolarAd AutoStart") with RunLevel=Highest and an
///     "onlogon" trigger. This is what actually launches PolarAd.exe as admin
///     without a UAC prompt — a Run-key entry pointing straight at PolarAd.exe
///     would prompt UAC on every login, since neither mechanism pre-elevates.
///
///  2. A value in HKCU\...\CurrentVersion\Run that runs
///     "schtasks /run /tn PolarAd AutoStart". This is what makes PolarAd show
///     up in Task Manager's Startup apps tab (it lists Run-key entries; it
///     doesn't list Scheduled Tasks). Invoking the already-elevated task this
///     way still doesn't prompt UAC.
///
/// Both pieces fire at logon, so PolarAd.App's single-instance mutex makes the
/// second launch exit quietly.
/// </summary>
public static class AutoStartManager
{
    private const string TaskName = "PolarAd AutoStart";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "PolarAd";

    private static string SchtasksPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    public static bool IsEnabled()
    {
        return RunValueExists() && TaskExists();
    }

    /// <summary>
    /// Registers the Scheduled Task and the Run-key entry. Must be called from an
    /// already-elevated process (schtasks /create with /rl highest needs admin).
    /// </summary>
    public static void Enable(string exePath)
    {
        var args = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f";
        var (exitCode, output) = RunSchtasks(args);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"schtasks 등록 실패 (코드 {exitCode}): {output}");
        }

        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        runKey.SetValue(RunValueName, $"\"{SchtasksPath}\" /run /tn \"{TaskName}\"", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using (var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }

        var (exitCode, output) = RunSchtasks($"/delete /tn \"{TaskName}\" /f");
        if (exitCode != 0 && !output.Contains("찾을 수 없습니다") && !output.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"schtasks 삭제 실패 (코드 {exitCode}): {output}");
        }
    }

    private static bool RunValueExists()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return runKey?.GetValue(RunValueName) != null;
    }

    private static bool TaskExists()
    {
        var (exitCode, _) = RunSchtasks($"/query /tn \"{TaskName}\"");
        return exitCode == 0;
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            return (-1, "schtasks.exe를 실행할 수 없음");
        }

        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit(10000);
        return (proc.ExitCode, output);
    }
}
