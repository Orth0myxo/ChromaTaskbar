using System.Diagnostics;
using System.Text;

namespace ChromaTaskbar;

internal sealed class StartupDiagnosticLog
{
    private const long MaximumLogBytes = 512 * 1024;
    private readonly object _gate = new();

    internal StartupDiagnosticLog(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChromaTaskbar",
            "Logs",
            "startup.log");
    }

    internal string Path { get; }

    internal void Write(string eventName, string? details = null)
    {
        string line = $"{DateTimeOffset.UtcNow:O} [{eventName}] {Sanitize(details)}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                string? directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(Path) && new FileInfo(Path).Length > MaximumLogBytes)
                {
                    File.WriteAllText(Path, string.Empty, Encoding.UTF8);
                }

                File.AppendAllText(Path, line, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"ChromaTaskbar diagnostic log unavailable: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static string Sanitize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
