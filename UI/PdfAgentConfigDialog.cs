using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Gravity.Core;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Gravity.UI
{
    public class PdfAgentConfigDialog : MaterialForm
    {
        public class PdfToolItem
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Binary { get; set; } = "";
            public string Description { get; set; } = "";
            public string MappedOperations { get; set; } = "";
            public string SampleCommand { get; set; } = "";
            public string WingetInstall { get; set; } = "";
            public string ChocoInstall { get; set; } = "";
            public string AptInstall { get; set; } = "";
            public string PipInstall { get; set; } = "";
            public bool IsInstalled { get; set; }
            public string InstalledPath { get; set; } = "";

            public override string ToString() => Name;
        }

        private readonly ISettingsService _settings;
        private readonly ListBox _lstTools;

        // Detail Pane Controls
        private readonly Label _lblDetailTitle;
        private readonly Label _lblDetailStatus;
        private readonly Label _lblDetailBinary;
        private readonly TextBox _txtDescription;
        private readonly TextBox _txtOperations;
        private readonly TextBox _txtSampleCmd;
        private readonly MaterialTextBox2 _txtInstallCmd;
        private readonly MaterialButton _btnInstall;
        private readonly MaterialButton _btnCopyCmd;
        private readonly MaterialButton _btnRefresh;
        private readonly TextBox _txtLogOutput;

        private readonly List<PdfToolItem> _tools = new();
        private bool _isWindows;

        public PdfAgentConfigDialog(ISettingsService settings)
        {
            _settings = settings;
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            this.Text = "PDF Agent — Tool Mapping & Dependency Manager";
            this.Size = new Size(820, 620);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            MaterialSkinManager.Instance.AddFormToManage(this);

            var mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 70, 12, 12)
            };

            // ── Split Layout: Master (Left) & Detail (Right) ────────────────
            var splitPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            splitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
            splitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            splitPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // ──────────────── MASTER PANE (LEFT) ─────────────────────────────
            var masterBox = new GroupBox
            {
                Text = "Mapped PDF Engine Tools",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White
            };

            _lstTools = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 56,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(40, 40, 44),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            _lstTools.DrawItem += LstTools_DrawItem;
            _lstTools.SelectedIndexChanged += LstTools_SelectedIndexChanged;

            masterBox.Controls.Add(_lstTools);

            // ──────────────── DETAIL PANE (RIGHT) ────────────────────────────
            var detailBox = new GroupBox
            {
                Text = "Tool Configuration & Status",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White
            };

            var detailPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12)
            };

            _lblDetailTitle = new Label
            {
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 242, 254),
                AutoSize = true,
                Location = new Point(12, 10)
            };

            _lblDetailBinary = new Label
            {
                Font = new Font("Consolas", 9.5f, FontStyle.Italic),
                ForeColor = Color.FromArgb(180, 190, 210),
                AutoSize = true,
                Location = new Point(12, 38)
            };

            _lblDetailStatus = new Label
            {
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 60)
            };

            var lblDescTitle = new Label
            {
                Text = "Description & Capabilities:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.LightGray,
                Location = new Point(12, 90),
                AutoSize = true
            };

            _txtDescription = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(30, 30, 34),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(12, 110),
                Size = new Size(470, 48),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblOpsTitle = new Label
            {
                Text = "Mapped PDF Agent Verbs:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.LightGray,
                Location = new Point(12, 166),
                AutoSize = true
            };

            _txtOperations = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(30, 30, 34),
                ForeColor = Color.FromArgb(0, 242, 254),
                Font = new Font("Consolas", 8.5f),
                Location = new Point(12, 186),
                Size = new Size(470, 42),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblCmdTitle = new Label
            {
                Text = "Sample CLI Syntax:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.LightGray,
                Location = new Point(12, 234),
                AutoSize = true
            };

            _txtSampleCmd = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(20, 22, 28),
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(12, 254),
                Size = new Size(470, 52),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblInstallTitle = new Label
            {
                Text = "Recommended Install Command:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.LightGray,
                Location = new Point(12, 314),
                AutoSize = true
            };

            _txtInstallCmd = new MaterialTextBox2
            {
                Location = new Point(12, 334),
                Size = new Size(470, 44),
                ReadOnly = true
            };

            // Buttons Bar
            _btnInstall = new MaterialButton
            {
                Text = "⚡ Install Tool",
                Location = new Point(12, 390),
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true
            };
            _btnInstall.Click += BtnInstall_Click;

            _btnCopyCmd = new MaterialButton
            {
                Text = "📋 Copy Command",
                Location = new Point(140, 390),
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Outlined
            };
            _btnCopyCmd.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(_txtInstallCmd.Text))
                {
                    Clipboard.SetText(_txtInstallCmd.Text);
                    MessageBox.Show(this, "Install command copied to clipboard!", "PDF Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            _btnRefresh = new MaterialButton
            {
                Text = "🔄 Refresh",
                Location = new Point(310, 390),
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Text
            };
            _btnRefresh.Click += (s, e) => RefreshToolStatuses();

            // Log Output Box for live installation output
            _txtLogOutput = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(15, 15, 18),
                ForeColor = Color.Lime,
                Font = new Font("Consolas", 8f),
                Location = new Point(12, 436),
                Size = new Size(470, 75),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            detailPanel.Controls.Add(_lblDetailTitle);
            detailPanel.Controls.Add(_lblDetailBinary);
            detailPanel.Controls.Add(_lblDetailStatus);
            detailPanel.Controls.Add(lblDescTitle);
            detailPanel.Controls.Add(_txtDescription);
            detailPanel.Controls.Add(lblOpsTitle);
            detailPanel.Controls.Add(_txtOperations);
            detailPanel.Controls.Add(lblCmdTitle);
            detailPanel.Controls.Add(_txtSampleCmd);
            detailPanel.Controls.Add(lblInstallTitle);
            detailPanel.Controls.Add(_txtInstallCmd);
            detailPanel.Controls.Add(_btnInstall);
            detailPanel.Controls.Add(_btnCopyCmd);
            detailPanel.Controls.Add(_btnRefresh);
            detailPanel.Controls.Add(_txtLogOutput);

            detailBox.Controls.Add(detailPanel);

            splitPanel.Controls.Add(masterBox, 0, 0);
            splitPanel.Controls.Add(detailBox, 1, 0);

            mainContainer.Controls.Add(splitPanel);
            this.Controls.Add(mainContainer);

            PopulateTools();
            RefreshToolStatuses();

            if (_lstTools.Items.Count > 0)
                _lstTools.SelectedIndex = 0;
        }

        private void PopulateTools()
        {
            _tools.Clear();

            _tools.Add(new PdfToolItem
            {
                Id = "qpdf",
                Name = "QPDF Engine",
                Binary = "qpdf",
                Description = "Fast, structural C++ tool for content preserving transformation of PDF files.",
                MappedOperations = "Merge, Split, Extract Pages, Rotate, Encrypt, Decrypt, Linearize/Compress, Inspect Structure",
                SampleCommand = "qpdf --empty --pages file1.pdf file2.pdf -- merged.pdf\nqpdf input.pdf --rotate=90:1-z -- output.pdf",
                WingetInstall = "winget install --id QPDF.QPDF -e --accept-source-agreements --accept-package-agreements",
                ChocoInstall = "choco install qpdf -y",
                AptInstall = "sudo apt-get install -y qpdf"
            });

            _tools.Add(new PdfToolItem
            {
                Id = "pdftk",
                Name = "PDFTK Server",
                Binary = "pdftk",
                Description = "Popular utility for PDF manipulation, page bursting, merging, stamp overlays, and metadata dumps.",
                MappedOperations = "Cat/Merge, Burst to Individual Pages, Page Ranges, Watermark/Background, Dump Metadata",
                SampleCommand = "pdftk file1.pdf file2.pdf cat output merged.pdf\npdftk input.pdf burst",
                WingetInstall = "winget install --id PDFLabs.PDFTK -e --accept-source-agreements --accept-package-agreements",
                ChocoInstall = "choco install pdftk -y",
                AptInstall = "sudo apt-get install -y pdftk"
            });

            _tools.Add(new PdfToolItem
            {
                Id = "pdftotext",
                Name = "Poppler (pdftotext)",
                Binary = "pdftotext",
                Description = "High-performance text extraction utility from Poppler package. Preserves layout and enables full-text PDF search.",
                MappedOperations = "Extract Text, Extract Specific Page Range, Search Text Content via PowerShell Select-String",
                SampleCommand = "pdftotext input.pdf output.txt\npdftotext input.pdf - | Select-String 'Keyword' -Context 3",
                WingetInstall = "winget install --id oschwartz10612.Poppler -e --accept-source-agreements --accept-package-agreements",
                ChocoInstall = "choco install poppler -y",
                AptInstall = "sudo apt-get install -y poppler-utils"
            });

            _tools.Add(new PdfToolItem
            {
                Id = "pdftoppm",
                Name = "Poppler (pdftoppm)",
                Binary = "pdftoppm",
                Description = "Renders PDF pages directly to PNG, JPEG, or TIFF image files with configurable DPI resolution.",
                MappedOperations = "Convert PDF to PNG/JPEG Images, Render High-Res Page Screenshots for OCR or Vision",
                SampleCommand = "pdftoppm -png -r 300 input.pdf page_out",
                WingetInstall = "winget install --id oschwartz10612.Poppler -e --accept-source-agreements --accept-package-agreements",
                ChocoInstall = "choco install poppler -y",
                AptInstall = "sudo apt-get install -y poppler-utils"
            });

            _tools.Add(new PdfToolItem
            {
                Id = "pdfinfo",
                Name = "Poppler (pdfinfo)",
                Binary = "pdfinfo",
                Description = "Extracts document properties: Title, Author, Page Count, MediaBox size, PDF version, Encrypted state.",
                MappedOperations = "Inspect Document Metadata, Count Pages, Read Security Flags",
                SampleCommand = "pdfinfo input.pdf",
                WingetInstall = "winget install --id oschwartz10612.Poppler -e --accept-source-agreements --accept-package-agreements",
                ChocoInstall = "choco install poppler -y",
                AptInstall = "sudo apt-get install -y poppler-utils"
            });

            _tools.Add(new PdfToolItem
            {
                Id = "gs",
                Name = "Ghostscript (gs)",
                Binary = _isWindows ? "gswin64c" : "gs",
                Description = "PostScript and PDF interpreter for aggressive size compression, printing, and file conversions.",
                MappedOperations = "Compress PDF, Downsample DPI, Format Conversion, Merge PDFs",
                SampleCommand = "gs -sDEVICE=pdfwrite -dPDFSETTINGS=/prepress -sOutputFile=out.pdf in.pdf",
                WingetInstall = "winget install --id ArtifexSoftware.GhostScript -e --accept-source-agreements --accept-package-agreements",
                ChocoInstall = "choco install ghostscript -y",
                AptInstall = "sudo apt-get install -y ghostscript"
            });

            _tools.Add(new PdfToolItem
            {
                Id = "python",
                Name = "Python PDF Libraries",
                Binary = "python",
                Description = "Fallback engine using Python libraries (pdfplumber, pypdf, pymupdf, reportlab) for programmatic PDF tasks.",
                MappedOperations = "Programmatic PDF Generation, Form Filling, Table Extraction, Advanced Scraping",
                SampleCommand = "python -c \"import pdfplumber; print('PDF engine ready')\"",
                PipInstall = "pip install pdfplumber pypdf pymupdf reportlab",
                WingetInstall = "winget install --id Python.Python.3.12 -e --accept-source-agreements --accept-package-agreements",
                ChocoInstall = "choco install python -y",
                AptInstall = "sudo apt-get install -y python3 python3-pip"
            });
        }

        private void RefreshToolStatuses()
        {
            _lstTools.BeginUpdate();
            _lstTools.Items.Clear();

            foreach (var tool in _tools)
            {
                string? path = FindOnPath(tool.Binary);
                if (path == null && _isWindows && !tool.Binary.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    path = FindOnPath(tool.Binary + ".exe");
                }

                tool.IsInstalled = path != null;
                tool.InstalledPath = path ?? "";

                _lstTools.Items.Add(tool);
            }

            _lstTools.EndUpdate();

            if (_lstTools.SelectedIndex >= 0)
            {
                DisplaySelectedTool((PdfToolItem)_lstTools.SelectedItem!);
            }
        }

        private static string? FindOnPath(string binaryName)
        {
            try
            {
                var pathVar = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    var full = System.IO.Path.Combine(dir.Trim(), binaryName);
                    if (System.IO.File.Exists(full)) return full;
                }
            }
            catch
            {
                // Ignore path check errors
            }
            return null;
        }

        private void LstTools_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstTools.Items.Count) return;

            var item = (PdfToolItem)_lstTools.Items[e.Index];
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Background
            Color bg = isSelected ? Color.FromArgb(26, 43, 86) : Color.FromArgb(40, 40, 44);
            using (var b = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }

            // Status indicator pill
            Color statusColor = item.IsInstalled ? Color.FromArgb(46, 204, 113) : Color.FromArgb(231, 76, 60);
            string statusText = item.IsInstalled ? "INSTALLED" : "NOT INSTALLED";

            using (var b = new SolidBrush(statusColor))
            {
                e.Graphics.FillRectangle(b, e.Bounds.X + 8, e.Bounds.Y + 12, 8, 32);
            }

            // Title
            using (var fontTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var brushTitle = new SolidBrush(isSelected ? Color.FromArgb(0, 242, 254) : Color.White))
            {
                e.Graphics.DrawString(item.Name, fontTitle, brushTitle, e.Bounds.X + 24, e.Bounds.Y + 8);
            }

            // Binary & Status Text
            using (var fontSub = new Font("Segoe UI", 7.5f, FontStyle.Regular))
            using (var brushSub = new SolidBrush(item.IsInstalled ? Color.FromArgb(160, 220, 180) : Color.FromArgb(220, 150, 150)))
            {
                string text = $"{item.Binary} • {statusText}";
                e.Graphics.DrawString(text, fontSub, brushSub, e.Bounds.X + 24, e.Bounds.Y + 30);
            }

            // Bottom Separator
            using (var pen = new Pen(Color.FromArgb(55, 55, 60), 1))
            {
                e.Graphics.DrawLine(pen, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        private void LstTools_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_lstTools.SelectedItem is PdfToolItem item)
            {
                DisplaySelectedTool(item);
            }
        }

        private void DisplaySelectedTool(PdfToolItem item)
        {
            _lblDetailTitle.Text = item.Name;
            _lblDetailBinary.Text = $"Binary: {item.Binary}";

            if (item.IsInstalled)
            {
                _lblDetailStatus.Text = $"✓ INSTALLED ({item.InstalledPath})";
                _lblDetailStatus.ForeColor = Color.FromArgb(46, 204, 113);
                _btnInstall.Text = "✓ Re-Install Tool";
            }
            else
            {
                _lblDetailStatus.Text = "✗ NOT INSTALLED (Missing from system PATH)";
                _lblDetailStatus.ForeColor = Color.FromArgb(231, 76, 60);
                _btnInstall.Text = "⚡ Install Tool";
            }

            _txtDescription.Text = item.Description;
            _txtOperations.Text = item.MappedOperations;
            _txtSampleCmd.Text = item.SampleCommand;

            // Pick best install command
            if (!string.IsNullOrWhiteSpace(item.PipInstall) && item.Id == "python")
            {
                _txtInstallCmd.Text = item.PipInstall;
            }
            else if (_isWindows)
            {
                _txtInstallCmd.Text = !string.IsNullOrWhiteSpace(item.WingetInstall) ? item.WingetInstall : item.ChocoInstall;
            }
            else
            {
                _txtInstallCmd.Text = item.AptInstall;
            }

            _txtLogOutput.Visible = false;
            _txtLogOutput.Clear();
        }

        private async void BtnInstall_Click(object? sender, EventArgs e)
        {
            if (_lstTools.SelectedItem is not PdfToolItem tool) return;

            string cmd = _txtInstallCmd.Text.Trim();
            if (string.IsNullOrWhiteSpace(cmd))
            {
                MessageBox.Show(this, "No installation command available for this tool.", "PDF Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnInstall.Enabled = false;
            _btnRefresh.Enabled = false;
            _txtLogOutput.Visible = true;
            _txtLogOutput.AppendText($"[INSTALL LOG] Starting installation of '{tool.Name}'...\r\n");
            _txtLogOutput.AppendText($"> {cmd}\r\n\r\n");

            try
            {
                ProcessStartInfo psi;
                if (_isWindows)
                {
                    var base64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(cmd));
                    psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {base64}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }

                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (s, ev) =>
                {
                    if (ev.Data != null)
                    {
                        this.BeginInvoke((Action)(() => _txtLogOutput.AppendText(ev.Data + "\r\n")));
                    }
                };
                process.ErrorDataReceived += (s, ev) =>
                {
                    if (ev.Data != null)
                    {
                        this.BeginInvoke((Action)(() => _txtLogOutput.AppendText("[ERR] " + ev.Data + "\r\n")));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                _txtLogOutput.AppendText($"\r\n[INSTALL LOG] Process finished with exit code {process.ExitCode}.\r\n");

                RefreshToolStatuses();

                if (tool.IsInstalled)
                {
                    MessageBox.Show(this, $"Successfully installed '{tool.Name}'!", "PDF Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, $"Installation attempt finished. System status updated.", "PDF Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _txtLogOutput.AppendText($"\r\n[ERROR] Exception during installation: {ex.Message}\r\n");
                MessageBox.Show(this, $"Failed to launch installer: {ex.Message}", "PDF Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnInstall.Enabled = true;
                _btnRefresh.Enabled = true;
            }
        }
    }
}
