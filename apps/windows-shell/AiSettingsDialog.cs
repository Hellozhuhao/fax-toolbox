using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FaxToolbox.Windows;

internal sealed class AiSettingsDialog : Window
{
    private readonly AiSettings? previous;
    private readonly AiConsumer consumer;
    private readonly PasswordBox key = new();
    private readonly TextBox endpoint = new();
    private readonly TextBox model = new();
    private readonly StackPanel customFields = new();
    private readonly TextBlock summary = new();
    private readonly TextBlock keyHint = new();
    private readonly Dictionary<string, Button> providerButtons = [];
    private readonly SolidColorBrush background, surface, text, subtle, border, accent;
    private AiProviderPreset selected;
    public AiSettings? Settings { get; private set; }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public AiSettingsDialog(Window owner, AiSettings? previousSettings, string consumerId = "course")
    {
        consumer = AiConsumerRegistry.Require(consumerId);
        previous = previousSettings;
        selected = AiProviderPresets.Match(previous);
        var dark = owner.Background is SolidColorBrush ownerBrush && ownerBrush.Color.R < 80;
        background = Brush(dark ? "#202126" : "#F5F5F3");
        surface = Brush(dark ? "#17181C" : "#FFFFFF");
        text = Brush(dark ? "#F5F5F8" : "#171716");
        subtle = Brush(dark ? "#A3A3AD" : "#74716E");
        border = Brush(dark ? "#35363E" : "#E6E4E1");
        accent = Brush(dark ? "#7858EB" : "#D66B49");
        Owner = owner; FontFamily = owner.FontFamily; Title = "配置 " + consumer.Name;
        Width = 620; Height = 625; MinWidth = 540; MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Foreground = text;
        KeyDown += (_, args) => { if (args.Key == Key.Escape) Close(); };

        var frame = new Border { Background = background, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(26), Margin = new Thickness(12),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 7, Opacity = dark ? .45 : .18 } };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Child = layout; Content = frame;

