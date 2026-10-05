using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private void ConfigureEmbeddedContextMenu(WebView2 view)
    {
        view.CoreWebView2.ContextMenuRequested += (_, args) => ShowEmbeddedContextMenu(view, args);
    }

    private void ShowEmbeddedContextMenu(WebView2 view, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        args.Handled = true;
        var deferral = args.GetDeferral();
        try
        {
            var dark = darkTaskTabs;
            static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
            var menu = new ContextMenu
            {
                PlacementTarget = view,
                Placement = PlacementMode.RelativePoint,
                HorizontalOffset = args.Location.X / VisualTreeHelper.GetDpi(view).DpiScaleX,
                VerticalOffset = args.Location.Y / VisualTreeHelper.GetDpi(view).DpiScaleY,
                Background = Color(dark ? "#24252C" : "#FFFFFF"),
                Foreground = Color(dark ? "#F5F5F8" : "#171716"),
                BorderBrush = Color(dark ? "#454650" : "#E7EBF4"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                MinWidth = 180,
                StaysOpen = false
            };
            var itemStyle = (Style)FindResource("EmbeddedContextItemStyle");
            void Add(string label, Action action, bool enabled = true)
            {
                var item = new MenuItem
                {
                    Header = label, Style = itemStyle, IsEnabled = enabled
                };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            var target = args.ContextMenuTarget;
            void BuiltIn(string name, string label)
            {
                var item = args.MenuItems.FirstOrDefault(value =>
                    string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase));
                if (item is not null) Add(label, () => args.SelectedCommandId = item.CommandId, item.IsEnabled);
            }
            if (target.IsEditable)
            {
                BuiltIn("cut", "剪切"); BuiltIn("copy", "复制");
                BuiltIn("paste", "粘贴"); BuiltIn("selectAll", "全选");
            }
            else if (target.HasSelection)
                Add("复制所选内容", () => Clipboard.SetText(target.SelectionText ?? ""));
            if (target.HasLinkUri)
                Add("复制链接地址", () => Clipboard.SetText(target.LinkUri));
            if (target.HasSourceUri && target.Kind is CoreWebView2ContextMenuTargetKind.Image or CoreWebView2ContextMenuTargetKind.Video)
                Add(target.Kind == CoreWebView2ContextMenuTargetKind.Image ? "复制图片地址" : "复制视频地址",
                    () => Clipboard.SetText(target.SourceUri));
            if (ReferenceEquals(view, CourseView))
            {
                if (activeModule == "homework")
                {
                    Add("返回作业校对", () => _ = ShowTaskUiAsync("openHomework"));
                    Add("查看校对结果", () => ShowHomeworkResults());
                    Add("开始校对", () => _ = StartHomeworkReviewAsync(), !homeworkBusy);
                }
                else
                {
                    Add("返回学习通控制台", () => _ = ShowTaskUiAsync("openCourse"));
                    Add("保存当前登录状态", () => _ = SaveCourseSessionAsync());
                    if (developerMode) Add("分析当前页面", () => _ = AnalyzeCoursePageAsync());
                }
            }
            else
            {
                Add("返回工具箱", () => _ = ShowTaskUiAsync("openDemo"));
                Add("开始页面观察", () => _ = StartSessionAsync());
            }
            if (!string.IsNullOrWhiteSpace(target.PageUri))
                Add("复制页面地址", () => Clipboard.SetText(target.PageUri));
            var completed = false;
            void Complete()
            {
                if (completed) return;
                completed = true;
                deferral.Complete();
            }
            menu.Closed += (_, _) => Complete();
            menu.IsOpen = true;
            if (!menu.IsOpen) Complete();
        }
        catch (Exception error)
        {
            Trace("context menu: " + error.Message);
            deferral.Complete();
        }
    }
}
