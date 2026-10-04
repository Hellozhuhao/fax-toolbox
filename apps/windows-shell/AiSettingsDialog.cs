using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;
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
    private readonly ComboBox model = new() { IsEditable = true, IsTextSearchEnabled = false };
    private readonly TextBox inputPrice = new();
    private readonly TextBox cachedPrice = new();
    private readonly TextBox outputPrice = new();
    private readonly ComboBox priceCurrency = new();
    private readonly Expander pricingFields = new();
    private readonly Expander promptFields = new();
    private readonly Dictionary<string, TextBox> promptEditors = [];
    private readonly StackPanel customFields = new();
    private readonly StackPanel modelFields = new();
    private readonly StackPanel presetModelChoices = new();
    private readonly Dictionary<string, Button> presetModelButtons = [];
    private readonly Dictionary<string, string> selectedPresetModels = new()
    {
        ["qwen"] = AiProviderPresets.QwenModels[0].Id,
        ["venice"] = AiProviderPresets.VeniceModels[0].Id
    };
    private readonly StackPanel keyFields = new();
    private readonly TextBlock summary = new();
    private readonly TextBlock keyHint = new();
    private readonly TextBlock privacyHint = new();
    private readonly Dictionary<string, Button> providerButtons = [];
    private readonly SolidColorBrush background, surface, text, subtle, border, accent;
    private AiProviderPreset selected;
    private Border modelInputFrame = null!;
    private bool localModelsLoaded;
    private string promptLoadedKey = "";
    public AiSettings? Settings { get; private set; }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public AiSettingsDialog(Window owner, AiSettings? previousSettings, string consumerId = "course")
    {
        consumer = AiConsumerRegistry.Require(consumerId);
        previous = previousSettings;
        selected = AiProviderPresets.Match(previous);
        if (selected.Id is "qwen" or "venice") selectedPresetModels[selected.Id] = previous!.Model;
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
        title.Children.Add(new TextBlock { Text = "选择模型连接。", FontSize = 12, Foreground = subtle, Margin = new Thickness(0, 5, 0, 0) });
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
        body.Children.Add(customFields);
        modelFields.Children.Add(Label("模型名称")); model.Text = previous?.Model ?? "";
        StyleInput(model); modelInputFrame = InputFrame(model, new Thickness(0, 8, 0, 15));
        modelFields.Children.Add(modelInputFrame);
        foreach (var option in AiProviderPresets.QwenModels.Concat(AiProviderPresets.VeniceModels))
        {
            var card = PlainButton("");
            card.Padding = new Thickness(13, 11, 13, 11);
            card.Margin = new Thickness(0, 0, 0, 8);
            card.HorizontalContentAlignment = HorizontalAlignment.Left;
            var contents = new StackPanel();
            contents.Children.Add(new TextBlock { Text = option.Name, FontSize = 13, FontWeight = FontWeights.SemiBold });
            contents.Children.Add(new TextBlock { Text = option.Hint, FontSize = 11, Foreground = subtle,
                Margin = new Thickness(0, 3, 0, 0) });
            card.Content = contents;
            card.Click += (_, _) => SelectPresetModel(option.Id);
            presetModelButtons.Add(option.Id, card);
            presetModelChoices.Children.Add(card);
        }
        modelFields.Children.Add(presetModelChoices);
        body.Children.Add(modelFields);
        keyFields.Children.Add(Label("API Key"));
        key.Background = surface; key.Foreground = text; key.BorderThickness = new Thickness(0);
        key.Padding = new Thickness(10, 7, 10, 7); key.FontSize = 13; key.Height = 36;
        keyFields.Children.Add(InputFrame(key, new Thickness(0, 9, 0, 6)));
        keyHint.Foreground = subtle; keyHint.FontSize = 11; keyHint.TextWrapping = TextWrapping.Wrap;
        keyFields.Children.Add(keyHint); body.Children.Add(keyFields);
        if (consumer.Id == "agent")
        {
            pricingFields.Header = "自定义模型单价"; pricingFields.Foreground = text;
            pricingFields.Margin = new Thickness(0, 15, 0, 0);
            pricingFields.IsExpanded = previous?.Pricing is not null;
            var fields = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            fields.Children.Add(new TextBlock { Text = "每百万 token 的单价；费用仅为本次会话估算。", Foreground = subtle, FontSize = 11 });
            priceCurrency.Items.Add("CNY"); priceCurrency.Items.Add("USD");
            priceCurrency.SelectedItem = previous?.Pricing?.Currency ?? "CNY";
            fields.Children.Add(Label("货币")); fields.Children.Add(priceCurrency);
            foreach (var (caption, field, value) in new[] {
                ("输入", inputPrice, previous?.Pricing?.InputPerMillion),
                ("缓存输入", cachedPrice, previous?.Pricing?.CachedInputPerMillion),
                ("输出", outputPrice, previous?.Pricing?.OutputPerMillion) })
            {
                fields.Children.Add(Label(caption)); field.Text = value?.ToString(CultureInfo.InvariantCulture) ?? "";
                StyleInput(field); fields.Children.Add(InputFrame(field, new Thickness(0, 5, 0, 10)));
            }
            pricingFields.Content = fields; body.Children.Add(pricingFields);
        }
        else
        {
            promptFields.Header = "自定义系统提示词"; promptFields.Foreground = text;
            promptFields.Margin = new Thickness(0, 12, 0, 0);
            var fields = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
            foreach (var (scope, titleText) in consumer.Id == "course" ?
                new[] { ("course.answer", "答题") } :
                new[] { ("homework.review", "作业校对"), ("homework.explain", "详解") })
            {
                fields.Children.Add(Label(titleText));
                var editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 3000 };
                StyleInput(editor); editor.Height = 95;
                promptEditors[scope] = editor;
                fields.Children.Add(InputFrame(editor, new Thickness(0, 7, 0, 13)));
            }
            promptFields.Content = fields; body.Children.Add(promptFields);
            model.SelectionChanged += (_, _) => RefreshPromptFields();
            model.LostFocus += (_, _) => RefreshPromptFields();
            endpoint.LostFocus += (_, _) => RefreshPromptFields();
        }
        privacyHint.Text = "密钥由 Windows 当前用户加密保存在本机。输入内容会发送给所选服务商。";
        privacyHint.Foreground = subtle; privacyHint.FontSize = 11; privacyHint.TextWrapping = TextWrapping.Wrap;
        privacyHint.Margin = new Thickness(0, 17, 0, 4); body.Children.Add(privacyHint);

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
    private void StyleInput(ComboBox field)
    {
        field.Background = surface; field.Foreground = text; field.BorderThickness = new Thickness(0);
        field.Padding = new Thickness(10, 7, 10, 7); field.FontSize = 12; field.Height = 36;
    }

    private async Task LoadLocalModelsAsync()
    {
        if (localModelsLoaded) return;
        localModelsLoaded = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var installed = await OllamaModelCatalog.ListAsync(timeout.Token);
            if (selected.Id != "ollama") return;
            var current = model.Text;
            model.Items.Clear();
            foreach (var item in installed) model.Items.Add(item.Name);
            model.Text = installed.Any(item => item.Name == current) ? current :
                previous is not null && AiProviderPresets.Match(previous).Id == "ollama" ? previous.Model :
                installed.FirstOrDefault()?.Name ?? current;
            summary.Text = installed.Count > 0 ? $"已发现 {installed.Count} 个本地模型；也可输入其他名称。" :
                "本机尚未发现已下载模型；仍可输入模型名称。";
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        { summary.Text = "未连接到本机 Ollama；启动后会自动列出已安装模型。"; }
    }
    private void SelectProvider(AiProviderPreset preset)
    {
        var changedProvider = selected.Id != preset.Id;
        selected = preset;
        foreach (var (id, button) in providerButtons)
        {
            var active = id == preset.Id;
            button.BorderBrush = active ? accent : border;
            button.BorderThickness = new Thickness(active ? 2 : 1);
            button.Background = active ? Brush(background.Color.R < 80 ? "#332B50" : "#F9ECE5") : surface;
        }
        customFields.Visibility = preset.Id == "custom" ? Visibility.Visible : Visibility.Collapsed;
        modelFields.Visibility = preset.Id is "custom" or "ollama" or "venice" or "qwen" ? Visibility.Visible : Visibility.Collapsed;
        modelInputFrame.Visibility = preset.Id is "venice" or "qwen" ? Visibility.Collapsed : Visibility.Visible;
        presetModelChoices.Visibility = preset.Id is "venice" or "qwen" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (id, button) in presetModelButtons)
            button.Visibility = AiProviderPresets.ModelsFor(preset.Id).Any(option => option.Id == id) ?
                Visibility.Visible : Visibility.Collapsed;
        if (preset.Id is "venice" or "qwen") SelectPresetModel(selectedPresetModels[preset.Id]);
        if (preset.Id == "ollama" && changedProvider)
            model.Text = previous is not null && AiProviderPresets.Match(previous).Id == "ollama" ?
                previous.Model : preset.Model;
        if (preset.Id == "ollama") _ = LoadLocalModelsAsync();
        keyFields.Visibility = preset.Id == "ollama" ? Visibility.Collapsed : Visibility.Visible;
        privacyHint.Visibility = preset.Id == "ollama" ? Visibility.Collapsed : Visibility.Visible;
        pricingFields.Visibility = consumer.Id == "agent" && preset.Id == "custom" ?
            Visibility.Visible : Visibility.Collapsed;
        summary.Text = preset.Id == "custom" ? "填写兼容 Chat Completions 的 HTTPS 接口地址和模型名称。" :
            preset.Id == "ollama" ? "通过本机 Ollama 运行；填写已下载的模型名称。" :
            preset.Id == "venice" ? "选择云端模型，填写 Venice API Key。" :
            preset.Id == "qwen" ? "选择北京地域模型，填写百炼 API Key。" :
            $"默认模型：{preset.Model}\n接口：{preset.Endpoint}";
        var existing = previous is not null &&
            string.Equals(previous.Endpoint, preset.Id == "custom" ? endpoint.Text.Trim() : preset.Endpoint, StringComparison.OrdinalIgnoreCase);
        keyHint.Text = existing && !string.IsNullOrEmpty(previous!.EncryptedKey) ?
            "已保存此服务的密钥；留空将继续使用。" :
            preset.RequiresKey ? "请输入此服务商提供的 API Key。" : "如服务不需要密钥，可留空。";
        RefreshPromptFields();
    }

    private void SelectPresetModel(string id)
    {
        if (!AiProviderPresets.ModelsFor(selected.Id).Any(option => option.Id == id)) return;
        selectedPresetModels[selected.Id] = id;
        foreach (var (optionId, button) in presetModelButtons)
        {
            var active = optionId == id;
            button.BorderBrush = active ? accent : border;
            button.BorderThickness = new Thickness(active ? 2 : 1);
            button.Background = active ? Brush(background.Color.R < 80 ? "#332B50" : "#F9ECE5") : surface;
        }
        RefreshPromptFields();
    }

    private void RefreshPromptFields()
    {
        if (promptEditors.Count == 0) return;
        var url = selected.Id == "custom" ? endpoint.Text.Trim() : selected.Endpoint;
        var name = selected.Id is "venice" or "qwen" ? selectedPresetModels[selected.Id] :
            selected.Id is "custom" or "ollama" ? model.Text.Trim() : selected.Model;
        var keyName = url + "\n" + name;
        if (promptLoadedKey == keyName) return;
        promptLoadedKey = keyName;
        var settings = new AiSettings(url, name, "");
        foreach (var (scope, editor) in promptEditors)
            editor.Text = AgentPromptStore.Load(settings, scope, AgentPromptStore.DefaultPath);
    }
    private void Save(object sender, RoutedEventArgs args)
    {
        try
        {
            var url = selected.Id == "custom" ? endpoint.Text.Trim() : selected.Endpoint;
            var name = selected.Id is "venice" or "qwen" ? selectedPresetModels[selected.Id] :
                selected.Id is "custom" or "ollama" ? model.Text.Trim() : selected.Model;
            var sameService = previous is not null &&
                string.Equals(previous.Endpoint, url, StringComparison.OrdinalIgnoreCase);
            if (selected.RequiresKey && key.Password.Length == 0 &&
                (!sameService || string.IsNullOrEmpty(previous!.EncryptedKey)))
                throw new InvalidDataException("请填写所选服务的 API Key");
            var encrypted = selected.Id == "ollama" ? "" : key.Password.Length > 0 ? VaultCrypto.Protect(key.Password) :
                sameService ? previous!.EncryptedKey : "";
            AiTokenPricing? pricing = null;
            if (consumer.Id == "agent" && selected.Id == "custom" &&
                (inputPrice.Text.Length > 0 || cachedPrice.Text.Length > 0 || outputPrice.Text.Length > 0))
            {
                static decimal ParsePrice(string input) =>
                    decimal.TryParse(input.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) &&
                    value is >= 0 and <= 100000 ? value : throw new InvalidDataException("请输入有效的每百万 token 单价");
                pricing = new AiTokenPricing(priceCurrency.SelectedItem?.ToString() ?? "CNY",
                    ParsePrice(inputPrice.Text), ParsePrice(cachedPrice.Text), ParsePrice(outputPrice.Text));
            }
            var settings = new AiSettings(url, name, encrypted, pricing);
            AiSettings.Validate(url, name);
            foreach (var (scope, editor) in promptEditors)
                AgentPromptStore.Save(settings, scope, editor.Text.Trim(), AgentPromptStore.DefaultPath);
            AiSettings.Save(consumer.Id, settings); Settings = settings; key.Clear(); DialogResult = true;
        }
        catch (Exception error) when (error is InvalidDataException or UriFormatException)
        { MessageBox.Show(this, error.Message, "AI 答题设置", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
