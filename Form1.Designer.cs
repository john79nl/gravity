namespace Gravity
{
    partial class Form1
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

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.ribbonBar = new System.Windows.Forms.Panel();
            this.ribbonFlowLayout = new System.Windows.Forms.FlowLayoutPanel();
            this.btnRibbonFolder = new System.Windows.Forms.Button();
            this.btnRibbonRun = new System.Windows.Forms.Button();
            this.btnRibbonSettings = new System.Windows.Forms.Button();
            this.btnRibbonHelp = new System.Windows.Forms.Button();
            this.btnRibbonSearchAgent = new System.Windows.Forms.Button();

            this.activityBar = new System.Windows.Forms.Panel();
            this.btnNavSessions = new System.Windows.Forms.Button();
            this.btnNavClose = new System.Windows.Forms.Button();
            this.btnNavTheme = new System.Windows.Forms.Button();
            this.btnNavSettings = new System.Windows.Forms.Button();
            this.btnNavExplorer = new System.Windows.Forms.Button();

            this.statusBar = new System.Windows.Forms.Panel();
            this.statusLabelLeft = new System.Windows.Forms.Label();
            this.statusLabelRight = new System.Windows.Forms.Label();

            this.breadcrumbBar = new System.Windows.Forms.Panel();
            this.breadcrumbLabel = new System.Windows.Forms.Label();

            this.mainSplitContainer = new System.Windows.Forms.SplitContainer();
            this.explorerPanel = new System.Windows.Forms.Panel();
            this.fileTreeView = new Gravity.UI.MultiSelectTreeView();
            this.explorerLabel = new System.Windows.Forms.Label();
            this.sessionPanel = new System.Windows.Forms.Panel();
            this.btnNewSession = new System.Windows.Forms.Button();
            this.sessionList = new System.Windows.Forms.FlowLayoutPanel();
            this.sessionLabel = new System.Windows.Forms.Label();
            this.btnSaveFile = new System.Windows.Forms.Button();

            this.contentSplitContainer = new System.Windows.Forms.SplitContainer();
            this.inputPanel = new System.Windows.Forms.Panel();
            this.attachImageButton = new System.Windows.Forms.Button();
            this.inputBox = new System.Windows.Forms.TextBox();
            this.sendButton = new System.Windows.Forms.Button();
            this.stopButton = new System.Windows.Forms.Button();

            this.tabStrip = new System.Windows.Forms.ToolStrip();
            this.mainDocumentArea = new System.Windows.Forms.Panel();

            this.diagnosticsList = new System.Windows.Forms.ListView();
            this._chatFlow = new Gravity.UI.DoubleBufferedFlowLayoutPanel();
            this.shellConsoleBox = new System.Windows.Forms.RichTextBox();
            this._spinner = new Gravity.UI.LoadingSpinner();

            this.explorerPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).BeginInit();
            this.mainSplitContainer.Panel1.SuspendLayout();
            this.mainSplitContainer.Panel2.SuspendLayout();
            this.mainSplitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.contentSplitContainer)).BeginInit();
            this.contentSplitContainer.Panel1.SuspendLayout();
            this.contentSplitContainer.Panel2.SuspendLayout();
            this.contentSplitContainer.SuspendLayout();
            this.inputPanel.SuspendLayout();
            this.SuspendLayout();

            // ribbonBar
            this.ribbonBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.ribbonBar.Height = 64;
            this.ribbonBar.Padding = new System.Windows.Forms.Padding(16, 8, 16, 8);
            this.ribbonBar.BackColor = System.Drawing.Color.FromArgb(8, 12, 40);
            this.ribbonBar.Controls.Add(this.ribbonFlowLayout);
            this.ribbonBar.Name = "ribbonBar";
            this.ribbonBar.TabIndex = 0;

            // ribbonFlowLayout
            this.ribbonFlowLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ribbonFlowLayout.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            this.ribbonFlowLayout.WrapContents = false;
            this.ribbonFlowLayout.BackColor = System.Drawing.Color.Transparent;
            this.ribbonFlowLayout.Margin = new System.Windows.Forms.Padding(0);
            this.ribbonFlowLayout.Controls.Add(this.btnRibbonFolder);
            this.ribbonFlowLayout.Controls.Add(this.btnRibbonRun);
            this.ribbonFlowLayout.Controls.Add(this.btnRibbonSettings);
            this.ribbonFlowLayout.Controls.Add(this.btnRibbonHelp);
            this.ribbonFlowLayout.Controls.Add(this.btnRibbonSearchAgent);
            this.ribbonFlowLayout.Controls.Add(this._spinner);

            // btnRibbonFolder
            this.btnRibbonFolder.Text = "📁  Open Folder";
            this.btnRibbonFolder.Font = new System.Drawing.Font("Segoe UI", 13F);
            this.btnRibbonFolder.ForeColor = System.Drawing.Color.FromArgb(190, 210, 255);
            this.btnRibbonFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRibbonFolder.FlatAppearance.BorderSize = 0;
            this.btnRibbonFolder.AutoSize = true;
            this.btnRibbonFolder.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnRibbonFolder.Height = 48;
            this.btnRibbonFolder.MinimumSize = new System.Drawing.Size(0, 48);
            this.btnRibbonFolder.Margin = new System.Windows.Forms.Padding(0, 0, 12, 0);
            this.btnRibbonFolder.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRibbonFolder.Click += new System.EventHandler(this.toolStripOpen_Click);

            // btnRibbonRun
            this.btnRibbonRun.Text = "▶  Run";
            this.btnRibbonRun.Font = new System.Drawing.Font("Segoe UI", 13F, System.Drawing.FontStyle.Bold);
            this.btnRibbonRun.ForeColor = System.Drawing.Color.FromArgb(100, 230, 140);
            this.btnRibbonRun.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRibbonRun.FlatAppearance.BorderSize = 0;
            this.btnRibbonRun.AutoSize = true;
            this.btnRibbonRun.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnRibbonRun.Height = 48;
            this.btnRibbonRun.MinimumSize = new System.Drawing.Size(0, 48);
            this.btnRibbonRun.Margin = new System.Windows.Forms.Padding(0, 0, 12, 0);
            this.btnRibbonRun.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRibbonRun.Click += new System.EventHandler(this.toolStripRun_Click);

            // btnRibbonSettings
            this.btnRibbonSettings.Text = "⚙  Settings";
            this.btnRibbonSettings.Font = new System.Drawing.Font("Segoe UI", 13F);
            this.btnRibbonSettings.ForeColor = System.Drawing.Color.FromArgb(190, 210, 255);
            this.btnRibbonSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRibbonSettings.FlatAppearance.BorderSize = 0;
            this.btnRibbonSettings.AutoSize = true;
            this.btnRibbonSettings.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnRibbonSettings.Height = 48;
            this.btnRibbonSettings.MinimumSize = new System.Drawing.Size(0, 48);
            this.btnRibbonSettings.Margin = new System.Windows.Forms.Padding(0, 0, 0, 0);
            this.btnRibbonSettings.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRibbonSettings.Click += new System.EventHandler(this.toolStripSettings_Click);

            // btnRibbonHelp
            this.btnRibbonHelp.Text = "❓  Help";
            this.btnRibbonHelp.Font = new System.Drawing.Font("Segoe UI", 13F);
            this.btnRibbonHelp.ForeColor = System.Drawing.Color.FromArgb(245, 200, 50);
            this.btnRibbonHelp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRibbonHelp.FlatAppearance.BorderSize = 0;
            this.btnRibbonHelp.AutoSize = true;
            this.btnRibbonHelp.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnRibbonHelp.Height = 48;
            this.btnRibbonHelp.MinimumSize = new System.Drawing.Size(0, 48);
            this.btnRibbonHelp.Margin = new System.Windows.Forms.Padding(0, 0, 0, 0);
            this.btnRibbonHelp.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRibbonHelp.Click += new System.EventHandler(this.btnRibbonHelp_Click);

            // btnRibbonSearchAgent
            this.btnRibbonSearchAgent.Text = "🔍  Search Agent";
            this.btnRibbonSearchAgent.Font = new System.Drawing.Font("Segoe UI", 13F);
            this.btnRibbonSearchAgent.ForeColor = System.Drawing.Color.FromArgb(120, 210, 255);
            this.btnRibbonSearchAgent.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRibbonSearchAgent.FlatAppearance.BorderSize = 0;
            this.btnRibbonSearchAgent.AutoSize = true;
            this.btnRibbonSearchAgent.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.btnRibbonSearchAgent.Height = 48;
            this.btnRibbonSearchAgent.MinimumSize = new System.Drawing.Size(0, 48);
            this.btnRibbonSearchAgent.Margin = new System.Windows.Forms.Padding(12, 0, 0, 0);
            this.btnRibbonSearchAgent.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRibbonSearchAgent.Name = "btnRibbonSearchAgent";
            this.btnRibbonSearchAgent.Click += new System.EventHandler(this.btnRibbonSearchAgent_Click);

            // statusBar (bottom)
            this.statusBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.statusBar.Height = 32;
            this.statusBar.Controls.Add(this.statusLabelRight);
            this.statusBar.Controls.Add(this.statusLabelLeft);

            this._spinner.Margin = new System.Windows.Forms.Padding(30, 0, 0, 0);
            this._spinner.Size = new System.Drawing.Size(80, 48);

            this.statusLabelLeft.Dock = System.Windows.Forms.DockStyle.Left;
            this.statusLabelLeft.Width = 350;
            this.statusLabelLeft.Text = "  Ln 1, Col 1";
            this.statusLabelLeft.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.statusLabelLeft.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.statusLabelRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.statusLabelRight.Width = 350;
            this.statusLabelRight.Text = "Spaces: 4  UTF-8  C#  ";
            this.statusLabelRight.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.statusLabelRight.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            // breadcrumbBar (below tabs)
            this.breadcrumbBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.breadcrumbBar.Height = 30;
            this.breadcrumbBar.Controls.Add(this.breadcrumbLabel);
            this.breadcrumbBar.Controls.Add(this.btnSaveFile);

            this.btnSaveFile.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnSaveFile.Width = 100;
            this.btnSaveFile.Text = "💾 Save";
            this.btnSaveFile.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnSaveFile.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSaveFile.FlatAppearance.BorderSize = 0;
            this.btnSaveFile.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSaveFile.Visible = false;
            this.btnSaveFile.Click += new System.EventHandler(this.btnSaveFile_Click);

            this.breadcrumbLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.breadcrumbLabel.Text = "";
            this.breadcrumbLabel.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.breadcrumbLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.breadcrumbLabel.Padding = new System.Windows.Forms.Padding(12, 0, 0, 0);

            // activityBar
            this.activityBar.Dock = System.Windows.Forms.DockStyle.Left;
            this.activityBar.Width = 80;
            this.activityBar.Padding = new System.Windows.Forms.Padding(0, 14, 0, 14);
            this.activityBar.Controls.Add(this.btnNavSettings);
            this.activityBar.Controls.Add(this.btnNavTheme);
            this.activityBar.Controls.Add(this.btnNavSessions);
            this.activityBar.Controls.Add(this.btnNavClose);

            this.activityBar.Controls.Add(this.btnNavExplorer);

            this.btnNavExplorer.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNavExplorer.Height = 80;
            this.btnNavExplorer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavExplorer.FlatAppearance.BorderSize = 0;
            this.btnNavExplorer.Text = "";
            this.btnNavExplorer.Click += new System.EventHandler(this.btnNavExplorer_Click);

            this.btnNavSessions.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNavSessions.Height = 80;
            this.btnNavSessions.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavSessions.FlatAppearance.BorderSize = 0;
            this.btnNavSessions.Text = "";
            this.btnNavSessions.Click += new System.EventHandler(this.btnNavSessions_Click);

            this.btnNavClose.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.btnNavClose.Height = 80;
            this.btnNavClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavClose.FlatAppearance.BorderSize = 0;
            this.btnNavClose.Text = "";
            this.btnNavClose.Click += new System.EventHandler(this.btnNavClose_Click);

            this.btnNavTheme.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.btnNavTheme.Height = 80;
            this.btnNavTheme.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavTheme.FlatAppearance.BorderSize = 0;
            this.btnNavTheme.Text = "";
            this.btnNavTheme.Click += new System.EventHandler(this.toolStripTheme_Click);

            this.btnNavSettings.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.btnNavSettings.Height = 80;
            this.btnNavSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNavSettings.FlatAppearance.BorderSize = 0;
            this.btnNavSettings.Text = "";
            this.btnNavSettings.Click += new System.EventHandler(this.toolStripSettings_Click);

            // mainSplitContainer
            this.mainSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainSplitContainer.Location = new System.Drawing.Point(0, 31);
            this.mainSplitContainer.Name = "mainSplitContainer";
            this.mainSplitContainer.SplitterDistance = 390;
            this.mainSplitContainer.TabIndex = 1;
            this.mainSplitContainer.Panel1Collapsed = false;

            // explorerPanel -> mainSplitContainer.Panel1
            this.mainSplitContainer.Panel1.Controls.Add(this.explorerPanel);
            this.mainSplitContainer.Panel1.Controls.Add(this.sessionPanel);
            this.explorerPanel.Controls.Add(this.fileTreeView);
            this.explorerPanel.Controls.Add(this.explorerLabel);
            this.explorerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.explorerPanel.Padding = new System.Windows.Forms.Padding(5);

            // sessionPanel
            this.sessionPanel.Controls.Add(this.sessionList);
            this.sessionPanel.Controls.Add(this.btnNewSession);
            this.sessionPanel.Controls.Add(this.sessionLabel);
            this.sessionPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.sessionPanel.Padding = new System.Windows.Forms.Padding(5);
            this.sessionPanel.Visible = false;

            this.sessionLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.sessionLabel.Height = 40;
            this.sessionLabel.Text = "   SESSIONS";
            this.sessionLabel.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.sessionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.btnNewSession.Dock = System.Windows.Forms.DockStyle.Top;
            this.btnNewSession.Height = 35;
            this.btnNewSession.Text = "➕ New Session";
            this.btnNewSession.Font = new System.Drawing.Font("Segoe UI", 11F);
            this.btnNewSession.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnNewSession.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnNewSession.Click += new System.EventHandler(this.btnNewSession_Click);

            this.sessionList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.sessionList.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.sessionList.AutoScroll = true;
            this.sessionList.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.sessionList.WrapContents = false;

            this.explorerLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.explorerLabel.Height = 40;
            this.explorerLabel.Text = "   SOLUTION EXPLORER";
            this.explorerLabel.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.explorerLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.fileTreeView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.fileTreeView.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.fileTreeView.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.fileTreeView.NodeMouseDoubleClick += new System.Windows.Forms.TreeNodeMouseClickEventHandler(this.fileTreeView_NodeMouseDoubleClick);

            // contentSplitContainer -> mainSplitContainer.Panel2
            this.mainSplitContainer.Panel2.Controls.Add(this.contentSplitContainer);
            this.contentSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentSplitContainer.SplitterDistance = 750;
            this.contentSplitContainer.Panel2Collapsed = true;
            this.contentSplitContainer.Panel2.Controls.Add(this.diagnosticsList);

            // contentSplitContainer.Panel1 components
            this.contentSplitContainer.Panel1.Controls.Add(this.breadcrumbBar);
            this.contentSplitContainer.Panel1.Controls.Add(this.inputPanel);
            this.contentSplitContainer.Panel1.Controls.Add(this.mainDocumentArea);
            this.contentSplitContainer.Panel1.Controls.Add(this.tabStrip);

            // inputPanel
            this.inputPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.inputPanel.Height = 90;
            this.inputPanel.Padding = new System.Windows.Forms.Padding(16, 14, 16, 14);
            this.inputPanel.Controls.Add(this.inputBox);
            this.inputPanel.Controls.Add(this.stopButton);
            this.inputPanel.Controls.Add(this.sendButton);
            this.inputPanel.Controls.Add(this.attachImageButton);

            this.inputBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.inputBox.Font = new System.Drawing.Font("Segoe UI", 14F);
            this.inputBox.Multiline = true;
            this.inputBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.inputBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.inputBox_KeyDown);

            this.attachImageButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.attachImageButton.Width = 44;
            this.attachImageButton.Text = "+";
            this.attachImageButton.Font = new System.Drawing.Font("Segoe UI", 15F, System.Drawing.FontStyle.Bold);
            this.attachImageButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.attachImageButton.FlatAppearance.BorderSize = 0;
            this.attachImageButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.attachImageButton.Click += new System.EventHandler(this.attachImageButton_Click);

            this.sendButton.Dock = System.Windows.Forms.DockStyle.Right;
            this.sendButton.Width = 110;
            this.sendButton.Text = "  Send  ▶";
            this.sendButton.Font = new System.Drawing.Font("Segoe UI", 13F, System.Drawing.FontStyle.Bold);
            this.sendButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.sendButton.FlatAppearance.BorderSize = 0;
            this.sendButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.sendButton.Click += new System.EventHandler(this.sendButton_Click);

            this.stopButton.Dock = System.Windows.Forms.DockStyle.Right;
            this.stopButton.Width = 100;
            this.stopButton.Text = "  Stop  ◼";
            this.stopButton.Font = new System.Drawing.Font("Segoe UI", 13F);
            this.stopButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.stopButton.FlatAppearance.BorderSize = 0;
            this.stopButton.Cursor = System.Windows.Forms.Cursors.Hand;
            this.stopButton.Click += new System.EventHandler(this.stopButton_Click);

            // diagnosticsList
            this.diagnosticsList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.diagnosticsList.View = System.Windows.Forms.View.Details;
            this.diagnosticsList.FullRowSelect = true;
            this.diagnosticsList.Columns.Add("File", 150);
            this.diagnosticsList.Columns.Add("Line", 50);
            this.diagnosticsList.Columns.Add("Message", 400);

            // tabStrip (Custom Tab Headers)
            this.tabStrip.Dock = System.Windows.Forms.DockStyle.Top;
            this.tabStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.tabStrip.Padding = new System.Windows.Forms.Padding(0);
            this.tabStrip.BackColor = System.Drawing.Color.FromArgb(5, 7, 28);
            
            // mainDocumentArea (Replaces TabControl Client Area)
            this.mainDocumentArea.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainDocumentArea.Padding = new System.Windows.Forms.Padding(0);


            this.shellConsoleBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.shellConsoleBox.ReadOnly = true;
            this.shellConsoleBox.BackColor = System.Drawing.Color.Black;
            this.shellConsoleBox.ForeColor = System.Drawing.Color.White;
            this.shellConsoleBox.Font = new System.Drawing.Font("Consolas", 10F);
            this.shellConsoleBox.BorderStyle = System.Windows.Forms.BorderStyle.None;

            // Form1
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1400, 900);
            this.Controls.Add(this.mainSplitContainer);
            this.Controls.Add(this.activityBar);
            this.Controls.Add(this.statusBar);
            this.Controls.Add(this.ribbonBar);
            this.Name = "Form1";
            this.Text = "Gravity";
            this.mainSplitContainer.Panel1.ResumeLayout(false);
            this.mainSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).EndInit();
            this.mainSplitContainer.ResumeLayout(false);
            this.explorerPanel.ResumeLayout(false);
            this.contentSplitContainer.Panel1.ResumeLayout(false);
            this.contentSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.contentSplitContainer)).EndInit();
            this.contentSplitContainer.ResumeLayout(false);
            this.inputPanel.ResumeLayout(false);
            this.inputPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void btnRibbonSearchAgent_Click(object sender, EventArgs e)
        {
            if (_form5 == null || _form5.IsDisposed)
            {
                _form5 = new Form5(_agentService, _orchestrator, (Gravity.Core.ProjectContext)_projectContext);
                _form5.FormClosed += (_, __) => _form5 = null;
            }
            _form5.Show();
            _form5.BringToFront();
        }

        #endregion

        private System.Windows.Forms.Panel ribbonBar;
        private System.Windows.Forms.FlowLayoutPanel ribbonFlowLayout;
        private System.Windows.Forms.Button btnRibbonFolder;
        private System.Windows.Forms.Button btnRibbonRun;
        private System.Windows.Forms.Button btnRibbonSettings;
        private System.Windows.Forms.Button btnRibbonHelp;

        private System.Windows.Forms.Panel activityBar;
        private System.Windows.Forms.Button btnNavExplorer;
        private System.Windows.Forms.Button btnNavSessions;
        private System.Windows.Forms.Button btnNavClose;
        private System.Windows.Forms.Button btnNavTheme;
        private System.Windows.Forms.Button btnNavSettings;

        private System.Windows.Forms.Panel statusBar;
        private System.Windows.Forms.Label statusLabelLeft;
        private System.Windows.Forms.Label statusLabelRight;

        private System.Windows.Forms.Panel breadcrumbBar;
        private System.Windows.Forms.Label breadcrumbLabel;

        private System.Windows.Forms.SplitContainer mainSplitContainer;
        private System.Windows.Forms.Panel explorerPanel;
        private System.Windows.Forms.Label explorerLabel;
        private Gravity.UI.MultiSelectTreeView fileTreeView;

        private System.Windows.Forms.Panel sessionPanel;
        private System.Windows.Forms.Label sessionLabel;
        private System.Windows.Forms.Button btnNewSession;
        private System.Windows.Forms.FlowLayoutPanel sessionList;
        private System.Windows.Forms.Button btnSaveFile;

        private System.Windows.Forms.SplitContainer contentSplitContainer;
        private System.Windows.Forms.ToolStrip tabStrip;
        private System.Windows.Forms.Panel mainDocumentArea;
        private System.Windows.Forms.Panel inputPanel;
        private System.Windows.Forms.Button attachImageButton;
        private System.Windows.Forms.TextBox inputBox;
        private System.Windows.Forms.Button sendButton;
        private System.Windows.Forms.Button stopButton;

        private System.Windows.Forms.ListView diagnosticsList;
        private Gravity.UI.DoubleBufferedFlowLayoutPanel _chatFlow;
        private System.Windows.Forms.RichTextBox shellConsoleBox;
        private Gravity.UI.LoadingSpinner _spinner;
        private System.Windows.Forms.Button btnRibbonSearchAgent;
        private Form5? _form5;
    }
}
