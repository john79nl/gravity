using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gravity.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UserControl = System.Windows.Controls.UserControl;
using Color       = System.Windows.Media.Color;
using Button      = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using FontFamily  = System.Windows.Media.FontFamily;
using Application = System.Windows.Application;

namespace Gravity.UI
{
    /// <summary>
    /// WPF panel hosting a Quill rich-text editor for .docx files.
    /// Supports: read-only preview, edit mode, image resize handles,
    /// image drag-to-reposition, and Save back to .docx.
    /// </summary>
    public class DocxPreviewPanel : UserControl
    {
        private readonly WebView2           _browser;
        private readonly TextBlock          _titleLabel;
        private readonly Button             _btnEdit;
        private readonly Button             _btnSave;
        private readonly Button             _btnCancel;
        private readonly Button             _btnOpenInWord;
        private readonly DocxPreviewService? _service;
        private string  _currentFilePath = string.Empty;
        private bool    _editMode        = false;
        private string? _pendingHtml;

        // ── Constructor ───────────────────────────────────────────────────────
        public DocxPreviewPanel(DocxPreviewService? service = null, IThemeService? themeService = null)
        {
            _service = service;

            var colors = themeService?.Colors;
            var headerBg = colors != null ? System.Drawing.Color.FromArgb(colors.HeaderBackground.A, colors.HeaderBackground.R, colors.HeaderBackground.G, colors.HeaderBackground.B) : System.Drawing.Color.FromArgb(30, 32, 45);
            var border   = colors != null ? System.Drawing.Color.FromArgb(colors.Border.A, colors.Border.R, colors.Border.G, colors.Border.B) : System.Drawing.Color.FromArgb(45, 48, 65);
            var accent   = colors != null ? System.Drawing.Color.FromArgb(colors.Accent.A, colors.Accent.R, colors.Accent.G, colors.Accent.B) : System.Drawing.Color.FromArgb(114, 137, 218);
            var text     = colors != null ? System.Drawing.Color.FromArgb(colors.Foreground.A, colors.Foreground.R, colors.Foreground.G, colors.Foreground.B) : System.Drawing.Color.FromArgb(230, 230, 240);
            var bg       = colors != null ? System.Drawing.Color.FromArgb(colors.Background.A, colors.Background.R, colors.Background.G, colors.Background.B) : System.Drawing.Color.FromArgb(10, 12, 20);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── Toolbar ──────────────────────────────────────────────────────
            var toolbar = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(headerBg.R, headerBg.G, headerBg.B)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(border.R, border.G, border.B)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(12, 7, 12, 7)
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            stack.Children.Add(new TextBlock
            {
                Text = "📄", FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0,0,10,0)
            });

            _titleLabel = new TextBlock
            {
                Text              = "DOCX Preview",
                Foreground        = new SolidColorBrush(Color.FromRgb(accent.R, accent.G, accent.B)),
                FontFamily        = new FontFamily("Segoe UI"),
                FontSize          = 14,
                FontWeight        = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0,0,24,0)
            };
            stack.Children.Add(_titleLabel);

            var whiteText  = Color.FromRgb(255, 255, 255);
            var greenColor = Color.FromRgb(46, 160, 67);   // Vivid Emerald Save Green
            var redColor   = Color.FromRgb(218, 54, 51);   // Vivid Coral Cancel Red
            var accentRgb  = Color.FromRgb(accent.R, accent.G, accent.B);
            var wordBg     = Color.FromArgb(40, accent.R, accent.G, accent.B);
            var wordBorder = Color.FromArgb(100, accent.R, accent.G, accent.B);

            _btnEdit      = MakeBtn("✏️  Edit",   accentRgb,  whiteText, accentRgb);
            _btnSave      = MakeBtn("💾 Save",   greenColor, whiteText, greenColor);
            _btnCancel    = MakeBtn("✕ Cancel",  redColor,   whiteText, redColor);
            _btnOpenInWord= MakeBtn("📂 Word",   wordBg,     Color.FromRgb(text.R, text.G, text.B), wordBorder);

            _btnSave.Visibility   = Visibility.Collapsed;
            _btnCancel.Visibility = Visibility.Collapsed;

