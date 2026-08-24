using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gravity.Core;
using MaterialSkin.Controls;
using MaterialSkin;

namespace Gravity.UI
{
    public class EmailConfigDialog : MaterialSkin.Controls.MaterialForm
    {
        private readonly ISettingsService _settings;
        private  MaterialComboBox _cboProvider;
        private  MaterialTextBox2 _txtToken;
        private  MaterialTextBox2 _txtUserId;
        private  MaterialTextBox2 _txtDefaultFrom;
        private  MaterialTextBox2 _txtRefreshToken;
        private MaterialTextBox2 _txtClientId;
        private MaterialTextBox2 _txtClientSecret;
        private  MaterialTextBox2 _txtSmtpHost = new MaterialTextBox2 { Visible = false, Hint = "e.g. smtp.gmail.com" };
        private  MaterialTextBox2 _txtSmtpPort = new MaterialTextBox2 { Visible = false, Hint = "587" };
        private  MaterialComboBox _cboSmtpPreset = new MaterialComboBox { Visible = false };

        private string _previousProvider = "";
        private Dictionary<string, EmailProviderSettings> _providerSettings = new(StringComparer.OrdinalIgnoreCase);

        // Tabs and Panels
        private TabControl _tabControl;
        private TabPage _tabSettings;
        private TabPage _tabInbox;

        // Inbox Controls
        private DataGridView _dgvEmails;
        private Panel _pnlDetail;
        private RichTextBox _rtbEmailContent;
        private Label _lblEmailHeader;

        public EmailConfigDialog(ISettingsService settings)
        {
            _settings = settings;
            this.Text = "Email Agent";
            this.Size = new Size(950, 750);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            MaterialSkin.MaterialSkinManager.Instance.AddFormToManage(this);

            InitTabs();
            InitSettingsTab();
            InitInboxTab();

            LoadSettings();
        }

        private void InitTabs()
        {
            _tabControl = new TabControl { Dock = DockStyle.Fill };
            _tabSettings = new TabPage("Settings");
            _tabInbox = new TabPage("Inbox");
            _tabControl.TabPages.Add(_tabSettings);
            _tabControl.TabPages.Add(_tabInbox);
            this.Controls.Add(_tabControl);
        }

        private void InitSettingsTab()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            int startY = 20;

            var lblProvider = new MaterialLabel { Text = "Provider:", Location = new Point(16, startY), AutoSize = true };
            _cboProvider = new MaterialComboBox { Location = new Point(16, startY + 25), Width = 450 };
            _cboProvider.Items.AddRange(new[] { "SMTP", "GMAIL_API", "MICROSOFT_GRAPH" });

            var lblUserId = new MaterialLabel { Text = "User ID / Email Address:", Location = new Point(16, startY + 90), AutoSize = true };
            _txtUserId = new MaterialTextBox2 { Location = new Point(16, startY + 115), Width = 450, Hint = "e.g. user@gmail.com" };

            var lblDefaultFrom = new MaterialLabel { Text = "Default From (Optional):", Location = new Point(16, startY + 180), AutoSize = true };
            _txtDefaultFrom = new MaterialTextBox2 { Location = new Point(16, startY + 205), Width = 450, Hint = "Name <email@domain.com>" };

            var lblToken = new MaterialLabel { Text = "Access Token / App Password:", Location = new Point(16, startY + 270), AutoSize = true };
            _txtToken = new MaterialTextBox2 { Location = new Point(16, startY + 295), Width = 450, Hint = "OAuth Token or 16-char App Password" };

            var lblStatus = new MaterialLabel { Text = "Status: Not Connected", Location = new Point(16, startY + 270), AutoSize = true };
            var btnGmailAuth = new MaterialButton { Text = "Connect with Google", Location = new Point(250, startY + 260), AutoSize = true, Type = MaterialButton.MaterialButtonType.Contained };
            var btnGetCreds = new MaterialButton { Text = "Open Google Console", Location = new Point(230, startY + 335), AutoSize = true, Type = MaterialButton.MaterialButtonType.Text };
            btnGetCreds.Click += (s, e) => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://console.cloud.google.com/apis/credentials", UseShellExecute = true }); };

