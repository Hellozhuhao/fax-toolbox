using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using FaxToolbox.Windows;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); checks++; }
    private static double Contrast(Color a, Color b)
    {
        static double Light(Color color)
        {
            var values=new[] {color.R,color.G,color.B}.Select(value=>value/255d).Select(value=>value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4)).ToArray();
            return values[0]*.2126+values[1]*.7152+values[2]*.0722;
        }
        var x=Light(a);var y=Light(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Paint() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Capture(FrameworkElement element, string name)
    {
        var directory=Environment.GetEnvironmentVariable("FAX_NATIVE_SCREENSHOTS");
        if(string.IsNullOrEmpty(directory))return;
        element.BeginAnimation(UIElement.OpacityProperty,null);element.Opacity=1;Paint();element.UpdateLayout();
        Directory.CreateDirectory(directory);
        var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        image.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using var output=File.Create(Path.Combine(directory,name+".png"));encoder.Save(output);
    }

    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FaxToolbox.Windows;component/UiResources.xaml", UriKind.Relative) });
        var assembly = typeof(MainWindow).Assembly;
        var native = assembly.GetType("FaxToolbox.Windows.NativeUi", true)!;
        var owner = new Window { Width = 500, Height = 300, Title = "UI contract test" };
        native.GetMethod("ApplyDefault", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [owner, true]);
        var combo = new ComboBox { IsEditable = true, Width = 280, Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        combo.Items.Add("模型一"); combo.Items.Add("模型二"); combo.SelectedIndex = 0;
        owner.Content = combo; owner.Show(); owner.UpdateLayout(); combo.ApplyTemplate(); Paint();
        var editor = combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;
        Check(editor is not null && editor.IsVisible, "Editable model selection lost its native editor");
        editor!.Text = "custom-model-id"; Paint();
        Check(combo.Text == "custom-model-id", "Custom model text no longer reaches ComboBox.Text");
        combo.IsDropDownOpen = true; Paint();
        var popup = combo.Template.FindName("PART_Popup", combo) as System.Windows.Controls.Primitives.Popup;
        Check(popup?.IsOpen == true, "Native dropdown does not open");
        Check(popup?.Child is Border panel && panel.Background == owner.FindResource("Ui.Panel"), "Popup did not inherit the active native palette");
        Capture((FrameworkElement)popup!.Child,"native-dropdown-dark");
        combo.SelectedIndex = 1; combo.IsDropDownOpen = false; Paint();
        Check(combo.Text == "模型二", "Selecting an item did not preserve native model selection semantics");

        var aiType = assembly.GetType("FaxToolbox.Windows.AiSettingsDialog", true)!;
        var ai = (Window)Activator.CreateInstance(aiType, [owner, null, "agent", "custom", null])!;
        ai.Show(); ai.UpdateLayout(); Paint();
        var save = Descendants(ai).OfType<Button>().Single(button => Equals(button.Content, "保存设置"));
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Paint();
        Check(ai.IsVisible, "Invalid AI configuration closed the dialog");
        var validation=(TextBlock)aiType.GetField("validation",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(ai)!;
        Check(validation.IsVisible && !string.IsNullOrWhiteSpace(validation.Text), "AI field validation is not visible in the existing dialog");
        Check(Contrast(((SolidColorBrush)validation.Foreground).Color,((SolidColorBrush)owner.FindResource("Ui.Background")).Color)>=4.5,"Dark AI validation contrast is below WCAG AA");
        Capture(ai,"native-ai-validation-dark");
        ai.Close();

        var vaultType = assembly.GetType("FaxToolbox.Windows.VaultEntryDialog", true)!;
        var vault = (Window)Activator.CreateInstance(vaultType, [owner])!;
        vault.Show(); vault.UpdateLayout(); Paint();
        Descendants(vault).OfType<Button>().Single(button => Equals(button.Content, "加密保存")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Paint();
        Check(vault.IsVisible, "Empty account input closed the dialog");
        Check(Descendants(vault).OfType<TextBlock>().Any(text => text.Text == "请填写学习通账号。"), "Account error did not identify the missing field");
        var accountError=Descendants(vault).OfType<TextBlock>().Single(text=>text.Text=="请填写学习通账号。");
        Check(Contrast(((SolidColorBrush)accountError.Foreground).Color,((SolidColorBrush)owner.FindResource("Ui.Panel")).Color)>=4.5,"Dark account validation contrast is below WCAG AA");
        Check(Descendants(vault).OfType<PasswordBox>().Count() == 1, "Password input lost its native protected control");
        Capture(vault,"native-vault-validation-dark");
        vault.Close();
        var android=(Window)Activator.CreateInstance(assembly.GetType("FaxToolbox.Windows.AndroidMirrorWindow",true)!,[Directory.GetCurrentDirectory()])!;
        native.GetMethod("ApplyDefault",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[android,false]);
        android.Show();android.UpdateLayout();Paint();Capture(android,"native-android-disconnected");android.Close();
        owner.Close(); app.Shutdown();
        Console.WriteLine($"UI_NATIVE_OK {checks} assertions");
    }
}