            _btnEdit.Click       += OnEdit;
            _btnSave.Click       += OnSave;
            _btnCancel.Click     += OnCancel;
            _btnOpenInWord.Click += OnOpenInWord;

            stack.Children.Add(_btnEdit);
            stack.Children.Add(_btnSave);
            stack.Children.Add(_btnCancel);
            stack.Children.Add(_btnOpenInWord);

            toolbar.Child = stack;
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            // ── WebView2 ─────────────────────────────────────────────────────
            _browser = new WebView2
            {
                DefaultBackgroundColor = bg
            };
            Grid.SetRow(_browser, 1);
            root.Children.Add(_browser);

            Content = root;
            _ = InitBrowserAsync();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ShowPreview(string filePath, string htmlContent)
        {
            _currentFilePath = filePath;
            this.Dispatcher.Invoke(() =>
            {
                _titleLabel.Text = Path.GetFileName(filePath);
                if (_browser.CoreWebView2 != null)
                    _browser.NavigateToString(htmlContent);
                else
                    _pendingHtml = htmlContent;

                // Reset to read-only on every refresh
                SetEditMode(false);
            });
        }

        // ── Toolbar handlers ──────────────────────────────────────────────────

        private void OnEdit(object sender, RoutedEventArgs e)
        {
            SetEditMode(true);
            _ = _browser.CoreWebView2?.ExecuteScriptAsync("window.setEditMode(true)");
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            _ = _browser.CoreWebView2?.ExecuteScriptAsync("window.requestSave()");
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            SetEditMode(false);
            _ = _browser.CoreWebView2?.ExecuteScriptAsync("window.setEditMode(false)");
            // Reload from disk to discard edits
            if (!string.IsNullOrEmpty(_currentFilePath))
                _service?.QueuePreview(_currentFilePath);
        }

        private void OnOpenInWord(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_currentFilePath)) return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_currentFilePath)
                    { UseShellExecute = true });
            }
            catch { }
        }

        // ── WebView2 init + message bridge ────────────────────────────────────

        private async System.Threading.Tasks.Task InitBrowserAsync()
        {
            try
            {
                var userDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Gravity", "WebView2");
                System.IO.Directory.CreateDirectory(userDataDir);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                await _browser.EnsureCoreWebView2Async(env);

                _browser.CoreWebView2.Settings.IsStatusBarEnabled           = false;
                _browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                // Receive save payload from JS
                _browser.CoreWebView2.WebMessageReceived += OnWebMessage;

                if (_pendingHtml != null)
                {
                    _browser.NavigateToString(_pendingHtml);
                    _pendingHtml = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocxPreviewPanel] Init error: {ex.Message}");
            }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var raw = e.WebMessageAsJson;
                var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // Unwrap string-encoded JSON if WebView2 double-serialised
                if (root.ValueKind == JsonValueKind.String)
                {
                    doc  = JsonDocument.Parse(root.GetString()!);
                    root = doc.RootElement;
                }

                if (root.TryGetProperty("type", out var t) && t.GetString() == "save"
                    && root.TryGetProperty("html", out var h))
                {
                    var html = h.GetString() ?? "";
                    _service?.SaveFromHtml(_currentFilePath, html);

                    this.Dispatcher.Invoke(() => SetEditMode(false));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocxPreviewPanel] WebMessage error: {ex.Message}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetEditMode(bool on)
        {
            _editMode             = on;
            _btnEdit.Visibility   = on ? Visibility.Collapsed  : Visibility.Visible;
            _btnSave.Visibility   = on ? Visibility.Visible    : Visibility.Collapsed;
            _btnCancel.Visibility = on ? Visibility.Visible    : Visibility.Collapsed;
        }

        private static Button MakeBtn(string label, Color bg, Color fg, Color border)
        {
            return new Button
            {
                Content         = label,
                Background      = new SolidColorBrush(bg),
                Foreground      = new SolidColorBrush(fg),
                BorderBrush     = new SolidColorBrush(border),
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(12, 4, 12, 4),
                Margin          = new Thickness(0, 0, 6, 0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                FontFamily      = new FontFamily("Segoe UI"),
                FontSize        = 12,
                FontWeight      = FontWeights.SemiBold
            };
        }
    }
}
