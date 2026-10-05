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
        var dark = owner.Background is SolidColorBrush ownerBrush && ownerBrush.Color.R < 80;
        static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        var background = Brush(dark ? "#202126" : "#F7F7FC");
        var surface = Brush(dark ? "#17181C" : "#FFFFFF");
        var text = Brush(dark ? "#F5F5F8" : "#171716");
        var border = Brush(dark ? "#35363E" : "#E7EBF4");
        var accent = Brush(dark ? "#4F8CF5" : "#387DF3");
        Owner = owner;
        FontFamily = owner.FontFamily;
        Title = "保存学习通账号";
        Width = 380;
        Height = 275;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = background;
        Foreground = text;
        var panel = new StackPanel { Margin = new Thickness(27) };
        panel.Children.Add(new TextBlock { Text = "学习通账号", FontWeight = FontWeights.SemiBold });
        username.Background = surface; username.Foreground = text; username.BorderBrush = border;
        username.Padding = new Thickness(8, 6, 8, 6);
        panel.Children.Add(username);
        panel.Children.Add(new TextBlock { Text = "密码", FontWeight = FontWeights.SemiBold });
        password.Background = surface; password.Foreground = text; password.BorderBrush = border;
        password.Padding = new Thickness(8, 6, 8, 6);
        panel.Children.Add(password);
        var buttonFrame = new FrameworkElementFactory(typeof(Border));
        buttonFrame.SetValue(Border.CornerRadiusProperty, new CornerRadius(19));
        buttonFrame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        var buttonContent = new FrameworkElementFactory(typeof(ContentPresenter));
        buttonContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        buttonContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        buttonFrame.AppendChild(buttonContent);
        var save = new Button { Content = "加密保存", Height = 37, IsDefault = true,
            Background = accent, Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = buttonFrame } };
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
