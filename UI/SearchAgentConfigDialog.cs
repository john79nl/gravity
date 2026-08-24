using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Gravity.Core;
using MaterialSkin.Controls;
using MaterialSkin;

namespace Gravity.UI
{
    public class SearchAgentConfigDialog : MaterialForm
    {
        private class ProviderItem
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string TypeLabel { get; set; } = "";
            public bool IsGatewaySource { get; set; }
            public string DocUrl { get; set; } = "";
            public string Description { get; set; } = "";

            public override string ToString() => Name;
        }

        private readonly ISettingsService _settings;
        private readonly ListBox _lstProviders;

        // Detail Pane Controls
        private readonly Label _lblDetailTitle;
        private readonly Label _lblDetailStatus;
        private readonly Label _lblDetailType;
        private readonly TextBox _txtDescription;
        private readonly MaterialTextBox2 _txtLangSearchKey;
        private readonly MaterialTextBox2 _txtSearxngUrl;
        private readonly MaterialTextBox2 _txtSearxngToken;
        private readonly Label _lblZeroAuthInfo;
        private readonly MaterialButton _btnSetActive;
        private readonly MaterialButton _btnOpenDocs;

        private readonly List<ProviderItem> _providers = new();

        public SearchAgentConfigDialog(ISettingsService settings)
        {
            _settings = settings;

            this.Text = "Search Agent — Gateway & Provider Manager";
            this.Size = new Size(760, 540);
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
            splitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240f));
            splitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            splitPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // ──────────────── MASTER PANE (LEFT) ─────────────────────────────
            var masterBox = new GroupBox
            {
                Text = "Search Providers & Sources",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White
            };

            _lstProviders = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 52,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(40, 40, 44),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            _lstProviders.DrawItem += LstProviders_DrawItem;
            _lstProviders.SelectedIndexChanged += LstProviders_SelectedIndexChanged;

            masterBox.Controls.Add(_lstProviders);

            // ──────────────── DETAIL PANE (RIGHT) ────────────────────────────
            var detailBox = new GroupBox
            {
                Text = "Provider Details & Configuration",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White
            };

            var detailInner = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12)
            };

            int y = 8;

            _lblDetailTitle = new Label
            {
                Location = new Point(12, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.White
            };

            _lblDetailStatus = new Label
            {
                Location = new Point(280, y + 2),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            y += 28;

            _lblDetailType = new Label
            {
                Location = new Point(12, y),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic),
                ForeColor = Color.DarkGray
            };
            y += 28;

            _txtDescription = new TextBox
            {
                Location = new Point(12, y),
                Size = new Size(440, 60),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(48, 48, 54),
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            y += 70;

            // Input Fields
            _txtLangSearchKey = new MaterialTextBox2
            {
                Location = new Point(12, y),
                Width = 440,
                Hint = "LangSearch API Key (e.g. ls_api_key_...)"
            };

            _txtSearxngUrl = new MaterialTextBox2
            {
                Location = new Point(12, y),
                Width = 440,
                Hint = "SearXNG Base URL (e.g. http://localhost:8080)"
            };

            _txtSearxngToken = new MaterialTextBox2
            {
                Location = new Point(12, y + 65),
                Width = 440,
                Hint = "SearXNG Bearer Token (optional)"
            };

            _lblZeroAuthInfo = new Label
            {
                Location = new Point(12, y + 10),
                Size = new Size(440, 40),
                Text = "⚡ Zero-Authentication Source: No API key or server setup required. Connected and managed automatically by the Search Gateway.",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.LightGreen
            };

            y += 135;

            // Buttons
            _btnSetActive = new MaterialButton
            {
                Text = "Set as Primary Web Provider",
                Location = new Point(12, y),
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Contained
            };
            _btnSetActive.Click += BtnSetActive_Click;

            _btnOpenDocs = new MaterialButton
            {
                Text = "Open Docs ↗",
                Location = new Point(320, y),
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Text
            };
            _btnOpenDocs.Click += BtnOpenDocs_Click;

            detailInner.Controls.AddRange(new Control[]
            {
                _lblDetailTitle, _lblDetailStatus, _lblDetailType,
                _txtDescription,
                _txtLangSearchKey,
                _txtSearxngUrl, _txtSearxngToken,
                _lblZeroAuthInfo,
                _btnSetActive, _btnOpenDocs
            });

            detailBox.Controls.Add(detailInner);

            splitPanel.Controls.Add(masterBox, 0, 0);
            splitPanel.Controls.Add(detailBox, 1, 0);

            // ──────────────── BOTTOM DIALOG BUTTONS ──────────────────────────
            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 45
            };

            var btnSave = new MaterialButton
            {
                Text = "Save All Settings",
                Location = new Point(575, 6),
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Contained
            };
            btnSave.Click += BtnSave_Click;

            var btnCancel = new MaterialButton
            {
                Text = "Cancel",
                Location = new Point(475, 6),
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Outlined
            };
            btnCancel.Click += (s, e) => this.Close();

            pnlBottom.Controls.Add(btnSave);
            pnlBottom.Controls.Add(btnCancel);

            mainContainer.Controls.Add(splitPanel);
            mainContainer.Controls.Add(pnlBottom);

            this.Controls.Add(mainContainer);

            InitProviders();
            LoadSettings();
        }

        private void InitProviders()
        {
            _providers.Clear();
            _providers.Add(new ProviderItem
            {
                Id = "duckduckgo",
                Name = "DuckDuckGo",
                TypeLabel = "Web Search Engine (Zero-Config)",
                IsGatewaySource = false,
                DocUrl = "https://duckduckgo.com",
                Description = "Privacy-focused public web search. Serves as the default zero-authentication fallback for general web search queries."
            });

            _providers.Add(new ProviderItem
            {
                Id = "langsearch",
                Name = "LangSearch",
                TypeLabel = "Web Search API Service",
                IsGatewaySource = false,
                DocUrl = "https://langsearch.com",
                Description = "High-performance structured web search endpoint tailored for AI search agents and LLM tool calling."
            });

            _providers.Add(new ProviderItem
            {
                Id = "searxng",
                Name = "SearXNG",
                TypeLabel = "Self-Hosted Privacy Engine",
                IsGatewaySource = false,
                DocUrl = "https://docs.searxng.org",
                Description = "Self-hosted metasearch engine. Connects Gravity to your private local or remote SearXNG instance."
            });

            _providers.Add(new ProviderItem
            {
                Id = "wikipedia",
                Name = "Wikipedia API",
                TypeLabel = "Gateway Integrated Source",
                IsGatewaySource = true,
                DocUrl = "https://www.wikipedia.org",
                Description = "MediaWiki REST API integrated directly behind Gravity's Search Gateway. Concurrently enriches search results with canonical encyclopedic summaries."
            });

            _lstProviders.Items.Clear();
            foreach (var p in _providers)
                _lstProviders.Items.Add(p);
        }

        private void LoadSettings()
        {
            var config = _settings.Current;
            _txtLangSearchKey.Text = config.LangSearchApiKey ?? "";
            _txtSearxngUrl.Text    = config.SearxngUrl ?? "";
            _txtSearxngToken.Text  = config.SearxngToken ?? "";

            // Select active provider item
            int defaultIndex = 0;
            if (config.SearchProvider == SearchProvider.LangSearch) defaultIndex = 1;
            else if (!string.IsNullOrWhiteSpace(config.SearxngUrl)) defaultIndex = 2;

            if (_lstProviders.Items.Count > defaultIndex)
                _lstProviders.SelectedIndex = defaultIndex;
        }

        private void LstProviders_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstProviders.Items.Count) return;

            var item = (ProviderItem)_lstProviders.Items[e.Index];
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            e.Graphics.FillRectangle(new SolidBrush(isSelected ? Color.FromArgb(64, 64, 76) : Color.FromArgb(40, 40, 44)), e.Bounds);

            // Item Name
            using (var nameFont = new Font("Segoe UI", 10f, FontStyle.Bold))
            {
                e.Graphics.DrawString(item.Name, nameFont, Brushes.White, e.Bounds.X + 10, e.Bounds.Y + 6);
            }

            // Status Indicator
            string statusText;
            Color statusColor;

            if (item.IsGatewaySource)
            {
                statusText = "🟢 Active Gateway";
                statusColor = Color.LightGreen;
            }
            else
            {
                bool isPrimary = IsPrimaryProvider(item.Id);
                bool isConfigured = IsConfigured(item.Id);

                if (isPrimary)
                {
                    statusText = "🟢 Primary";
                    statusColor = Color.LightGreen;
                }
                else if (isConfigured)
                {
                    statusText = "🔵 Available";
                    statusColor = Color.LightSkyBlue;
                }
                else
                {
                    statusText = "⚪ Unconfigured";
                    statusColor = Color.Gray;
                }
            }

            using (var statusFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            {
                e.Graphics.DrawString(statusText, statusFont, new SolidBrush(statusColor), e.Bounds.X + 10, e.Bounds.Y + 28);
            }

            // Bottom Separator Line
            using (var pen = new Pen(Color.FromArgb(55, 55, 60)))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            e.DrawFocusRectangle();
        }

        private bool IsPrimaryProvider(string id)
        {
            var config = _settings.Current;
            if (id == "langsearch") return config.SearchProvider == SearchProvider.LangSearch;
            if (id == "searxng") return !string.IsNullOrWhiteSpace(config.SearxngUrl);
            if (id == "duckduckgo") return config.SearchProvider == SearchProvider.DuckDuckGo && string.IsNullOrWhiteSpace(config.SearxngUrl);
            return false;
        }

        private bool IsConfigured(string id)
        {
            var config = _settings.Current;
            if (id == "duckduckgo" || id == "wikipedia") return true;
            if (id == "langsearch") return !string.IsNullOrWhiteSpace(_txtLangSearchKey.Text);
            if (id == "searxng") return !string.IsNullOrWhiteSpace(_txtSearxngUrl.Text);
            return false;
        }

        private void LstProviders_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_lstProviders.SelectedItem is not ProviderItem item) return;

            _lblDetailTitle.Text = item.Name;
            _lblDetailType.Text = item.TypeLabel;
            _txtDescription.Text = item.Description;

            if (item.IsGatewaySource)
            {
                _lblDetailStatus.Text = "[🟢 Active Gateway Source]";
                _lblDetailStatus.ForeColor = Color.LightGreen;
                _btnSetActive.Visible = false;
                _txtLangSearchKey.Visible = false;
                _txtSearxngUrl.Visible = false;
                _txtSearxngToken.Visible = false;
                _lblZeroAuthInfo.Visible = true;
            }
            else
            {
                bool isPrimary = IsPrimaryProvider(item.Id);
                _lblDetailStatus.Text = isPrimary ? "[🟢 Primary Web Engine]" : "[⚪ Secondary / Standby]";
                _lblDetailStatus.ForeColor = isPrimary ? Color.LightGreen : Color.LightGray;

                _btnSetActive.Visible = !isPrimary;
                _btnSetActive.Text = $"Set {item.Name} as Primary";

                if (item.Id == "langsearch")
                {
                    _txtLangSearchKey.Visible = true;
                    _txtSearxngUrl.Visible = false;
                    _txtSearxngToken.Visible = false;
                    _lblZeroAuthInfo.Visible = false;
                }
                else if (item.Id == "searxng")
                {
                    _txtLangSearchKey.Visible = false;
                    _txtSearxngUrl.Visible = true;
                    _txtSearxngToken.Visible = true;
                    _lblZeroAuthInfo.Visible = false;
                }
                else // DuckDuckGo
                {
                    _txtLangSearchKey.Visible = false;
                    _txtSearxngUrl.Visible = false;
                    _txtSearxngToken.Visible = false;
                    _lblZeroAuthInfo.Visible = true;
                }
            }
        }

        private void BtnSetActive_Click(object? sender, EventArgs e)
        {
            if (_lstProviders.SelectedItem is not ProviderItem item) return;

            var config = _settings.Current;
            if (item.Id == "langsearch")
            {
                config.SearchProvider = SearchProvider.LangSearch;
            }
            else if (item.Id == "searxng")
            {
                config.SearchProvider = SearchProvider.DuckDuckGo;
                if (string.IsNullOrWhiteSpace(_txtSearxngUrl.Text))
                {
                    _txtSearxngUrl.Text = "http://localhost:8080";
                }
            }
            else if (item.Id == "duckduckgo")
            {
                config.SearchProvider = SearchProvider.DuckDuckGo;
                _txtSearxngUrl.Text = "";
            }

            _lstProviders.Invalidate();
            LstProviders_SelectedIndexChanged(sender, e);
        }

        private void BtnOpenDocs_Click(object? sender, EventArgs e)
        {
            if (_lstProviders.SelectedItem is ProviderItem item && !string.IsNullOrEmpty(item.DocUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = item.DocUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var config = _settings.Current;

            if (_lstProviders.SelectedItem is ProviderItem item)
            {
                if (item.Id == "langsearch") config.SearchProvider = SearchProvider.LangSearch;
                else if (item.Id == "duckduckgo") config.SearchProvider = SearchProvider.DuckDuckGo;
            }

            config.LangSearchApiKey = _txtLangSearchKey.Text.Trim();
            var searxUrl = _txtSearxngUrl.Text.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(searxUrl) && !Uri.TryCreate(searxUrl, UriKind.Absolute, out _))
            {
                MessageBox.Show("Please enter a valid SearXNG URL (e.g. http://localhost:8080).", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            config.SearxngUrl = searxUrl;
            config.SearxngToken = _txtSearxngToken.Text.Trim();

            config.EnvironmentVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            config.EnvironmentVariables["GRAVITY_SEARXNG_URL"]   = config.SearxngUrl ?? "";
            config.EnvironmentVariables["GRAVITY_SEARXNG_TOKEN"] = config.SearxngToken ?? "";

            _settings.Save(config);

            MessageBox.Show("Search Gateway and Provider settings saved successfully.", "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close();
        }
    }
}
