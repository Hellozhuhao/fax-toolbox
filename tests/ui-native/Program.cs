using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Text.Json;
using FaxToolbox.Windows;

internal static class Program
{
    private static int checks;
    private static readonly List<string> captures = [];
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, PrivateInstance)!.GetValue(instance)!;
    private static void Click(Window window, string caption) => Descendants(window).OfType<Button>().Single(button => Equals(button.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        Check(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0, "Screenshot surface is not visible: " + name);
        Directory.CreateDirectory(directory);
        var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        // Render in the element's own coordinates. Rendering an arranged child
        // directly carries its parent offset into the bitmap and crops toolbars.
        var visual=new DrawingVisual();
        using (var drawing=visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,element.ActualWidth,element.ActualHeight));
        image.Render(visual);
        var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);
        Check(Enumerable.Range(0,image.PixelWidth*image.PixelHeight).Any(index=>pixels[index*4+3]!=0),"Screenshot is fully transparent: "+name);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using (var output=File.Create(Path.Combine(directory,name+".png"))) encoder.Save(output);
        // A compact public review copy contains synthetic fixture UI only, never account data.
        var compactDirectory=Path.Combine(directory,"review"); Directory.CreateDirectory(compactDirectory);
        var compact=new JpegBitmapEncoder { QualityLevel=88 }; compact.Frames.Add(BitmapFrame.Create(image));
        using (var output=File.Create(Path.Combine(compactDirectory,name+".jpg"))) compact.Save(output);
        captures.Add(name);
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
        var vaultType = assembly.GetType("FaxToolbox.Windows.VaultEntryDialog", true)!;
        foreach (var dark in new[] {false,true})
        {
            var theme=dark?"dark":"light";
            native.GetMethod("ApplyDefault", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [owner, dark]);
            var ai = (Window)Activator.CreateInstance(aiType, [owner, null, "agent", "custom", null])!;
            ai.Show(); ai.UpdateLayout(); Paint(); Capture(ai,"native-ai-custom-"+theme);
            var modelPicker=Field<ComboBox>(ai,"model");
            modelPicker.Items.Add("演示模型"); modelPicker.IsDropDownOpen=true;Paint();
            var modelPopup=(System.Windows.Controls.Primitives.Popup)modelPicker.Template.FindName("PART_Popup",modelPicker);
            Check(modelPopup.IsOpen && modelPopup.Child is Border modelPanel && ((SolidColorBrush)modelPanel.Background).Color==((SolidColorBrush)owner.FindResource("Ui.Panel")).Color,"AI model dropdown lost the active palette: "+theme);
            Capture((FrameworkElement)modelPopup.Child,"native-ai-model-dropdown-"+theme);
            modelPicker.IsDropDownOpen=false;Paint();
            Click(ai,"保存设置"); Paint();
            Check(ai.IsVisible, "Invalid AI configuration closed the dialog");
            var validation=Field<TextBlock>(ai,"validation");
            Check(validation.IsVisible && !string.IsNullOrWhiteSpace(validation.Text), "AI field validation is not visible in the existing dialog");
            Check(Contrast(((SolidColorBrush)validation.Foreground).Color,((SolidColorBrush)owner.FindResource("Ui.Background")).Color)>=4.5,"AI validation contrast is below WCAG AA");
            Capture(ai,"native-ai-validation-"+theme);
            Field<Expander>(ai,"pricingFields").IsExpanded=true; Paint();
            Descendants(ai).OfType<ScrollViewer>().First().ScrollToBottom(); Paint();
            Capture(ai,"native-ai-pricing-"+theme); ai.Close();

            foreach (var provider in new[] {"qwen","notrack","deepseek","siliconflow","openai","ollama"})
            {
                var preset=(Window)Activator.CreateInstance(aiType,[owner,null,"agent",provider,null])!;
                preset.Show();preset.UpdateLayout();Paint();
                var selectedProvider=Field<Dictionary<string,Button>>(preset,"providerButtons")[provider];
                var providerTitle=Descendants(selectedProvider).OfType<TextBlock>().First();
                Check(Contrast(((SolidColorBrush)providerTitle.Foreground).Color,((SolidColorBrush)selectedProvider.Background).Color)>=4.5,"Selected provider contrast is below WCAG AA: "+provider+"/"+theme);
                var saveButton=Descendants(preset).OfType<Button>().Single(button=>Equals(button.Content,"保存设置"));
                Check(Contrast(((SolidColorBrush)saveButton.Foreground).Color,((SolidColorBrush)saveButton.Background).Color)>=4.5,"Save action contrast is below WCAG AA: "+theme);
                Capture(preset,"native-ai-"+provider+"-"+theme);preset.Close();
            }
            var course=(Window)Activator.CreateInstance(aiType,[owner,null,"course","custom",null])!;
            course.Show();course.UpdateLayout();Paint();Field<Expander>(course,"promptFields").IsExpanded=true;
            Descendants(course).OfType<ScrollViewer>().First().ScrollToBottom();Paint();
            Capture(course,"native-ai-course-prompts-"+theme);course.Close();
            var homework=(Window)Activator.CreateInstance(aiType,[owner,null,"homework","custom",null])!;
            homework.Show();homework.UpdateLayout();Paint();Field<Expander>(homework,"promptFields").IsExpanded=true;
            Descendants(homework).OfType<ScrollViewer>().First().ScrollToBottom();Paint();
            Capture(homework,"native-ai-homework-prompts-"+theme);homework.Close();
            var noticeType=assembly.GetType("FaxToolbox.Windows.NativeNotice",true)!;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                var notice=app.Windows.OfType<Window>().Single(window=>window.Title=="连接暂时不可用");
                notice.UpdateLayout();Capture(notice,"native-notice-"+theme);notice.Close();
            }));
            noticeType.GetMethod("Show",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[owner,"连接暂时不可用","请检查连接设置后重试。当前任务和未发送的内容仍然保留。"]);


            var vault = (Window)Activator.CreateInstance(vaultType, [owner])!;
            vault.Show(); vault.UpdateLayout(); Paint(); Capture(vault,"native-vault-empty-"+theme);
            Click(vault,"加密保存"); Paint();
            Check(vault.IsVisible, "Empty account input closed the dialog");
            var accountError=Descendants(vault).OfType<TextBlock>().Single(text=>text.Text=="请填写学习通账号。");
            Check(Contrast(((SolidColorBrush)accountError.Foreground).Color,((SolidColorBrush)owner.FindResource("Ui.Panel")).Color)>=4.5,"Account validation contrast is below WCAG AA");
            Check(Descendants(vault).OfType<PasswordBox>().Count()==1,"Password input lost its native protected control");
            Capture(vault,"native-vault-validation-"+theme);
            Field<TextBox>(vault,"username").Text="演示账号"; Click(vault,"加密保存");Paint();
            Check(Descendants(vault).OfType<TextBlock>().Any(text=>text.Text=="请填写密码。"),"Password validation did not identify the missing field");
            Capture(vault,"native-vault-password-required-"+theme);vault.Close();
        }
        var android=(Window)Activator.CreateInstance(assembly.GetType("FaxToolbox.Windows.AndroidMirrorWindow",true)!,[Directory.GetCurrentDirectory()])!;
        native.GetMethod("ApplyDefault",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[android,false]);
        android.Show();android.UpdateLayout();Paint();Capture(android,"native-android-usb");
        ((RadioButton)android.FindName("wirelessMode")).IsChecked=true;
        ((Expander)android.FindName("pairingDetails")).IsExpanded=true;
        android.Width=860;android.Height=620;android.UpdateLayout();Paint();
        Check(((StackPanel)android.FindName("wirelessPanel")).IsVisible,"Wireless form did not open");
        Capture(android,"native-android-wireless-pairing");
        ((ScrollViewer)android.FindName("connectionScroll")).ScrollToBottom();Paint();
        Capture(android,"native-android-pairing-controls");
        ((ScrollViewer)android.FindName("connectionScroll")).ScrollToTop();Paint();
        var deviceType=assembly.GetType("FaxToolbox.Windows.AndroidDevice",true)!;
        var devices=(ComboBox)android.FindName("devices");
        devices.ItemsSource=new[] { Activator.CreateInstance(deviceType,["demo-device","device","演示手机"])! };
        devices.SelectedIndex=0;
        ((RadioButton)android.FindName("appMode")).IsChecked=true;
        android.Width=1180;android.Height=780;android.UpdateLayout();Paint();
        Check(((TextBlock)android.FindName("startLabel")).Text=="打开应用窗口","App mode label did not update");
        Capture(android,"native-android-app-mode");
        ((TextBox)android.FindName("endpoint")).Text="invalid-endpoint";
        ((Button)android.FindName("connect")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Paint();
        Check(((TextBlock)android.FindName("status")).Text.Contains("IP:端口"),"Connection validation did not explain the invalid endpoint");
        Capture(android,"native-android-connection-error");android.Close();

        // Render real native tab controls without starting browsers, network services or a backend.
        var windows=new List<MainWindow>();
        foreach(var module in new[] {"ai","course","homework","proxy","ai","course","homework","proxy"})
        {
            var shell=new MainWindow();
            var loaded=(RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler),shell,typeof(MainWindow).GetMethod("Window_Loaded",PrivateInstance)!);
            shell.Loaded-=loaded;
            typeof(MainWindow).GetField("assignedModule",PrivateInstance)!.SetValue(shell,module);
            windows.Add(shell);
        }
        var chrome=windows[0];
        typeof(MainWindow).GetMethod("UpdateAllTaskTabs",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null);
        chrome.Show();chrome.UpdateLayout();Paint();
        Capture((FrameworkElement)chrome.FindName("TitleBar"),"native-titlebar-light");
        Capture((FrameworkElement)chrome.FindName("BrowserToolbar"),"native-tabs-wide-light");
        chrome.Width=780;chrome.UpdateLayout();Paint();
        var tabScroll=(ScrollViewer)chrome.FindName("TaskTabsScroller");
        Check(tabScroll.ExtentWidth>tabScroll.ViewportWidth,"Long native tab strip should scroll within its viewport");
        Check(((Button)chrome.FindName("NewTaskTabButton")).ActualWidth>=40,"New tab action has a small pointer target");
        Capture((FrameworkElement)chrome.FindName("BrowserToolbar"),"native-tabs-narrow-light");
        typeof(MainWindow).GetMethod("ApplyHostTheme",PrivateInstance)!.Invoke(chrome,[true]);
        chrome.UpdateLayout();Paint();Capture((FrameworkElement)chrome.FindName("BrowserToolbar"),"native-tabs-narrow-dark");
        chrome.Width=1280;chrome.Height=800;
        typeof(MainWindow).GetMethod("SetWorkspaceLayout",PrivateInstance)!.Invoke(chrome,[true]);
        ((TextBlock)chrome.FindName("HomeworkExplanationQuestion")).Text="解方程：2x + 1 = 5";
        ((TextBlock)chrome.FindName("HomeworkExplanationAnswer")).Text="x = 2";
        ((TextBlock)chrome.FindName("HomeworkExplanationText")).Text="先在等式两边减去 1，得到 2x = 4。再除以 2，得到 x = 2。";
        ((Grid)chrome.FindName("HomeworkExplanationOverlay")).Visibility=Visibility.Visible;
        chrome.UpdateLayout();Paint();
        var explanation=(Border)chrome.FindName("HomeworkExplanationBoard");
        var explanationText=(TextBlock)chrome.FindName("HomeworkExplanationText");
        Check(Contrast(((SolidColorBrush)explanationText.Foreground).Color,((SolidColorBrush)explanation.Background).Color)>=4.5,"Dark homework explanation contrast is below WCAG AA");
        Capture(explanation,"native-homework-explanation-dark");
        typeof(MainWindow).GetMethod("ApplyHostTheme",PrivateInstance)!.Invoke(chrome,[false]);Paint();
        Check(Contrast(((SolidColorBrush)explanationText.Foreground).Color,((SolidColorBrush)explanation.Background).Color)>=4.5,"Light homework explanation contrast is below WCAG AA");
        Capture(explanation,"native-homework-explanation-light");
        typeof(MainWindow).GetMethod("ApplyHostTheme",PrivateInstance)!.Invoke(chrome,[true]);Paint();
        typeof(MainWindow).GetMethod("DetachHomeworkExplanation",PrivateInstance)!.Invoke(chrome,null);
        var detached=Field<Window>(chrome,"homeworkExplanationWindow");detached.UpdateLayout();Paint();Capture(detached,"native-homework-detached");detached.Close();

        foreach(var shell in windows) { typeof(MainWindow).GetField("closingAsTab",PrivateInstance)!.SetValue(shell,true);shell.Close(); }
        var captureDirectory=Environment.GetEnvironmentVariable("FAX_NATIVE_SCREENSHOTS");
        if(!string.IsNullOrEmpty(captureDirectory)) File.WriteAllText(Path.Combine(captureDirectory,"manifest.json"),JsonSerializer.Serialize(new {commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),checks,captures,scope="Synthetic native dialogs and shell chrome; no live websites or device projection"},new JsonSerializerOptions {WriteIndented=true}));
        owner.Close(); app.Shutdown();
        Console.WriteLine($"UI_NATIVE_OK {checks} assertions");
    }
}
