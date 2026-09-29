using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private sealed record ChapterEntry([property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);
    private sealed record CatalogSnapshot(string CurrentId, string CurrentName, List<ChapterEntry> Entries);
    private readonly DispatcherTimer catalogTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool catalogBusy;
    private string? catalogReaderScript;
    private string? catalogKey;
    private string catalogSignature = "";
    private string catalogCurrentId = "";
    private string catalogCurrentName = "";
    private List<ChapterEntry> catalogEntries = [];
    private CoreWebView2Frame? catalogFrame;
    private bool catalogLive;

    private static string CourseKeyFromUrl(string? value)
    {
        if (!IsCoursePage(value)) return "";
        var uri = new Uri(value!);
        var parts = new List<string> { uri.Host.ToLowerInvariant() };
        var foundCourse = false;
        foreach (var key in new[] { "courseid", "clazzid", "classid", "cpi" })
        {
            var item = QueryValue(uri, key);
            if (!string.IsNullOrEmpty(item))
            {
                parts.Add(key + "=" + item);
                if (key == "courseid") foundCourse = true;
            }
        }
        if (!foundCourse) parts.Add(uri.AbsolutePath.ToLowerInvariant());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts))));
    }

    private static string QueryValue(Uri uri, string key)
    {
        foreach (var segment in uri.Query.TrimStart('?').Split('&'))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[1]);
        }
        return "";
    }

    private void InitializeCatalog()
    {
        catalogTimer.Tick += async (_, _) => await RefreshCatalogAsync();
    }

    private void PublishCatalog(string? note = null)
    {
        if (ShellView.CoreWebView2 is null) return;
        ShellView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            v = 1, kind = "course.catalog", entries = catalogEntries,
            currentId = catalogCurrentId, currentName = catalogCurrentName,
            live = catalogLive, note = note ?? ""
        }));
    }

    private async Task<CatalogSnapshot?> ReadCatalogAsync(CoreWebView2Frame? frame)
    {
        try
        {
            if (frame is not null && frame.IsDestroyed() != 0) return null;
            var response = frame is null ?
                await CourseView.CoreWebView2.ExecuteScriptAsync(catalogReaderScript!) :
                await frame.ExecuteScriptAsync(catalogReaderScript!);
            var payload = JsonSerializer.Deserialize<string>(response);
            if (string.IsNullOrEmpty(payload)) return null;
            using var document = JsonDocument.Parse(payload);
            var data = document.RootElement;
            var entries = new List<ChapterEntry>();
            foreach (var item in data.GetProperty("entries").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString() ?? "";
                var name = item.GetProperty("name").GetString() ?? "";
                if (id.Length is < 1 or > 128 || name.Length is < 1 or > 80) continue;
                entries.Add(new ChapterEntry(id, name));
            }
            return new CatalogSnapshot(data.GetProperty("currentId").GetString() ?? "",
                data.GetProperty("currentName").GetString() ?? "", entries);
        }
        catch (Exception) { return null; }
    }

    private async Task RefreshCatalogAsync()
    {
        if (catalogBusy || activeModule != "chaoxing" || CourseView.CoreWebView2 is null ||
            catalogReaderScript is null) return;
        catalogBusy = true;
        try
        {
            var source = CourseView.CoreWebView2.Source;
            var key = CourseKeyFromUrl(source);
            if (key != catalogKey)
            {
                catalogKey = key;
                catalogSignature = "";
                catalogEntries = [];
                catalogCurrentId = "";
                catalogCurrentName = "";
                catalogFrame = null;
                catalogLive = false;
                PublishCatalog();
                if (key.Length > 0 && backend is not null)
                {
                    var saved = await backend.RequestAsync("catalog.get", new { courseKey = key });
                    catalogEntries = saved.GetProperty("entries").EnumerateArray()
                        .Select(item => new ChapterEntry(item.GetProperty("id").GetString() ?? "",
                            item.GetProperty("name").GetString() ?? "")).ToList();
                    PublishCatalog(catalogEntries.Count > 0 ? "已读取本机保存的章节目录；等待网页目录刷新。" : "");
                }
            }
            if (key.Length == 0 || source != CourseView.CoreWebView2.Source) return;
            var best = await ReadCatalogAsync(null);
            CoreWebView2Frame? bestFrame = null;
            var frameCurrent = "";
            foreach (var frame in courseFrames.ToArray())
            {
                var snapshot = await ReadCatalogAsync(frame);
                if (snapshot is null) continue;
                if (snapshot.CurrentId.Length > 0) frameCurrent = snapshot.CurrentId;
                if (best is null || snapshot.Entries.Count > best.Entries.Count)
                {
                    best = snapshot;
                    bestFrame = frame;
                }
            }
            if (source != CourseView.CoreWebView2.Source || best is null) return;
            var currentId = frameCurrent.Length > 0 ? frameCurrent :
                best.CurrentId.Length > 0 ? best.CurrentId : ChapterFromUrl(source);
            var currentName = best.Entries.FirstOrDefault(item => item.Id == currentId)?.Name ?? best.CurrentName;
            if (currentName.Length > 80) currentName = "";
            var live = best.Entries.Count > 0;
            var signature = JsonSerializer.Serialize(best.Entries);
            if (live)
            {
                catalogEntries = best.Entries;
                catalogFrame = bestFrame;
                if (signature != catalogSignature && backend is not null && key.Length > 0)
                {
                    // Keep each request and future get response under the 32 KiB bridge limit.
                    var persisted = best.Entries.Take(120).ToList();
                    while (persisted.Count > 0 && Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(persisted)) > 24_000)
                        persisted.RemoveAt(persisted.Count - 1);
                    if (persisted.Count > 0)
                        await backend.RequestAsync("catalog.put", new { courseKey = key, entries = persisted });
                    catalogSignature = signature;
                }
            }
            if (currentId != catalogCurrentId || currentName != catalogCurrentName || live != catalogLive || live)
            {
                catalogCurrentId = currentId;
                catalogCurrentName = currentName;
                catalogLive = live;
                PublishCatalog();
            }
        }
        catch (Exception error)
        {
            PublishCatalog("读取章节目录失败：" + error.Message);
        }
        finally { catalogBusy = false; }
    }

    private async Task JumpToChapterAsync(string id)
    {
        if (!catalogLive || !catalogEntries.Any(item => item.Id == id) ||
            activeModule != "chaoxing") return;
        aiAnswerCancellation?.Cancel();
        if (courseState == "RUNNING") await CourseCommandAsync("pause", "正在切换章节，任务已暂停。");
        var script = "window.FaxCatalogReader?.jump(" + JsonSerializer.Serialize(id) + ") === true";
        var result = catalogFrame is { } frame && frame.IsDestroyed() == 0 ?
            await frame.ExecuteScriptAsync(script) : await CourseView.CoreWebView2.ExecuteScriptAsync(script);
        if (result != "true") PublishCatalog("该章节暂时无法跳转，请在右侧目录中选择。 ");
        else await RefreshCatalogAsync();
    }
}
