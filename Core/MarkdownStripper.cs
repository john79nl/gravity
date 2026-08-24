using System.Text.RegularExpressions;

namespace Gravity.Core
{
    /// <summary>
    /// Converts Markdown text to clean, human-readable plain text suitable for WinForms TextBox display.
    /// Preserves structure (line breaks, indentation, bullets) while stripping Markdown syntax.
    /// </summary>
    public static class MarkdownStripper
    {
        /// <summary>
        /// Converts Markdown-formatted text into clean plain text.
        /// </summary>
        public static string ToPlainText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return markdown ?? string.Empty;

            var text = markdown;

            // Normalize line endings
            text = text.Replace("\r\n", "\n");

            // ── Headers: "## Title" → "TITLE" (uppercase, with spacing) ──────
            text = Regex.Replace(text, @"^#{1,6}\s+(.+)$", m =>
            {
                return m.Groups[1].Value.Trim().ToUpperInvariant();
            }, RegexOptions.Multiline);

            // ── Horizontal rules: "---" or "***" or "___" → blank line ────────
            text = Regex.Replace(text, @"^[-*_]{3,}\s*$", "", RegexOptions.Multiline);

            // ── Bold + Italic: ***text*** or ___text___ → text ────────────────
            text = Regex.Replace(text, @"\*{3}(.+?)\*{3}", "$1");
            text = Regex.Replace(text, @"_{3}(.+?)_{3}", "$1");

            // ── Bold: **text** or __text__ → text ────────────────────────────
            text = Regex.Replace(text, @"\*{2}(.+?)\*{2}", "$1");
            text = Regex.Replace(text, @"_{2}(.+?)_{2}", "$1");

            // ── Italic: *text* or _text_ → text ─────────────────────────────
            text = Regex.Replace(text, @"(?<!\w)\*([^*\n]+?)\*(?!\w)", "$1");
            text = Regex.Replace(text, @"(?<!\w)_([^_\n]+?)_(?!\w)", "$1");

            // ── Strikethrough: ~~text~~ → text ──────────────────────────────
            text = Regex.Replace(text, @"~~(.+?)~~", "$1");

            // ── Inline code: `code` → code ──────────────────────────────────
            text = Regex.Replace(text, @"`([^`]+)`", "$1");

            // ── Code blocks: ```...``` → content indented ───────────────────
            text = Regex.Replace(text, @"```[a-zA-Z]*\n([\s\S]*?)```", m =>
            {
                var lines = m.Groups[1].Value.TrimEnd('\n').Split('\n');
                return string.Join("\n", lines.Select(l => "  " + l));
            });

            // ── Links: [label](url) → label ─────────────────────────────────
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");

            // ── Images: ![alt](url) → (alt) ─────────────────────────────────
            text = Regex.Replace(text, @"!\[([^\]]*)\]\([^)]+\)", "($1)");

            // ── Blockquotes: "> text" → "  text" ────────────────────────────
            text = Regex.Replace(text, @"^>\s?", "  ", RegexOptions.Multiline);

            // ── Unordered list bullets: "- item" or "* item" → "• item" ─────
            text = Regex.Replace(text, @"^(\s*)[-*]\s+", "$1• ", RegexOptions.Multiline);

            // ── Ordered list: "1. item" → "1) item" ─────────────────────────
            text = Regex.Replace(text, @"^(\s*)(\d+)\.\s+", "$1$2) ", RegexOptions.Multiline);

            // ── Collapse excessive blank lines (max 2) ──────────────────────
            text = Regex.Replace(text, @"\n{4,}", "\n\n\n");

            // ── Trim leading/trailing whitespace ────────────────────────────
            text = text.Trim();

            // Restore Windows line endings for TextBox
            text = text.Replace("\n", "\r\n");

            return text;
        }
    }
}
