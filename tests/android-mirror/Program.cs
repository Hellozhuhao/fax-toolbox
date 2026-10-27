using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using FaxToolbox.Windows;

internal static class Program
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid input accepted"); }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args[0]);
            var parsed = AndroidMirrorService.ParseDevices("List of devices attached\nusb1 device product:test model:Pixel_8 transport_id:1\nusb2 unauthorized\n10.0.0.2:1234 offline\n* daemon started successfully *\n");
            Check(parsed.Count == 3 && parsed[0].Model == "Pixel 8" && parsed[1].State == "unauthorized", "Device parsing");
            Check(AndroidMirrorService.ValidateEndpoint("192.168.1.2:5555") == "192.168.1.2:5555", "IP endpoint");
            Check(AndroidMirrorService.ValidateEndpoint("[::1]:4444") == "[::1]:4444", "IPv6 endpoint");
            Reject(() => AndroidMirrorService.ValidateEndpoint("-s device"));
            Reject(() => AndroidMirrorService.ValidateEndpoint("127.0.0.1:65536"));
            Reject(() => AndroidMirrorService.ValidateEndpoint("127.0.0.1:0"));
            Reject(() => AndroidMirrorService.ValidatePackage("com.app;shutdown"));
            Check(AndroidMirrorService.ValidatePackage("com.android.settings") == "com.android.settings", "Package");

            var host = new AndroidMirrorHost();
            var window = new Window { Width = 800, Height = 500, Content = host, ShowInTaskbar = false, Left = -20000, Top = -20000 };
            window.Show(); window.UpdateLayout();
            var native = CreateWindowEx(0, "static", "Synthetic video", unchecked((int)0x90000000), 0, 0, 10, 10, 0, 0, 0, 0);
            Check(native != 0, "Synthetic window creation");
            host.Attach(native);
            Check(GetParent(native) == host.Handle, "Video must be embedded into HwndHost");
            Check((GetWindowLong(native, -16) & 0x40000000) != 0, "Video child style");
            GetClientRect(native, out var first);
            window.Width = 1000; window.Height = 700; window.UpdateLayout(); Pump();
            GetClientRect(native, out var resized);
            Check(resized.Right > first.Right && resized.Bottom > first.Bottom, "Video resize");
            host.Detach(); DestroyWindow(native); window.Close(); host.Dispose();

            var module = new AndroidMirrorWindow(root) { Left = -20000, Top = -20000, ShowInTaskbar = false };
            module.Show(); module.UpdateLayout(); Pump();
            var pending = typeof(AndroidMirrorWindow).GetField("busy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var limit = DateTime.UtcNow.AddSeconds(25);
            while ((bool)pending.GetValue(module)! && DateTime.UtcNow < limit) { Pump(); Thread.Sleep(10); }
            Check(!(bool)pending.GetValue(module)!, "Initial device scan did not finish");
            var wireless = (RadioButton)module.FindName("wirelessMode");
            var wirelessPanel = (StackPanel)module.FindName("wirelessPanel");
            Check(wirelessPanel.Visibility == Visibility.Collapsed, "Wireless form must be hidden in USB mode");
            wireless.IsChecked = true;
            Check(wirelessPanel.Visibility == Visibility.Visible, "Wireless form must open on selection");
            ((Expander)module.FindName("pairingDetails")).IsExpanded = true;
            module.Width = 860; module.Height = 620; module.UpdateLayout(); Pump();
            var primary = (Button)module.FindName("start");
            var primaryLabel = (TextBlock)module.FindName("startLabel");
            Check(primaryLabel.Text == "开始投屏", "Primary action should match screen mode initially");
            var location = primary.TransformToAncestor(module).Transform(new Point(0, 0));
            Check(primary.ActualWidth > 200 && location.Y + primary.ActualHeight < module.ActualHeight, "Primary action must remain visible with expanded wireless form");
            Capture(module, Path.Combine(root, ".cache", "android-mirror-wireless-preview.png"));
            module.Width = 1180; module.Height = 780;
            ((RadioButton)module.FindName("appMode")).IsChecked = true;
            Check(((StackPanel)module.FindName("appPanel")).Visibility == Visibility.Visible, "App settings must open on selection");
            Check(primaryLabel.Text == "打开应用窗口", "Primary action should match app mode");
            var selector = (ComboBox)module.FindName("devices");
            selector.ItemsSource = new[] { new AndroidDevice("test-unauthorized", "unauthorized", "Pixel"), new AndroidDevice("test-ready", "device", "Pixel") };
            selector.SelectedIndex = 0;
            Check(!((Button)module.FindName("start")).IsEnabled, "Unauthorized devices must not start");
            selector.SelectedIndex = 1;
            Check(((Button)module.FindName("start")).IsEnabled, "Authorized devices should enable primary action");
            ((RadioButton)module.FindName("usbMode")).IsChecked = true;
            ((RadioButton)module.FindName("screenMode")).IsChecked = true;
            Check(primaryLabel.Text == "开始投屏", "Primary action should return to screen mode");
            selector.ItemsSource = Array.Empty<AndroidDevice>();
            module.UpdateLayout(); Pump();
            Capture(module, Path.Combine(root, ".cache", "android-mirror-preview.png"));
            module.Close();
            var service = new AndroidMirrorService(Path.Combine(root, "runtime", "scrcpy"));
            Check(service.Available, "Bundled runtime is missing");
            var devices = service.DevicesAsync().GetAwaiter().GetResult();
            Console.WriteLine($"PASS: input validation, device parsing, native embedding, resize, connection/mode interaction, authorization state, module render, runtime ADB scan ({devices.Count} devices).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
}
