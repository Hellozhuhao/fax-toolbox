using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace FaxToolbox.Windows;

internal sealed record AgentTextAttachment(string Name, string Text);

internal static class AgentAttachmentReader
{
    internal static bool SupportsFileName(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".pdf" or ".docx" or ".pptx" or ".xlsx" or ".txt" or ".md" or ".csv" or
        ".tsv" or ".json" or ".xml" or ".html" or ".htm" or ".css" or ".js" or
        ".ts" or ".py" or ".cs" or ".rs" or ".go" or ".java" or ".yaml" or
        ".yml" or ".toml" or ".sql" or ".log";

    internal static IReadOnlyList<AgentTextAttachment> Parse(JsonElement message)
    {
        if (!message.TryGetProperty("files", out var files)) return [];
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() > 12)
            throw new InvalidDataException("一次最多添加 12 个文件。");
        var result = new List<AgentTextAttachment>();
        var totalBytes = 0;
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object ||
                !file.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String ||
                !file.TryGetProperty("data", out var dataValue) || dataValue.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("附件格式无效。");
            var name = nameValue.GetString() ?? "";
            if (name.Length is < 1 or > 240 || name.Any(char.IsControl) ||
                name.Split('/', '\\').Any(part => part is "" or "." or "..") ||
                name.StartsWith('/') || name.Contains(':'))
                throw new InvalidDataException("附件名称无效。");
            var encoded = dataValue.GetString() ?? "";
            if (encoded.Length > 2_800_000) throw new InvalidDataException("单个附件不能超过 2 MB。");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded); }
            catch (FormatException) { throw new InvalidDataException("附件内容无法读取。"); }
            totalBytes += bytes.Length;
            if (bytes.Length > 2_000_000 || totalBytes > 5_000_000)
                throw new InvalidDataException("附件总量不能超过 5 MB，单个不能超过 2 MB。");
            var extension = Path.GetExtension(name).ToLowerInvariant();
            string text;
            try
            {
                text = extension switch
                {
                    ".pdf" => ExtractPdf(bytes),
                    ".docx" => ExtractOpenXml(bytes, "word/document.xml"),
                    ".pptx" => ExtractOpenXml(bytes, "ppt/slides/slide"),
                    ".xlsx" => ExtractSpreadsheet(bytes),
                    ".txt" or ".md" or ".csv" or ".tsv" or ".json" or ".xml" or
                        ".html" or ".htm" or ".css" or ".js" or ".ts" or ".py" or
                        ".cs" or ".rs" or ".go" or ".java" or ".yaml" or ".yml" or
                        ".toml" or ".sql" or ".log" => new UTF8Encoding(false, true).GetString(bytes),
                    _ => throw new InvalidDataException("暂不支持附件格式：" + extension)
                };
            }
            catch (Exception error) when (error is not OutOfMemoryException and not InvalidDataException)
            { throw new InvalidDataException("无法读取附件“" + name + "”：文件可能损坏或已加密。", error); }
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("无法从“" + name + "”提取文字；扫描版 PDF 请改用截图。");
            result.Add(new AgentTextAttachment(name, text.Trim()));
        }
        return result;
    }

    private static string ExtractOpenXml(byte[] bytes, string prefix)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > 200) throw new InvalidDataException("文档内部文件过多，请拆分后添加。");
        var result = new StringBuilder();
        var count = 0;
        foreach (var entry in archive.Entries.Where(item => item.FullName.StartsWith(prefix, StringComparison.Ordinal) &&
                     item.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(item => item.FullName, StringComparer.Ordinal))
        {
            if (++count > 60 || entry.Length > 2_000_000 || result.Length > 35_000)
                throw new InvalidDataException("文档过大，请只添加需要的部分。");
            using var part = entry.Open();
            var xml = XDocument.Load(part, LoadOptions.None);
            foreach (var element in xml.Descendants())
                if (element.Name.LocalName is "t" or "v" && !element.HasElements)
                    result.Append(element.Value).Append(' ');
            result.AppendLine();
        }
        return result.ToString();
    }

    private static string ExtractPdf(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        if (document.NumberOfPages > 60)
            throw new InvalidDataException("PDF 页数超过 60，请拆分后添加。");
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            text.AppendLine(ContentOrderTextExtractor.GetText(page));
            if (text.Length > 35_000)
                throw new InvalidDataException("PDF 文字过长，请拆分后添加。");
        }
        return text.ToString();
    }

    private static string ExtractSpreadsheet(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > 200) throw new InvalidDataException("表格内部文件过多，请拆分后添加。");
        var shared = new List<string>();
        var strings = archive.GetEntry("xl/sharedStrings.xml");
        if (strings is { Length: > 2_000_000 }) throw new InvalidDataException("表格文字过长。");
        if (strings is not null)
        {
            using var part = strings.Open();
            var xml = XDocument.Load(part, LoadOptions.None);
            foreach (var item in xml.Descendants().Where(element => element.Name.LocalName == "si"))
                shared.Add(string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t")
                    .Select(element => element.Value)));
        }
        var output = new StringBuilder();
        var sheets = archive.Entries.Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) &&
            entry.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(entry => entry.FullName, StringComparer.Ordinal);
        foreach (var sheet in sheets.Take(30))
        {
            if (sheet.Length > 2_000_000) throw new InvalidDataException("表格过大，请拆分后添加。");
            output.AppendLine(sheet.Name + "：");
            using var part = sheet.Open();
            var xml = XDocument.Load(part, LoadOptions.None);
            foreach (var row in xml.Descendants().Where(element => element.Name.LocalName == "row"))
            {
                foreach (var cell in row.Elements().Where(element => element.Name.LocalName == "c"))
                {
                    var value = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ??
                        string.Concat(cell.Descendants().Where(element => element.Name.LocalName == "t")
                            .Select(element => element.Value));
                    if (cell.Attribute("t")?.Value == "s" && int.TryParse(value, out var index) &&
                        index >= 0 && index < shared.Count) value = shared[index];
                    if (value.Length > 0) output.Append(cell.Attribute("r")?.Value).Append('=').Append(value).Append(' ');
                }
                output.AppendLine();
                if (output.Length > 35_000) throw new InvalidDataException("表格文字过长，请拆分后添加。");
            }
        }
        return output.ToString();
    }
}
