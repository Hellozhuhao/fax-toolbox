using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private sealed record HomeworkPageImageRef(int QuestionNumber, string Marker, int ImageIndex);
    private sealed record HomeworkImageRef(int QuestionNumber, string Marker, int ImageIndex, CoreWebView2Frame? Frame);
    private sealed record HomeworkSnapshotRead(AiQuizSnapshot Snapshot, HomeworkImageRef[] Images);
    private sealed record HomeworkPageRead(AiQuizQuestion[]? Questions, HomeworkPageImageRef[]? ImageRefs, int CandidateCount,
        int SkippedMarkerCount, int UnreadableQuestionCount, int UnreadableImageCount);
    private bool homeworkHasOpened;
    private bool homeworkBusy;
    private bool homeworkLoading;
    private bool homeworkClosed;
    private bool homeworkBrowserVisible;
    private long homeworkGeneration;
    private string homeworkDetail = "请在右侧打开作业题目。";
    private CancellationTokenSource? homeworkCancellation;

    private void PublishHomeworkStatus(string detail)
    {
        homeworkDetail = detail;
        RefreshTaskTabs();
        if (homeworkClosed || ShellView.CoreWebView2 is null) return;
        var url = CourseView.CoreWebView2?.Source;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "homework.status", detail, busy = homeworkBusy,
            ready = !homeworkBusy && !homeworkLoading && activeModule == "homework" &&
                IsChaoxingUrl(url) && !IsLoginPage(url),
            url = DisplayUrl(url)
        }));
    }

    private void CancelHomeworkReview()
    {
        homeworkGeneration++;
        homeworkCancellation?.Cancel();
    }

    private void ClearHomeworkResults()
    {
        ShellView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "homework.result", items = Array.Empty<object>()
        }));
    }

    private void PublishHomeworkProgress(int? percent, string detail)
    {
        if (homeworkClosed || ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "homework.progress", percent, detail
        }));
    }

    private Task ActivateHomeworkAsync()
    {
        if (!homeworkHasOpened)
        {
            homeworkHasOpened = true;
            activeModule = "homework";
            catalogTimer.Stop();
            SetWorkspaceLayout(homeworkBrowserVisible);
            ModuleView.Visibility = Visibility.Collapsed;
            CourseView.Visibility = Visibility.Visible;
            CourseOverlay.Visibility = Visibility.Collapsed;
            CourseView.CoreWebView2!.Navigate(homeworkSmokeMode ?
                "https://mooc1.chaoxing.com/mycourse/homework.html" : CourseHome);
            PublishHomeworkStatus("正在打开学习通，请登录并进入作业页面。");
            return Task.CompletedTask;
        }
        activeModule = "homework";
        catalogTimer.Stop();
        SetWorkspaceLayout(homeworkBrowserVisible);
        ModuleView.Visibility = Visibility.Collapsed;
        CourseView.Visibility = CourseOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        PublishHomeworkStatus(homeworkDetail);
        return Task.CompletedTask;
    }

    private async Task OpenHomeworkAsync()
    {
        homeworkBrowserVisible = true;
        var opened = homeworkHasOpened;
        await ActivateHomeworkAsync();
        if (opened && !IsChaoxingUrl(CourseView.CoreWebView2?.Source))
            CourseView.CoreWebView2!.Navigate(CourseHome);
    }

    private void ShowHomeworkResults()
    {
        homeworkBrowserVisible = false;
        if (activeModule == "homework") SetWorkspaceLayout(false);
    }

    private async Task HomeworkNavigationCompletedAsync(CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.NavigationId != courseLatestNavigationId) return;
        homeworkLoading = false;
        if (!args.IsSuccess)
        {
            var reason = $"学习通页面加载失败（{args.WebErrorStatus}）。请检查网络后重试。";
            ShowCourseError(reason);
            PublishHomeworkStatus(reason);
            return;
        }
        CourseView.Visibility = Visibility.Visible;
        CourseOverlay.Visibility = Visibility.Collapsed;
        PublishHomeworkStatus(IsLoginPage(CourseView.CoreWebView2.Source) ?
            "请在右侧完成学习通登录。" : "打开作业题目后，点击“开始作业校对”。");
        if (homeworkSmokeMode)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var ready = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "!document.getElementById('homework-start').disabled");
                if (ready == "true") break;
                await Task.Delay(100);
            }
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('homework-open').click()");
            for (var attempt = 0; attempt < 20 && !homeworkBrowserVisible; attempt++)
                await Task.Delay(100);
            if (!homeworkBrowserVisible || WorkspaceColumn.Width.GridUnitType != GridUnitType.Star)
            { FailSmoke("homework browser view did not open"); return; }
            await ShellView.CoreWebView2.ExecuteScriptAsync("document.getElementById('homework-start').click()");
        }
    }

    private async Task<HomeworkSnapshotRead> ReadHomeworkSnapshotAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(root, "modules", "chaoxing", "homework-reader.js"));
        var all = new List<AiQuizQuestion>();
        var images = new List<HomeworkImageRef>();
        var candidateCount = 0;
        var unreadableQuestionCount = 0;
        void AddQuestions(string raw, CoreWebView2Frame? frame)
        {
            if (raw is "null" or "undefined" or "") return;
            var scan = JsonSerializer.Deserialize<HomeworkPageRead>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (scan?.Questions is null) return;
            candidateCount += scan.CandidateCount;
            unreadableQuestionCount += scan.UnreadableQuestionCount;
            var firstNumber = all.Count;
            var localImages = scan.ImageRefs ?? [];
            var markerMap = localImages.Select((image, index) => (image.Marker,
                Global: $"[公式图片{images.Count + index + 1}]")).ToDictionary(
                    pair => pair.Marker, pair => pair.Global, StringComparer.Ordinal);
            string ReplaceMarkers(string text)
            {
                foreach (var (local, global) in markerMap)
                    text = text.Replace(local, global, StringComparison.Ordinal);
                return text;
            }
            foreach (var question in scan.Questions)
                all.Add(question with { Number = all.Count + 1, Text = ReplaceMarkers(question.Text),
                    Options = question.Options.Select(ReplaceMarkers).ToArray() });
            foreach (var image in localImages)
            {
                if (image.QuestionNumber < 1 || image.QuestionNumber > scan.Questions.Length || image.ImageIndex < 0)
                    throw new InvalidDataException("作业图片定位信息无效，请重新打开页面后重试。");
                images.Add(new HomeworkImageRef(firstNumber + image.QuestionNumber,
                    markerMap[image.Marker], image.ImageIndex, frame));
            }
        }
        AddQuestions(await CourseView.CoreWebView2.ExecuteScriptAsync(script), null);
        foreach (var frame in courseFrames.ToArray())
        {
            try
            {
                if (frame.IsDestroyed() == 0)
                    AddQuestions(await frame.ExecuteScriptAsync(script), frame);
            }
            catch (Exception error) when (error is InvalidOperationException or COMException)
            { /* The frame may disappear during extraction. */ }
        }
        if (unreadableQuestionCount > 0)
            throw new InvalidDataException($"已找到 {candidateCount} 道题，但有 {unreadableQuestionCount} 道题题干不完整；请等待页面加载完成后重试。");
        var snapshot = HomeworkReviewService.ParseSnapshot(all.ToArray());
        return new HomeworkSnapshotRead(snapshot, images.ToArray());
    }

    private async Task StartHomeworkReviewAsync()
    {
        if (assignedModule != "homework" || activeModule != "homework" || homeworkBusy || homeworkLoading ||
            !IsChaoxingUrl(CourseView.CoreWebView2?.Source) || IsLoginPage(CourseView.CoreWebView2!.Source)) return;
        AiSettings? settings;
        try { settings = homeworkSmokeMode ? null : AiSettings.Load() ??
            throw new InvalidDataException("尚未配置 AI 服务。请先填写 API Key。"); }
        catch (Exception error) when (error is InvalidDataException or JsonException or IOException)
        { PublishHomeworkStatus(error.Message); PublishHomeworkProgress(0, error.Message); return; }
        homeworkBusy = true;
        CancelHomeworkReview();
        ClearHomeworkResults();
        var run = homeworkGeneration;
        var cancellation = new CancellationTokenSource();
        homeworkCancellation = cancellation;
        var source = CourseView.CoreWebView2.Source;
        PublishHomeworkStatus("正在读取当前作业题目…");
        PublishHomeworkProgress(8, "正在扫描本网页的全部题目…");
        try
        {
            var read = await ReadHomeworkSnapshotAsync();
            var snapshot = read.Snapshot;
            if (run != homeworkGeneration || source != CourseView.CoreWebView2.Source) return;
            if (homeworkSmokeMode && (snapshot.Questions.Length != 3 ||
                snapshot.Questions.Any(question => question.Text.Contains("此次作业", StringComparison.Ordinal) ||
                    question.Options.Length != 0) ||
                snapshot.Questions[0].Type != "single" || snapshot.Questions[0].BlankCount != 0 ||
                snapshot.Questions[1].Type != "blank" || snapshot.Questions[2].Type != "essay"))
                throw new InvalidDataException("作业读取器未按题干识别三种题型，或误读了选项。");
            if (!homeworkSmokeMode && read.Images.Length > 0 && !HomeworkReviewService.SupportsImages(settings!))
                throw new InvalidDataException("当前 AI 配置不支持公式图片。请在管理配置中选择 DeepSeek · deepseek-flash。");
            var images = await CaptureHomeworkImagesAsync(read.Images, cancellation.Token);
            if (run != homeworkGeneration || source != CourseView.CoreWebView2.Source) return;
            if (homeworkSmokeMode)
            {
                if (read.Images.Length != 2 || images.Length != 2 ||
                    images.Any(image => !image.DataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)))
                    throw new InvalidDataException($"公式图片未能从网页画面截取为 PNG（定位 {read.Images.Length}，已读取 {images.Length}；" +
                        string.Join(",", read.Images.Select(image => image.Marker + ":" + (image.Frame is null ? "主页面" : "框架"))) +
                        "；成功 " + string.Join(",", images.Select(image => image.Marker)) + "）。");
                var imageIndex = read.Images[0].ImageIndex;
                var originalSource = await CourseView.CoreWebView2.ExecuteScriptAsync(
                    $"document.images[{imageIndex}].src");
                var pngBase64 = JsonSerializer.Serialize(images[0].DataUrl.Split(',', 2)[1]);
                try
                {
                    await CourseView.CoreWebView2.ExecuteScriptAsync(
                        $"(()=>{{const raw=atob({pngBase64});const bytes=Uint8Array.from(raw,c=>c.charCodeAt(0));" +
                        $"const image=document.images[{imageIndex}];image.src=URL.createObjectURL(new Blob([bytes],{{type:'image/png'}}));return true}})()");
                    var fetched = await ReadHomeworkImageDataAsync(read.Images[0]);
                    if (fetched is null || !fetched.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
                        throw new InvalidDataException("异步读取网页原始图片失败。");
                }
                finally
                {
                    await CourseView.CoreWebView2.ExecuteScriptAsync(
                        $"(()=>{{const image=document.images[{imageIndex}];URL.revokeObjectURL(image.src);" +
                        $"image.src={originalSource}}})()");
                }
                using var request = JsonDocument.Parse(AiAnswerService.BuildCompletionPayload(
                    new AiSettings("https://api.deepseek.com/chat/completions", "deepseek-flash", ""),
                    "系统提示", "作业题目", images));
                var content = request.RootElement.GetProperty("messages")[1].GetProperty("content");
                if (content.GetArrayLength() != 5 ||
                    content[2].GetProperty("image_url").GetProperty("url").GetString() != images[0].DataUrl ||
                    content[4].GetProperty("image_url").GetProperty("url").GetString() != images[1].DataUrl)
                    throw new InvalidDataException("公式图片没有加入 DeepSeek 图文请求。");
            }
            PublishHomeworkStatus($"已读取 {snapshot.Questions.Length} 道题，正在请求 {settings?.Model ?? "本地示例模型"}…");
            PublishHomeworkProgress(42, $"已读取 {snapshot.Questions.Length} 道题和 {images.Length} 张公式图，正在发送给模型…");
            PublishHomeworkProgress(null, $"已发送 {snapshot.Questions.Length} 道题、{images.Length} 张公式图，模型正在分析…");
            var smokeReply = JsonSerializer.Serialize(new
            {
                images = images.Select(image => new { marker = image.Marker,
                    latex = image.QuestionNumber == 1 ? @"\begin{pmatrix}1&2\\3&4\end{pmatrix}" :
                        @"\begin{vmatrix}1&2\\3&4\end{vmatrix}" }).ToArray(),
                answers = new[]
                {
                    new { number = 1, answer = "2", explanation = "该矩阵行列式为 -2，不为零，所以秩为 2。" },
                    new { number = 2, answer = @"\(\begin{pmatrix}1&2\\3&4\end{pmatrix}\)",
                        explanation = @"行列式 \(\begin{vmatrix}1&2\\3&4\end{vmatrix}=1\cdot4-2\cdot3=-2\)。" },
                    new { number = 3, answer = "x=2", explanation = "两边先减 1，再除以 2。" }
                }
            });
            var results = homeworkSmokeMode ? HomeworkReviewService.ParseResults(smokeReply, snapshot, images) :
                await HomeworkReviewService.ReviewAsync(settings!, snapshot, images, cancellation.Token);
            if (homeworkSmokeMode)
            {
                var twoBlanks = new AiQuizSnapshot([
                    new AiQuizQuestion(1, "blank", "分别求 x=__，y=__", [], 2)
                ]);
                var partial = HomeworkReviewService.ParseResults(
                    "{\"answers\":[{\"number\":1,\"answer\":\"x=2\",\"explanation\":\"只返回一个空。\"}]}", twoBlanks);
                if (partial[0].Answer.Length != 0 ||
                    !partial[0].Explanation.Contains("人工核对", StringComparison.Ordinal))
                    throw new InvalidDataException("多空题缺少答案时应逐题提示人工核对。");
                var withoutImageTranscription = HomeworkReviewService.ParseResults(
                    JsonSerializer.Serialize(new
                    {
                        answers = new[]
                        {
                            new { number = 1, answer = "2", explanation = "秩为 2。" },
                            new { number = 2, answer = "-2", explanation = "行列式为 -2。" },
                            new { number = 3, answer = "x=2", explanation = "移项后求解。" }
                        }
                    }), snapshot, images);
                if (withoutImageTranscription[0].Answer.Length != 0 ||
                    withoutImageTranscription[1].Answer.Length != 0 ||
                    withoutImageTranscription[2].Answer != "x=2")
                    throw new InvalidDataException("未转写的题干图片应只影响对应题目。");
            }
            if (run != homeworkGeneration || source != CourseView.CoreWebView2.Source || cancellation.IsCancellationRequested) return;
            PublishHomeworkProgress(82, "模型已返回答案，正在核对题号和解析…");
            var current = await ReadHomeworkSnapshotAsync();
            if (JsonSerializer.Serialize(current.Snapshot) != JsonSerializer.Serialize(snapshot))
                throw new InvalidDataException("校对期间页面题目发生变化，请重新开始。");
            ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                v = 1, kind = "homework.result", items = results.Select(item => new
                {
                    number = item.Number, type = item.Type, question = item.Question,
                    answer = item.Answer, explanation = item.Explanation
                }).ToArray()
            }));
            var missedImages = read.Images.Length - images.Length;
            var imageNote = missedImages > 0 ? $"有 {missedImages} 张公式图未能读取，对应题目需人工核对。" : "解析可逐题展开。";
            PublishHomeworkStatus($"已完成 {results.Length} 道题的校对。{imageNote}");
            PublishHomeworkProgress(100, $"完成：{results.Length} 道题。{imageNote}");
            ShowHomeworkResults();
            if (homeworkSmokeMode)
            {
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    var rendered = await ShellView.CoreWebView2.ExecuteScriptAsync(
                        "document.querySelectorAll('.homework-item').length === 3 && " +
                        "!document.querySelector('.homework-explanation').open && " +
                        "document.querySelector('.homework-answer').textContent.includes('2') && " +
                        "document.querySelectorAll('.homework-item .katex').length >= 5 && " +
                        "!document.querySelectorAll('.homework-question')[1].textContent.includes('[公式图片')");
                    if (rendered == "true") break;
                    await Task.Delay(100);
                }
                var ui = await ShellView.CoreWebView2.ExecuteScriptAsync(
                    "document.querySelectorAll('.homework-item').length === 3 && " +
                    "!document.querySelector('.homework-explanation').open && " +
                    "document.querySelectorAll('.homework-item .katex').length >= 5 && " +
                    "document.getElementById('homework-progress').value === 100 && " +
                    "document.querySelectorAll('#view-homework details.collapsible').length === 4");
                var untouched = await CourseView.CoreWebView2.ExecuteScriptAsync(
                    "document.getElementById('submit').textContent === '提交作业' && " +
                    "document.querySelector('textarea').value === '' && !document.querySelector('input:checked')");
                var frameUntouched = courseFrames.Count == 1 &&
                    await courseFrames[0].ExecuteScriptAsync("[...document.querySelectorAll('textarea')].every(node => node.value === '')") == "true";
                if (ui == "true" && untouched == "true" && frameUntouched &&
                    !homeworkBrowserVisible && WorkspaceColumn.Width.Value == 0)
                {
                    if (Environment.GetEnvironmentVariable("FAX_UI_PREVIEW") == "1")
                    {
                        await ShellView.CoreWebView2.ExecuteScriptAsync(
                            "document.getElementById('homework-results').scrollIntoView({block:'start'})");
                        await Task.Delay(180);
                        var preview = Path.Combine(root, ".cache", "ui-homework-preview.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
                        await using var output = File.Create(preview);
                        await ShellView.CoreWebView2.CapturePreviewAsync(
                            CoreWebView2CapturePreviewImageFormat.Png, output);
                    }
                    PassSmoke();
                }
                else FailSmoke("homework result UI or read-only page behavior failed");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is InvalidDataException or JsonException or HttpRequestException or
            TimeoutException or OperationCanceledException or InvalidOperationException or IOException or
            System.Security.Cryptography.CryptographicException)
        {
            if (run == homeworkGeneration)
            {
                PublishHomeworkStatus("作业校对未完成：" + error.Message);
                PublishHomeworkProgress(0, "未完成：" + error.Message);
                if (homeworkSmokeMode) FailSmoke(error.ToString());
            }
        }
        finally
        {
            if (ReferenceEquals(homeworkCancellation, cancellation)) homeworkCancellation = null;
            cancellation.Dispose();
            homeworkBusy = false;
            if (activeModule == "homework" && !homeworkClosed) PublishHomeworkStatus(homeworkDetail);
        }
    }
}