        var heading = new Grid { Margin = new Thickness(28, 22, 22, 16) };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = consumer.Name + " · 模型配置", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = text });
        title.Children.Add(new TextBlock { Text = "选择服务商，填写自己的 API Key。", FontSize = 12, Foreground = subtle, Margin = new Thickness(0, 5, 0, 0) });
        heading.Children.Add(title);
        var close = PlainButton("✕"); close.Width = 32; close.Height = 32; close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1); heading.Children.Add(close);
        heading.MouseLeftButtonDown += (_, args) => { if (args.ChangedButton == MouseButton.Left) DragMove(); };
        layout.Children.Add(heading);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var body = new StackPanel { Margin = new Thickness(28, 0, 28, 8) }; scroll.Content = body;
        body.Children.Add(Label("服务商"));
        var providers = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 17) };
        foreach (var preset in AiProviderPresets.All)
        {
            var card = PlainButton("");
            card.Padding = new Thickness(13, 11, 13, 11); card.Margin = new Thickness(0, 0, 8, 8);
            card.HorizontalContentAlignment = HorizontalAlignment.Left;
            var contents = new StackPanel();
            contents.Children.Add(new TextBlock { Text = preset.Name, FontSize = 13, FontWeight = FontWeights.SemiBold });
            contents.Children.Add(new TextBlock { Text = preset.Hint, FontSize = 11, Foreground = subtle, Margin = new Thickness(0, 3, 0, 0) });
            card.Content = contents; card.Click += (_, _) => SelectProvider(preset);
            providerButtons.Add(preset.Id, card); providers.Children.Add(card);
        }
        body.Children.Add(providers);
        var summaryCard = new Border { Background = surface, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 18) };
        summary.TextWrapping = TextWrapping.Wrap; summary.FontSize = 12; summary.Foreground = subtle;
        summaryCard.Child = summary; body.Children.Add(summaryCard);

        customFields.Children.Add(Label("接口地址"));
        endpoint.ToolTip = "完整的 HTTPS /chat/completions 地址"; endpoint.Text = previous?.Endpoint ?? "";
        StyleInput(endpoint); customFields.Children.Add(InputFrame(endpoint, new Thickness(0, 8, 0, 15)));
        customFields.Children.Add(Label("模型名称")); model.Text = previous?.Model ?? "";
        StyleInput(model); customFields.Children.Add(InputFrame(model, new Thickness(0, 8, 0, 15))); body.Children.Add(customFields);
        body.Children.Add(Label("API Key"));
        key.Background = surface; key.Foreground = text; key.BorderThickness = new Thickness(0);
        key.Padding = new Thickness(10, 7, 10, 7); key.FontSize = 13; key.Height = 36;
        body.Children.Add(InputFrame(key, new Thickness(0, 9, 0, 6)));
        keyHint.Foreground = subtle; keyHint.FontSize = 11; keyHint.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(keyHint);
        body.Children.Add(new TextBlock { Text = "密钥由 Windows 当前用户加密保存在本机。答题时，当前可见题目会发送给所选服务商。",
            Foreground = subtle, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 17, 0, 4) });

        var actions = new Grid { Margin = new Thickness(28, 16, 28, 22) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cancel = PlainButton("取消"); cancel.Width = 92; cancel.Click += (_, _) => Close();
        Grid.SetColumn(cancel, 1); actions.Children.Add(cancel);
        var save = PlainButton("保存设置"); save.Width = 116; save.Margin = new Thickness(10, 0, 0, 0);
        save.Background = accent; save.Foreground = Brushes.White; save.BorderBrush = accent; save.IsDefault = true;
        save.Click += Save; Grid.SetColumn(save, 2); actions.Children.Add(save);
        Grid.SetRow(actions, 2); layout.Children.Add(actions);
        SelectProvider(selected);
    }

    private TextBlock Label(string value) => new() { Text = value, Foreground = text, FontSize = 12, FontWeight = FontWeights.SemiBold };
    private Button PlainButton(string value)
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(20));
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(Button.ContentProperty));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Button.HorizontalContentAlignmentProperty));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Button.PaddingProperty));
        frame.AppendChild(content);
        return new Button { Content = value, Background = surface, Foreground = text,
            BorderBrush = border, BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7),
            MinHeight = 36, Cursor = Cursors.Hand, FontSize = 12,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = frame } };
    }
    private Border InputFrame(Control field, Thickness margin) => new()
    {
        Child = field, Background = surface, BorderBrush = border,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Margin = margin
    };
    private void StyleInput(TextBox field)
    {
        field.Background = surface; field.Foreground = text; field.BorderThickness = new Thickness(0);
        field.Padding = new Thickness(10, 7, 10, 7);
        field.FontSize = 12; field.Height = 36;
    }
    private void SelectProvider(AiProviderPreset preset)
    {
        selected = preset;
        foreach (var (id, button) in providerButtons)
        {
            var active = id == preset.Id;
            button.BorderBrush = active ? accent : border;
            button.BorderThickness = new Thickness(active ? 2 : 1);
            button.Background = active ? Brush(background.Color.R < 80 ? "#332B50" : "#F9ECE5") : surface;
        }
        customFields.Visibility = preset.Id == "custom" ? Visibility.Visible : Visibility.Collapsed;
        summary.Text = preset.Id == "custom" ? "填写兼容 Chat Completions 的 HTTPS 接口地址和模型名称。" :
            $"默认模型：{preset.Model}\n接口：{preset.Endpoint}";
        var existing = previous is not null &&
            string.Equals(previous.Endpoint, preset.Id == "custom" ? endpoint.Text.Trim() : preset.Endpoint, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(previous.Model, preset.Id == "custom" ? model.Text.Trim() : preset.Model, StringComparison.Ordinal);
        keyHint.Text = existing && !string.IsNullOrEmpty(previous!.EncryptedKey) ?
            "已保存此服务的密钥；留空将继续使用。" :
            preset.RequiresKey ? "请输入此服务商提供的 API Key。" : "如服务不需要密钥，可留空。";
    }
    private void Save(object sender, RoutedEventArgs args)
    {
        try
        {
            var url = selected.Id == "custom" ? endpoint.Text.Trim() : selected.Endpoint;
            var name = selected.Id == "custom" ? model.Text.Trim() : selected.Model;
            var sameConfiguration = previous is not null &&
                string.Equals(previous.Endpoint, url, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(previous.Model, name, StringComparison.Ordinal);
            if (selected.RequiresKey && key.Password.Length == 0 &&
                (!sameConfiguration || string.IsNullOrEmpty(previous!.EncryptedKey)))
                throw new InvalidDataException("请填写所选服务的 API Key");
            var encrypted = key.Password.Length > 0 ? VaultCrypto.Protect(key.Password) :
                sameConfiguration ? previous!.EncryptedKey : "";
            var settings = new AiSettings(url, name, encrypted);
            AiSettings.Save(consumer.Id, settings); Settings = settings; key.Clear(); DialogResult = true;
        }
        catch (Exception error) when (error is InvalidDataException or UriFormatException)
        { MessageBox.Show(this, error.Message, "AI 答题设置", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
