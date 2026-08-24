using System;
using System.Windows.Forms;

public class InputDialog : Form
{
    private Label lblPrompt = new Label();
    private TextBox txtInput = new TextBox();
    private Button btnOk = new Button();
    private Button btnCancel = new Button();

    public InputDialog(string prompt, string defaultText = "")
    {
        this.Text = "Input";
        this.Size = new System.Drawing.Size(300, 150);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        lblPrompt.Text = prompt;
        lblPrompt.Location = new System.Drawing.Point(10, 10);
        lblPrompt.AutoSize = true;

        txtInput.Text = defaultText;
        txtInput.Location = new System.Drawing.Point(10, 40);
        txtInput.Width = 260;

        btnOk.Text = "OK";
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Location = new System.Drawing.Point(150, 70);

        btnCancel.Text = "Cancel";
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location = new System.Drawing.Point(210, 70);

        this.Controls.Add(lblPrompt);
        this.Controls.Add(txtInput);
        this.Controls.Add(btnOk);
        this.Controls.Add(btnCancel);
        this.AcceptButton = btnOk;
        this.CancelButton = btnCancel;
    }

    public string GetText() => txtInput.Text;
}