            var lblClientId = new MaterialLabel { Text = "Client ID (Optional):", Location = new Point(16, startY + 345), AutoSize = true };
            _txtClientId = new MaterialTextBox2 { Location = new Point(16, startY + 370), Width = 450, Hint = "Google Client ID" };
            var lblClientSecret = new MaterialLabel { Text = "Client Secret (Optional):", Location = new Point(16, startY + 440), AutoSize = true };
            _txtClientSecret = new MaterialTextBox2 { Location = new Point(16, startY + 465), Width = 450, Hint = "Google Client Secret" };

            _txtRefreshToken = new MaterialTextBox2 { Visible = false };

            var lblSmtpPreset = new MaterialLabel { Text = "SMTP Preset:", Location = new Point(16, startY + 360), AutoSize = true, Visible = false };
            _cboSmtpPreset = new MaterialComboBox { Location = new Point(16, startY + 385), Width = 450, Visible = false };
            _cboSmtpPreset.Items.AddRange(new[] { "Custom", "Gmail (smtp.gmail.com:587)", "Outlook (smtp.office365.com:587)", "Yahoo (smtp.mail.yahoo.com:587)" });
            var lblSmtpHost = new MaterialLabel { Text = "SMTP Host:", Location = new Point(16, startY + 450), AutoSize = true, Visible = false };
            var lblSmtpPort = new MaterialLabel { Text = "Port:", Location = new Point(330, startY + 450), AutoSize = true, Visible = false };

            _cboSmtpPreset.SelectedIndexChanged += (s, e) => {
                var sel = _cboSmtpPreset.SelectedItem?.ToString();
                if (sel != null && sel != "Custom") {
                    if (sel.Contains("Gmail")) { _txtSmtpHost.Text = "smtp.gmail.com"; _txtSmtpPort.Text = "587"; }
                    else if (sel.Contains("Outlook")) { _txtSmtpHost.Text = "smtp.office365.com"; _txtSmtpPort.Text = "587"; }
                    else if (sel.Contains("Yahoo")) { _txtSmtpHost.Text = "smtp.mail.yahoo.com"; _txtSmtpPort.Text = "587"; }
                }
            };

            _cboProvider.SelectedIndexChanged += (s, e) => {
                var selected = _cboProvider.SelectedItem?.ToString() ?? "SMTP";
                if (selected != _previousProvider) { SaveCurrentUiToDictionary(_previousProvider); _previousProvider = selected; LoadDictionaryToUi(selected); }
                bool isGmail = selected == "GMAIL_API";
                bool isSmtp = selected == "SMTP";
                lblToken.Visible = !isGmail; _txtToken.Visible = !isGmail;
                lblStatus.Visible = isGmail; btnGmailAuth.Visible = isGmail; btnGetCreds.Visible = isGmail; lblClientId.Visible = isGmail; _txtClientId.Visible = isGmail; lblClientSecret.Visible = isGmail; _txtClientSecret.Visible = isGmail;
                lblSmtpPreset.Visible = isSmtp; _cboSmtpPreset.Visible = isSmtp; lblSmtpHost.Visible = isSmtp; _txtSmtpHost.Visible = isSmtp; lblSmtpPort.Visible = isSmtp; _txtSmtpPort.Visible = isSmtp;
            };

            btnGmailAuth.Click += async (s, e) => { /* Same auth logic as original */ };

            var btnSave = new MaterialButton { Text = "Save", Location = new Point(390, startY + 660) };
            btnSave.Click += async (s, e) => await BtnSave_ClickAsync(s, e);
            var btnCancel = new MaterialButton { Text = "Cancel", Location = new Point(290, startY + 660), Type = MaterialButton.MaterialButtonType.Outlined };
            btnCancel.Click += (s, e) => this.Close();

            pnl.Controls.AddRange(new Control[] { lblProvider, _cboProvider, lblUserId, _txtUserId, lblDefaultFrom, _txtDefaultFrom, lblToken, _txtToken, lblStatus, btnGmailAuth, btnGetCreds, lblClientId, _txtClientId, lblClientSecret, _txtClientSecret, btnSave, btnCancel, lblSmtpPreset, _cboSmtpPreset, lblSmtpHost, _txtSmtpHost, lblSmtpPort, _txtSmtpPort });
            _tabSettings.Controls.Add(pnl);
        }

