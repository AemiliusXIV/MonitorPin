namespace MonitorPin.Util;

/// <summary>
/// Creates a desktop .lnk. Uses the Windows Script Host COM object by late
/// binding, so there's no extra dependency to ship.
/// </summary>
public static class ShortcutMaker
{
    /// <summary>Create a desktop shortcut. Returns the path, or null if it failed.</summary>
    public static string? CreateOnDesktop(string shortcutName, string targetPath, string arguments, string description)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string safe = string.Join("_", shortcutName.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(desktop, safe + ".lnk");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = targetPath;
            link.Arguments = arguments;
            link.IconLocation = targetPath + ",0";
            link.Description = description;
            link.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
            link.Save();
            return path;
        }
        catch
        {
            return null;
        }
    }
}
