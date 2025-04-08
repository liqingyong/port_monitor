using System.Windows.Forms;

namespace port_minitor
{
    partial class Console
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            ListViewItem listViewItem1 = new ListViewItem("文件目录");
            ListViewItem listViewItem2 = new ListViewItem("文件名");
            ListViewItem listViewItem3 = new ListViewItem("端口号");
            ListViewItem listViewItem4 = new ListViewItem("是否在线");
            ListViewItem listViewItem5 = new ListViewItem("扫描倒计时");
            ListViewItem listViewItem6 = new ListViewItem("操作");
            label1 = new Label();
            textBox1 = new TextBox();
            button1 = new Button();
            label2 = new Label();
            numericUpDown1 = new NumericUpDown();
            button2 = new Button();
            label3 = new Label();
            numericUpDown2 = new NumericUpDown();
            listView1 = new ListView();
            checkBoxStartup = new CheckBox();
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown2).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(34, 35);
            label1.Name = "label1";
            label1.Size = new Size(44, 17);
            label1.TabIndex = 0;
            label1.Text = "地址：";
            // 
            // textBox1
            // 
            textBox1.Location = new Point(34, 65);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(353, 23);
            textBox1.TabIndex = 1;
            // 
            // button1
            // 
            button1.Location = new Point(407, 65);
            button1.Name = "button1";
            button1.Size = new Size(75, 23);
            button1.TabIndex = 2;
            button1.Text = "选择文件";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(526, 38);
            label2.Name = "label2";
            label2.Size = new Size(56, 17);
            label2.TabIndex = 3;
            label2.Text = "端口号：";
            
            // 
            // numericUpDown1
            // 
            numericUpDown1.Location = new Point(526, 65);
            numericUpDown1.Name = "numericUpDown1";
            numericUpDown1.Size = new Size(120, 23);
            numericUpDown1.TabIndex = 4;
            // 
            // button2
            // 
            button2.Location = new Point(679, 66);
            button2.Name = "button2";
            button2.Size = new Size(75, 23);
            button2.TabIndex = 5;
            button2.Text = "增加服务";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(816, 32);
            label3.Name = "label3";
            label3.Size = new Size(104, 17);
            label3.TabIndex = 7;
            label3.Text = "扫描间隔（分钟）";
            // 
            // numericUpDown2
            // 
            numericUpDown2.Location = new Point(816, 66);
            numericUpDown2.Name = "numericUpDown2";
            numericUpDown2.Size = new Size(120, 23);
            numericUpDown2.TabIndex = 8;
            numericUpDown2.Value = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDown2.ValueChanged += numericUpDown2_ValueChanged;
            // 
            // listView1
            // 
            listView1.Items.AddRange(new ListViewItem[] { listViewItem1, listViewItem2, listViewItem3, listViewItem4, listViewItem5, listViewItem6 });
            listView1.Location = new Point(34, 120);
            listView1.Name = "listView1";
            listView1.Size = new Size(1013, 306);
            listView1.TabIndex = 9;
            listView1.UseCompatibleStateImageBehavior = false;
            listView1.SelectedIndexChanged += listView1_SelectedIndexChanged;
            // 
            // checkBoxStartup
            // 
            checkBoxStartup.AutoSize = true;
            checkBoxStartup.Location = new Point(972, 65);
            checkBoxStartup.Name = "checkBoxStartup";
            checkBoxStartup.Size = new Size(75, 21);
            checkBoxStartup.TabIndex = 10;
            checkBoxStartup.Text = "开机自启";
            checkBoxStartup.UseVisualStyleBackColor = true;
            checkBoxStartup.CheckedChanged += checkBoxStartup_CheckedChanged;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1084, 450);
            Controls.Add(checkBoxStartup);
            Controls.Add(listView1);
            Controls.Add(numericUpDown2);
            Controls.Add(label3);
            Controls.Add(button2);
            Controls.Add(numericUpDown1);
            Controls.Add(label2);
            Controls.Add(button1);
            Controls.Add(textBox1);
            Controls.Add(label1);
            MaximizeBox = false;
            Name = "Form1";
            Text = "服务监控-V1.0";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)numericUpDown1).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown2).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox textBox1;
        private Button button1;
        private Label label2;
        private NumericUpDown numericUpDown1;
        private Button button2;
        private Label label3;
        private NumericUpDown numericUpDown2;
        private ListView listView1;
        private CheckBox checkBoxStartup;
    }
}
