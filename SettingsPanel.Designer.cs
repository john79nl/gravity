namespace Gravity
{
    partial class SettingsPanel
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.pnlHeader                = new System.Windows.Forms.Panel();
            this.lblHeaderTitle           = new System.Windows.Forms.Label();
            this.lblHeaderSubtitle        = new System.Windows.Forms.Label();

            this.tabControlSettings       = new System.Windows.Forms.TabControl();
            this.tabPageAi                = new System.Windows.Forms.TabPage();
            this.tabPageBehavior          = new System.Windows.Forms.TabPage();
            this.tabPageSystem            = new System.Windows.Forms.TabPage();

            // ── AI Provider Master-Detail Layout (PdfAgentConfigDialog style) ──
            this.splitPanelAi             = new System.Windows.Forms.TableLayoutPanel();
            this.grpProviderList          = new System.Windows.Forms.GroupBox();
            this.lstProviders             = new System.Windows.Forms.ListBox();

            this.pnlProviderDetail        = new System.Windows.Forms.Panel();
            this.lblProviderDetailTitle   = new System.Windows.Forms.Label();

            // Card 1: Endpoint & Auth
            this.grpProviderEndpoint      = new System.Windows.Forms.GroupBox();
            this.lblBaseUrl               = new System.Windows.Forms.Label();
            this.txtBaseUrl               = new System.Windows.Forms.TextBox();
            this.lblAccountId              = new System.Windows.Forms.Label();
            this.txtAccountId              = new System.Windows.Forms.TextBox();
            this.lblApiKey                = new System.Windows.Forms.Label();
            this.txtApiKey                = new System.Windows.Forms.TextBox();
            this.btnToggleApiKey          = new System.Windows.Forms.Button();
            this.lnkGetApiKey             = new System.Windows.Forms.LinkLabel();

            // Card 2: Models & Sync
            this.grpModelSelection        = new System.Windows.Forms.GroupBox();
            this.lblModel                 = new System.Windows.Forms.Label();
            this.cmbModel                 = new System.Windows.Forms.ComboBox();
            this.btnRefreshModels         = new System.Windows.Forms.Button();
            this.lblModelFetchStatus       = new System.Windows.Forms.Label();
            this.lblReasoningModel        = new System.Windows.Forms.Label();
            this.cmbReasoningModel        = new System.Windows.Forms.ComboBox();

            // Card 3: llama.cpp
            this.grpLlamaCpp              = new System.Windows.Forms.GroupBox();
            this.lblGgufPath              = new System.Windows.Forms.Label();
            this.txtGgufPath              = new System.Windows.Forms.TextBox();
            this.btnBrowseGguf            = new System.Windows.Forms.Button();
            this.lblLlamaCppExe           = new System.Windows.Forms.Label();
            this.txtLlamaCppExe           = new System.Windows.Forms.TextBox();
            this.btnBrowseLlamaCpp        = new System.Windows.Forms.Button();
            this.lblGpuLayers             = new System.Windows.Forms.Label();
            this.numGpuLayers             = new System.Windows.Forms.NumericUpDown();
            this.lblLlamaStatus           = new System.Windows.Forms.Label();
            this.btnStartServer           = new System.Windows.Forms.Button();
            this.btnStopServer            = new System.Windows.Forms.Button();

            // Behavior Tab controls
            this.grpModes                 = new System.Windows.Forms.GroupBox();
            this.lblDevMode               = new System.Windows.Forms.Label();
            this.cmbDevMode               = new System.Windows.Forms.ComboBox();
            this.lblPlanningMode          = new System.Windows.Forms.Label();
            this.cmbPlanningMode          = new System.Windows.Forms.ComboBox();

            this.grpLimits                = new System.Windows.Forms.GroupBox();
            this.lblMaxTokens             = new System.Windows.Forms.Label();
            this.numMaxTokens             = new System.Windows.Forms.NumericUpDown();
            this.lblMaxSteps              = new System.Windows.Forms.Label();
            this.numMaxSteps             = new System.Windows.Forms.NumericUpDown();
            this.lblMaxObservation        = new System.Windows.Forms.Label();
            this.numMaxObservation        = new System.Windows.Forms.NumericUpDown();

            this.grpFlags                 = new System.Windows.Forms.GroupBox();
            this.chkDebugJson             = new System.Windows.Forms.CheckBox();
            this.chkDisableNativeTools    = new System.Windows.Forms.CheckBox();
            this.chkTruncateObservations  = new System.Windows.Forms.CheckBox();
            this.chkPersistSession        = new System.Windows.Forms.CheckBox();

            // System Tab controls
            this.grpGitConfig             = new System.Windows.Forms.GroupBox();
            this.lblGitName               = new System.Windows.Forms.Label();
            this.txtGitName               = new System.Windows.Forms.TextBox();
            this.lblGitEmail              = new System.Windows.Forms.Label();
            this.txtGitEmail              = new System.Windows.Forms.TextBox();
            this.btnGitApply              = new System.Windows.Forms.Button();

            this.grpAppUpdate             = new System.Windows.Forms.GroupBox();
            this.btnUpdateApp             = new System.Windows.Forms.Button();

            // Bottom Action Bar
            this.pnlBottomBar             = new System.Windows.Forms.Panel();
            this.btnSave                  = new System.Windows.Forms.Button();
            this.btnCancel                = new System.Windows.Forms.Button();

            ((System.ComponentModel.ISupportInitialize)(this.numMaxTokens)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxSteps)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxObservation)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numGpuLayers)).BeginInit();

            this.tabControlSettings.SuspendLayout();
            this.tabPageAi.SuspendLayout();
            this.tabPageBehavior.SuspendLayout();
            this.tabPageSystem.SuspendLayout();
            this.splitPanelAi.SuspendLayout();
            this.grpProviderList.SuspendLayout();
            this.pnlProviderDetail.SuspendLayout();
            this.grpProviderEndpoint.SuspendLayout();
            this.grpModelSelection.SuspendLayout();
            this.grpLlamaCpp.SuspendLayout();
            this.grpModes.SuspendLayout();
            this.grpLimits.SuspendLayout();
            this.grpFlags.SuspendLayout();
            this.grpGitConfig.SuspendLayout();
            this.grpAppUpdate.SuspendLayout();
            this.pnlHeader.SuspendLayout();
            this.pnlBottomBar.SuspendLayout();
            this.SuspendLayout();

            // ── Top Header Panel ──────────────────────────────────────────────
            this.pnlHeader.Dock      = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Height    = 55;
            this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(18, 20, 32);
            this.pnlHeader.Padding   = new System.Windows.Forms.Padding(20, 8, 35, 5);

            this.lblHeaderTitle.Text      = "⚙ AI Engine & Provider Configuration";
            this.lblHeaderTitle.Font      = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblHeaderTitle.ForeColor = System.Drawing.Color.White;
            this.lblHeaderTitle.Location  = new System.Drawing.Point(20, 6);
            this.lblHeaderTitle.AutoSize  = true;

            this.lblHeaderSubtitle.Text      = "Manage AI providers, model parameters, authentication keys, and local server options";
            this.lblHeaderSubtitle.Font      = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular);
            this.lblHeaderSubtitle.ForeColor = System.Drawing.Color.FromArgb(150, 160, 185);
            this.lblHeaderSubtitle.Location  = new System.Drawing.Point(21, 28);
            this.lblHeaderSubtitle.AutoSize  = true;

            this.pnlHeader.Controls.Add(this.lblHeaderTitle);
            this.pnlHeader.Controls.Add(this.lblHeaderSubtitle);

            // ── Tab Control ──────────────────────────────────────────────────
            this.tabControlSettings.Dock        = System.Windows.Forms.DockStyle.Fill;
            this.tabControlSettings.DrawMode    = System.Windows.Forms.TabDrawMode.OwnerDrawFixed;
            this.tabControlSettings.ItemSize    = new System.Drawing.Size(140, 36);
            this.tabControlSettings.SizeMode    = System.Windows.Forms.TabSizeMode.Fixed;
            this.tabControlSettings.Padding     = new System.Drawing.Point(12, 3);
            this.tabControlSettings.DrawItem   += new System.Windows.Forms.DrawItemEventHandler(this.tabControlSettings_DrawItem);

            this.tabControlSettings.Controls.Add(this.tabPageAi);
            this.tabControlSettings.Controls.Add(this.tabPageBehavior);
            this.tabControlSettings.Controls.Add(this.tabPageSystem);

            // ── Tab 1: AI Providers & Models (Master-Detail Layout) ───────────
            this.tabPageAi.Text       = "🤖 AI Providers";
            this.tabPageAi.BackColor  = System.Drawing.Color.FromArgb(24, 26, 38);
            this.tabPageAi.Padding    = new System.Windows.Forms.Padding(12);

            this.splitPanelAi.Dock        = System.Windows.Forms.DockStyle.Fill;
            this.splitPanelAi.ColumnCount = 2;
            this.splitPanelAi.RowCount    = 1;
            this.splitPanelAi.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 230F));
            this.splitPanelAi.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.splitPanelAi.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));

            // Left Master Pane: Provider List
            this.grpProviderList.Dock      = System.Windows.Forms.DockStyle.Fill;
            this.grpProviderList.Text      = "AI Providers";
            this.grpProviderList.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.grpProviderList.ForeColor = System.Drawing.Color.White;
            this.grpProviderList.BackColor = System.Drawing.Color.FromArgb(30, 33, 48);
            this.grpProviderList.Padding   = new System.Windows.Forms.Padding(6, 26, 6, 6);

            this.lstProviders.Dock        = System.Windows.Forms.DockStyle.Fill;
            this.lstProviders.DrawMode    = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            this.lstProviders.ItemHeight  = 40;
            this.lstProviders.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.lstProviders.BackColor   = System.Drawing.Color.FromArgb(30, 33, 48);
            this.lstProviders.ForeColor   = System.Drawing.Color.White;
            this.lstProviders.Font        = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular);

            this.grpProviderList.Controls.Add(this.lstProviders);

            // Right Detail Pane: Selected Provider Configuration
            this.pnlProviderDetail.Dock       = System.Windows.Forms.DockStyle.Fill;
            this.pnlProviderDetail.AutoScroll = true;
            this.pnlProviderDetail.Padding    = new System.Windows.Forms.Padding(12, 0, 25, 12);
            this.pnlProviderDetail.BackColor  = System.Drawing.Color.FromArgb(24, 26, 38);

            this.lblProviderDetailTitle.Font      = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblProviderDetailTitle.ForeColor = System.Drawing.Color.FromArgb(0, 220, 255);
            this.lblProviderDetailTitle.Location  = new System.Drawing.Point(12, 4);
            this.lblProviderDetailTitle.Size      = new System.Drawing.Size(480, 28);
            this.lblProviderDetailTitle.Text      = "🤖 Provider Configuration";

            // Card 1: Provider Endpoint & Authentication
            this.grpProviderEndpoint.Location  = new System.Drawing.Point(10, 36);
            this.grpProviderEndpoint.Size      = new System.Drawing.Size(480, 175);
            this.grpProviderEndpoint.Text      = "🌐 Provider Endpoint & Authentication";
            this.grpProviderEndpoint.ForeColor = System.Drawing.Color.White;
            this.grpProviderEndpoint.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpProviderEndpoint.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);

            // API Base URL
            this.lblBaseUrl.Location = new System.Drawing.Point(12, 28);
            this.lblBaseUrl.Size     = new System.Drawing.Size(120, 20);
            this.lblBaseUrl.Text     = "API Base URL:";
            this.lblBaseUrl.Font     = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular);
            this.lblBaseUrl.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.txtBaseUrl.Location  = new System.Drawing.Point(12, 48);
            this.txtBaseUrl.Size      = new System.Drawing.Size(454, 25);
            this.txtBaseUrl.Font      = new System.Drawing.Font("Segoe UI", 9F);
            this.txtBaseUrl.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.txtBaseUrl.ForeColor = System.Drawing.Color.White;

            // Account ID
            this.lblAccountId.Location = new System.Drawing.Point(12, 80);
            this.lblAccountId.Size     = new System.Drawing.Size(250, 20);
            this.lblAccountId.Text     = "Account ID:";
            this.lblAccountId.Font     = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular);
            this.lblAccountId.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);
            this.lblAccountId.Visible  = false;

            this.txtAccountId.Location  = new System.Drawing.Point(12, 100);
            this.txtAccountId.Size      = new System.Drawing.Size(454, 25);
            this.txtAccountId.Font      = new System.Drawing.Font("Segoe UI", 9F);
            this.txtAccountId.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.txtAccountId.ForeColor = System.Drawing.Color.White;
            this.txtAccountId.Visible  = false;

            // API Key
            this.lblApiKey.Location = new System.Drawing.Point(12, 80);
            this.lblApiKey.Size     = new System.Drawing.Size(120, 20);
            this.lblApiKey.Text     = "API Key:";
            this.lblApiKey.Font     = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular);
            this.lblApiKey.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.txtApiKey.Location     = new System.Drawing.Point(12, 100);
            this.txtApiKey.Size         = new System.Drawing.Size(310, 25);
            this.txtApiKey.Font         = new System.Drawing.Font("Segoe UI", 9F);
            this.txtApiKey.PasswordChar = '*';
            this.txtApiKey.BackColor     = System.Drawing.Color.FromArgb(44, 48, 70);
            this.txtApiKey.ForeColor     = System.Drawing.Color.White;

            this.btnToggleApiKey.Location = new System.Drawing.Point(328, 99);
            this.btnToggleApiKey.Size     = new System.Drawing.Size(34, 27);
            this.btnToggleApiKey.Text     = "👁";
            this.btnToggleApiKey.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnToggleApiKey.BackColor = System.Drawing.Color.FromArgb(55, 60, 85);
            this.btnToggleApiKey.ForeColor = System.Drawing.Color.White;
            this.btnToggleApiKey.Click   += new System.EventHandler(this.btnToggleApiKey_Click);

            this.lnkGetApiKey.Location     = new System.Drawing.Point(368, 103);
            this.lnkGetApiKey.Size         = new System.Drawing.Size(98, 20);
            this.lnkGetApiKey.Text         = "Get Key →";
            this.lnkGetApiKey.TextAlign    = System.Drawing.ContentAlignment.MiddleLeft;
            this.lnkGetApiKey.LinkColor    = System.Drawing.Color.FromArgb(98, 160, 255);
            this.lnkGetApiKey.ActiveLinkColor   = System.Drawing.Color.LightSkyBlue;
            this.lnkGetApiKey.VisitedLinkColor  = System.Drawing.Color.FromArgb(98, 160, 255);
            this.lnkGetApiKey.LinkBehavior      = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.lnkGetApiKey.Click       += new System.EventHandler(this.lnkGetApiKey_Click);

            this.grpProviderEndpoint.Controls.Add(this.lblBaseUrl);
            this.grpProviderEndpoint.Controls.Add(this.txtBaseUrl);
            this.grpProviderEndpoint.Controls.Add(this.lblAccountId);
            this.grpProviderEndpoint.Controls.Add(this.txtAccountId);
            this.grpProviderEndpoint.Controls.Add(this.lblApiKey);
            this.grpProviderEndpoint.Controls.Add(this.txtApiKey);
            this.grpProviderEndpoint.Controls.Add(this.btnToggleApiKey);
            this.grpProviderEndpoint.Controls.Add(this.lnkGetApiKey);

            // Card 2: Model Selection & Sync
            this.grpModelSelection.Location  = new System.Drawing.Point(10, 222);
            this.grpModelSelection.Size      = new System.Drawing.Size(480, 165);
            this.grpModelSelection.Text      = "🎯 Model Selection & Provider Sync";
            this.grpModelSelection.ForeColor = System.Drawing.Color.White;
            this.grpModelSelection.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpModelSelection.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);

            this.lblModel.Location  = new System.Drawing.Point(12, 26);
            this.lblModel.Size      = new System.Drawing.Size(120, 20);
            this.lblModel.Text      = "Primary Model:";
            this.lblModel.Font      = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular);
            this.lblModel.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.cmbModel.Location      = new System.Drawing.Point(12, 46);
            this.cmbModel.Size          = new System.Drawing.Size(290, 25);
            this.cmbModel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown;
            this.cmbModel.Font          = new System.Drawing.Font("Segoe UI", 9F);
            this.cmbModel.BackColor     = System.Drawing.Color.FromArgb(44, 48, 70);
            this.cmbModel.ForeColor     = System.Drawing.Color.White;

            this.btnRefreshModels.Location  = new System.Drawing.Point(308, 45);
            this.btnRefreshModels.Size      = new System.Drawing.Size(158, 27);
            this.btnRefreshModels.Text      = "🔄 Fetch Live Models";
            this.btnRefreshModels.Font      = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
            this.btnRefreshModels.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefreshModels.BackColor = System.Drawing.Color.FromArgb(55, 80, 150);
            this.btnRefreshModels.ForeColor = System.Drawing.Color.White;
            this.btnRefreshModels.Click    += new System.EventHandler(this.btnRefreshModels_Click);

            this.lblModelFetchStatus.Location  = new System.Drawing.Point(12, 73);
            this.lblModelFetchStatus.Size      = new System.Drawing.Size(454, 18);
            this.lblModelFetchStatus.Text      = "Ready to sync provider models.";
            this.lblModelFetchStatus.Font      = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Italic);
            this.lblModelFetchStatus.ForeColor = System.Drawing.Color.FromArgb(160, 170, 195);

            this.lblReasoningModel.Location  = new System.Drawing.Point(12, 94);
            this.lblReasoningModel.Size      = new System.Drawing.Size(140, 20);
            this.lblReasoningModel.Text      = "Reasoning Model:";
            this.lblReasoningModel.Font      = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Regular);
            this.lblReasoningModel.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.cmbReasoningModel.Location      = new System.Drawing.Point(12, 114);
            this.cmbReasoningModel.Size          = new System.Drawing.Size(454, 25);
            this.cmbReasoningModel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown;
            this.cmbReasoningModel.Font          = new System.Drawing.Font("Segoe UI", 9F);
            this.cmbReasoningModel.BackColor     = System.Drawing.Color.FromArgb(44, 48, 70);
            this.cmbReasoningModel.ForeColor     = System.Drawing.Color.White;

            this.grpModelSelection.Controls.Add(this.lblModel);
            this.grpModelSelection.Controls.Add(this.cmbModel);
            this.grpModelSelection.Controls.Add(this.btnRefreshModels);
            this.grpModelSelection.Controls.Add(this.lblModelFetchStatus);
            this.grpModelSelection.Controls.Add(this.lblReasoningModel);
            this.grpModelSelection.Controls.Add(this.cmbReasoningModel);

            // Card 3: Local llama.cpp Server Instance
            this.grpLlamaCpp.Location  = new System.Drawing.Point(10, 398);
            this.grpLlamaCpp.Size      = new System.Drawing.Size(480, 160);
            this.grpLlamaCpp.Text      = "💻 Local llama.cpp Server Instance";
            this.grpLlamaCpp.ForeColor = System.Drawing.Color.White;
            this.grpLlamaCpp.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpLlamaCpp.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.grpLlamaCpp.Visible   = false;

            this.lblGgufPath.Location  = new System.Drawing.Point(12, 25);
            this.lblGgufPath.Size      = new System.Drawing.Size(90, 20);
            this.lblGgufPath.Text      = "GGUF Model:";
            this.lblGgufPath.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblGgufPath.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.txtGgufPath.Location  = new System.Drawing.Point(105, 23);
            this.txtGgufPath.Size      = new System.Drawing.Size(275, 23);
            this.txtGgufPath.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.txtGgufPath.ForeColor = System.Drawing.Color.White;

            this.btnBrowseGguf.Location  = new System.Drawing.Point(386, 22);
            this.btnBrowseGguf.Size      = new System.Drawing.Size(80, 25);
            this.btnBrowseGguf.Text      = "Browse…";
            this.btnBrowseGguf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseGguf.BackColor = System.Drawing.Color.FromArgb(55, 60, 85);
            this.btnBrowseGguf.ForeColor = System.Drawing.Color.White;
            this.btnBrowseGguf.Click    += new System.EventHandler(this.btnBrowseGguf_Click);

            this.lblLlamaCppExe.Location  = new System.Drawing.Point(12, 55);
            this.lblLlamaCppExe.Size      = new System.Drawing.Size(90, 20);
            this.lblLlamaCppExe.Text      = "Server EXE:";
            this.lblLlamaCppExe.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblLlamaCppExe.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.txtLlamaCppExe.Location  = new System.Drawing.Point(105, 53);
            this.txtLlamaCppExe.Size      = new System.Drawing.Size(275, 23);
            this.txtLlamaCppExe.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.txtLlamaCppExe.ForeColor = System.Drawing.Color.White;

            this.btnBrowseLlamaCpp.Location  = new System.Drawing.Point(386, 52);
            this.btnBrowseLlamaCpp.Size      = new System.Drawing.Size(80, 25);
            this.btnBrowseLlamaCpp.Text      = "Browse…";
            this.btnBrowseLlamaCpp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowseLlamaCpp.BackColor = System.Drawing.Color.FromArgb(55, 60, 85);
            this.btnBrowseLlamaCpp.ForeColor = System.Drawing.Color.White;
            this.btnBrowseLlamaCpp.Click    += new System.EventHandler(this.btnBrowseLlamaCpp_Click);

            this.lblGpuLayers.Location  = new System.Drawing.Point(12, 85);
            this.lblGpuLayers.Size      = new System.Drawing.Size(90, 20);
            this.lblGpuLayers.Text      = "GPU Layers:";
            this.lblGpuLayers.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblGpuLayers.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.numGpuLayers.Location  = new System.Drawing.Point(105, 83);
            this.numGpuLayers.Size      = new System.Drawing.Size(70, 23);
            this.numGpuLayers.Minimum   = 0;
            this.numGpuLayers.Maximum   = 128;
            this.numGpuLayers.Value     = 0;
            this.numGpuLayers.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.numGpuLayers.ForeColor = System.Drawing.Color.White;

            this.lblLlamaStatus.Location  = new System.Drawing.Point(190, 85);
            this.lblLlamaStatus.Size      = new System.Drawing.Size(180, 20);
            this.lblLlamaStatus.Text      = "⚫ Stopped";
            this.lblLlamaStatus.Font      = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
            this.lblLlamaStatus.ForeColor = System.Drawing.Color.Gray;

            this.btnStartServer.Location  = new System.Drawing.Point(12, 118);
            this.btnStartServer.Size      = new System.Drawing.Size(115, 28);
            this.btnStartServer.Text      = "▶ Start Server";
            this.btnStartServer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStartServer.BackColor = System.Drawing.Color.FromArgb(40, 130, 70);
            this.btnStartServer.ForeColor = System.Drawing.Color.White;
            this.btnStartServer.Click    += new System.EventHandler(this.btnStartServer_Click);

            this.btnStopServer.Location  = new System.Drawing.Point(135, 118);
            this.btnStopServer.Size      = new System.Drawing.Size(115, 28);
            this.btnStopServer.Text      = "⏹ Stop Server";
            this.btnStopServer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStopServer.BackColor = System.Drawing.Color.FromArgb(150, 50, 50);
            this.btnStopServer.ForeColor = System.Drawing.Color.White;
            this.btnStopServer.Click    += new System.EventHandler(this.btnStopServer_Click);

            this.grpLlamaCpp.Controls.Add(this.lblGgufPath);
            this.grpLlamaCpp.Controls.Add(this.txtGgufPath);
            this.grpLlamaCpp.Controls.Add(this.btnBrowseGguf);
            this.grpLlamaCpp.Controls.Add(this.lblLlamaCppExe);
            this.grpLlamaCpp.Controls.Add(this.txtLlamaCppExe);
            this.grpLlamaCpp.Controls.Add(this.btnBrowseLlamaCpp);
            this.grpLlamaCpp.Controls.Add(this.lblGpuLayers);
            this.grpLlamaCpp.Controls.Add(this.numGpuLayers);
            this.grpLlamaCpp.Controls.Add(this.lblLlamaStatus);
            this.grpLlamaCpp.Controls.Add(this.btnStartServer);
            this.grpLlamaCpp.Controls.Add(this.btnStopServer);

            this.pnlProviderDetail.Controls.Add(this.lblProviderDetailTitle);
            this.pnlProviderDetail.Controls.Add(this.grpProviderEndpoint);
            this.pnlProviderDetail.Controls.Add(this.grpModelSelection);
            this.pnlProviderDetail.Controls.Add(this.grpLlamaCpp);

            this.splitPanelAi.Controls.Add(this.grpProviderList, 0, 0);
            this.splitPanelAi.Controls.Add(this.pnlProviderDetail, 1, 0);

            this.tabPageAi.Controls.Add(this.splitPanelAi);

            // ── Tab 2: Agent & Behavior ───────────────────────────────────────
            this.tabPageBehavior.Text       = "⚙️ Behavior";
            this.tabPageBehavior.BackColor  = System.Drawing.Color.FromArgb(24, 26, 38);
            this.tabPageBehavior.AutoScroll = true;
            this.tabPageBehavior.Padding    = new System.Windows.Forms.Padding(16, 16, 35, 16);

            // Group: Modes
            this.grpModes.Location  = new System.Drawing.Point(16, 12);
            this.grpModes.Size      = new System.Drawing.Size(710, 115);
            this.grpModes.Text      = "Execution Modes";
            this.grpModes.ForeColor = System.Drawing.Color.White;
            this.grpModes.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpModes.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);

            this.lblDevMode.Location  = new System.Drawing.Point(12, 26);
            this.lblDevMode.Size      = new System.Drawing.Size(120, 20);
            this.lblDevMode.Text      = "Agent Mode:";
            this.lblDevMode.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblDevMode.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.cmbDevMode.Location      = new System.Drawing.Point(140, 24);
            this.cmbDevMode.Size          = new System.Drawing.Size(550, 24);
            this.cmbDevMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDevMode.Font          = new System.Drawing.Font("Segoe UI", 8.5F);
            this.cmbDevMode.BackColor     = System.Drawing.Color.FromArgb(44, 48, 70);
            this.cmbDevMode.ForeColor     = System.Drawing.Color.White;

            this.lblPlanningMode.Location  = new System.Drawing.Point(12, 62);
            this.lblPlanningMode.Size      = new System.Drawing.Size(120, 20);
            this.lblPlanningMode.Text      = "Planning:";
            this.lblPlanningMode.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblPlanningMode.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.cmbPlanningMode.Location      = new System.Drawing.Point(140, 60);
            this.cmbPlanningMode.Size          = new System.Drawing.Size(550, 24);
            this.cmbPlanningMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPlanningMode.Font          = new System.Drawing.Font("Segoe UI", 8.5F);
            this.cmbPlanningMode.BackColor     = System.Drawing.Color.FromArgb(44, 48, 70);
            this.cmbPlanningMode.ForeColor     = System.Drawing.Color.White;

            this.grpModes.Controls.Add(this.lblDevMode);
            this.grpModes.Controls.Add(this.cmbDevMode);
            this.grpModes.Controls.Add(this.lblPlanningMode);
            this.grpModes.Controls.Add(this.cmbPlanningMode);

            // Group: Limits
            this.grpLimits.Location  = new System.Drawing.Point(16, 137);
            this.grpLimits.Size      = new System.Drawing.Size(710, 130);
            this.grpLimits.Text      = "Context & Token Limits";
            this.grpLimits.ForeColor = System.Drawing.Color.White;
            this.grpLimits.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpLimits.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);

            this.lblMaxTokens.Location  = new System.Drawing.Point(12, 26);
            this.lblMaxTokens.Size      = new System.Drawing.Size(140, 20);
            this.lblMaxTokens.Text      = "Max Tokens:";
            this.lblMaxTokens.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMaxTokens.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.numMaxTokens.Location           = new System.Drawing.Point(160, 24);
            this.numMaxTokens.Size               = new System.Drawing.Size(530, 23);
            this.numMaxTokens.Minimum            = 1;
            this.numMaxTokens.Maximum            = 200000;
            this.numMaxTokens.Value              = 8192;
            this.numMaxTokens.Increment          = 512;
            this.numMaxTokens.ThousandsSeparator = true;
            this.numMaxTokens.BackColor          = System.Drawing.Color.FromArgb(44, 48, 70);
            this.numMaxTokens.ForeColor          = System.Drawing.Color.White;

            this.lblMaxSteps.Location  = new System.Drawing.Point(12, 58);
            this.lblMaxSteps.Size      = new System.Drawing.Size(140, 20);
            this.lblMaxSteps.Text      = "Max Steps (0=∞):";
            this.lblMaxSteps.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMaxSteps.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.numMaxSteps.Location  = new System.Drawing.Point(160, 56);
            this.numMaxSteps.Size      = new System.Drawing.Size(530, 23);
            this.numMaxSteps.Minimum  = 0;
            this.numMaxSteps.Maximum  = 9999;
            this.numMaxSteps.Value    = 0;
            this.numMaxSteps.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.numMaxSteps.ForeColor = System.Drawing.Color.White;

            this.lblMaxObservation.Location  = new System.Drawing.Point(12, 90);
            this.lblMaxObservation.Size      = new System.Drawing.Size(140, 20);
            this.lblMaxObservation.Text      = "Max Observation:";
            this.lblMaxObservation.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblMaxObservation.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.numMaxObservation.Location           = new System.Drawing.Point(160, 88);
            this.numMaxObservation.Size               = new System.Drawing.Size(530, 23);
            this.numMaxObservation.Minimum            = 500;
            this.numMaxObservation.Maximum            = 500000;
            this.numMaxObservation.Value              = 5000;
            this.numMaxObservation.Increment          = 500;
            this.numMaxObservation.ThousandsSeparator = true;
            this.numMaxObservation.BackColor          = System.Drawing.Color.FromArgb(44, 48, 70);
            this.numMaxObservation.ForeColor          = System.Drawing.Color.White;

            this.grpLimits.Controls.Add(this.lblMaxTokens);
            this.grpLimits.Controls.Add(this.numMaxTokens);
            this.grpLimits.Controls.Add(this.lblMaxSteps);
            this.grpLimits.Controls.Add(this.numMaxSteps);
            this.grpLimits.Controls.Add(this.lblMaxObservation);
            this.grpLimits.Controls.Add(this.numMaxObservation);

            // Group: Diagnostic & Session Flags
            this.grpFlags.Location  = new System.Drawing.Point(16, 277);
            this.grpFlags.Size      = new System.Drawing.Size(710, 160);
            this.grpFlags.Text      = "Options & Diagnostics";
            this.grpFlags.ForeColor = System.Drawing.Color.White;
            this.grpFlags.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpFlags.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);

            this.chkDebugJson.Location  = new System.Drawing.Point(12, 24);
            this.chkDebugJson.Size      = new System.Drawing.Size(680, 28);
            this.chkDebugJson.Text      = "Show Agent JSON output in chat";
            this.chkDebugJson.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.chkDebugJson.ForeColor = System.Drawing.Color.FromArgb(210, 215, 230);

            this.chkDisableNativeTools.Location  = new System.Drawing.Point(12, 56);
            this.chkDisableNativeTools.Size      = new System.Drawing.Size(680, 28);
            this.chkDisableNativeTools.Text      = "Disable native tool calls (prompt injection mode)";
            this.chkDisableNativeTools.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.chkDisableNativeTools.ForeColor = System.Drawing.Color.FromArgb(210, 215, 230);

            this.chkTruncateObservations.Location  = new System.Drawing.Point(12, 88);
            this.chkTruncateObservations.Size      = new System.Drawing.Size(680, 28);
            this.chkTruncateObservations.Text      = "Truncate long observations in history";
            this.chkTruncateObservations.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.chkTruncateObservations.ForeColor = System.Drawing.Color.FromArgb(210, 215, 230);

            this.chkPersistSession.Location  = new System.Drawing.Point(12, 120);
            this.chkPersistSession.Size      = new System.Drawing.Size(680, 28);
            this.chkPersistSession.Text      = "Remember session state between restarts";
            this.chkPersistSession.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.chkPersistSession.ForeColor = System.Drawing.Color.FromArgb(210, 215, 230);

            this.grpFlags.Controls.Add(this.chkDebugJson);
            this.grpFlags.Controls.Add(this.chkDisableNativeTools);
            this.grpFlags.Controls.Add(this.chkTruncateObservations);
            this.grpFlags.Controls.Add(this.chkPersistSession);

            this.tabPageBehavior.Controls.Add(this.grpModes);
            this.tabPageBehavior.Controls.Add(this.grpLimits);
            this.tabPageBehavior.Controls.Add(this.grpFlags);

            // ── Tab 3: System & Integrations ──────────────────────────────────
            this.tabPageSystem.Text       = "🌐 System";
            this.tabPageSystem.BackColor  = System.Drawing.Color.FromArgb(24, 26, 38);
            this.tabPageSystem.AutoScroll = true;
            this.tabPageSystem.Padding    = new System.Windows.Forms.Padding(16, 16, 35, 16);

            // Group: Git Config
            this.grpGitConfig.Location  = new System.Drawing.Point(16, 12);
            this.grpGitConfig.Size      = new System.Drawing.Size(710, 135);
            this.grpGitConfig.Text      = "Git Global Configuration";
            this.grpGitConfig.ForeColor = System.Drawing.Color.White;
            this.grpGitConfig.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpGitConfig.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);

            this.lblGitName.Location  = new System.Drawing.Point(12, 25);
            this.lblGitName.Size      = new System.Drawing.Size(120, 20);
            this.lblGitName.Text      = "Author Name:";
            this.lblGitName.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblGitName.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.txtGitName.Location  = new System.Drawing.Point(140, 23);
            this.txtGitName.Size      = new System.Drawing.Size(550, 23);
            this.txtGitName.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.txtGitName.ForeColor = System.Drawing.Color.White;

            this.lblGitEmail.Location  = new System.Drawing.Point(12, 58);
            this.lblGitEmail.Size      = new System.Drawing.Size(120, 20);
            this.lblGitEmail.Text      = "Author Email:";
            this.lblGitEmail.Font      = new System.Drawing.Font("Segoe UI", 8.5F);
            this.lblGitEmail.ForeColor = System.Drawing.Color.FromArgb(200, 205, 220);

            this.txtGitEmail.Location  = new System.Drawing.Point(140, 56);
            this.txtGitEmail.Size      = new System.Drawing.Size(550, 23);
            this.txtGitEmail.BackColor = System.Drawing.Color.FromArgb(44, 48, 70);
            this.txtGitEmail.ForeColor = System.Drawing.Color.White;

            this.btnGitApply.Location  = new System.Drawing.Point(540, 90);
            this.btnGitApply.Size      = new System.Drawing.Size(150, 30);
            this.btnGitApply.Text      = "Apply Git Config";
            this.btnGitApply.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnGitApply.BackColor = System.Drawing.Color.FromArgb(55, 60, 85);
            this.btnGitApply.ForeColor = System.Drawing.Color.White;
            this.btnGitApply.Click    += new System.EventHandler(this.btnGitApply_Click);

            this.grpGitConfig.Controls.Add(this.lblGitName);
            this.grpGitConfig.Controls.Add(this.txtGitName);
            this.grpGitConfig.Controls.Add(this.lblGitEmail);
            this.grpGitConfig.Controls.Add(this.txtGitEmail);
            this.grpGitConfig.Controls.Add(this.btnGitApply);

            // Group: App Updates
            this.grpAppUpdate.Location  = new System.Drawing.Point(16, 160);
            this.grpAppUpdate.Size      = new System.Drawing.Size(710, 80);
            this.grpAppUpdate.Text      = "Application Updates";
            this.grpAppUpdate.ForeColor = System.Drawing.Color.White;
            this.grpAppUpdate.BackColor = System.Drawing.Color.FromArgb(32, 35, 52);
            this.grpAppUpdate.Font      = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);

            this.btnUpdateApp.Location  = new System.Drawing.Point(12, 28);
            this.btnUpdateApp.Size      = new System.Drawing.Size(680, 35);
            this.btnUpdateApp.Text      = "🔄 Check for Application Updates";
            this.btnUpdateApp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnUpdateApp.BackColor = System.Drawing.Color.FromArgb(45, 60, 100);
            this.btnUpdateApp.ForeColor = System.Drawing.Color.White;
            this.btnUpdateApp.Click    += new System.EventHandler(this.btnUpdateApp_Click);

            this.grpAppUpdate.Controls.Add(this.btnUpdateApp);

            this.tabPageSystem.Controls.Add(this.grpGitConfig);
            this.tabPageSystem.Controls.Add(this.grpAppUpdate);

            // ── Bottom Action Bar ─────────────────────────────────────────────
            this.pnlBottomBar.Dock      = System.Windows.Forms.DockStyle.Bottom;
            this.pnlBottomBar.Height    = 55;
            this.pnlBottomBar.BackColor = System.Drawing.Color.FromArgb(18, 20, 32);
            this.pnlBottomBar.Padding   = new System.Windows.Forms.Padding(20, 10, 35, 10);

            this.btnSave.Location  = new System.Drawing.Point(500, 11);
            this.btnSave.Size      = new System.Drawing.Size(130, 34);
            this.btnSave.Text      = "💾 Save Settings";
            this.btnSave.Font      = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.BackColor = System.Drawing.Color.FromArgb(40, 130, 70);
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Click    += new System.EventHandler(this.btnSave_Click);

            this.btnCancel.Location  = new System.Drawing.Point(640, 11);
            this.btnCancel.Size      = new System.Drawing.Size(90, 34);
            this.btnCancel.Text      = "Cancel";
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.BackColor = System.Drawing.Color.FromArgb(60, 65, 85);
            this.btnCancel.ForeColor = System.Drawing.Color.White;
            this.btnCancel.Click    += new System.EventHandler(this.btnCancel_Click);

            this.pnlBottomBar.Controls.Add(this.btnSave);
            this.pnlBottomBar.Controls.Add(this.btnCancel);

            // ── Root UserControl Settings ─────────────────────────────────────
            this.Dock            = System.Windows.Forms.DockStyle.Fill;
            this.Size            = new System.Drawing.Size(820, 680);
            this.BackColor       = System.Drawing.Color.FromArgb(18, 20, 32);
            this.Font            = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);

            this.Controls.Add(this.tabControlSettings);
            this.Controls.Add(this.pnlHeader);
            this.Controls.Add(this.pnlBottomBar);

            ((System.ComponentModel.ISupportInitialize)(this.numMaxTokens)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxSteps)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxObservation)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numGpuLayers)).EndInit();

            this.tabControlSettings.ResumeLayout(false);
            this.tabPageAi.ResumeLayout(false);
            this.tabPageBehavior.ResumeLayout(false);
            this.tabPageSystem.ResumeLayout(false);
            this.splitPanelAi.ResumeLayout(false);
            this.grpProviderList.ResumeLayout(false);
            this.pnlProviderDetail.ResumeLayout(false);
            this.grpProviderEndpoint.ResumeLayout(false);
            this.grpProviderEndpoint.PerformLayout();
            this.grpModelSelection.ResumeLayout(false);
            this.grpModelSelection.PerformLayout();
            this.grpLlamaCpp.ResumeLayout(false);
            this.grpLlamaCpp.PerformLayout();
            this.grpModes.ResumeLayout(false);
            this.grpLimits.ResumeLayout(false);
            this.grpFlags.ResumeLayout(false);
            this.grpGitConfig.ResumeLayout(false);
            this.grpGitConfig.PerformLayout();
            this.grpAppUpdate.ResumeLayout(false);
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.pnlBottomBar.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        // ── Controls Declarations ─────────────────────────────────────────────
        private System.Windows.Forms.Panel         pnlHeader;
        private System.Windows.Forms.Label         lblHeaderTitle;
        private System.Windows.Forms.Label         lblHeaderSubtitle;

        private System.Windows.Forms.TabControl    tabControlSettings;
        private System.Windows.Forms.TabPage        tabPageAi;
        private System.Windows.Forms.TabPage        tabPageBehavior;
        private System.Windows.Forms.TabPage        tabPageSystem;

        private System.Windows.Forms.TableLayoutPanel splitPanelAi;
        private System.Windows.Forms.GroupBox      grpProviderList;
        private System.Windows.Forms.ListBox       lstProviders;

        private System.Windows.Forms.Panel         pnlProviderDetail;
        private System.Windows.Forms.Label         lblProviderDetailTitle;

        private System.Windows.Forms.GroupBox      grpProviderEndpoint;
        private System.Windows.Forms.Label         lblBaseUrl;
        private System.Windows.Forms.TextBox       txtBaseUrl;
        private System.Windows.Forms.Label         lblAccountId;
        private System.Windows.Forms.TextBox       txtAccountId;
        private System.Windows.Forms.Label         lblApiKey;
        private System.Windows.Forms.TextBox       txtApiKey;
        private System.Windows.Forms.Button        btnToggleApiKey;
        private System.Windows.Forms.LinkLabel     lnkGetApiKey;

        private System.Windows.Forms.GroupBox      grpModelSelection;
        private System.Windows.Forms.Label         lblModel;
        private System.Windows.Forms.ComboBox      cmbModel;
        private System.Windows.Forms.Button        btnRefreshModels;
        private System.Windows.Forms.Label         lblModelFetchStatus;
        private System.Windows.Forms.Label         lblReasoningModel;
        private System.Windows.Forms.ComboBox      cmbReasoningModel;

        private System.Windows.Forms.GroupBox      grpLlamaCpp;
        private System.Windows.Forms.Label         lblGgufPath;
        private System.Windows.Forms.TextBox       txtGgufPath;
        private System.Windows.Forms.Button        btnBrowseGguf;
        private System.Windows.Forms.Label         lblLlamaCppExe;
        private System.Windows.Forms.TextBox       txtLlamaCppExe;
        private System.Windows.Forms.Button        btnBrowseLlamaCpp;
        private System.Windows.Forms.Label         lblGpuLayers;
        private System.Windows.Forms.NumericUpDown numGpuLayers;
        private System.Windows.Forms.Label         lblLlamaStatus;
        private System.Windows.Forms.Button        btnStartServer;
        private System.Windows.Forms.Button        btnStopServer;

        private System.Windows.Forms.GroupBox      grpModes;
        private System.Windows.Forms.Label         lblDevMode;
        private System.Windows.Forms.ComboBox      cmbDevMode;
        private System.Windows.Forms.Label         lblPlanningMode;
        private System.Windows.Forms.ComboBox      cmbPlanningMode;

        private System.Windows.Forms.GroupBox      grpLimits;
        private System.Windows.Forms.Label         lblMaxTokens;
        private System.Windows.Forms.NumericUpDown numMaxTokens;
        private System.Windows.Forms.Label         lblMaxSteps;
        private System.Windows.Forms.NumericUpDown numMaxSteps;
        private System.Windows.Forms.Label         lblMaxObservation;
        private System.Windows.Forms.NumericUpDown numMaxObservation;

        private System.Windows.Forms.GroupBox      grpFlags;
        private System.Windows.Forms.CheckBox      chkDebugJson;
        private System.Windows.Forms.CheckBox      chkDisableNativeTools;
        private System.Windows.Forms.CheckBox      chkTruncateObservations;
        private System.Windows.Forms.CheckBox      chkPersistSession;

        private System.Windows.Forms.GroupBox      grpGitConfig;
        private System.Windows.Forms.Label         lblGitName;
        private System.Windows.Forms.TextBox       txtGitName;
        private System.Windows.Forms.Label         lblGitEmail;
        private System.Windows.Forms.TextBox       txtGitEmail;
        private System.Windows.Forms.Button        btnGitApply;

        private System.Windows.Forms.GroupBox      grpAppUpdate;
        private System.Windows.Forms.Button        btnUpdateApp;

        private System.Windows.Forms.Panel         pnlBottomBar;
        private System.Windows.Forms.Button        btnSave;
        private System.Windows.Forms.Button        btnCancel;
    }
}
