using System.Reflection;
using System.Runtime.InteropServices;

namespace ChromaTaskbar;

/// <summary>Manages this user's single ChromaTaskbar login shortcut.</summary>
internal sealed class StartupRegistrationController
{
    private const string ShortcutName = "ChromaTaskbar.lnk";
    private readonly string _startupFolder;
    private readonly string _launcherPath;
    private readonly string _commandPromptPath;
    private readonly string _iconPath;

    internal StartupRegistrationController(string? startupFolder = null, string? launcherPath = null,
        string? commandPromptPath = null, string? iconPath = null)
    {
        string installRoot = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName ?? AppContext.BaseDirectory;
        _startupFolder = startupFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        _launcherPath = Path.GetFullPath(launcherPath ?? Path.Combine(installRoot, "Run ChromaTaskbar.cmd"));
        _commandPromptPath = Path.GetFullPath(commandPromptPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe"));
        _iconPath = Path.GetFullPath(iconPath ?? Path.Combine(installRoot, "app", "ChromaTaskbar.exe"));
    }

    internal string ShortcutPath => Path.Combine(_startupFolder, ShortcutName);

    internal bool IsEnabled
    {
        get
        {
            if (!File.Exists(ShortcutPath))
            {
                return false;
            }

            ShortcutData shortcut = ReadShortcut(ShortcutPath);
            return IsOwnedShortcut(shortcut);
        }
    }

    internal void SetEnabled(bool enabled)
    {
        string path = ShortcutPath;
        if (!enabled)
        {
            if (!File.Exists(path)) return;
            if (!IsOwnedShortcut(ReadShortcut(path)))
                throw new IOException("A different Startup shortcut already uses the ChromaTaskbar name; it was left unchanged.");
            File.Delete(path);
            return;
        }

        if (!File.Exists(_launcherPath))
            throw new FileNotFoundException("The ChromaTaskbar launcher was not found. Install the app before enabling login startup.", _launcherPath);
        if (File.Exists(path) && !IsOwnedShortcut(ReadShortcut(path)))
            throw new IOException("A different Startup shortcut already uses the ChromaTaskbar name; it was left unchanged.");

        Directory.CreateDirectory(_startupFolder);
        WriteShortcut(path);
    }

    private bool IsOwnedShortcut(ShortcutData shortcut) =>
        string.Equals(Path.GetFileName(_launcherPath), "Run ChromaTaskbar.cmd", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(NormalizePath(shortcut.TargetPath), NormalizePath(_commandPromptPath), StringComparison.OrdinalIgnoreCase) &&
        shortcut.Arguments.IndexOf("Run ChromaTaskbar.cmd", StringComparison.OrdinalIgnoreCase) >= 0;

    private void WriteShortcut(string path)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host shortcut support is unavailable.");
        object shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create Windows Script Host.");
        object? shortcut = null;
        try
        {
            object created = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path])
                ?? throw new InvalidOperationException("Windows Script Host did not create the startup shortcut.");
            shortcut = created;
            Type type = created.GetType();
            Set(type, created, "TargetPath", _commandPromptPath);
            Set(type, created, "Arguments", "/c \"\"" + _launcherPath + "\"\"");
            Set(type, created, "WorkingDirectory", Path.GetDirectoryName(_launcherPath)!);
            Set(type, created, "IconLocation", _iconPath);
            Set(type, created, "WindowStyle", 7);
            type.InvokeMember("Save", BindingFlags.InvokeMethod, null, created, null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static ShortcutData ReadShortcut(string path)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host shortcut support is unavailable.");
        object shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create Windows Script Host.");
        object? shortcut = null;
        try
        {
            object created = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path])
                ?? throw new InvalidOperationException("Windows Script Host could not read the startup shortcut.");
            shortcut = created;
            Type type = created.GetType();
            return new ShortcutData(
                (string)(type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, created, null) ?? string.Empty),
                (string)(type.InvokeMember("Arguments", BindingFlags.GetProperty, null, created, null) ?? string.Empty));
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static void Set(Type type, object target, string name, object value) =>
        type.InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static string NormalizePath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private sealed record ShortcutData(string TargetPath, string Arguments);
}
