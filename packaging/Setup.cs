using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("ChromaTaskbar Setup")]
[assembly: AssemblyProduct("ChromaTaskbar")]
[assembly: AssemblyDescription("Install ChromaTaskbar for the current Windows user")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyCompany("")]

internal static class SetupProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if ((args.Length == 2 || args.Length == 3) && args[0].Equals("/install", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                bool startAtLogin = args.Length != 3 || !args[2].Equals("/no-startup", StringComparison.OrdinalIgnoreCase);
                if (args.Length == 3 && !args[2].Equals("/no-startup", StringComparison.OrdinalIgnoreCase)) return 2;
                InstallTo(args[1], startAtLogin);
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        Application.Run(new SetupForm());
        return 0;
    }

    internal static string InstallTo(string installRoot, bool startAtLogin = true)
    {
        if (String.IsNullOrWhiteSpace(installRoot))
            throw new ArgumentException("Choose an installation folder.");

        installRoot = Path.GetFullPath(installRoot);
        Process[] running = Process.GetProcessesByName("ChromaTaskbar");
        bool isRunning = running.Length > 0;
        foreach (Process process in running)
            process.Dispose();
        if (isRunning)
            throw new InvalidOperationException("Close ChromaTaskbar normally before installing or updating it.");

        Directory.CreateDirectory(installRoot);
        ExtractPortablePayload(installRoot);
        CopyEmbeddedResource("ChromaTaskbar.Setup.Uninstall.ps1", Path.Combine(installRoot, "Uninstall.ps1"));
        try
        {
            CreateShortcuts(installRoot, startAtLogin);
            return null;
        }
        catch (Exception exception)
        {
            return "ChromaTaskbar files were installed, but a Start menu or login shortcut could not be created. " +
                "You can run 'Run ChromaTaskbar.cmd' from the installation folder. Details: " + exception.Message;
        }
    }

    private static void ExtractPortablePayload(string installRoot)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChromaTaskbar.Setup.Portable.zip"))
        {
            if (stream == null)
                throw new InvalidOperationException("The portable release payload is missing from this installer.");

            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                string root = installRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string destination = Path.GetFullPath(Path.Combine(installRoot, relative));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The installer contains an invalid file path.");

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                        entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    string directory = Path.GetDirectoryName(destination);
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
            }
        }
    }

    private static void CopyEmbeddedResource(string resourceName, string destination)
    {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
        {
            if (input == null)
                throw new InvalidOperationException("The uninstaller script is missing from this installer.");
            using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
        }
    }

    private static void CreateShortcuts(string installRoot, bool startAtLogin)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string shortcutDirectory = Path.Combine(programs, "ChromaTaskbar");
        Directory.CreateDirectory(shortcutDirectory);

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string commandPrompt = Path.Combine(windows, "System32", "cmd.exe");
        string launcher = Path.Combine(installRoot, "Run ChromaTaskbar.cmd");
        string applicationShortcut = Path.Combine(shortcutDirectory, "ChromaTaskbar.lnk");
        CreateShortcut(applicationShortcut, commandPrompt, "/c \"\"" + launcher + "\"\"", installRoot,
            Path.Combine(installRoot, "app", "ChromaTaskbar.exe"), 7);

        string startupShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ChromaTaskbar.lnk");
        if (startAtLogin)
        {
            if (File.Exists(startupShortcut) && !IsOurStartupShortcut(startupShortcut, commandPrompt))
                throw new IOException("A different Startup shortcut already uses the ChromaTaskbar name; it was left unchanged.");
            CreateShortcut(startupShortcut, commandPrompt, "/c \"\"" + launcher + "\"\"", installRoot,
                Path.Combine(installRoot, "app", "ChromaTaskbar.exe"), 7);
        }
        else if (File.Exists(startupShortcut) && IsOurStartupShortcut(startupShortcut, commandPrompt))
        {
            File.Delete(startupShortcut);
        }

        string powershell = Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        string uninstaller = Path.Combine(installRoot, "Uninstall.ps1");
        string uninstallShortcut = Path.Combine(shortcutDirectory, "Uninstall ChromaTaskbar.lnk");
        CreateShortcut(uninstallShortcut, powershell, "-NoProfile -ExecutionPolicy Bypass -File \"" + uninstaller + "\"",
            installRoot, powershell, 7);
    }

    private static bool IsOurStartupShortcut(string shortcutPath, string commandPrompt)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return false;
        object shell = Activator.CreateInstance(shellType);
        object shortcut = null;
        try
        {
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { shortcutPath });
            Type type = shortcut.GetType();
            string target = (string)type.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            string arguments = (string)type.InvokeMember("Arguments", BindingFlags.GetProperty, null, shortcut, null);
            return String.Equals(Path.GetFullPath(target), Path.GetFullPath(commandPrompt), StringComparison.OrdinalIgnoreCase) &&
                arguments.IndexOf("Run ChromaTaskbar.cmd", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static void CreateShortcut(string shortcutPath, string target, string arguments, string workingDirectory,
        string icon, int windowStyle)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new InvalidOperationException("Windows Script Host shortcut support is unavailable.");

        object shell = Activator.CreateInstance(shellType);
        try
        {
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            SetShortcutProperty(shortcutType, shortcut, "TargetPath", target);
            SetShortcutProperty(shortcutType, shortcut, "Arguments", arguments);
            SetShortcutProperty(shortcutType, shortcut, "WorkingDirectory", workingDirectory);
            SetShortcutProperty(shortcutType, shortcut, "IconLocation", icon);
            SetShortcutProperty(shortcutType, shortcut, "WindowStyle", windowStyle);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            Marshal.ReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    private static void SetShortcutProperty(Type type, object shortcut, string name, object value)
    {
        type.InvokeMember(name, BindingFlags.SetProperty, null, shortcut, new object[] { value });
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox _path = new TextBox();
    private readonly Label _status = new Label();
    private readonly CheckBox _startAtLogin = new CheckBox();

    internal SetupForm()
    {
        Text = "ChromaTaskbar Setup";
        ClientSize = new Size(520, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Label heading = new Label();
        heading.Text = "Install ChromaTaskbar";
        heading.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        heading.SetBounds(24, 20, 460, 30);

        Label description = new Label();
        description.Text = "Installs for this Windows user. The .NET 8 runtime is included; no administrator access or development tools are needed.";
        description.SetBounds(24, 58, 470, 44);

        Label pathLabel = new Label();
        pathLabel.Text = "Install folder";
        pathLabel.SetBounds(24, 111, 90, 24);
        _path.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ChromaTaskbar");
        _path.SetBounds(112, 108, 290, 26);

        _startAtLogin.Text = "Start ChromaTaskbar when I sign in";
        _startAtLogin.Checked = true;
        _startAtLogin.SetBounds(112, 140, 300, 24);

        Button browse = new Button();
        browse.Text = "Browse...";
        browse.SetBounds(410, 107, 84, 28);
        browse.Click += Browse;

        _status.Text = "MIT license is included in the installed folder.";
        _status.ForeColor = Color.DimGray;
        _status.SetBounds(24, 169, 470, 22);

        Button install = new Button();
        install.Text = "Install";
        install.SetBounds(318, 205, 84, 30);
        install.Click += Install;

        Button cancel = new Button();
        cancel.Text = "Cancel";
        cancel.DialogResult = DialogResult.Cancel;
        cancel.SetBounds(410, 205, 84, 30);

        Controls.AddRange(new Control[] { heading, description, pathLabel, _path, browse, _startAtLogin, _status, install, cancel });
        AcceptButton = install;
        CancelButton = cancel;
    }

    private void Browse(object sender, EventArgs args)
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "Choose where to install ChromaTaskbar.";
            dialog.SelectedPath = _path.Text;
            dialog.ShowNewFolderButton = true;
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _path.Text = Path.Combine(dialog.SelectedPath, "ChromaTaskbar");
        }
    }

    private void Install(object sender, EventArgs args)
    {
        UseWaitCursor = true;
        _status.Text = "Installing files and creating shortcuts...";
        Application.DoEvents();
        try
        {
            string warning = SetupProgram.InstallTo(_path.Text, _startAtLogin.Checked);
            if (warning == null)
                MessageBox.Show(this, "ChromaTaskbar is installed. Start it from the Start menu.", "ChromaTaskbar Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, warning, "ChromaTaskbar Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _status.Text = "Installation could not be completed.";
            MessageBox.Show(this, exception.Message, "ChromaTaskbar Setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
