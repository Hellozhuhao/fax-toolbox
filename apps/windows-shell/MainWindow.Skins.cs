using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private JsonObject? skinState;
    private SkinStore CreateSkinStore() => new(Path.Combine(root, "apps", "windows-shell", "ui", "skins", "builtin.json"),
        Path.Combine(smokeDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaxToolbox"), "skins.json"));

    private void HandleSkinMessage(string command, JsonElement message)
    {
        try
        {
            var store = CreateSkinStore();
            var result = command switch
            {
                "skins.get" => store.Read(),
                "skins.reset" => store.Reset(),
                "skins.update" => store.Write(message.GetProperty("state")),
                _ => throw new InvalidDataException("未知皮肤操作")
            };
            if (command == "skins.get") PublishSkinState(result);
            else foreach (var window in TaskWindows.Append(this).Distinct().ToArray()) window.PublishSkinState(result);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or InvalidOperationException or ArgumentException or FormatException)
        {
            ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { v = 1, kind = "skins.error", detail = error.Message }));
        }
    }

    private void PublishSkinState(JsonObject value)
    {
        skinState = (JsonObject)value.DeepClone();
        SaveHostTheme(SkinStore.Resolve(skinState, "shell")["appearance"]!.GetValue<string>() == "dark");
        ApplyNativeSkin();
        ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { v = 1, kind = "skins.state", state = value }));
    }

    private void ApplyNativeSkin()
    {
        if (skinState is null) return;
        var scope = currentShellView is "home" or "modules" or "workspace" or "activity" or "settings" or "vault" ? "shell" : currentShellView;
        var skin = SkinStore.Resolve(skinState, scope);
        var tokens = skin["tokens"]!;
        SolidColorBrush Brush(string name) => new((Color)ColorConverter.ConvertFromString(tokens[name]!.GetValue<string>()));
        ApplyHostTheme(skin["appearance"]!.GetValue<string>() == "dark");
        NativeUi.Apply(this, tokens);
        Background = Brush("background"); TitleBar.Background = Brush("navigation"); BrowserToolbar.Background = Brush("surface");
        TitleName.Foreground = Brush("text"); TitleStatus.Foreground = ToolbarTitle.Foreground = Brush("muted");
        WorkspaceSplitter.Background = Brush("border"); ToggleControlsButton.Background = Brush("panel"); ToggleControlsButton.Foreground = Brush("text");
        CourseRetryButton.Background = Brush("accent"); CourseRetryButton.Foreground = NativeUi.Brush(this, "OnAccent"); CourseOverlay.Background = TaskLoadingSurface.Background = Brush("background");
        CourseOverlayTitle.Foreground = TaskLoadingTitle.Foreground = Brush("text");
        CourseOverlayDetail.Foreground = TaskLoadingDetail.Foreground = Brush("muted");
        Resources["ContextTextBrush"] = Brush("text"); Resources["ContextHoverBrush"] = Brush("surface"); Resources["ContextHoverTextBrush"] = Brush("accent");
        UpdateTaskTabs();
        ApplyDemoSkin();
        if (androidMirrorWindow is not null) ApplyAndroidSkin();
    }

    private void ApplyDemoSkin()
    {
        if (skinState is null || ModuleView.CoreWebView2 is null || ModuleView.Source?.AbsoluteUri != ModuleUrl) return;
        var skin = SkinStore.Resolve(skinState, "demo");
        var palette = JsonSerializer.Serialize(skin["tokens"]);
        _ = ModuleView.CoreWebView2.ExecuteScriptAsync("(()=>{const t=" + palette + ";const r=document.documentElement;for(const [key,value] of Object.entries(t)){const name={background:'bg',muted:'subtle',fontFamily:'skin-font-family',radius:'skin-radius'}[key]||key;r.style.setProperty('--'+name,key==='radius'?value+'px':value);}r.style.colorScheme=" + JsonSerializer.Serialize(skin["appearance"]!.GetValue<string>()) + ";})();");
    }

    private void ApplyAndroidSkin()
    {
        if (androidMirrorWindow is null || skinState is null) return;
        var skin = SkinStore.Resolve(skinState, "android");
        var tokens = skin["tokens"]!;
        NativeUi.Apply(androidMirrorWindow, tokens);
        androidMirrorWindow.Resources["Skin.radius"] = new CornerRadius(Math.Min(16, tokens["radius"]!.GetValue<int>()));
        foreach (var name in new[] { "background", "panel", "surface", "text", "muted", "accent", "border", "navigation" })
            androidMirrorWindow.Resources["Skin." + name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tokens[name]!.GetValue<string>()));
        androidMirrorWindow.Resources["Skin.onAccent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(NativeUi.OnAccent(NativeUi.Brush(androidMirrorWindow, "Accent")).Color.ToString()));
    }

    private void SkinRecoveryKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.F12 || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) || string.IsNullOrWhiteSpace(root)) return;
        args.Handled = true;
        using var empty = JsonDocument.Parse("{}");
        HandleSkinMessage("skins.reset", empty.RootElement);
        ShellView.CoreWebView2?.Reload();
    }
}
