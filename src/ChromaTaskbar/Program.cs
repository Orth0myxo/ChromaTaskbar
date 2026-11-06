using ChromaTaskbar.Core;

namespace ChromaTaskbar;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        DpiAwareness.TryEnablePerMonitorV2();
        ApplicationConfiguration.Initialize();
        StartupDiagnosticLog log = new();
        log.Write("process.start", $"pid={Environment.ProcessId}; runtime={Environment.Version}; os={Environment.OSVersion.Version}");

        SingleInstanceLease? lease;
        try
        {
            if (!SingleInstanceLease.TryAcquire("Local\\ChromaTaskbar.SingleInstance", out lease))
            {
                log.Write("startup.duplicate_instance");
                return;
            }
        }
        catch (Exception exception)
        {
            log.Write("startup.single_instance.failed", $"{exception.GetType().Name}: {exception.Message}");
            MessageBox.Show(
                $"ChromaTaskbar could not verify single-instance status.\r\n\r\n{exception.Message}",
                "ChromaTaskbar startup failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using (lease!)
        {
            try
            {
                using ChromaTaskbarApplicationContext context = new(log: log);
                Application.Run(context);
            }
            catch (Exception exception)
            {
                log.Write("startup.fatal", $"{exception.GetType().Name}: {exception.Message}");
                MessageBox.Show(
                    $"ChromaTaskbar could not start its notification-area icon. No taskbar changes were applied.\r\n\r\n{exception.Message}",
                    "ChromaTaskbar startup failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
