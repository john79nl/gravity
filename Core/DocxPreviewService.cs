using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
// Aliases keep Drawing types from clashing with Wordprocessing types (same short names)
using A      = DocumentFormat.OpenXml.Drawing;
using AWp    = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Pic    = DocumentFormat.OpenXml.Drawing.Pictures;
using Vml    = DocumentFormat.OpenXml.Vml;

namespace Gravity.Core
{
    /// <summary>
    /// Bidirectional DOCX <-> HTML service.
    ///   QueuePreview  — DOCX -> HTML (rendered in WebView2 via Quill editor)
    ///   SaveFromHtml  — Quill HTML -> DOCX (saves edits back to disk)
    /// </summary>
    public class DocxPreviewService
    {
        private readonly IThemeService? _themeService;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _tempFileMap = new(StringComparer.OrdinalIgnoreCase);

        public DocxPreviewService(IThemeService? themeService = null)
        {
            _themeService = themeService;
        }

        private static string ToHex(System.Drawing.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private string GetOrCreateTempFile(string originalPath)
        {
            if (_tempFileMap.TryGetValue(originalPath, out var existingTemp) && File.Exists(existingTemp))
            {
                try { File.Copy(originalPath, existingTemp, overwrite: true); } catch { }
                return existingTemp;
            }

            var tempFileName = $"gravity_editor_{Guid.NewGuid():N}.docx";
            var tempPath = Path.Combine(Path.GetTempPath(), tempFileName);
            try
            {
                File.Copy(originalPath, tempPath, overwrite: true);
                _tempFileMap[originalPath] = tempPath;
                return tempPath;
            }
            catch
            {
                return originalPath;
            }
        }

        private static void SafeCopyFile(string source, string destination)
        {
            int retries = 5;
            while (retries > 0)
            {
                try
                {
                    File.Copy(source, destination, overwrite: true);
                    return;
                }
                catch (IOException) when (retries > 1)
                {
                    retries--;
                    System.Threading.Thread.Sleep(50);
                }
            }
            File.Copy(source, destination, overwrite: true);
        }

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<string, string>? DocxPreviewReady; // (filePath, html)
        public event Action<string>?         DocxSaved;        // (filePath)

        // ── DOCX -> HTML ──────────────────────────────────────────────────────
        public void QueuePreview(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath)) return;
            if (!absolutePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(absolutePath)) return;

            Task.Run(() =>
            {
                try
                {
                    var workingPath = GetOrCreateTempFile(absolutePath);
                    DocxPreviewReady?.Invoke(absolutePath, ConvertToHtml(workingPath));
                }
                catch (Exception ex) { DocxPreviewReady?.Invoke(absolutePath, BuildErrorHtml(absolutePath, ex.Message)); }
            });
        }

        public static void WriteDocxFromTextOrHtml(string path, string textOrHtml)
        {
            var html = ConvertMarkdownOrTextToHtml(textOrHtml);
            WriteDocx(path, html);
        }

        public static string ConvertMarkdownOrTextToHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "<p></p>";
            if (text.Contains("<p>") || text.Contains("<h1>") || text.Contains("<div>") || text.Contains("<body>") || text.Contains("<h2"))
                return text;

