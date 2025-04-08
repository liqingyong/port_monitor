namespace port_minitor
{
    partial class LoginForm
    {
        private TextBox txtPassword;
        private Button btnLogin;
        private Label lblPrompt;
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
            txtPassword = new TextBox();
            btnLogin = new Button();
            lblPrompt = new Label();
            SuspendLayout();
            // 
            // txtPassword
            // 
            txtPassword.Location = new Point(12, 35);
            txtPassword.Name = "txtPassword";
            txtPassword.PasswordChar = '*';
            txtPassword.Size = new Size(260, 23);
            txtPassword.TabIndex = 1;
            txtPassword.KeyDown += TxtPassword_KeyDown;
            // 
            // btnLogin
            // 
            btnLogin.Location = new Point(100, 65);
            btnLogin.Name = "btnLogin";
            btnLogin.Size = new Size(75, 23);
            btnLogin.TabIndex = 2;
            btnLogin.Text = "登录";
            btnLogin.UseVisualStyleBackColor = true;
            btnLogin.Click += BtnLogin_Click;
            // 
            // lblPrompt
            // 
            lblPrompt.AutoSize = true;
            lblPrompt.Location = new Point(12, 15);
            lblPrompt.Name = "lblPrompt";
            lblPrompt.Size = new Size(71, 17);
            lblPrompt.TabIndex = 0;
            lblPrompt.Text = "请输入密码:";
            // 
            // LoginForm
            // 
            ClientSize = new Size(284, 100);
            Controls.Add(lblPrompt);
            Controls.Add(txtPassword);
            Controls.Add(btnLogin);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "LoginForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "登录";
            FormClosing += LoginForm_FormClosing;
            Load += LoginForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            ValidatePassword();
        }

        private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ValidatePassword();
            }
        }

        private void ValidatePassword()
        {
            if (txtPassword.Text == Password)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("密码错误，请重试！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                txtPassword.Clear();
                txtPassword.Focus();
            }
        }

        private void LoginForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.DialogResult != DialogResult.OK)
            {
                e.Cancel = false;
                this.DialogResult = DialogResult.Cancel;
            }
        }

        #endregion
    }
}