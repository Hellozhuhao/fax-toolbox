using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FaxToolbox.Windows;

internal static class NativeButtonGlow
{
    private const double Radius = 270;

    public static void Attach(Button button, Func<bool> isDark, bool primary = false)
    {
        var state = new GlowState(button, isDark, primary);
        button.MouseEnter += state.MouseEnter;
        button.MouseMove += state.MouseMove;
        button.MouseLeave += state.MouseLeave;
        button.IsEnabledChanged += (_, _) => { if (!button.IsEnabled) state.Stop(); };
        button.IsVisibleChanged += (_, _) => { if (!button.IsVisible) state.Stop(); };
        button.Unloaded += (_, _) => state.Stop();
    }

    private sealed class GlowState(Button button, Func<bool> isDark, bool primary)
    {
        private Brush? original;
        private RadialGradientBrush? glow;
        private Point current;
        private Point target;
        private bool rendering;

        public void MouseEnter(object sender, MouseEventArgs args) => Start(args.GetPosition(button));

        public void MouseMove(object sender, MouseEventArgs args)
        {
            if (!button.IsEnabled) { Stop(); return; }
            var position = args.GetPosition(button);
            if (glow is not null && !ReferenceEquals(button.Background, glow)) Stop();
            if (glow is null) Start(position);
            if (glow is null) return;
            target = position;
            if (!rendering)
            {
                CompositionTarget.Rendering += Render;
                rendering = true;
            }
        }

        public void MouseLeave(object sender, MouseEventArgs args) => Stop();

        private void Start(Point position)
        {
            if (!button.IsEnabled || glow is not null || button.Background is not SolidColorBrush baseBrush) return;
            original = button.Background;
            current = target = position;
            var dark = isDark();
            var highlight = dark ? primary ? Color.FromRgb(197, 176, 255) : Color.FromRgb(156, 121, 247) :
                primary ? Colors.White : Color.FromRgb(214, 107, 73);
            var strength = dark ? primary ? .68 : .50 : primary ? .53 : .42;
            glow = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = position,
                GradientOrigin = position,
                RadiusX = Radius,
                RadiusY = Radius,
                GradientStops = new GradientStopCollection
                {
                    new(Blend(baseBrush.Color, highlight, strength), 0),
                    new(Blend(baseBrush.Color, highlight, strength * .5), .48),
                    new(baseBrush.Color, .78),
                    new(baseBrush.Color, 1)
                }
            };
            button.Background = glow;
        }

        private void Render(object? sender, EventArgs args)
        {
            if (glow is null || !button.IsEnabled || !button.IsMouseOver ||
                !ReferenceEquals(button.Background, glow)) { Stop(); return; }
            current = new Point(current.X + (target.X - current.X) * .28,
                current.Y + (target.Y - current.Y) * .28);
            if (Math.Abs(target.X - current.X) + Math.Abs(target.Y - current.Y) < .4)
            {
                current = target;
                CompositionTarget.Rendering -= Render;
                rendering = false;
            }
            glow.Center = current;
            glow.GradientOrigin = current;
        }

        public void Stop()
        {
            if (rendering)
            {
                CompositionTarget.Rendering -= Render;
                rendering = false;
            }
            if (glow is not null && ReferenceEquals(button.Background, glow) && original is not null)
                button.Background = original;
            glow = null;
            original = null;
        }

        private static Color Blend(Color baseColor, Color highlight, double amount) => Color.FromRgb(
            (byte)Math.Round(baseColor.R + (highlight.R - baseColor.R) * amount),
            (byte)Math.Round(baseColor.G + (highlight.G - baseColor.G) * amount),
            (byte)Math.Round(baseColor.B + (highlight.B - baseColor.B) * amount));
    }
}