            var sb = new StringBuilder();
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.StartsWith("# "))
                    sb.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(2).Trim())}</h1>");
                else if (trimmed.StartsWith("## "))
                    sb.AppendLine($"<h2>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(3).Trim())}</h2>");
                else if (trimmed.StartsWith("### "))
                    sb.AppendLine($"<h3>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(4).Trim())}</h3>");
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                    sb.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(trimmed.Substring(2).Trim())}</li>");
                else
                    sb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(trimmed)}</p>");
            }

            return sb.ToString();
        }

        // ── HTML -> DOCX ──────────────────────────────────────────────────────
        public void SaveFromHtml(string absolutePath, string html)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(html)) return;
            Task.Run(() =>
            {
                try
                {
                    var workingPath = GetOrCreateTempFile(absolutePath);
                    WriteDocx(workingPath, html);
                    SafeCopyFile(workingPath, absolutePath);
                    DocxSaved?.Invoke(absolutePath);
                    QueuePreview(absolutePath); // refresh panel
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DocxPreviewService] Save failed: {ex.Message}");
                }
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  DOCX -> HTML  (all WP types fully qualified where ambiguous)
        // ═══════════════════════════════════════════════════════════════════════

        private string ConvertToHtml(string path)
        {
            try
            {
                using var doc   = WordprocessingDocument.Open(path, isEditable: false);
                var mainPart    = doc.MainDocumentPart;
                var body        = mainPart?.Document?.Body;
                if (body == null) return BuildErrorHtml(path, "Document body is empty or could not be read.");

                var sb = new StringBuilder();
                foreach (var el in body.ChildElements)
                {
                    if      (el is Paragraph p) sb.Append(ParagraphToHtml(p, mainPart!));
                    else if (el is Table t)     sb.Append(TableToHtml(t, mainPart!));
                }
                return WrapInEditorPage(Path.GetFileName(path), sb.ToString());
            }
            catch (Exception ex)
            {
                // Auto-healing: If the file on disk is plain text / markdown instead of a valid binary OpenXML package,
                // automatically convert the plain text into a valid binary .docx OpenXML document and re-open it!
                try
                {
                    var textContent = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(textContent))
                    {
                        WriteDocxFromTextOrHtml(path, textContent);

                        using var healedDoc = WordprocessingDocument.Open(path, isEditable: false);
                        var mainPart        = healedDoc.MainDocumentPart;
                        var body            = mainPart?.Document?.Body;
                        if (body != null)
                        {
                            var sb = new StringBuilder();
                            foreach (var el in body.ChildElements)
                            {
                                if      (el is Paragraph p) sb.Append(ParagraphToHtml(p, mainPart!));
                                else if (el is Table t)     sb.Append(TableToHtml(t, mainPart!));
                            }
                            return WrapInEditorPage(Path.GetFileName(path), sb.ToString());
                        }
                    }
                }
                catch { }

                return BuildErrorHtml(path, ex.Message);
            }
        }

        private static string ParagraphToHtml(Paragraph para, MainDocumentPart mp)
        {
            var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
            var runs    = new StringBuilder();

            foreach (var child in para.ChildElements)
            {
                if      (child is Run r)       runs.Append(RunToHtml(r, mp));
                else if (child is Hyperlink hl) runs.Append(HyperlinkToHtml(hl, mp));
            }

            var html     = runs.ToString();
            bool hasImg  = html.Contains("<img ");
            if (!hasImg && string.IsNullOrEmpty(html.Trim())) return "<p><br></p>\n";

            // Heading styles (e.g. "Heading1", "Heading 1")
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                var lvlStr = styleId.Replace("Heading", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (int.TryParse(lvlStr, out int h) && h >= 1 && h <= 6)
                    return $"<h{h}>{html}</h{h}>\n";
            }

            // Numbered / bulleted lists
            if (para.ParagraphProperties?.NumberingProperties != null)
            {
                var lvl = para.ParagraphProperties.NumberingProperties
                              .NumberingLevelReference?.Val?.Value ?? 0;
                return $"<li style=\"margin-left:{lvl * 20}px\">{html}</li>\n";
            }

            return $"<p>{html}</p>\n";
        }

        private static string RunToHtml(Run run, MainDocumentPart mp)
        {
            var sb = new StringBuilder();

            // ── Images inside the run ─────────────────────────────────────────
            foreach (var child in run.ChildElements)
            {
                if (child is Drawing drawing)
                {
                    var img = DrawingToImgTag(drawing, mp);
                    if (img != null) sb.Append(img);
                }
                else if (child is Picture pic)
                {
                    var img = VmlPictureToImgTag(pic, mp);
                    if (img != null) sb.Append(img);
                }
            }

            // ── Text with inline formatting ───────────────────────────────────
            var text = run.InnerText;
            if (string.IsNullOrEmpty(text)) return sb.ToString();

            var rpr    = run.RunProperties;
            bool bold  = rpr?.Bold      != null;
            bool ital  = rpr?.Italic    != null;
            bool ul    = rpr?.Underline?.Val != null && rpr.Underline.Val != UnderlineValues.None;
            bool st    = rpr?.Strike    != null;
            var  color = rpr?.Color?.Val?.Value;

            var esc = System.Net.WebUtility.HtmlEncode(text);
            if (bold) esc = $"<strong>{esc}</strong>";
            if (ital) esc = $"<em>{esc}</em>";
            if (ul)   esc = $"<u>{esc}</u>";
            if (st)   esc = $"<s>{esc}</s>";
            if (!string.IsNullOrEmpty(color) && color != "auto")
                esc = $"<span style=\"color:#{color}\">{esc}</span>";

            sb.Append(esc);
            return sb.ToString();
        }

        private static string HyperlinkToHtml(Hyperlink hl, MainDocumentPart mp)
        {
            var sb = new StringBuilder();
            foreach (var child in hl.ChildElements)
                if (child is Run r) sb.Append(RunToHtml(r, mp));
            return $"<a href=\"#\">{sb}</a>";
        }

        private static string TableToHtml(Table table, MainDocumentPart mp)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<table>");
            foreach (var row in table.Elements<TableRow>())
            {
                sb.AppendLine("  <tr>");
                foreach (var cell in row.Elements<TableCell>())
                {
                    var cellSb = new StringBuilder();
                    foreach (var p in cell.Elements<Paragraph>())
                        cellSb.Append(ParagraphToHtml(p, mp));
                    sb.AppendLine($"    <td>{cellSb}</td>");
                }
                sb.AppendLine("  </tr>");
            }
            sb.AppendLine("</table>");
            return sb.ToString();
        }

        // ── Image helpers ─────────────────────────────────────────────────────

        private static string? DrawingToImgTag(Drawing drawing, MainDocumentPart mp)
        {
            try
            {
                var blip  = drawing.Descendants<A.Blip>().FirstOrDefault();
                var relId = blip?.Embed?.Value;
                if (string.IsNullOrEmpty(relId)) return null;

                var part  = (ImagePart)mp.GetPartById(relId);
                var dims  = GetEmuDimensions(drawing);
                return ImagePartToTag(part, dims);
            }
            catch { return null; }
        }

        private static string? VmlPictureToImgTag(Picture pic, MainDocumentPart mp)
        {
            try
            {
                var imgData = pic.Descendants<Vml.ImageData>().FirstOrDefault();
                var relId   = imgData?.RelationshipId?.Value;
                if (string.IsNullOrEmpty(relId)) return null;

                var part = (ImagePart)mp.GetPartById(relId);
                return ImagePartToTag(part, null);
            }
            catch { return null; }
        }

        private static string ImagePartToTag(ImagePart part, (int w, int h)? dims)
        {
            using var stream = part.GetStream();
            using var ms     = new MemoryStream();
            stream.CopyTo(ms);
            var b64  = Convert.ToBase64String(ms.ToArray());
            var mime = part.ContentType;

            var sizeAttr = dims.HasValue
                ? $" width=\"{dims.Value.w}\" height=\"{dims.Value.h}\""
                : "";

            return $"<img src=\"data:{mime};base64,{b64}\"{sizeAttr} " +
                   $"style=\"max-width:100%;height:auto;display:block;margin:.5em 0\">";
        }

        /// <summary>Returns pixel dimensions from a Drawing's EMU extents (96 dpi).</summary>
        private static (int w, int h)? GetEmuDimensions(Drawing drawing)
        {
            try
            {
                var ext = drawing.Descendants<AWp.Extent>().FirstOrDefault();
                if (ext == null) return null;
                return ((int)(ext.Cx!.Value / 9525), (int)(ext.Cy!.Value / 9525));
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  HTML -> DOCX  (save path)
        // ═══════════════════════════════════════════════════════════════════════

        private static void WriteDocx(string path, string html)
        {
            using var doc    = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
            var mainPart     = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body         = mainPart.Document.AppendChild(new Body());

            AddDefaultStyles(mainPart);
            var blocks = ParseHtmlToBlocks(html, mainPart);
            foreach (var b in blocks) body.AppendChild(b);
            body.AppendChild(new Paragraph()); // Word requires trailing paragraph
            mainPart.Document.Save();
        }

        private static List<OpenXmlElement> ParseHtmlToBlocks(string html, MainDocumentPart mainPart)
        {
            var blocks = new List<OpenXmlElement>();

            var bodyMatch = Regex.Match(html, @"<body[^>]*>(.*?)</body>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var content = bodyMatch.Success ? bodyMatch.Groups[1].Value : html;

            var re = new Regex(
                @"<(h[1-6]|p|li|tr|table)[^>]*>(.*?)</(h[1-6]|p|li|tr|table)>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match m in re.Matches(content))
            {
                var tag   = m.Groups[1].Value.ToLowerInvariant();
                var inner = m.Groups[2].Value;

                if (tag.Length == 2 && tag[0] == 'h' && char.IsDigit(tag[1]))
                {
                    blocks.Add(MakeHeading(StripTags(inner).Trim(), tag[1] - '0'));
                }
                else if (tag is "p" or "li")
                {
                    blocks.Add(MakeRichPara(inner, mainPart));
                }
                else if (tag == "tr")
                {
                    foreach (Match cell in Regex.Matches(inner,
                        @"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                        blocks.Add(MakeRichPara(cell.Groups[1].Value, mainPart));
                }
            }

            if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(content))
                blocks.Add(MakeRichPara(content, mainPart));

            return blocks;
        }

        private static Paragraph MakeHeading(string text, int level)
        {
            var p = new Paragraph();
            p.ParagraphProperties = new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = $"Heading{level}" }
            };
            p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            return p;
        }

        private static Paragraph MakeRichPara(string html, MainDocumentPart mainPart)
        {
            var para   = new Paragraph();
            var tokens = Regex.Split(html, @"(<[^>]+>)");
            var rpr    = new RunProperties();

            foreach (var tok in tokens)
            {
                if (tok.StartsWith("<"))
                {
                    bool close = tok.StartsWith("</");
                    var  tag   = tok.TrimStart('<').TrimEnd('>').TrimStart('/').ToLowerInvariant().Split(' ')[0];

                    if (tag == "img")
                    {
                        var srcMatch = Regex.Match(tok, @"src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                        if (srcMatch.Success)
                        {
                            var src = srcMatch.Groups[1].Value;
                            byte[]? imgBytes = null;
                            string contentType = "image/png";

                            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = src.Split(new[] { ";base64," }, StringSplitOptions.None);
                                if (parts.Length == 2)
                                {
                                    contentType = parts[0].Replace("data:", "").ToLowerInvariant();
                                    try { imgBytes = Convert.FromBase64String(parts[1]); } catch { }
                                }
                            }
                            else if (File.Exists(src))
                            {
                                try { imgBytes = File.ReadAllBytes(src); } catch { }
                            }

                            if (imgBytes != null && imgBytes.Length > 0)
                            {
                                try
                                {
                                    var imagePart = mainPart.AddImagePart(contentType);
                                    using (var stream = imagePart.GetStream())
                                    {
                                        stream.Write(imgBytes, 0, imgBytes.Length);
                                    }
                                    var relId = mainPart.GetIdOfPart(imagePart);

                                    long wEmu = 2857500L; // default 300px
                                    long hEmu = 1905000L; // default 200px

                                    var wMatch = Regex.Match(tok, @"width=[""']?(\d+)", RegexOptions.IgnoreCase);
                                    var hMatch = Regex.Match(tok, @"height=[""']?(\d+)", RegexOptions.IgnoreCase);
                                    if (wMatch.Success && int.TryParse(wMatch.Groups[1].Value, out int wPx))
                                        wEmu = wPx * 9525L;
                                    if (hMatch.Success && int.TryParse(hMatch.Groups[1].Value, out int hPx))
                                        hEmu = hPx * 9525L;

                                    var drawingEl = CreateDrawingElement(relId, wEmu, hEmu);
                                    para.AppendChild(new Run(drawingEl));
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[DocxPreviewService] Error saving image: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        switch (tag)
                        {
                            case "strong": case "b":
                                rpr.Bold   = close ? null : new Bold();   break;
                            case "em":    case "i":
                                rpr.Italic = close ? null : new Italic();  break;
                            case "u":
                                rpr.Underline = close ? null
                                    : new Underline { Val = UnderlineValues.Single }; break;
                            case "s":    case "strike":
                                rpr.Strike = close ? null : new Strike();  break;
                        }
                    }
                }
                else
                {
                    var decoded = System.Net.WebUtility.HtmlEncode(System.Net.WebUtility.HtmlDecode(tok));
                    if (string.IsNullOrEmpty(decoded)) continue;
                    var run = new Run();
                    if (rpr.ChildElements.Count > 0)
                        run.RunProperties = (RunProperties)rpr.CloneNode(true);
                    run.AppendChild(new Text(decoded) { Space = SpaceProcessingModeValues.Preserve });
                    para.AppendChild(run);
                }
            }
            return para;
        }

        private static DocumentFormat.OpenXml.Wordprocessing.Drawing CreateDrawingElement(string relationshipId, long widthEmu, long heightEmu)
        {
            return new DocumentFormat.OpenXml.Wordprocessing.Drawing(
                new AWp.Inline(
                    new AWp.Extent() { Cx = widthEmu, Cy = heightEmu },
                    new AWp.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new AWp.DocProperties() { Id = (UInt32Value)1U, Name = "Picture" },
                    new AWp.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks() { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new Pic.Picture(
                                new Pic.NonVisualPictureProperties(
                                    new Pic.NonVisualDrawingProperties() { Id = (UInt32Value)0U, Name = "Image.png" },
                                    new Pic.NonVisualPictureDrawingProperties()),
                                new Pic.BlipFill(
                                    new A.Blip() { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                                    new A.Stretch(new A.FillRectangle())),
                                new Pic.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset() { X = 0L, Y = 0L },
                                        new A.Extents() { Cx = widthEmu, Cy = heightEmu }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })
                            )
                        ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                    )
                ) { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U }
            );
        }

        private static string StripTags(string html) =>
            Regex.Replace(System.Net.WebUtility.HtmlDecode(html), @"<[^>]+>", "");

        private static void AddDefaultStyles(MainDocumentPart mp)
        {
            var sp = mp.AddNewPart<StyleDefinitionsPart>();
            sp.Styles = new Styles();
            for (int h = 1; h <= 6; h++)
            {
                var size  = (36 - (h - 1) * 4).ToString();
                var style = new Style { Type = StyleValues.Paragraph, StyleId = $"Heading{h}" };
                style.AppendChild(new StyleName { Val = $"heading {h}" });
                var srp = new StyleRunProperties();
                srp.AppendChild(new Bold());
                srp.AppendChild(new FontSize { Val = size });
                style.AppendChild(srp);
                sp.Styles.AppendChild(style);
            }
            sp.Styles.Save();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  HTML page wrapper  (Quill rich-text editor)
        // ═══════════════════════════════════════════════════════════════════════

        internal string WrapInEditorPage(string title, string body)
        {
            var safeTitle = System.Net.WebUtility.HtmlEncode(title);

            var colors = _themeService?.Colors;
            var isDark = _themeService?.CurrentMode == ThemeMode.Dark;

            string bgHex       = colors != null ? ToHex(colors.Background)       : "#0a0c14";
            string surfaceHex  = colors != null ? ToHex(colors.PanelBackground)  : "#181a26";
            string textHex     = colors != null ? ToHex(colors.Foreground)       : "#e6e6f0";
            string accentHex   = colors != null ? ToHex(colors.Accent)           : "#7289da";
            string borderHex   = colors != null ? ToHex(colors.Border)           : "#2d3041";
            string headerBgHex = colors != null ? ToHex(colors.HeaderBackground): "#1e202d";
            string shadow      = isDark ? "0 4px 20px rgba(0,0,0,0.4)" : "0 2px 12px rgba(0,0,0,0.08)";

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <title>{safeTitle}</title>
  <link rel=""stylesheet"" href=""https://cdn.quilljs.com/1.3.7/quill.snow.css"">
  <style>
    :root {{
      --bg:{bgHex};
      --surface:{surfaceHex};
      --text:{textHex};
      --accent:{accentHex};
      --border:{borderHex};
      --header-bg:{headerBgHex};
    }}
    * {{ box-sizing:border-box; margin:0; padding:0; }}
    html,body {{ height:100%; background:var(--bg); color:var(--text); font-family:'Segoe UI',system-ui,sans-serif; overflow:hidden; }}
    .ql-toolbar.ql-snow {{ background:var(--header-bg); border:none; border-bottom:1px solid var(--border); padding:6px 12px; display:none; }}
    .ql-container.ql-snow {{ border:none; font-family:'Segoe UI',system-ui,sans-serif; font-size:15px; color:var(--text); background:var(--bg); height:calc(100vh - 36px); overflow-y:auto; }}
    .ql-editor {{ width:100%; max-width:100%; margin:0; padding:24px 36px; line-height:1.75; background:var(--bg); border:none; border-radius:0; box-shadow:none; min-height:100%; }}
    .ql-editor.ql-blank::before {{ color:#6c7086; }}
    .ql-editor h1,.ql-editor h2,.ql-editor h3,.ql-editor h4,.ql-editor h5,.ql-editor h6 {{ color:var(--accent); margin:1.2em 0 0.3em; }}
    .ql-editor h1 {{ font-size:2em; border-bottom:1px solid var(--border); padding-bottom:.3em; }}
    .ql-editor h2 {{ font-size:1.5em; }} .ql-editor h3 {{ font-size:1.25em; }}
    .ql-editor p {{ margin:.6em 0; }}
    .ql-editor strong {{ color:var(--accent); filter:brightness(1.2); }}
    .ql-editor em {{ color:var(--text); font-style:italic; }}
    .ql-editor a {{ color:var(--accent); }}
    .ql-editor img {{ max-width:100%; height:auto; display:block; margin:.5em 0; cursor:grab; transition:outline .15s; }}
    .ql-editor img.img-selected {{ outline:2px solid var(--accent); }}
    .ql-editor img.img-dragging {{ opacity:.45; cursor:grabbing; }}
    .ql-editor table {{ width:100%; border-collapse:collapse; margin:1em 0; }}
    .ql-editor td {{ border:1px solid var(--border); padding:7px 12px; }}
    .ql-snow .ql-stroke {{ stroke:var(--accent); }} .ql-snow .ql-fill {{ fill:var(--accent); }}
    .ql-snow .ql-picker {{ color:var(--text); }} .ql-snow .ql-picker-options {{ background:var(--surface); border-color:var(--border); }}
    #mode-bar {{ display:flex; align-items:center; gap:8px; padding:8px 16px; font-size:12px; color:var(--text); opacity:0.8; background:var(--header-bg); border-bottom:1px solid var(--border); }}
    #img-overlay {{ position:fixed; pointer-events:none; z-index:9999; display:none; border:2px solid var(--accent); }}
    .rh {{ position:absolute; width:10px; height:10px; background:var(--accent); border:1px solid #1e1e2e; border-radius:2px; pointer-events:all; }}
    .rh[data-p=""tl""] {{ top:-5px; left:-5px; cursor:nw-resize; }}
    .rh[data-p=""tm""] {{ top:-5px; left:calc(50% - 5px); cursor:n-resize; }}
    .rh[data-p=""tr""] {{ top:-5px; right:-5px; cursor:ne-resize; }}
    .rh[data-p=""ml""] {{ top:calc(50% - 5px); left:-5px; cursor:w-resize; }}
    .rh[data-p=""mr""] {{ top:calc(50% - 5px); right:-5px; cursor:e-resize; }}
    .rh[data-p=""bl""] {{ bottom:-5px; left:-5px; cursor:sw-resize; }}
    .rh[data-p=""bm""] {{ bottom:-5px; left:calc(50% - 5px); cursor:s-resize; }}
    .rh[data-p=""br""] {{ bottom:-5px; right:-5px; cursor:se-resize; }}
    .drop-line {{ position:absolute; left:0; right:0; height:2px; background:var(--accent); pointer-events:none; z-index:8888; display:none; }}
  </style>
</head>
<body>
  <div id=""mode-bar"">📄 <span id=""mode-label"">Read-only preview</span></div>
  <div id=""img-overlay"">
    <div class=""rh"" data-p=""tl""></div><div class=""rh"" data-p=""tm""></div>
    <div class=""rh"" data-p=""tr""></div><div class=""rh"" data-p=""ml""></div>
    <div class=""rh"" data-p=""mr""></div><div class=""rh"" data-p=""bl""></div>
    <div class=""rh"" data-p=""bm""></div><div class=""rh"" data-p=""br""></div>
  </div>
  <div id=""editor"">{body}</div>
  <script src=""https://cdn.quilljs.com/1.3.7/quill.min.js""></script>
  <script>
  (function() {{
    var quill=new Quill('#editor',{{theme:'snow',readOnly:true,modules:{{toolbar:[[{{header:[1,2,3,4,false]}}],['bold','italic','underline','strike'],[{{color:[]}},{{background:[]}}],[{{list:'ordered'}},{{list:'bullet'}}],['link','image','clean']]}}}});
    document.body.style.opacity = '1';
    var editMode=false,overlay=document.getElementById('img-overlay');
    var selImg=null,resizing=false,resizePos='',resStart={{}};
    var dragImg=null,dropLine=null;
    window.setEditMode=function(on){{editMode=on;quill.enable(on);document.querySelector('.ql-toolbar').style.display=on?'block':'none';document.getElementById('mode-label').textContent=on?'✏️ Editing — click image to resize, drag to reposition':'📄 Read-only preview';if(!on)hideOverlay();}};
    window.requestSave=function(){{var html=document.querySelector('.ql-editor').innerHTML;if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(JSON.stringify({{type:'save',html:html}}));}};
    function showOverlay(img){{selImg=img;img.classList.add('img-selected');positionOverlay();overlay.style.display='block';}}
    function hideOverlay(){{overlay.style.display='none';if(selImg){{selImg.classList.remove('img-selected');selImg=null;}}}}
    function positionOverlay(){{if(!selImg)return;var r=selImg.getBoundingClientRect();overlay.style.cssText='display:block;border:2px solid var(--accent);position:fixed;left:'+r.left+'px;top:'+r.top+'px;width:'+r.width+'px;height:'+r.height+'px;';}}
    document.querySelector('.ql-editor').addEventListener('click',function(e){{if(!editMode)return;if(e.target.tagName==='IMG')showOverlay(e.target);else hideOverlay();}});
    overlay.querySelectorAll('.rh').forEach(function(h){{h.addEventListener('mousedown',function(e){{if(!selImg||!editMode)return;resizing=true;resizePos=h.dataset.p;var r=selImg.getBoundingClientRect();resStart={{x:e.clientX,y:e.clientY,w:r.width,h:r.height}};e.preventDefault();e.stopPropagation();}});}});
    document.addEventListener('mousemove',function(e){{if(!resizing||!selImg)return;var dx=e.clientX-resStart.x,dy=e.clientY-resStart.y,nw=resStart.w,nh=resStart.h;if(resizePos.includes('r'))nw=Math.max(40,resStart.w+dx);if(resizePos.includes('l'))nw=Math.max(40,resStart.w-dx);if(resizePos.includes('b'))nh=Math.max(20,resStart.h+dy);if(resizePos.includes('t'))nh=Math.max(20,resStart.h-dy);if(resizePos.length===2&&['tl','tr','bl','br'].includes(resizePos)){{var ratio=resStart.h/resStart.w;if(Math.abs(dx)>Math.abs(dy))nh=nw*ratio;else nw=nh/ratio;}}selImg.width=Math.round(nw);selImg.height=Math.round(nh);positionOverlay();}});
    document.addEventListener('mouseup',function(){{if(resizing){{resizing=false;positionOverlay();}}}});
    window.addEventListener('scroll',positionOverlay,true);
    function getDropLine(){{if(!dropLine){{dropLine=document.createElement('div');dropLine.className='drop-line';document.querySelector('.ql-editor').appendChild(dropLine);}}return dropLine;}}
    document.querySelector('.ql-editor').addEventListener('mousedown',function(e){{if(!editMode||e.target.tagName!=='IMG'||resizing)return;dragImg=e.target;dragImg.classList.add('img-dragging');e.preventDefault();}});
    document.addEventListener('mousemove',function(e){{if(!dragImg||resizing)return;var editor=document.querySelector('.ql-editor'),dl=getDropLine();var srcPara=dragImg.closest('p')||dragImg.parentElement;var children=Array.from(editor.children).filter(function(c){{return c!==srcPara;}});var target=null,before=true;for(var i=0;i<children.length;i++){{var r=children[i].getBoundingClientRect();if(e.clientY>=r.top&&e.clientY<=r.bottom){{target=children[i];before=(e.clientY-r.top)<r.height/2;break;}}else if(e.clientY<r.top&&i===0){{target=children[0];before=true;break;}}else if(e.clientY>r.bottom&&i===children.length-1){{target=children[children.length-1];before=false;break;}}}}if(target){{var eRect=editor.getBoundingClientRect(),tRect=target.getBoundingClientRect();dl.style.display='block';dl.style.top=(before?tRect.top:tRect.bottom)-eRect.top+'px';dl._target=target;dl._before=before;}}}});
    document.addEventListener('mouseup',function(){{if(!dragImg)return;dragImg.classList.remove('img-dragging');var dl=getDropLine();if(dl&&dl._target){{var editor=document.querySelector('.ql-editor');var srcPara=dragImg.closest('p')||dragImg.parentElement;var newPara=document.createElement('p');var clone=dragImg.cloneNode(false);clone.width=dragImg.width;clone.height=dragImg.height;newPara.appendChild(clone);if(dl._before)editor.insertBefore(newPara,dl._target);else dl._target.insertAdjacentElement('afterend',newPara);if(srcPara&&srcPara!==newPara){{if(srcPara.querySelectorAll('img').length===1&&srcPara.textContent.trim()==='')srcPara.remove();else dragImg.remove();}}dl._target=null;}}dragImg=null;if(dropLine)dropLine.style.display='none';hideOverlay();}});
  }})();
  </script>
</body>
</html>";
        }

        private string BuildErrorHtml(string path, string msg) =>
            WrapInEditorPage(Path.GetFileName(path),
                $"<h2>Preview Error</h2><p style=\"color:#f38ba8\">" +
                $"{System.Net.WebUtility.HtmlEncode(msg)}</p>" +
                $"<p>File: <code>{System.Net.WebUtility.HtmlEncode(path)}</code></p>");
    }
}
