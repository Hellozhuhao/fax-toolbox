using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FaxToolbox.Windows;

internal static class NativeNotice
{
    internal static void Show(Window owner, string title, string message)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 520, SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.WorkArea.Height * .8, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent };
        NativeUi.Inherit(dialog, owner);
        var body = new StackPanel { Margin = new Thickness(26) };
        var heading = new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = NativeUi.Brush(owner, "Text"), Margin = new Thickness(0, 0, 0, 14) };
        heading.MouseLeftButtonDown += (_, args) => { if (args.ChangedButton == MouseButton.Left) dialog.DragMove(); };
        body.Children.Add(heading);
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = NativeUi.Brush(owner, "Muted"), FontSize = 13 });
        var close = new Button { Content = "关闭", IsDefault = true, IsCancel = true, MinWidth = 84,
            HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 22, 0, 0), Background = NativeUi.Brush(owner, "Accent"), Foreground = NativeUi.Brush(owner, "OnAccent") };
        close.Click += (_, _) => dialog.Close(); body.Children.Add(close);
        dialog.Content = new Border { Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Margin = new Thickness(8), BorderThickness = new Thickness(1), BorderBrush = NativeUi.Brush(owner, "Border"),
            Background = NativeUi.Brush(owner, "Panel"), CornerRadius = new CornerRadius(14) };
        dialog.ShowDialog();
    }
}
