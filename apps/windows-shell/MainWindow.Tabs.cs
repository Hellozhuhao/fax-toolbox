using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    // Each tab owns its WebViews and task state. Closing the tab disposes that context.
    private static readonly List<MainWindow> TaskWindows = [];
    private string? assignedModule;
    private string? initialModule;
    private bool closingAsTab;
    private bool closingTask;
    private bool darkTaskTabs;
    private readonly TaskCompletionSource<bool> taskWindowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<MainWindow, TaskTabVisual> taskTabVisuals = [];

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
            var shrink = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            shrink.Completed += (_, _) =>
            {
                TaskTabsPanel.Children.Remove(view.Shell);
                taskTabVisuals.Remove(stale);
            };
            view.Shell.BeginAnimation(WidthProperty, shrink);
            view.Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        }
        for (var index = 0; index < windows.Length; index++)
        {
            var window = windows[index];
            if (!taskTabVisuals.TryGetValue(window, out var view))
            {
                view = CreateTaskTabVisual(window);
                taskTabVisuals.Add(window, view);
                TaskTabsPanel.Children.Insert(Math.Min(index, TaskTabsPanel.Children.Count), view.Shell);
                view.Shell.BeginAnimation(WidthProperty, new DoubleAnimation(0, 164,
                    TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                view.Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
                    TimeSpan.FromMilliseconds(180)));
            }
            var selected = ReferenceEquals(window, this);
            var label = window.assignedModule switch
            {
                "course" => (window.courseState == "RUNNING" ? "● " : "") + "学习通",
                "homework" => (window.homeworkBusy ? "● " : "") + "作业校对",
                "demo" => (window.state == "OBSERVING" ? "● " : "") + "页面演示",
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
        UpdateBrowserBackButton();
    }

    private TaskTabVisual CreateTaskTabVisual(MainWindow window)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        var open = new Button { Height = 35, Style = (Style)FindResource("TaskTabInnerStyle") };
        open.Click += (_, _) => _ = ActivateTaskWindowAsync(window);
        Grid.SetColumn(open, 0);
        grid.Children.Add(open);
        var close = new Button
        {
            Content = "×", Height = 27, Width = 27, FontSize = 18,
            ToolTip = "关闭标签并结束此任务", Style = (Style)FindResource("TaskTabInnerStyle")
        };
        close.Click += (_, _) => CloseTaskWindow(window);
        NativeButtonGlow.Attach(close, () => darkTaskTabs);
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);
        var shell = new Border
        {
            Child = grid, Width = 164, Height = 37, Opacity = 1,
            CornerRadius = new CornerRadius(13, 13, 5, 5),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 5, 0), ClipToBounds = true
        };
        return new TaskTabVisual(shell, open, close);
    }

    private void UpdateBrowserBackButton()
    {
        if (BrowserBackButton is null) return;
        var module = assignedModule is "course" or "homework" || activeModule is "chaoxing" or "homework";
        BrowserBackButton.Visibility = assignedModule is null ? Visibility.Collapsed : Visibility.Visible;
        BrowserBackButton.IsEnabled = module ? CourseView.CoreWebView2?.CanGoBack == true :
            ModuleView.CoreWebView2?.CanGoBack == true;
    }

    private void BrowserBack_Click(object sender, RoutedEventArgs e)
    {
        if (assignedModule is "course" or "homework")
        {
            if (CourseView.CoreWebView2?.CanGoBack == true) CourseView.CoreWebView2.GoBack();
        }
        else if (ModuleView.CoreWebView2?.CanGoBack == true) ModuleView.CoreWebView2.GoBack();
    }

    private MainWindow CreateTaskWindow(string? module = null)
    {
        var window = new MainWindow(tabSmokeMode) { initialModule = module };
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
        ShowTaskUiAsync(assignedModule switch { "course" => "openCourse", "homework" => "openHomework", _ => "openDemo" });

    private async Task ShowTaskUiAsync(string action)
    {
        if (ShellView.CoreWebView2 is null) return;
        try { await ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi?." + action + "()"); }
        catch (Exception error) { Trace("task tab navigation failed: " + error.Message); }
    }

    private async Task ActivateTaskWindowAsync(MainWindow target)
    {
        if (ReferenceEquals(target, this) || !TaskWindows.Contains(target)) return;
        await Task.WhenAny(target.taskWindowReady.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (!TaskWindows.Contains(target) || target.closingTask) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
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
        target.ShowActivated = true;
        if (target.Visibility != Visibility.Visible) target.Show();
        target.ShowInTaskbar = !smokeMode;
        target.Activate();
        await target.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        Hide();
        target.UpdateTaskTabs();
    }

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
            target.CancelHomeworkReview();
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
        // The system close button exits the toolbox and ends all tabs.
        foreach (var window in TaskWindows.ToArray())
        {
            if (ReferenceEquals(window, this)) continue;
            window.closingAsTab = true;
            window.Close();
        }
    }
}
