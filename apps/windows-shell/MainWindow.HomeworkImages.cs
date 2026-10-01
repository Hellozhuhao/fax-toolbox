using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace FaxToolbox.Windows;

public partial class MainWindow
{
    private sealed record HomeworkImageBounds(double X, double Y, double Width, double Height,
        double ViewportWidth, double ViewportHeight);
    private sealed record HomeworkImageFetchState(bool Done, string? Value);

    private async Task<AiImageInput[]> CaptureHomeworkImagesAsync(HomeworkImageRef[] references,
        CancellationToken cancellationToken)
    {
        if (references.Length > 100)
            throw new InvalidDataException($"当前页面有 {references.Length} 张待识别公式图，超过单次图片上限 100 张。");
        var images = new List<AiImageInput>();
        for (var index = 0; index < references.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = references[index];
            PublishHomeworkProgress(12 + 26 * index / Math.Max(1, references.Length),
                $"正在读取公式图片 {index + 1}/{references.Length}…");
            string? dataUrl = null;
            if (!homeworkSmokeMode)
            {
                try
                {
                    dataUrl = await ReadHomeworkImageDataAsync(reference);
                }
                catch (Exception error) when (error is COMException or InvalidOperationException or IOException or
                    JsonException or ArgumentException or FormatException or NotSupportedException)
                {
                    Trace($"homework image file read failed for {reference.Marker}: {error.GetType().Name}");
                }
            }
            if (dataUrl is null)
            {
                try
                {
                    dataUrl = await CaptureHomeworkImagePreviewAsync(reference, cancellationToken);
                }
                catch (Exception error) when (error is COMException or InvalidOperationException or IOException or
                    JsonException or ArgumentException or FormatException or NotSupportedException)
                {
                    Trace($"homework image capture failed for {reference.Marker}: {error.GetType().Name}");
                }
            }
            if (dataUrl is not null)
                images.Add(new AiImageInput(reference.QuestionNumber, reference.Marker, dataUrl));
        }
        if (references.Length > 0)
            PublishHomeworkProgress(38, images.Count == references.Length ?
                $"已读取全部 {images.Count} 张公式图片。" :
                $"已读取 {images.Count}/{references.Length} 张公式图片；其余图片对应题目会标记待核对。");
        if (images.Sum(image => image.DataUrl.Length) > 40_000_000)
            throw new InvalidDataException("当前页面公式图片总量超过单次图文请求上限，请缩小当前页面题量。");
        return images.ToArray();
    }

