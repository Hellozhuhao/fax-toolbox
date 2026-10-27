using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FaxToolbox.Windows;

internal static class NativeUi
{
    static NativeUi()
    {
        void CopyPopup(FrameworkElement popup, FrameworkElement? target)
        {
            if (target is null) return;
            foreach (var role in Roles) popup.Resources["Ui." + role] = Brush(target, role);
            popup.Resources["Ui.Font"] = target.TryFindResource("Ui.Font");
        }
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is ContextMenu menu) CopyPopup(menu, menu.PlacementTarget as FrameworkElement);
        }));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is ToolTip tip) CopyPopup(tip, tip.PlacementTarget as FrameworkElement);
        }));
    }
    internal static readonly string[] Roles = ["Background", "Panel", "Surface", "Text", "Muted", "Accent", "Border", "OnAccent", "Error"];
    internal static SolidColorBrush Brush(FrameworkElement owner, string role) =>
        owner.TryFindResource("Ui." + role) as SolidColorBrush ?? new SolidColorBrush(Colors.Transparent);

    internal static FontFamily Font(string? family) => string.IsNullOrWhiteSpace(family) || family.Contains("Fax Sans", StringComparison.OrdinalIgnoreCase)
        ? new FontFamily("pack://application:,,,/assets/fonts/#Fax Sans, Segoe UI, Microsoft YaHei UI")
        : new FontFamily(family.Replace("\"", "").Replace("'", ""));

    internal static SolidColorBrush OnAccent(SolidColorBrush accent)
    {
        static double Linear(byte value) { var n = value / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        var color = accent.Color;
        var light = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        return new SolidColorBrush((light + .05) / .05 > 1.05 / (light + .05) ? Color.FromRgb(16, 24, 32) : Colors.White);
    }

    internal static void Apply(Window window, JsonNode tokens)
    {
        foreach (var (key, name) in new[] { ("background", "Background"), ("panel", "Panel"), ("surface", "Surface"), ("text", "Text"), ("muted", "Muted"), ("accent", "Accent"), ("border", "Border") })
            SetBrush(window, name, (Color)ColorConverter.ConvertFromString(tokens[key]!.GetValue<string>()));
        SetBrush(window, "OnAccent", OnAccent(Brush(window, "Accent")).Color);
        ApplyError(window);
        window.FontFamily = Font(tokens["fontFamily"]?.GetValue<string>());
        window.Resources["Ui.Font"] = window.FontFamily;
    }

    private static void SetBrush(Window window, string role, Color value)
    {
        var key = "Ui." + role;
        if (window.Resources.Contains(key) && window.Resources[key] is SolidColorBrush existing && !existing.IsFrozen)
            existing.Color = value;
        else window.Resources[key] = new SolidColorBrush(value);
    }

    private static void ApplyError(Window window)
    {
        static double Light(Color color)
        {
            static double Linear(byte value) { var n=value/255d; return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4); }
            return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
        }
        var panel=Brush(window,"Panel").Color;
        var background=Light(panel);
        var candidates=new[] { Color.FromRgb(179,41,57),Color.FromRgb(255,160,173) };
        var error=candidates.FirstOrDefault(value=>(Math.Max(Light(value),background)+.05)/(Math.Min(Light(value),background)+.05)>=4.5);
        SetBrush(window,"Error",error==default?OnAccent(new SolidColorBrush(panel)).Color:error);
    }

    internal static void ApplyDefault(Window window, bool dark)
    {
        foreach (var (role, light, night) in new[] {
            ("Background", "#F6F7FB", "#14171E"), ("Panel", "#FFFFFF", "#20252F"),
            ("Surface", "#EEF1F7", "#2B3240"), ("Text", "#202B3C", "#F2F4FA"),
            ("Muted", "#59677A", "#A6B0C2"), ("Accent", "#2859D6", "#B5A0FF"), ("Border", "#D7DEEA", "#3D4658") })
            SetBrush(window, role, (Color)ColorConverter.ConvertFromString(dark ? night : light));
        SetBrush(window, "OnAccent", OnAccent(Brush(window, "Accent")).Color);
        ApplyError(window);
        window.FontFamily = Font(null); window.Resources["Ui.Font"] = window.FontFamily;
    }

    internal static void Inherit(Window window, Window owner)
    {
        foreach (var role in Roles) window.Resources["Ui." + role] = Brush(owner, role);
        window.Resources["Ui.Font"] = owner.FontFamily;
        window.FontFamily = owner.FontFamily;
    }
}
