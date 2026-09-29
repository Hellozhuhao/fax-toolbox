using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FaxToolbox.Windows;

internal sealed class AiSettingsDialog : Window
{
    private readonly TextBox endpoint = new() { Margin = new Thickness(0, 5, 0, 12) };
    private readonly TextBox model = new() { Margin = new Thickness(0, 5, 0, 12) };
    private readonly PasswordBox key = new() { Margin = new Thickness(0, 5, 0, 12) };
    private readonly AiSettings? previous;

    public AiSettings? Settings { get; private set; }

    public AiSettingsDialog(Window owner, AiSettings? previousSettings)
    {
        previous = previousSettings;
        Owner = owner;
        Title = "AI 答题设置";
        Width = 470;
        Height = 380;
        MinWidth = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Foreground = new SolidColorBrush(Color.FromRgb(29, 29, 31));
        var panel = new StackPanel { Margin = new Thickness(27) };
        panel.Children.Add(new TextBlock { Text = "兼容 Chat Completions 的 HTTPS 接口", FontWeight = FontWeights.SemiBold });
        endpoint.Text = previous?.Endpoint ?? "";
        endpoint.ToolTip = "例如 https://服务地址/v1/chat/completions";
        panel.Children.Add(endpoint);
        panel.Children.Add(new TextBlock { Text = "模型名称", FontWeight = FontWeights.SemiBold });
        model.Text = previous?.Model ?? "";
        panel.Children.Add(model);
        panel.Children.Add(new TextBlock { Text = "API Key", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(key);
        panel.Children.Add(new TextBlock
        {
            Text = "已保存密钥时留空可继续使用原密钥。密钥只在此原生窗口输入，并以 Windows 当前用户加密保存。当前可见题目会发送到你配置的服务。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(105, 105, 110)),
            Margin = new Thickness(0, 4, 0, 17)
        });
        var save = new Button { Content = "加密保存", Height = 38, IsDefault = true };
        save.Click += (_, _) =>
        {
            try
            {
                var encrypted = key.Password.Length > 0 ? VaultCrypto.Protect(key.Password) :
                    previous?.EncryptedKey ?? "";
                var settings = new AiSettings(endpoint.Text.Trim(), model.Text.Trim(), encrypted);
                AiSettings.Save(settings);
                Settings = settings;
                key.Clear();
                DialogResult = true;
            }
            catch (Exception error) when (error is InvalidDataException or UriFormatException)
            {
                MessageBox.Show(this, error.Message, "AI 答题设置", MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        };
        panel.Children.Add(save);
        Content = panel;
    }
}
