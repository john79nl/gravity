namespace Gravity.UI
{
    partial class Form4
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.inputBox = new Gravity.UI.RoundedTextBox();
            this.animationTimer = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            // 
            // inputBox
            // 
            this.inputBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.inputBox.BackColor = System.Drawing.Color.Transparent;
            this.inputBox.BorderColor = System.Drawing.Color.FromArgb(70, 190, 145, 0);
            this.inputBox.BorderFocusColor = System.Drawing.Color.FromArgb(120, 210, 165, 0);
            this.inputBox.BorderThickness = 1;
            this.inputBox.CornerRadius = 20;
            this.inputBox.FillColor = System.Drawing.Color.FromArgb(12, 22, 58);
            this.inputBox.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.inputBox.ForeColor = System.Drawing.Color.White;
            this.inputBox.Location = new System.Drawing.Point(50, 500);
            this.inputBox.Multiline = false;
            this.inputBox.Name = "inputBox";
            this.inputBox.Size = new System.Drawing.Size(700, 40);
            this.inputBox.TabIndex = 0;
            this.inputBox.Text = "Ask the brain...";
            this.inputBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.InputBox_KeyDown);
            this.inputBox.Enter += new System.EventHandler(this.InputBox_Enter);
            this.inputBox.Leave += new System.EventHandler(this.InputBox_Leave);
            // 
            // animationTimer
            // 
            this.animationTimer.Enabled = true;
            this.animationTimer.Interval = 16; // ~60fps
            this.animationTimer.Tick += new System.EventHandler(this.AnimationTimer_Tick);
            // 
            // Form4
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(10, 10, 25);
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.Controls.Add(this.inputBox);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "Form4";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Cognitive Integration Router";
            this.Load += new System.EventHandler(this.Form4_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Gravity.UI.RoundedTextBox inputBox;
        private System.Windows.Forms.Timer animationTimer;
    }
}
