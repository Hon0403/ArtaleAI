namespace ArtaleAI
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            checkBox1 = new CheckBox();
            panel1 = new Panel();
            panel2 = new Panel();
            splitter1 = new Splitter();
            textBox1 = new TextBox();
            tabControl1 = new TabControl();
            tabPage1 = new TabPage();
            tabPage2 = new TabPage();
            panel4 = new Panel();
            groupBox4 = new GroupBox();
            checkBox2 = new CheckBox();
            label2 = new Label();
            label1 = new Label();
            hScrollBar2 = new HScrollBar();
            hScrollBar1 = new HScrollBar();
            groupBox3 = new GroupBox();
            radioButton5 = new RadioButton();
            radioButton4 = new RadioButton();
            radioButton3 = new RadioButton();
            radioButton2 = new RadioButton();
            radioButton1 = new RadioButton();
            groupBox2 = new GroupBox();
            pictureBoxZoom = new PictureBox();
            labelZoomFactor = new Label();
            numericUpDownZoom = new NumericUpDown();
            groupBox1 = new GroupBox();
            btn_create = new Button();
            btn_save = new Button();
            comboBox1 = new ComboBox();
            panel3 = new Panel();
            pictureBoxMinimap = new PictureBox();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            tabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
            panel4.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxZoom).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownZoom).BeginInit();
            groupBox1.SuspendLayout();
            panel3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxMinimap).BeginInit();
            SuspendLayout();
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(19, 16);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(86, 19);
            checkBox1.TabIndex = 0;
            checkBox1.Text = "checkBox1";
            checkBox1.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            panel1.Controls.Add(checkBox1);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new Point(3, 3);
            panel1.Name = "panel1";
            panel1.Size = new Size(238, 554);
            panel1.TabIndex = 2;
            // 
            // panel2
            // 
            panel2.Controls.Add(splitter1);
            panel2.Controls.Add(textBox1);
            panel2.Dock = DockStyle.Fill;
            panel2.Location = new Point(241, 3);
            panel2.Name = "panel2";
            panel2.Size = new Size(555, 554);
            panel2.TabIndex = 3;
            // 
            // splitter1
            // 
            splitter1.Location = new Point(0, 0);
            splitter1.Name = "splitter1";
            splitter1.Size = new Size(3, 554);
            splitter1.TabIndex = 0;
            splitter1.TabStop = false;
            // 
            // textBox1
            // 
            textBox1.Dock = DockStyle.Fill;
            textBox1.Location = new Point(0, 0);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.ScrollBars = ScrollBars.Both;
            textBox1.Size = new Size(555, 554);
            textBox1.TabIndex = 1;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabPage1);
            tabControl1.Controls.Add(tabPage2);
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Location = new Point(0, 0);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(807, 588);
            tabControl1.TabIndex = 2;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(panel2);
            tabPage1.Controls.Add(panel1);
            tabPage1.Location = new Point(4, 24);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(799, 560);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "tabPage1";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(panel4);
            tabPage2.Controls.Add(panel3);
            tabPage2.Location = new Point(4, 24);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(799, 560);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "路徑編輯";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // panel4
            // 
            panel4.Controls.Add(groupBox4);
            panel4.Controls.Add(groupBox3);
            panel4.Controls.Add(groupBox2);
            panel4.Controls.Add(groupBox1);
            panel4.Dock = DockStyle.Right;
            panel4.Location = new Point(598, 3);
            panel4.Name = "panel4";
            panel4.Size = new Size(198, 554);
            panel4.TabIndex = 2;
            // 
            // groupBox4
            // 
            groupBox4.AutoSize = true;
            groupBox4.Controls.Add(checkBox2);
            groupBox4.Controls.Add(label2);
            groupBox4.Controls.Add(label1);
            groupBox4.Controls.Add(hScrollBar2);
            groupBox4.Controls.Add(hScrollBar1);
            groupBox4.Dock = DockStyle.Bottom;
            groupBox4.Location = new Point(0, 227);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(198, 106);
            groupBox4.TabIndex = 4;
            groupBox4.TabStop = false;
            groupBox4.Text = "編輯工具";
            // 
            // checkBox2
            // 
            checkBox2.AutoSize = true;
            checkBox2.Location = new Point(64, 65);
            checkBox2.Name = "checkBox2";
            checkBox2.Size = new Size(74, 19);
            checkBox2.TabIndex = 4;
            checkBox2.Text = "顯示網格";
            checkBox2.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(4, 45);
            label2.Name = "label2";
            label2.Size = new Size(58, 15);
            label2.TabIndex = 3;
            label2.Text = "網格大小:";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(3, 21);
            label1.Name = "label1";
            label1.Size = new Size(58, 15);
            label1.TabIndex = 2;
            label1.Text = "筆刷大小:";
            // 
            // hScrollBar2
            // 
            hScrollBar2.Location = new Point(65, 45);
            hScrollBar2.Name = "hScrollBar2";
            hScrollBar2.Size = new Size(121, 17);
            hScrollBar2.TabIndex = 1;
            // 
            // hScrollBar1
            // 
            hScrollBar1.Location = new Point(64, 21);
            hScrollBar1.Name = "hScrollBar1";
            hScrollBar1.Size = new Size(121, 17);
            hScrollBar1.TabIndex = 0;
            // 
            // groupBox3
            // 
            groupBox3.AutoSize = true;
            groupBox3.Controls.Add(radioButton5);
            groupBox3.Controls.Add(radioButton4);
            groupBox3.Controls.Add(radioButton3);
            groupBox3.Controls.Add(radioButton2);
            groupBox3.Controls.Add(radioButton1);
            groupBox3.Dock = DockStyle.Bottom;
            groupBox3.Location = new Point(0, 333);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(198, 113);
            groupBox3.TabIndex = 3;
            groupBox3.TabStop = false;
            groupBox3.Text = "標記編輯模式";
            // 
            // radioButton5
            // 
            radioButton5.AutoSize = true;
            radioButton5.Location = new Point(119, 47);
            radioButton5.Name = "radioButton5";
            radioButton5.Size = new Size(73, 19);
            radioButton5.TabIndex = 4;
            radioButton5.TabStop = true;
            radioButton5.Text = "刪除標記";
            radioButton5.UseVisualStyleBackColor = true;
            // 
            // radioButton4
            // 
            radioButton4.AutoSize = true;
            radioButton4.Location = new Point(119, 22);
            radioButton4.Name = "radioButton4";
            radioButton4.Size = new Size(73, 19);
            radioButton4.TabIndex = 3;
            radioButton4.TabStop = true;
            radioButton4.Text = "繩索路徑";
            radioButton4.UseVisualStyleBackColor = true;
            // 
            // radioButton3
            // 
            radioButton3.AutoSize = true;
            radioButton3.Location = new Point(10, 72);
            radioButton3.Name = "radioButton3";
            radioButton3.Size = new Size(73, 19);
            radioButton3.TabIndex = 2;
            radioButton3.TabStop = true;
            radioButton3.Text = "不可進入";
            radioButton3.UseVisualStyleBackColor = true;
            // 
            // radioButton2
            // 
            radioButton2.AutoSize = true;
            radioButton2.Location = new Point(10, 47);
            radioButton2.Name = "radioButton2";
            radioButton2.Size = new Size(88, 19);
            radioButton2.TabIndex = 1;
            radioButton2.TabStop = true;
            radioButton2.Text = "可行走區域 ";
            radioButton2.UseVisualStyleBackColor = true;
            // 
            // radioButton1
            // 
            radioButton1.AutoSize = true;
            radioButton1.Location = new Point(8, 22);
            radioButton1.Name = "radioButton1";
            radioButton1.Size = new Size(73, 19);
            radioButton1.TabIndex = 0;
            radioButton1.TabStop = true;
            radioButton1.Text = "路線標記";
            radioButton1.UseVisualStyleBackColor = true;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(pictureBoxZoom);
            groupBox2.Controls.Add(labelZoomFactor);
            groupBox2.Controls.Add(numericUpDownZoom);
            groupBox2.Location = new Point(1, 3);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(200, 224);
            groupBox2.TabIndex = 2;
            groupBox2.TabStop = false;
            groupBox2.Text = "放大鏡";
            // 
            // pictureBoxZoom
            // 
            pictureBoxZoom.BorderStyle = BorderStyle.FixedSingle;
            pictureBoxZoom.Dock = DockStyle.Top;
            pictureBoxZoom.Location = new Point(3, 19);
            pictureBoxZoom.Name = "pictureBoxZoom";
            pictureBoxZoom.Size = new Size(194, 170);
            pictureBoxZoom.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBoxZoom.TabIndex = 1;
            pictureBoxZoom.TabStop = false;
            pictureBoxZoom.Paint += pictureBoxZoom_Paint;
            // 
            // labelZoomFactor
            // 
            labelZoomFactor.Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 136);
            labelZoomFactor.Location = new Point(3, 197);
            labelZoomFactor.Name = "labelZoomFactor";
            labelZoomFactor.Size = new Size(58, 15);
            labelZoomFactor.TabIndex = 0;
            labelZoomFactor.Text = "放大倍率:";
            // 
            // numericUpDownZoom
            // 
            numericUpDownZoom.Location = new Point(67, 195);
            numericUpDownZoom.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
            numericUpDownZoom.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDownZoom.Name = "numericUpDownZoom";
            numericUpDownZoom.Size = new Size(130, 23);
            numericUpDownZoom.TabIndex = 1;
            numericUpDownZoom.Value = new decimal(new int[] { 5, 0, 0, 0 });
            // 
            // groupBox1
            // 
            groupBox1.AutoSize = true;
            groupBox1.Controls.Add(btn_create);
            groupBox1.Controls.Add(btn_save);
            groupBox1.Controls.Add(comboBox1);
            groupBox1.Dock = DockStyle.Bottom;
            groupBox1.Location = new Point(0, 446);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(198, 108);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "路徑檔管理";
            // 
            // btn_create
            // 
            btn_create.AutoSize = true;
            btn_create.Location = new Point(107, 61);
            btn_create.Name = "btn_create";
            btn_create.Size = new Size(75, 25);
            btn_create.TabIndex = 2;
            btn_create.Text = "新建";
            btn_create.UseVisualStyleBackColor = true;
            // 
            // btn_save
            // 
            btn_save.AutoSize = true;
            btn_save.Location = new Point(6, 61);
            btn_save.Name = "btn_save";
            btn_save.Size = new Size(75, 25);
            btn_save.TabIndex = 1;
            btn_save.Text = "保存";
            btn_save.UseVisualStyleBackColor = true;
            // 
            // comboBox1
            // 
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(25, 32);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(141, 23);
            comboBox1.TabIndex = 0;
            comboBox1.Text = "路徑檔";
            // 
            // panel3
            // 
            panel3.Controls.Add(pictureBoxMinimap);
            panel3.Dock = DockStyle.Left;
            panel3.Location = new Point(3, 3);
            panel3.Name = "panel3";
            panel3.Size = new Size(589, 554);
            panel3.TabIndex = 1;
            // 
            // pictureBoxMinimap
            // 
            pictureBoxMinimap.Dock = DockStyle.Fill;
            pictureBoxMinimap.Location = new Point(0, 0);
            pictureBoxMinimap.Name = "pictureBoxMinimap";
            pictureBoxMinimap.Size = new Size(589, 554);
            pictureBoxMinimap.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxMinimap.TabIndex = 0;
            pictureBoxMinimap.TabStop = false;
            pictureBoxMinimap.MouseClick += pictureBoxMinimap_MouseClick;
            pictureBoxMinimap.MouseLeave += pictureBoxMinimap_MouseLeave;
            pictureBoxMinimap.MouseMove += pictureBoxMinimap_MouseMove;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(807, 588);
            Controls.Add(tabControl1);
            Name = "Form1";
            Text = "Form1";
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            tabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage2.ResumeLayout(false);
            panel4.ResumeLayout(false);
            panel4.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBoxZoom).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDownZoom).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            panel3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBoxMinimap).EndInit();
            ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.CheckBox checkBox1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Splitter splitter1;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.PictureBox pictureBoxMinimap;
        private System.Windows.Forms.Panel panel4;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.PictureBox pictureBoxZoom;
        private System.Windows.Forms.NumericUpDown numericUpDownZoom;
        private Label labelZoomFactor;
        private GroupBox groupBox2;
        private GroupBox groupBox3;
        private RadioButton radioButton5;
        private RadioButton radioButton4;
        private RadioButton radioButton3;
        private RadioButton radioButton2;
        private RadioButton radioButton1;
        private Button btn_create;
        private Button btn_save;
        private GroupBox groupBox4;
        private HScrollBar hScrollBar1;
        private CheckBox checkBox2;
        private Label label2;
        private Label label1;
        private HScrollBar hScrollBar2;
    }
}
