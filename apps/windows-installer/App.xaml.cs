using System.IO;
using System.Reflection;
using System.Windows;

namespace FaxToolbox.Setup;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--install-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (string.IsNullOrEmpty(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(item => item.Key == "SmokePayloadSha256")?.Value))
                    throw new InvalidOperationException("This build does not include a smoke payload.");
                await InstallEngine.InstallAsync(e.Args[1], false, _ => { }, true);
                File.WriteAllText(e.Args[2], "PASS bootstrap dependency detection, verified payload and custom directory install"); Shutdown(0);
            }
            catch (Exception error) { File.WriteAllText(e.Args[2], "FAIL " + error); Shutdown(1); }
            return;
        }
        var window = new MainWindow(); MainWindow = window; window.Show();
        if (e.Args.Length >= 2 && e.Args[0] == "--preview")
        {
            try { await window.SavePreviewAsync(e.Args[1], e.Args.Length >= 3 ? e.Args[2] : "welcome"); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(e.Args[1] + ".error", error.ToString()); Shutdown(1); }
        }
    }
}