    private async Task<string?> ReadHomeworkImageDataAsync(HomeworkImageRef reference)
    {
        var start = "(()=>{const image=document.images[" + reference.ImageIndex + "];" +
            "if(!image)return null;const url=image.currentSrc||image.src;if(!url)return null;" +
            "if(/^data:image\\/(?:png|jpeg|webp|gif);base64,/i.test(url))return url;" +
            "if(url.startsWith('data:'))return null;" +
            "const state={done:false,value:null};window.__faxHomeworkImageFetch=state;" +
            "const controller=new AbortController();const timer=setTimeout(()=>controller.abort(),2500);" +
            "fetch(url,{credentials:'include',cache:'force-cache',signal:controller.signal})" +
            ".then(response=>response.ok?response.blob():null)" +
            ".then(blob=>!blob||!/^image\\/(?:png|jpeg|webp|gif)$/i.test(blob.type)||blob.size>3000000?" +
            "null:new Promise((resolve,reject)=>{const reader=new FileReader();" +
            "reader.onload=()=>resolve(reader.result);reader.onerror=()=>reject(reader.error);" +
            "reader.readAsDataURL(blob)}))" +
            ".then(value=>{state.value=value}).catch(()=>{})" +
            ".finally(()=>{clearTimeout(timer);state.done=true});return 'pending'})()";
        async Task<string> Execute(string script) => reference.Frame is null ?
            await CourseView.CoreWebView2.ExecuteScriptAsync(script) :
            await reference.Frame.ExecuteScriptAsync(script);
        var raw = await Execute(start);
        if (raw is "null" or "undefined" or "") return null;
        var state = JsonSerializer.Deserialize<string>(raw);
        if (state != "pending") return NormalizeHomeworkImage(state);
        try
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var result = await Execute(
                    "(()=>{const state=window.__faxHomeworkImageFetch;" +
                    "return state?{done:state.done,value:state.done?state.value:null}:null})()");
                if (result is "null" or "undefined" or "") return null;
                var fetch = JsonSerializer.Deserialize<HomeworkImageFetchState>(result,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (fetch?.Done == true) return NormalizeHomeworkImage(fetch.Value);
                await Task.Delay(100);
            }
            return null;
        }
        finally
        {
            await Execute("delete window.__faxHomeworkImageFetch");
        }
    }

    private async Task<string?> CaptureHomeworkImagePreviewAsync(HomeworkImageRef reference,
        CancellationToken cancellationToken)
    {
        if (reference.Frame is not null)
            return await CaptureHomeworkFrameImagePreviewAsync(reference, cancellationToken);
        var prepare = "(()=>{const image=document.images[" + reference.ImageIndex + "];" +
            "if(!image)return null;const parents=[];" +
            "for(let node=image.parentElement;node;node=node.parentElement)" +
            "if(node.scrollHeight>node.clientHeight+1||node.scrollWidth>node.clientWidth+1)" +
            "parents.push({node,top:node.scrollTop,left:node.scrollLeft});" +
            "window.__faxHomeworkScroll={top:scrollY,left:scrollX,parents};" +
            "image.scrollIntoView({block:'center',inline:'center',behavior:'instant'});return true})()";
        var inspect = "(()=>{const image=document.images[" + reference.ImageIndex + "];" +
            "if(!image||!image.complete||!image.naturalWidth||!image.naturalHeight)return null;" +
            "const rect=image.getBoundingClientRect();return {x:rect.x,y:rect.y,width:rect.width," +
            "height:rect.height,viewportWidth:innerWidth,viewportHeight:innerHeight}})()";
        try
        {
            if (await CourseView.CoreWebView2.ExecuteScriptAsync(prepare) != "true") return null;
            var raw = await WaitForHomeworkImageBoundsAsync(null, inspect, cancellationToken);
            if (raw is null)
            {
                Trace("root image capture: image not ready");
                return null;
            }
            var bounds = JsonSerializer.Deserialize<HomeworkImageBounds>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (bounds is null || bounds.Width < 8 || bounds.Height < 8 ||
                bounds.ViewportWidth < 1 || bounds.ViewportHeight < 1 ||
                bounds.X < 0 || bounds.Y < 0 ||
                bounds.X + bounds.Width > bounds.ViewportWidth ||
                bounds.Y + bounds.Height > bounds.ViewportHeight)
            {
                Trace($"root image capture: bounds unavailable {bounds}");
                return null;
            }
            var result = await CaptureHomeworkScreenshotAsync(bounds, cancellationToken);
            if (result is null) Trace($"root image capture: crop unavailable {bounds}");
            return result;
        }
        finally
        {
            try
            {
                await CourseView.CoreWebView2.ExecuteScriptAsync(
                    "(()=>{const state=window.__faxHomeworkScroll;if(!state)return;" +
                    "for(const item of state.parents){item.node.scrollTop=item.top;item.node.scrollLeft=item.left}" +
                    "scrollTo(state.left,state.top);delete window.__faxHomeworkScroll})()");
            }
            catch (Exception error) when (error is COMException or InvalidOperationException)
            { /* Navigation may have disposed the page while capture was ending. */ }
        }
    }

    private async Task<string?> CaptureHomeworkFrameImagePreviewAsync(HomeworkImageRef reference,
        CancellationToken cancellationToken)
    {
        var frame = reference.Frame!;
        var rawUrl = await frame.ExecuteScriptAsync("location.href");
        var frameUrl = JsonSerializer.Deserialize<string>(rawUrl) ?? "";
        var name = JsonSerializer.Serialize(frame.Name ?? "");
        var url = JsonSerializer.Serialize(frameUrl);
        var findFrame = "(()=>{const name=" + name + ",url=" + url + ";" +
            "return [...document.querySelectorAll('iframe,frame')].find(node=>" +
            "(name&&node.name===name)||(url&&node.src===url))||null})()";
        var select = "(()=>{const frame=" + findFrame + ";if(!frame)return false;" +
            "const parents=[];for(let node=frame.parentElement;node;node=node.parentElement)" +
            "if(node.scrollHeight>node.clientHeight+1||node.scrollWidth>node.clientWidth+1)" +
            "parents.push({node,top:node.scrollTop,left:node.scrollLeft});" +
            "window.__faxHomeworkFrameScroll={top:scrollY,left:scrollX,parents};" +
            "frame.scrollIntoView({block:'center',inline:'center',behavior:'instant'});return true})()";
        var prepareInner = "(()=>{const image=document.images[" + reference.ImageIndex + "];" +
            "if(!image)return null;" +
            "const parents=[];for(let node=image.parentElement;node;node=node.parentElement)" +
            "if(node.scrollHeight>node.clientHeight+1||node.scrollWidth>node.clientWidth+1)" +
            "parents.push({node,top:node.scrollTop,left:node.scrollLeft});" +
            "window.__faxHomeworkScroll={top:scrollY,left:scrollX,parents};" +
            "image.scrollIntoView({block:'center',inline:'center',behavior:'instant'});return true})()";
        var inspectInner = "(()=>{const image=document.images[" + reference.ImageIndex + "];" +
            "if(!image||!image.complete||!image.naturalWidth||!image.naturalHeight)return null;" +
            "const rect=image.getBoundingClientRect();return {x:rect.x,y:rect.y,width:rect.width," +
            "height:rect.height,viewportWidth:innerWidth,viewportHeight:innerHeight}})()";
        try
        {
            var selected = await CourseView.CoreWebView2.ExecuteScriptAsync(select);
            if (selected != "true") { Trace("frame image capture: iframe not found"); return null; }
            if (await frame.ExecuteScriptAsync(prepareInner) != "true") return null;
            var innerRaw = await WaitForHomeworkImageBoundsAsync(frame, inspectInner, cancellationToken);
            if (innerRaw is null) { Trace("frame image capture: image not ready"); return null; }
            var inner = JsonSerializer.Deserialize<HomeworkImageBounds>(innerRaw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var outerRaw = await CourseView.CoreWebView2.ExecuteScriptAsync(
                "(()=>{const frame=" + findFrame + ";if(!frame)return null;" +
                "const rect=frame.getBoundingClientRect();return {x:rect.x,y:rect.y,width:rect.width," +
                "height:rect.height,viewportWidth:innerWidth,viewportHeight:innerHeight}})()");
            if (outerRaw is "null" or "undefined" or "") { Trace("frame image capture: outer bounds unavailable"); return null; }
            var outer = JsonSerializer.Deserialize<HomeworkImageBounds>(outerRaw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (inner is null || outer is null || inner.ViewportWidth < 1 || inner.ViewportHeight < 1)
                return null;
            var bounds = new HomeworkImageBounds(
                outer.X + inner.X * outer.Width / inner.ViewportWidth,
                outer.Y + inner.Y * outer.Height / inner.ViewportHeight,
                inner.Width * outer.Width / inner.ViewportWidth,
                inner.Height * outer.Height / inner.ViewportHeight,
                outer.ViewportWidth, outer.ViewportHeight);
            var result = await CaptureHomeworkScreenshotAsync(bounds, cancellationToken);
            if (result is null)
                Trace($"frame image capture: crop unavailable inner={inner} outer={outer} combined={bounds}");
            return result;
        }
        finally
        {
            const string restore = "(()=>{const state=window.__faxHomeworkScroll;if(!state)return;" +
                "for(const item of state.parents){item.node.scrollTop=item.top;item.node.scrollLeft=item.left}" +
                "scrollTo(state.left,state.top);delete window.__faxHomeworkScroll})()";
            try { await frame.ExecuteScriptAsync(restore); }
            catch (Exception error) when (error is COMException or InvalidOperationException) { }
            try
            {
                await CourseView.CoreWebView2.ExecuteScriptAsync(
                    "(()=>{const state=window.__faxHomeworkFrameScroll;if(!state)return;" +
                    "for(const item of state.parents){item.node.scrollTop=item.top;item.node.scrollLeft=item.left}" +
                    "scrollTo(state.left,state.top);delete window.__faxHomeworkFrameScroll})()");
            }
            catch (Exception error) when (error is COMException or InvalidOperationException) { }
        }
    }

    private async Task<string?> WaitForHomeworkImageBoundsAsync(CoreWebView2Frame? frame,
        string inspectScript, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = frame is null ? await CourseView.CoreWebView2.ExecuteScriptAsync(inspectScript) :
                await frame.ExecuteScriptAsync(inspectScript);
            if (raw is not ("null" or "undefined" or "")) return raw;
            await Task.Delay(100, cancellationToken);
        }
        return null;
    }

    private async Task<string?> CaptureHomeworkScreenshotAsync(HomeworkImageBounds bounds,
        CancellationToken cancellationToken)
    {
        if (bounds.Width < 8 || bounds.Height < 8 || bounds.ViewportWidth < 1 ||
            bounds.ViewportHeight < 1 || bounds.X < 0 || bounds.Y < 0 ||
            bounds.X + bounds.Width > bounds.ViewportWidth ||
            bounds.Y + bounds.Height > bounds.ViewportHeight) return null;
        await Task.Delay(100, cancellationToken);
        await using var screen = new MemoryStream();
        await CourseView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screen);
        screen.Position = 0;
        var decoder = BitmapDecoder.Create(screen, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var bitmap = decoder.Frames[0];
        var scaleX = bitmap.PixelWidth / bounds.ViewportWidth;
        var scaleY = bitmap.PixelHeight / bounds.ViewportHeight;
        var left = Math.Max(0, (int)Math.Floor((bounds.X - 3) * scaleX));
        var top = Math.Max(0, (int)Math.Floor((bounds.Y - 3) * scaleY));
        var right = Math.Min(bitmap.PixelWidth, (int)Math.Ceiling((bounds.X + bounds.Width + 3) * scaleX));
        var bottom = Math.Min(bitmap.PixelHeight, (int)Math.Ceiling((bounds.Y + bounds.Height + 3) * scaleY));
        if (right <= left || bottom <= top) return null;
        return NormalizeHomeworkBitmap(new CroppedBitmap(bitmap,
            new Int32Rect(left, top, right - left, bottom - top)));
    }

    private static string? NormalizeHomeworkImage(string? dataUrl)
    {
        if (dataUrl is null || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
        var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        if (bytes.Length is < 100 or > 3_000_000) return null;
        using var stream = new MemoryStream(bytes);
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        return NormalizeHomeworkBitmap(bitmap);
    }

    private static string? NormalizeHomeworkBitmap(BitmapSource source)
    {
        if (source.PixelWidth < 8 || source.PixelHeight < 8 ||
            source.PixelWidth > 8192 || source.PixelHeight > 8192) return null;
        var scale = Math.Min(3, Math.Max(1, 640 / Math.Max(source.PixelWidth, source.PixelHeight)));
        var width = source.PixelWidth * scale;
        var height = source.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            drawing.DrawImage(source, new Rect(0, 0, width, height));
        }
        var composed = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        composed.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using var output = new MemoryStream();
        encoder.Save(output);
        if (output.Length > 3_000_000) return null;
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
}
