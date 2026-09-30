using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FaxToolbox.Windows;

internal sealed class VaultEntryDialog : Window
{
    private readonly TextBox username = new() { MinWidth = 280, Margin = new Thickness(0, 5, 0, 13) };
    private readonly PasswordBox password = new() { MinWidth = 280, Margin = new Thickness(0, 5, 0, 16) };

    public string EncryptedUsername { get; private set; } = "";
    public string Ciphertext { get; private set; } = "";

    public VaultEntryDialog(Window owner)
    {
        Owner = owner;
        Title = "保存学习通账号";
        Width = 380;
        Height = 275;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(23, 32, 48));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(27) };
        panel.Children.Add(new TextBlock { Text = "学习通账号", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(username);
        panel.Children.Add(new TextBlock { Text = "密码", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(password);
        var save = new Button { Content = "加密保存", Height = 37, IsDefault = true };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(username.Text) || username.Text.Length > 128 || password.Password.Length == 0)
            {
                MessageBox.Show(this, "请填写账号和密码。", "密码箱", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            EncryptedUsername = VaultCrypto.Protect(username.Text.Trim());
            Ciphertext = VaultCrypto.Protect(password.Password);
            username.Clear();
            password.Clear();
            DialogResult = true;
        };
        panel.Children.Add(save);
        Content = panel;
    }
}
