using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        TaskTabsPanel.Children.Clear();
        foreach (var window in smokeMode && !tabSmokeMode ? new[] { this } : TaskWindows.ToArray())
        {
            var tab = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 5, 0) };
            var selected = ReferenceEquals(window, this);
            var label = window.assignedModule switch
            {
                "course" => (window.courseState == "RUNNING" ? "● " : "") + "学习通",
                "demo" => (window.state == "OBSERVING" ? "● " : "") + "页面演示",
                _ => "首页"
            };
            var open = new Button
            {
                Content = label, MinWidth = 105, Height = 35,
                Style = (Style)FindResource("TaskTabButtonStyle"),
                Background = Color(selected ? darkTaskTabs ? "#0A84FF" : "#E5F1FF" :
                    darkTaskTabs ? "#2C2C2E" : "#FFFFFF"),
                Foreground = Color(selected ? darkTaskTabs ? "#FFFFFF" : "#0068D9" :
                    darkTaskTabs ? "#F5F5F7" : "#1D1D1F"),
                BorderBrush = Color(darkTaskTabs ? "#48484A" : "#DEDEE3")
            };
            open.Click += (_, _) => ActivateTaskWindow(window);
            var close = new Button
            {
                Content = "×", Width = 27, Height = 35, Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(0), Style = (Style)FindResource("TaskTabButtonStyle"),
                ToolTip = "关闭标签并结束此任务",
                Background = Color(darkTaskTabs ? "#2C2C2E" : "#FFFFFF"),
                Foreground = Color(darkTaskTabs ? "#F5F5F7" : "#515158"),
                BorderBrush = Color(darkTaskTabs ? "#48484A" : "#DEDEE3")
            };
            close.Click += (_, _) => CloseTaskWindow(window);
            tab.Children.Add(open);
            tab.Children.Add(close);
            TaskTabsPanel.Children.Add(tab);
        }
        NewTaskTabButton.Background = Color(darkTaskTabs ? "#2C2C2E" : "#FFFFFF");
        NewTaskTabButton.Foreground = Color(darkTaskTabs ? "#F5F5F7" : "#515158");
        NewTaskTabButton.BorderBrush = Color(darkTaskTabs ? "#48484A" : "#DEDEE3");
    }

    private MainWindow CreateTaskWindow(string? module = null)
    {
        var window = new MainWindow(tabSmokeMode) { initialModule = module };
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.WindowState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        window.Show();
        return window;
    }

    private Task RestoreAssignedModuleUiAsync() =>
        ShowTaskUiAsync(assignedModule == "course" ? "openCourse" : "openDemo");

    private async Task ShowTaskUiAsync(string action)
    {
        if (ShellView.CoreWebView2 is null) return;
        try { await ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi?." + action + "()"); }
        catch (Exception error) { Trace("task tab navigation failed: " + error.Message); }
    }

    private void ActivateTaskWindow(MainWindow target)
    {
        if (ReferenceEquals(target, this) || !TaskWindows.Contains(target)) return;
        Hide();
        target.Show();
        target.Activate();
        target.UpdateTaskTabs();
    }

    private void NewTaskTab_Click(object sender, RoutedEventArgs e)
    {
        if (smokeMode && !tabSmokeMode) return;
        var window = CreateTaskWindow();
        Hide();
        window.Activate();
    }

    private void OpenModuleInNewTab(string module)
    {
        if (smokeMode && !tabSmokeMode) return;
        var window = CreateTaskWindow(module);
        Hide();
        window.Activate();
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
            if (target.courseSessionId is not null && target.courseState != "STOPPED")
                await target.CourseCommandAsync("stop");
            if (target.sessionId is not null) await target.CancelSessionAsync();
        }
        catch (Exception error) { target.Trace("task close cleanup: " + error.Message); }
        MainWindow? next = null;
        if (ReferenceEquals(target, this))
            next = TaskWindows.Count > 1 ? TaskWindows[index > 0 ? index - 1 : 1] : CreateTaskWindow();
        target.closingAsTab = true;
        target.Close();
        if (next is not null)
        {
            next.Show();
            next.Activate();
            next.UpdateTaskTabs();
        }
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
