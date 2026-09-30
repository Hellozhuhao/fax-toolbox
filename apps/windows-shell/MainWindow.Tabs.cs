using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private bool courseTaskTabOpen;
    private bool demoTaskTabOpen;
    private bool darkTaskTabs;
    private string? selectedTaskTab;

    private void UpdateTaskTabs()
    {
        static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
        CourseTaskTab.Visibility = courseTaskTabOpen ? Visibility.Visible : Visibility.Collapsed;
        DemoTaskTab.Visibility = demoTaskTabOpen ? Visibility.Visible : Visibility.Collapsed;
        CourseTaskTabButton.Content = (courseState == "RUNNING" ? "● " : "") + "学习通";
        DemoTaskTabButton.Content = (state == "OBSERVING" ? "● " : "") + "页面演示";
        foreach (var (button, selected) in new[]
        {
            (CourseTaskTabButton, selectedTaskTab == "course"),
            (DemoTaskTabButton, selectedTaskTab == "demo")
        })
        {
            button.Background = Color(selected ? darkTaskTabs ? "#0A84FF" : "#E5F1FF" :
                darkTaskTabs ? "#2C2C2E" : "#FFFFFF");
            button.Foreground = Color(selected ? darkTaskTabs ? "#FFFFFF" : "#0068D9" :
                darkTaskTabs ? "#F5F5F7" : "#1D1D1F");
            button.BorderBrush = Color(darkTaskTabs ? "#48484A" : "#DEDEE3");
        }
        foreach (var button in new[] { CloseCourseTaskTabButton, CloseDemoTaskTabButton, NewTaskTabButton })
        {
            button.Background = Color(darkTaskTabs ? "#2C2C2E" : "#FFFFFF");
            button.Foreground = Color(darkTaskTabs ? "#F5F5F7" : "#515158");
            button.BorderBrush = Color(darkTaskTabs ? "#48484A" : "#DEDEE3");
        }
    }

    private async Task ShowTaskUiAsync(string action)
    {
        if (ShellView.CoreWebView2 is null) return;
        try { await ShellView.CoreWebView2.ExecuteScriptAsync("window.FaxToolboxUi?." + action + "()"); }
        catch (Exception error) { Trace("task tab navigation failed: " + error.Message); }
    }

    private async void CourseTaskTab_Click(object sender, RoutedEventArgs e) => await ShowTaskUiAsync("openCourse");
    private async void DemoTaskTab_Click(object sender, RoutedEventArgs e) => await ShowTaskUiAsync("openDemo");
    private async void NewCourseTaskTab_Click(object sender, RoutedEventArgs e) => await ShowTaskUiAsync("openCourse");
    private async void NewDemoTaskTab_Click(object sender, RoutedEventArgs e) => await ShowTaskUiAsync("openDemo");

    private void NewTaskTab_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewTaskTabButton.ContextMenu;
        if (menu is null) return;
        menu.PlacementTarget = NewTaskTabButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void CloseCourseTaskTab_Click(object sender, RoutedEventArgs e)
    {
        courseTaskTabOpen = false;
        var wasSelected = selectedTaskTab == "course";
        if (wasSelected) selectedTaskTab = null;
        UpdateTaskTabs();
        if (wasSelected) await ShowTaskUiAsync(demoTaskTabOpen ? "openDemo" : "showHome");
    }

    private async void CloseDemoTaskTab_Click(object sender, RoutedEventArgs e)
    {
        demoTaskTabOpen = false;
        var wasSelected = selectedTaskTab == "demo";
        if (wasSelected) selectedTaskTab = null;
        UpdateTaskTabs();
        if (wasSelected) await ShowTaskUiAsync(courseTaskTabOpen ? "openCourse" : "showHome");
    }
}
