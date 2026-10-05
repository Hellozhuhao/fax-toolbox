using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    // Each tab owns its WebViews and task state. Closing the tab disposes that context.
    private static readonly List<MainWindow> TaskWindows = [];
    private string? assignedModule;
    private string? initialModule;
    private bool closingAsTab;
    private bool detachedWindow;
    private bool closingTask;
    private bool darkTaskTabs;
    private bool shellCanGoBack;
    private bool minimizing;
    private readonly TaskCompletionSource<bool> taskWindowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<MainWindow, TaskTabVisual> taskTabVisuals = [];

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleWindowMaximized(); e.Handled = true; }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void ToggleWindowMaximized()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            minimizing = false;
        }
        if (TitleMaximizeButton is not null)
            TitleMaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void TitleMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (minimizing || WindowState == WindowState.Minimized) return;
        if (!SystemParameters.MenuAnimation || smokeMode)
        {
            WindowState = WindowState.Minimized;
            return;
        }
        minimizing = true;
        var fade = new DoubleAnimation(1, 0.18, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) =>
        {
            if (!minimizing) return;
            WindowState = WindowState.Minimized;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            minimizing = false;
        };
        BeginAnimation(OpacityProperty, fade);
    }
    private void TitleMaximize_Click(object sender, RoutedEventArgs e) => ToggleWindowMaximized();
    private void TitleClose_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class TaskTabVisual(Border shell, Button open, Button close)
    {
        public Border Shell { get; } = shell;
        public Button Open { get; } = open;
        public Button Close { get; } = close;
        public string Label { get; set; } = "";
        public bool Selected { get; set; }
        public bool Dark { get; set; }
        public bool Closing { get; set; }
    }

    private void RegisterTaskWindow()
    {
        if (!smokeMode || tabSmokeMode) TaskWindows.Add(this);
        UpdateAllTaskTabs();
    }

    private static void UpdateAllTaskTabs()
    {
        foreach (var window in TaskWindows.ToArray()) window.UpdateTaskTabs();
    }

    private async Task SyncTaskThemesAsync(bool dark)
    {
        foreach (var window in TaskWindows.ToArray())
        {
            if (ReferenceEquals(window, this)) continue;
            window.ApplyHostTheme(dark);
            if (window.ShellView.CoreWebView2 is null) continue;
            try
            {
                await window.ShellView.CoreWebView2.ExecuteScriptAsync(
                    "window.FaxToolboxUi?.applyThemeFromHost('" + (dark ? "dark" : "light") + "')");
            }
            catch (InvalidOperationException) { /* The other tab may still be navigating. */ }
        }
    }

    private async Task SyncTaskDeveloperModesAsync(bool enabled)
    {
        foreach (var window in TaskWindows.ToArray())
        {
            if (ReferenceEquals(window, this)) continue;
            window.SetDeveloperMode(enabled);
            if (window.ShellView.CoreWebView2 is null) continue;
            try
            {
                await window.ShellView.CoreWebView2.ExecuteScriptAsync(
                    "window.FaxToolboxUi?.applyDeveloperModeFromHost(" + (enabled ? "true" : "false") + ")");
            }
            catch (InvalidOperationException) { /* The other tab may still be navigating. */ }
        }
    }

    private void RefreshTaskTabs()
    {
        if (TaskWindows.Count == 0) UpdateTaskTabs();
        else UpdateAllTaskTabs();
    }

    private void UpdateTaskTabs()
    {
        static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
        MainWindow[] windows = smokeMode && !tabSmokeMode ? [this] : TaskWindows.ToArray();
        foreach (var stale in taskTabVisuals.Keys.Where(window => !windows.Contains(window)).ToArray())
        {
            var view = taskTabVisuals[stale];
            if (view.Closing) continue;
            view.Closing = true;
            view.Shell.IsHitTestVisible = false;
            void RemoveTab()
            {
                TaskTabsPanel.Children.Remove(view.Shell);
                taskTabVisuals.Remove(stale);
            }
            if (!SystemParameters.MenuAnimation || smokeMode) { RemoveTab(); continue; }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) => RemoveTab();
            view.Shell.BeginAnimation(OpacityProperty, fade);
            if (view.Shell.RenderTransform is ScaleTransform closingScale)
            {
                var settle = new DoubleAnimation(.96, TimeSpan.FromMilliseconds(150));
                closingScale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                closingScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            }
        }
        for (var index = 0; index < windows.Length; index++)
        {
            var window = windows[index];
            if (!taskTabVisuals.TryGetValue(window, out var view))
            {
                view = CreateTaskTabVisual(window);
                taskTabVisuals.Add(window, view);
                TaskTabsPanel.Children.Insert(Math.Min(index, TaskTabsPanel.Children.Count), view.Shell);
                if (SystemParameters.MenuAnimation && !smokeMode)
                {
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    var duration = TimeSpan.FromMilliseconds(190);
                    var scale = (ScaleTransform)view.Shell.RenderTransform;
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                        new DoubleAnimation(.96, 1, duration) { EasingFunction = ease });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(.96, 1, duration) { EasingFunction = ease });
                    view.Shell.BeginAnimation(OpacityProperty,
                        new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
                }
            }
            var selected = ReferenceEquals(window, this);
            var label = window.assignedModule switch
            {
                "course" => (window.courseState == "RUNNING" ? "● " : "") + "学习通",
                "homework" => (window.homeworkBusy ? "● " : "") + "作业校对",
                "demo" => (window.state == "OBSERVING" ? "● " : "") + "页面演示",
                "ai" => "Agent",
                "proxy" => "网络管家",
                _ => "首页"
            };
            if (view.Label == label && view.Selected == selected && view.Dark == darkTaskTabs) continue;
            view.Label = label;
            view.Selected = selected;
            view.Dark = darkTaskTabs;
            view.Open.Content = label;
            view.Shell.Background = Color(selected ? darkTaskTabs ? "#7858EB" : "#F5E5DD" :
                darkTaskTabs ? "#24252C" : "#FFFFFF");
            view.Shell.BorderBrush = Color(darkTaskTabs ? "#454650" : "#E6E4E1");
            view.Open.Foreground = Color(selected ? darkTaskTabs ? "#FFFFFF" : "#A94F33" :
                darkTaskTabs ? "#F5F5F8" : "#171716");
            view.Close.Foreground = Color(selected && darkTaskTabs ? "#FFFFFF" :
                darkTaskTabs ? "#A3A3AD" : "#74716E");
        }
        NewTaskTabButton.Background = Color(darkTaskTabs ? "#24252C" : "#FFFFFF");
        NewTaskTabButton.Foreground = Color(darkTaskTabs ? "#F5F5F8" : "#74716E");
        NewTaskTabButton.BorderBrush = Color(darkTaskTabs ? "#454650" : "#E6E4E1");
        BrowserBackButton.Background = Color(darkTaskTabs ? "#24252C" : "#FFFFFF");
        BrowserBackButton.Foreground = Color(darkTaskTabs ? "#F5F5F8" : "#74716E");
        BrowserBackButton.BorderBrush = Color(darkTaskTabs ? "#454650" : "#E6E4E1");
        BrowserReloadButton.Background = BrowserBackButton.Background;
        BrowserReloadButton.Foreground = BrowserBackButton.Foreground;
        BrowserReloadButton.BorderBrush = BrowserBackButton.BorderBrush;
        UpdateBrowserBackButton();
    }

    private TaskTabVisual CreateTaskTabVisual(MainWindow window)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        var open = new Button { Height = 35, Style = (Style)FindResource("TaskTabInnerStyle") };
        open.Click += (_, _) => _ = ReferenceEquals(window, this) ? RestoreCurrentTabAsync() : ActivateTaskWindowAsync(window);
        Grid.SetColumn(open, 0);
        grid.Children.Add(open);
        var close = new Button
        {
            Content = "×", Height = 27, Width = 27, FontSize = 18,
            ToolTip = "关闭标签并结束此任务", Style = (Style)FindResource("TaskTabCloseStyle")
        };
        close.Click += (_, _) => CloseTaskWindow(window);
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);
        var shell = new Border
        {
            Child = grid, Width = 164, Height = 37, Opacity = 1,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1),
            CornerRadius = new CornerRadius(13, 13, 5, 5),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 5, 0), ClipToBounds = true
        };
        return new TaskTabVisual(shell, open, close);
    }

    private void UpdateBrowserBackButton()
    {
        if (BrowserBackButton is null) return;
        var module = WorkspaceColumn.Width.Value > 0 && activeModule is ("chaoxing" or "homework");
        BrowserBackButton.Visibility = Visibility.Visible;
        BrowserBackButton.IsEnabled = shellCanGoBack || (module ? CourseView.CoreWebView2?.CanGoBack == true :
            WorkspaceColumn.Width.Value > 0 && ModuleView.CoreWebView2?.CanGoBack == true);
    }

    private void BrowserBack_Click(object sender, RoutedEventArgs e) =>
        NavigateBack((Keyboard.Modifiers & ModifierKeys.Shift) != 0);

    private void NavigateBack(bool toolboxPage)
    {
        if (toolboxPage)
        {
            if (shellCanGoBack && ShellView.CoreWebView2 is not null)
                _ = ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi?.goBack()");
            return;
        }
        if (WorkspaceColumn.Width.Value > 0 && activeModule is ("chaoxing" or "homework"))
        {
            if (CourseView.CoreWebView2?.CanGoBack == true) { CourseView.CoreWebView2.GoBack(); return; }
        }
        else if (WorkspaceColumn.Width.Value > 0 && ModuleView.CoreWebView2?.CanGoBack == true)
        { ModuleView.CoreWebView2.GoBack(); return; }
        if (WorkspaceColumn.Width.Value == 0 && shellCanGoBack && ShellView.CoreWebView2 is not null)
            _ = ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi?.goBack()");
    }

    private Task RestoreCurrentTabAsync() =>
        assignedModule is not null ? RestoreAssignedModuleUiAsync() : Task.CompletedTask;

    private void BrowserReload_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceColumn.Width.Value > 0 && activeModule is ("chaoxing" or "homework") &&
            CourseView.CoreWebView2 is not null) CourseView.CoreWebView2.Reload();
        else if (WorkspaceColumn.Width.Value > 0 && ModuleView.CoreWebView2 is not null)
            ModuleView.CoreWebView2.Reload();
        else if (ShellView.CoreWebView2 is not null)
            _ = ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi?.refresh()");
    }

    private MainWindow CreateTaskWindow(string? module = null)
    {
        var window = new MainWindow(tabSmokeMode) { initialModule = module, assignedModule = module };
        UpdateAllTaskTabs();
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        // Initialize WebView2 off screen while the current tab remains visible.
        window.Left = -32000;
        window.Top = -32000;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();
        return window;
    }

    private Task RestoreAssignedModuleUiAsync() =>
        ShowTaskUiAsync(assignedModule switch { "course" => "openCourse", "homework" => "openHomework", "ai" => "openAi", "proxy" => "openProxy", _ => "openDemo" });

    private async Task ShowTaskUiAsync(string action)
    {
        if (ShellView.CoreWebView2 is null) return;
        try { await ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi?." + action + "()"); }
        catch (Exception error) { Trace("task tab navigation failed: " + error.Message); }
    }

    private async Task ActivateTaskWindowAsync(MainWindow target)
    {
        if (ReferenceEquals(target, this) || !TaskWindows.Contains(target)) return;
        if (!TaskWindows.Contains(target) || target.closingTask) return;
        // Keep the old HWND in front until the target has painted at its final size.
        if (!await target.taskWindowReady.Task) return;
        if (target.ShellView.CoreWebView2 is not null)
        {
            try
            {
                await target.ShellView.CoreWebView2.ExecuteScriptAsync(
                    "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))");
            }
            catch (InvalidOperationException) { return; }
        }
        if (target.closingTask || !TaskWindows.Contains(target)) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        // Changing taskbar membership can recreate a WPF HWND. Do it before
        // moving a freshly created off-screen tab into the visible bounds.
        target.ShowInTaskbar = !smokeMode;
        // Keep maximized tabs maximized throughout the handoff. Switching through
        // Normal made Windows animate two different window sizes on every click.
        if (WindowState == WindowState.Normal && target.WindowState != WindowState.Normal)
            target.WindowState = WindowState.Normal;
        if (target.WindowState == WindowState.Normal)
        {
            target.Left = bounds.Left;
            target.Top = bounds.Top;
            target.Width = bounds.Width;
            target.Height = bounds.Height;
        }
        if (target.WindowState != WindowState) target.WindowState = WindowState;
        // WPF requires ShowActivated when showing a maximized window. The
        // source is immediately reactivated so it still covers the repaint.
        target.ShowActivated = target.WindowState == WindowState.Maximized;
        if (target.Visibility != Visibility.Visible) target.Show();
        target.UpdateLayout();
        // Show() and resizing invalidate WebView2's surface even if the page has
        // loaded. Wait for the compositor at the new bounds while the old tab
        // still covers it, then perform the visible handoff.
        Activate();
        await target.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        if (target.ShellView.CoreWebView2 is not null)
        {
            try
            {
                await target.ShellView.CoreWebView2.ExecuteScriptAsync(
                    "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve(true))))");
            }
            catch (InvalidOperationException) { return; }
        }
        if (target.closingTask || !TaskWindows.Contains(target)) return;
        DwmFlush();
        target.Activate();
        DwmFlush();
        Hide();
        target.UpdateTaskTabs();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    private async void NewTaskTab_Click(object sender, RoutedEventArgs e)
    {
        if (smokeMode && !tabSmokeMode) return;
        var window = CreateTaskWindow();
        await ActivateTaskWindowAsync(window);
    }

    private async void OpenModuleInNewTab(string module)
    {
        if (smokeMode && !tabSmokeMode) return;
        var window = CreateTaskWindow(module);
        await ActivateTaskWindowAsync(window);
    }

    private async void CloseTaskWindow(MainWindow target)
    {
        if ((smokeMode && !tabSmokeMode) || target.closingTask) return;
        var index = TaskWindows.IndexOf(target);
        if (index < 0) return;
        target.closingTask = true;
        try
        {
            target.CancelAiRequest("标签已关闭，AI 请求已取消。");
            target.CancelAgent();
            target.CancelHomeworkReview();
            target.homeworkExplanationWindow?.Close();
            if (target.courseSessionId is not null && target.courseState != "STOPPED")
                await target.CourseCommandAsync("stop");
            if (target.sessionId is not null) await target.CancelSessionAsync();
        }
        catch (Exception error) { target.Trace("task close cleanup: " + error.Message); }
        MainWindow? next = null;
        if (ReferenceEquals(target, this))
            next = TaskWindows.Count > 1 ? TaskWindows[index > 0 ? index - 1 : 1] : CreateTaskWindow();
        if (next is not null)
        {
            await target.ActivateTaskWindowAsync(next);
        }
        target.closingAsTab = true;
        target.Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closingAsTab || smokeMode) return;
        if (detachedWindow)
        {
            if (!TaskWindows.Any(window => !ReferenceEquals(window, this) && window.Visibility == Visibility.Visible))
            {
                var next = TaskWindows.FirstOrDefault(window => !ReferenceEquals(window, this) && !window.closingTask);
                if (next is not null) { next.Show(); next.Activate(); }
            }
            return;
        }
        if (TaskWindows.Any(window => !ReferenceEquals(window, this) && window.detachedWindow &&
            window.Visibility == Visibility.Visible)) return;
        // The system close button exits the toolbox and ends all tabs.
        foreach (var window in TaskWindows.ToArray())
        {
            if (ReferenceEquals(window, this)) continue;
            window.closingAsTab = true;
            window.Close();
        }
    }
}
