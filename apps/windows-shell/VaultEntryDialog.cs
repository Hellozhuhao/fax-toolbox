using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FaxToolbox.Windows;

internal sealed class VaultEntryDialog : Window
{
    private readonly TextBox username = new() { Margin = new Thickness(0, 6, 0, 16), MaxLength = 128 };
    private readonly PasswordBox password = new() { Margin = new Thickness(0, 6, 0, 12) };
    public string EncryptedUsername { get; private set; } = "";
    public string Ciphertext { get; private set; } = "";

    public VaultEntryDialog(Window owner)
    {
        NativeUi.Inherit(this, owner);
        Owner = owner; Title = "保存学习通账号"; Width = 450; Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Foreground = NativeUi.Brush(this, "Text");
        var panel = new StackPanel { Margin = new Thickness(26) };
        var frame = new Border { Child = panel, CornerRadius = new CornerRadius(14),
            Background = NativeUi.Brush(this, "Panel"), BorderBrush = NativeUi.Brush(this, "Border"), BorderThickness = new Thickness(1), Margin = new Thickness(8) };
        var heading = new TextBlock { Text = "保存账号", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
        heading.MouseLeftButtonDown += (_, args) => { if (args.ChangedButton == MouseButton.Left) DragMove(); };
        panel.Children.Add(heading);
        panel.Children.Add(new TextBlock { Text = "账号由当前 Windows 用户加密保存在本机。", Foreground = NativeUi.Brush(this, "Muted"), FontSize = 12, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = "学习通账号", FontWeight = FontWeights.Medium }); panel.Children.Add(username);
        panel.Children.Add(new TextBlock { Text = "密码", FontWeight = FontWeights.Medium }); panel.Children.Add(password);
        AutomationProperties.SetName(username, "学习通账号"); AutomationProperties.SetName(password, "密码");
        var validation = new TextBlock { Foreground = NativeUi.Brush(this,"Error"), TextWrapping = TextWrapping.Wrap, FontSize = 12, MinHeight = 20 };
        panel.Children.Add(validation);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 76, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "加密保存", IsDefault = true, MinWidth = 96, Margin = new Thickness(10, 0, 0, 0),
            Background = NativeUi.Brush(this, "Accent"), Foreground = NativeUi.Brush(this, "OnAccent"), HorizontalContentAlignment = HorizontalAlignment.Center };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(username.Text)) { validation.Text = "请填写学习通账号。"; username.Focus(); return; }
            if (password.Password.Length == 0) { validation.Text = "请填写密码。"; password.Focus(); return; }
            EncryptedUsername = VaultCrypto.Protect(username.Text.Trim());
            Ciphertext = VaultCrypto.Protect(password.Password);
            username.Clear(); password.Clear(); DialogResult = true;
        };
        actions.Children.Add(cancel); actions.Children.Add(save); panel.Children.Add(actions); Content = frame;
        Loaded += (_, _) => username.Focus();
        KeyDown += (_, args) => { if (args.Key == Key.Escape) Close(); };
    }
}
