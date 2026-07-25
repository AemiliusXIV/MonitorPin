using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace MonitorPin.Startup;

/// <summary>
/// Registers MonitorPin as an elevated logon task via schtasks, so it starts at
/// login with highest privileges and no UAC prompt. Creating such a task needs
/// admin, so the toggle relaunches the app elevated to do the actual work.
/// </summary>
public static class StartupInstaller
{
    public const string TaskName = "MonitorPin";

    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool IsInstalled()
    {
        var (exit, _) = RunSchtasks($"/query /TN \"{TaskName}\"");
        return exit == 0;
    }

    /// <summary>Create the logon task. Requires the current process to be elevated.</summary>
    public static bool Install(string exePath)
    {
        var (exit, _) = RunSchtasks(
            $"/create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
        return exit == 0;
    }

    public static bool Uninstall()
    {
        var (exit, _) = RunSchtasks($"/delete /TN \"{TaskName}\" /F");
        return exit == 0;
    }

    /// <summary>
    /// Toggle from the UI. If we aren't elevated, relaunch ourselves elevated to
    /// perform the schtasks change, then return (the elevated instance exits).
    /// </summary>
    public static bool RequestSetEnabled(bool enabled, string exePath)
    {
        if (IsElevated())
            return enabled ? Install(exePath) : Uninstall();

        return RelaunchElevated(enabled ? "--install-task" : "--uninstall-task");
    }

    private static bool RelaunchElevated(string arg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Application.ExecutablePath,
                Arguments = arg,
                UseShellExecute = true,
                Verb = "runas", // UAC prompt for this one-off elevated action
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
            return p?.ExitCode == 0;
        }
        catch
        {
            // User declined the UAC prompt, or launch failed.
            return false;
        }
    }

    private static (int exitCode, string output) RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