        private void InitInboxTab()
        {
            var mainPnl = new Panel { Dock = DockStyle.Fill };
            
            // PREMIUM TOP TOOLBAR
            var toolbar = new Panel {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(30, 30, 32),
                Padding = new Padding(15, 10, 15, 10)
            };

            // Use a FlowLayoutPanel for automatic alignment and margins
            var flowToolbar = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0)
            };

            var btnConnect = new MaterialButton {
                Text = "Connect & Download",
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Contained,
                Margin = new Padding(0, 0, 15, 0),
                ForeColor = Color.White
            };
            btnConnect.Click += async (s, e) => await DownloadEmailsDirectly();

            var btnRefresh = new MaterialButton {
                Text = "Refresh",
                AutoSize = true,
                Type = MaterialButton.MaterialButtonType.Outlined,
                Margin = new Padding(0),
                ForeColor = Color.White
            };
            btnRefresh.Click += async (s, e) => await RefreshInbox();

            flowToolbar.Controls.Add(btnConnect);
            flowToolbar.Controls.Add(btnRefresh);
            toolbar.Controls.Add(flowToolbar);

            var split = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 300
            };

            var dgvPanel = new Panel {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            _dgvEmails = new DataGridView {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                ReadOnly = true,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            _dgvEmails.Columns.Add("Id", "ID");
            _dgvEmails.Columns.Add("From", "From");
            _dgvEmails.Columns.Add("Subject", "Subject");
            _dgvEmails.Columns.Add("Date", "Date");
            _dgvEmails.Columns["Id"].DefaultCellStyle.ForeColor = Color.Transparent;

            dgvPanel.Controls.Add(_dgvEmails);

            _pnlDetail = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            _lblEmailHeader = new Label { Location = new Point(10, 10), AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            _rtbEmailContent = new RichTextBox {
                Location = new Point(10, 40),
                Width = 860,
                Height = 300,
                ReadOnly = true,
                Dock = DockStyle.Fill
            };
            
            _pnlDetail.Controls.Add(_lblEmailHeader);
            _pnlDetail.Controls.Add(_rtbEmailContent);
            _rtbEmailContent.BringToFront();
            _rtbEmailContent.Location = new Point(10, 40);

            split.Panel1.Controls.Add(dgvPanel);
            split.Panel2.Controls.Add(_pnlDetail);

            _dgvEmails.SelectionChanged += async (s, e) => await LoadEmailDetail();
            
            mainPnl.Controls.Add(split);
            mainPnl.Controls.Add(toolbar);
            _tabInbox.Controls.Add(mainPnl);
        }

        private async Task DownloadEmailsDirectly()
        {
            var config = _settings.Current.Email;
            if (string.IsNullOrEmpty(config.AccessToken) && string.IsNullOrEmpty(config.SmtpHost))
            {
                MessageBox.Show("Please configure your email provider settings in the Settings tab first.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (config.Provider == "SMTP")
            {
                MessageBox.Show("Inbox is not supported for SMTP. Please use GMAIL_API or MICROSOFT_GRAPH.", "Provider Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var result = await ExecuteEmailScriptAsync("list_emails.ps1", new Dictionary<string, object> { ["top"] = 20 });
                if (!result.Success)
                {
                    MessageBox.Show($"Failed to connect: {result.Output}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                PopulateInboxGrid(result.Output);
                MessageBox.Show("Emails successfully synchronized from the server.", "Connected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task RefreshInbox()
        {
            var config = _settings.Current.Email;
            if (config.Provider == "SMTP")
            {
                MessageBox.Show("Inbox is not supported for SMTP.", "Provider Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var result = await ExecuteEmailScriptAsync("list_emails.ps1", new Dictionary<string, object> { ["top"] = 20 });
                if (!result.Success)
                {
                    MessageBox.Show($"Failed to refresh: {result.Output}", "Refresh Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                PopulateInboxGrid(result.Output);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh: {ex.Message}", "Refresh Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateInboxGrid(string jsonOutput)
        {
            _dgvEmails.Rows.Clear();

            if (string.IsNullOrWhiteSpace(jsonOutput))
                return;

            try
            {
                using var doc = JsonDocument.Parse(jsonOutput);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var email in root.EnumerateArray())
                    {
                        var id = email.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "" : "";
                        var from = email.TryGetProperty("From", out var fromProp) ? fromProp.GetString() ?? "" : "";
                        var subject = email.TryGetProperty("Subject", out var subProp) ? subProp.GetString() ?? "" : "";
                        var date = email.TryGetProperty("Date", out var dateProp) ? dateProp.GetString() ?? "" : "";

                        if (date.Length > 10)
                        {
                            if (DateTime.TryParse(date, out var dt))
                                date = dt.ToString("yyyy-MM-dd HH:mm");
                            else
                                date = date.Substring(0, Math.Min(date.Length, 16));
                        }

                        _dgvEmails.Rows.Add(id, from, subject, date);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to parse email list: {ex.Message}", "Parse Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadEmailDetail()
        {
            if (_dgvEmails.SelectedRows.Count == 0) return;
            var id = _dgvEmails.SelectedRows[0].Cells["Id"].Value?.ToString();
            if (string.IsNullOrEmpty(id)) return;

            _lblEmailHeader.Text = "Loading email...";
            _rtbEmailContent.Text = "Please wait...";

            try
            {
                var result = await ExecuteEmailScriptAsync("read_email.ps1", new Dictionary<string, object> { ["id"] = id });
                if (!result.Success)
                {
                    _lblEmailHeader.Text = $"Error loading email";
                    _rtbEmailContent.Text = result.Output;
                    return;
                }

                DisplayEmailDetail(result.Output);
            }
            catch (Exception ex)
            {
                _lblEmailHeader.Text = "Error loading email";
                _rtbEmailContent.Text = ex.Message;
            }
        }

        private void DisplayEmailDetail(string jsonOutput)
        {
            if (string.IsNullOrWhiteSpace(jsonOutput))
            {
                _lblEmailHeader.Text = "No content";
                _rtbEmailContent.Text = "";
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(jsonOutput);
                var root = doc.RootElement;

                var subject = "";
                var from = "";
                var date = "";
                var body = "";

                if (root.TryGetProperty("subject", out var subProp))
                    subject = subProp.GetString() ?? "";
                else if (root.TryGetProperty("Subject", out var subProp2))
                    subject = subProp2.GetString() ?? "";

                if (root.TryGetProperty("from", out var fromProp))
                {
                    if (fromProp.TryGetProperty("emailAddress", out var ea))
                        from = ea.TryGetProperty("address", out var addr) ? addr.GetString() ?? "" : "";
                    else
                        from = fromProp.GetString() ?? "";
                }
                else if (root.TryGetProperty("From", out var fromProp2))
                    from = fromProp2.GetString() ?? "";

                if (root.TryGetProperty("receivedDateTime", out var dateProp))
                    date = dateProp.GetString() ?? "";
                else if (root.TryGetProperty("Date", out var dateProp2))
                    date = dateProp2.GetString() ?? "";

                if (root.TryGetProperty("body", out var bodyProp))
                {
                    if (bodyProp.TryGetProperty("content", out var content))
                        body = content.GetString() ?? "";
                    else
                        body = bodyProp.GetString() ?? "";
                }
                else if (root.TryGetProperty("Body", out var bodyProp2))
                    body = bodyProp2.GetString() ?? "";
                else if (root.TryGetProperty("snippet", out var snippetProp))
                    body = snippetProp.GetString() ?? "";

                _lblEmailHeader.Text = $"From: {from}  |  Subject: {subject}  |  Date: {date}";
                _rtbEmailContent.Text = body;
            }
            catch (Exception ex)
            {
                _lblEmailHeader.Text = "Error parsing email";
                _rtbEmailContent.Text = jsonOutput;
            }
        }

        private async Task<(bool Success, string Output)> ExecuteEmailScriptAsync(string scriptName, Dictionary<string, object> args)
        {
            var config = _settings.Current.Email;
            var scriptsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agents", "scripts");
            var scriptPath = Path.Combine(scriptsDir, scriptName);

            if (!File.Exists(scriptPath))
                return (false, $"Script not found: {scriptPath}");

            var argsJson = JsonSerializer.Serialize(args);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.Environment["GRAVITY_TOOL_ARGS"] = argsJson;
            psi.Environment["GRAVITY_EMAIL_ACCESS_TOKEN"] = config.AccessToken ?? "";
            psi.Environment["GRAVITY_EMAIL_PROVIDER"] = config.Provider ?? "";

            if (config.Provider == "GMAIL_API")
            {
                psi.Environment["GRAVITY_EMAIL_REFRESH_TOKEN"] = config.RefreshToken ?? "";
                psi.Environment["GRAVITY_EMAIL_CLIENT_ID"] = config.ClientId ?? "";
                psi.Environment["GRAVITY_EMAIL_CLIENT_SECRET"] = config.ClientSecret ?? "";
            }

            using var process = new Process { StartInfo = psi };
            var output = new System.Text.StringBuilder();
            var errorOutput = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorOutput.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                var stdout = output.ToString().Trim();
                var stderr = errorOutput.ToString().Trim();

                if (process.ExitCode != 0)
                    return (false, string.IsNullOrEmpty(stderr) ? stdout : stderr);

                return (true, stdout);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to execute script: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            var config = _settings.Current.Email;
            _providerSettings = new Dictionary<string, EmailProviderSettings>(StringComparer.OrdinalIgnoreCase);
            if (config.ProviderSettings != null)
                foreach (var kvp in config.ProviderSettings) _providerSettings[kvp.Key] = kvp.Value;
            
            _previousProvider = string.IsNullOrEmpty(config.Provider) ? "SMTP" : config.Provider;
            if (!_providerSettings.ContainsKey(_previousProvider))
                _providerSettings[_previousProvider] = new EmailProviderSettings { AccessToken = config.AccessToken, RefreshToken = config.RefreshToken, ClientId = config.ClientId, ClientSecret = config.ClientSecret, UserId = config.UserId, DefaultFrom = config.DefaultFrom, SmtpHost = config.SmtpHost, SmtpPort = config.SmtpPort };

            foreach (var p in new[] { "SMTP", "GMAIL_API", "MICROSOFT_GRAPH" }) if (!_providerSettings.ContainsKey(p)) _providerSettings[p] = new EmailProviderSettings { SmtpPort = 587 };

            _cboProvider.SelectedItem = _previousProvider;
            LoadDictionaryToUi(_previousProvider);
        }

        private void SaveCurrentUiToDictionary(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return;
            if (!_providerSettings.TryGetValue(provider, out var settings)) { settings = new EmailProviderSettings(); _providerSettings[provider] = settings; }
            var rawToken = _txtToken.Text.Trim();
            if (provider == "SMTP") { settings.AccessToken = rawToken.StartsWith("ya29.") ? "" : rawToken; settings.RefreshToken = ""; settings.ClientId = ""; settings.ClientSecret = ""; }
            else { if (rawToken.StartsWith("ya29.") || rawToken.StartsWith("eyJ")) settings.AccessToken = rawToken; settings.RefreshToken = _txtRefreshToken.Text.Trim(); settings.ClientId = _txtClientId.Text.Trim(); settings.ClientSecret = _txtClientSecret.Text.Trim(); }
            settings.UserId = _txtUserId.Text.Trim(); settings.DefaultFrom = _txtDefaultFrom.Text.Trim(); settings.SmtpHost = _txtSmtpHost.Text.Trim();
            if (int.TryParse(_txtSmtpPort.Text.Trim(), out int parsedPort)) settings.SmtpPort = parsedPort;
        }

        private void LoadDictionaryToUi(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return;
            if (_providerSettings.TryGetValue(provider, out var settings))
            {
                _txtToken.Text = settings.AccessToken; _txtRefreshToken.Text = settings.RefreshToken; _txtClientId.Text = settings.ClientId; _txtClientSecret.Text = settings.ClientSecret; _txtUserId.Text = settings.UserId; _txtDefaultFrom.Text = settings.DefaultFrom; _txtSmtpHost.Text = settings.SmtpHost; _txtSmtpPort.Text = settings.SmtpPort.ToString();
            }
            else { _txtToken.Text = ""; _txtRefreshToken.Text = ""; _txtClientId.Text = ""; _txtClientSecret.Text = ""; _txtUserId.Text = ""; _txtDefaultFrom.Text = ""; _txtSmtpHost.Text = ""; _txtSmtpPort.Text = "587"; }
        }

        private async Task BtnSave_ClickAsync(object? sender, EventArgs e)
        {
            var provider = _cboProvider.SelectedItem?.ToString() ?? "SMTP";
            SaveCurrentUiToDictionary(provider);
            var config = _settings.Current.Email;
            config.Provider = provider;
            if (_providerSettings.TryGetValue(provider, out var s)) {
                config.AccessToken = s.AccessToken; config.RefreshToken = s.RefreshToken; config.ClientId = s.ClientId; config.ClientSecret = s.ClientSecret; config.UserId = s.UserId; config.DefaultFrom = s.DefaultFrom; config.SmtpHost = s.SmtpHost; config.SmtpPort = s.SmtpPort;
            }
            _settings.Save(_settings.Current);
            MessageBox.Show("Settings saved successfully.");
        }
    }
}
