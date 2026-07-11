namespace ArtaleAI
{
    partial class MainForm
    {
        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            panel1 = new Panel();
            cbo_LoadPathFile = new ComboBox();
            ckB_Start = new CheckBox();
            cbo_DetectMode = new ComboBox();
            btn_DownloadMonster = new Button();
            groupBox5 = new GroupBox();
            cbo_MonsterTemplates = new ComboBox();
            groupBox6 = new GroupBox();
            label4 = new Label();
            label5 = new Label();
            groupBox7 = new GroupBox();
            textBox3 = new TextBox();
            textBox2 = new TextBox();
            checkBox2 = new CheckBox();
            label3 = new Label();
            checkBox1 = new CheckBox();
            label2 = new Label();
            groupBox8 = new GroupBox();
            groupBox9 = new GroupBox();
            panel2 = new Panel();
            splitter1 = new Splitter();
            textBox1 = new TextBox();
            tabControl1 = new TabControl();
            tabPage1 = new TabPage();
            tabPage2 = new TabPage();
            panel3 = new Panel();
            pictureBoxMinimap = new PictureBox();
            panel4 = new Panel();
            panel4BottomHost = new Panel();
            lbl_MapStatus = new Label();
            splitSidebar = new SplitContainer();
            panelToolsScroll = new Panel();
            flowToolsStack = new FlowLayoutPanel();
            groupBox_Layers = new GroupBox();
            tableLayoutPanel_Layers = new TableLayoutPanel();
            chk_LayerPlatforms = new CheckBox();
            chk_LayerRopes = new CheckBox();
            chk_LayerJumpLinks = new CheckBox();
            chk_LayerManualAnchors = new CheckBox();
            chk_LayerNodes = new CheckBox();
            chk_LayerEdges = new CheckBox();
            chk_LayerValidation = new CheckBox();
            groupBox_PropertyPanel = new GroupBox();
            chk_AdvancedMode = new CheckBox();
            lbl_MouseCoords = new Label();
            groupBox_Action = new GroupBox();
            cbo_ActionType = new ComboBox();
            lbl_Action = new Label();
            groupBox3 = new GroupBox();
            rdo_TwoPointLink = new RadioButton();
            rdo_DeleteMarker = new RadioButton();
            rdo_JumpLinkMarker = new RadioButton();
            rdo_RopeMarker = new RadioButton();
            rdo_PathMarker = new RadioButton();
            rdo_SelectMode = new RadioButton();
            groupBox1 = new GroupBox();
            btn_New = new Button();
            btn_SaveMap = new Button();
            cbo_MapFiles = new ComboBox();
            tabPage3 = new TabPage();
            pictureBoxLiveView = new PictureBox();
            panel1.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox7.SuspendLayout();
            panel2.SuspendLayout();
            tabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
            panel3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxMinimap).BeginInit();
            panel4.SuspendLayout();
            panel4BottomHost.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitSidebar).BeginInit();
            splitSidebar.Panel1.SuspendLayout();
            splitSidebar.Panel2.SuspendLayout();
            panelToolsScroll.SuspendLayout();
            flowToolsStack.SuspendLayout();
            groupBox_Layers.SuspendLayout();
            tableLayoutPanel_Layers.SuspendLayout();
            groupBox_PropertyPanel.SuspendLayout();
            groupBox_Action.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox1.SuspendLayout();
            tabPage3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBoxLiveView).BeginInit();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.Controls.Add(cbo_LoadPathFile);
            panel1.Controls.Add(ckB_Start);
            panel1.Controls.Add(cbo_DetectMode);
            panel1.Controls.Add(btn_DownloadMonster);
            panel1.Controls.Add(groupBox5);
            panel1.Controls.Add(groupBox6);
            panel1.Controls.Add(groupBox7);
            panel1.Controls.Add(groupBox8);
            panel1.Controls.Add(groupBox9);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new Point(3, 3);
            panel1.Name = "panel1";
            panel1.Size = new Size(238, 575);
            panel1.TabIndex = 2;
            // 
            // cbo_LoadPathFile
            // 
            cbo_LoadPathFile.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo_LoadPathFile.FormattingEnabled = true;
            cbo_LoadPathFile.Location = new Point(5, 146);
            cbo_LoadPathFile.Name = "cbo_LoadPathFile";
            cbo_LoadPathFile.Size = new Size(121, 23);
            cbo_LoadPathFile.TabIndex = 7;
            cbo_LoadPathFile.SelectedIndexChanged += cbo_LoadPathFile_SelectedIndexChanged;
            // 
            // ckB_Start
            // 
            ckB_Start.AutoSize = true;
            ckB_Start.Location = new Point(3, 59);
            ckB_Start.Name = "ckB_Start";
            ckB_Start.Size = new Size(74, 19);
            ckB_Start.TabIndex = 6;
            ckB_Start.Text = "自動打怪";
            ckB_Start.UseVisualStyleBackColor = true;
            ckB_Start.CheckedChanged += ckB_Start_CheckedChanged;
            // 
            // cbo_DetectMode
            // 
            cbo_DetectMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo_DetectMode.FormattingEnabled = true;
            cbo_DetectMode.Location = new Point(5, 101);
            cbo_DetectMode.Name = "cbo_DetectMode";
            cbo_DetectMode.Size = new Size(193, 23);
            cbo_DetectMode.TabIndex = 5;
            // 
            // btn_DownloadMonster
            // 
            btn_DownloadMonster.Location = new Point(143, 59);
            btn_DownloadMonster.Name = "btn_DownloadMonster";
            btn_DownloadMonster.Size = new Size(75, 23);
            btn_DownloadMonster.TabIndex = 4;
            btn_DownloadMonster.Text = "下載怪物";
            btn_DownloadMonster.UseVisualStyleBackColor = true;
            btn_DownloadMonster.Click += btn_DownloadMonster_Click;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(cbo_MonsterTemplates);
            groupBox5.Dock = DockStyle.Top;
            groupBox5.Location = new Point(0, 0);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(238, 53);
            groupBox5.TabIndex = 1;
            groupBox5.TabStop = false;
            groupBox5.Text = "怪物模板";
            // 
            // cbo_MonsterTemplates
            // 
            cbo_MonsterTemplates.Dock = DockStyle.Fill;
            cbo_MonsterTemplates.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo_MonsterTemplates.FormattingEnabled = true;
            cbo_MonsterTemplates.Location = new Point(3, 19);
            cbo_MonsterTemplates.Name = "cbo_MonsterTemplates";
            cbo_MonsterTemplates.Size = new Size(232, 23);
            cbo_MonsterTemplates.TabIndex = 0;
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(label4);
            groupBox6.Controls.Add(label5);
            groupBox6.Dock = DockStyle.Bottom;
            groupBox6.Location = new Point(0, 189);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(238, 100);
            groupBox6.TabIndex = 0;
            groupBox6.TabStop = false;
            groupBox6.Text = "角色資訊";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(43, 70);
            label4.Name = "label4";
            label4.Size = new Size(29, 15);
            label4.TabIndex = 3;
            label4.Text = "MP:";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(43, 29);
            label5.Name = "label5";
            label5.Size = new Size(35, 15);
            label5.TabIndex = 2;
            label5.Text = "HP：";
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(textBox3);
            groupBox7.Controls.Add(textBox2);
            groupBox7.Controls.Add(checkBox2);
            groupBox7.Controls.Add(label3);
            groupBox7.Controls.Add(checkBox1);
            groupBox7.Controls.Add(label2);
            groupBox7.Dock = DockStyle.Bottom;
            groupBox7.Location = new Point(0, 289);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(238, 97);
            groupBox7.TabIndex = 0;
            groupBox7.TabStop = false;
            groupBox7.Text = "自動喝水";
            // 
            // textBox3
            // 
            textBox3.Location = new Point(155, 68);
            textBox3.Name = "textBox3";
            textBox3.Size = new Size(54, 23);
            textBox3.TabIndex = 3;
            // 
            // textBox2
            // 
            textBox2.Location = new Point(161, 31);
            textBox2.Name = "textBox2";
            textBox2.Size = new Size(37, 23);
            textBox2.TabIndex = 2;
            // 
            // checkBox2
            // 
            checkBox2.AutoSize = true;
            checkBox2.Location = new Point(29, 72);
            checkBox2.Name = "checkBox2";
            checkBox2.Size = new Size(86, 19);
            checkBox2.TabIndex = 5;
            checkBox2.Text = "checkBox2";
            checkBox2.UseVisualStyleBackColor = true;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(120, 76);
            label3.Name = "label3";
            label3.Size = new Size(29, 15);
            label3.TabIndex = 1;
            label3.Text = "MP:";
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.Location = new Point(29, 31);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(86, 19);
            checkBox1.TabIndex = 4;
            checkBox1.Text = "checkBox1";
            checkBox1.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(120, 35);
            label2.Name = "label2";
            label2.Size = new Size(35, 15);
            label2.TabIndex = 0;
            label2.Text = "HP：";
            // 
            // groupBox8
            // 
            groupBox8.Dock = DockStyle.Bottom;
            groupBox8.Location = new Point(0, 386);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(238, 100);
            groupBox8.TabIndex = 0;
            groupBox8.TabStop = false;
            groupBox8.Text = "攻擊設定";
            // 
            // groupBox9
            // 
            groupBox9.Dock = DockStyle.Bottom;
            groupBox9.Location = new Point(0, 486);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(238, 89);
            groupBox9.TabIndex = 0;
            groupBox9.TabStop = false;
            groupBox9.Text = "輔助技能設定";
            // 
            // panel2
            // 
            panel2.Controls.Add(splitter1);
            panel2.Controls.Add(textBox1);
            panel2.Dock = DockStyle.Fill;
            panel2.Location = new Point(241, 3);
            panel2.Name = "panel2";
            panel2.Size = new Size(664, 575);
            panel2.TabIndex = 3;
            // 
            // splitter1
            // 
            splitter1.Location = new Point(0, 0);
            splitter1.Name = "splitter1";
            splitter1.Size = new Size(3, 575);
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
            textBox1.Size = new Size(664, 575);
            textBox1.TabIndex = 1;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabPage1);
            tabControl1.Controls.Add(tabPage2);
            tabControl1.Controls.Add(tabPage3);
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Location = new Point(0, 0);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(916, 609);
            tabControl1.TabIndex = 2;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(panel2);
            tabPage1.Controls.Add(panel1);
            tabPage1.Location = new Point(4, 24);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(908, 581);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "主控台";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(panel3);
            tabPage2.Controls.Add(panel4);
            tabPage2.Location = new Point(4, 24);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(908, 581);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "路徑編輯";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // panel3
            // 
            panel3.Controls.Add(pictureBoxMinimap);
            panel3.Dock = DockStyle.Fill;
            panel3.Location = new Point(3, 3);
            panel3.Name = "panel3";
            panel3.Size = new Size(686, 575);
            panel3.TabIndex = 1;
            // 
            // pictureBoxMinimap
            // 
            pictureBoxMinimap.Dock = DockStyle.Fill;
            pictureBoxMinimap.Location = new Point(0, 0);
            pictureBoxMinimap.Name = "pictureBoxMinimap";
            pictureBoxMinimap.Size = new Size(686, 575);
            pictureBoxMinimap.SizeMode = PictureBoxSizeMode.Normal;
            pictureBoxMinimap.TabIndex = 0;
            pictureBoxMinimap.TabStop = false;
            pictureBoxMinimap.Paint += pictureBoxMinimap_Paint;
            pictureBoxMinimap.MouseClick += pictureBoxMinimap_Click;
            pictureBoxMinimap.MouseDown += pictureBoxMinimap_MouseDown;
            pictureBoxMinimap.MouseUp += pictureBoxMinimap_MouseUp;
            pictureBoxMinimap.MouseLeave += pictureBoxMinimap_MouseLeave;
            pictureBoxMinimap.MouseMove += pictureBoxMinimap_MouseMove;
            // 
            // panel4
            // 
            panel4.Controls.Add(splitSidebar);
            panel4.Controls.Add(panel4BottomHost);
            panel4.Dock = DockStyle.Right;
            panel4.Location = new Point(689, 3);
            panel4.MinimumSize = new Size(300, 200);
            panel4.Name = "panel4";
            panel4.Size = new Size(300, 575);
            panel4.TabIndex = 2;
            // 
            // panel4BottomHost
            // 
            panel4BottomHost.Controls.Add(lbl_MouseCoords);
            panel4BottomHost.Controls.Add(lbl_MapStatus);
            panel4BottomHost.Dock = DockStyle.Bottom;
            panel4BottomHost.Location = new Point(0, 527);
            panel4BottomHost.Name = "panel4BottomHost";
            panel4BottomHost.Size = new Size(300, 48);
            panel4BottomHost.TabIndex = 2;
            // 
            // lbl_MapStatus
            // 
            lbl_MapStatus.BackColor = Color.FromArgb(50, 50, 50);
            lbl_MapStatus.Dock = DockStyle.Top;
            lbl_MapStatus.ForeColor = Color.Gainsboro;
            lbl_MapStatus.Location = new Point(0, 0);
            lbl_MapStatus.Name = "lbl_MapStatus";
            lbl_MapStatus.Padding = new Padding(4, 0, 0, 0);
            lbl_MapStatus.Size = new Size(300, 20);
            lbl_MapStatus.TabIndex = 0;
            lbl_MapStatus.Text = "—";
            lbl_MapStatus.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // splitSidebar
            // 
            splitSidebar.Dock = DockStyle.Fill;
            splitSidebar.FixedPanel = FixedPanel.Panel2;
            splitSidebar.Location = new Point(0, 0);
            splitSidebar.Name = "splitSidebar";
            splitSidebar.Orientation = Orientation.Horizontal;
            // 
            // splitSidebar.Panel1
            // 
            splitSidebar.Panel1.Controls.Add(panelToolsScroll);
            splitSidebar.Panel1MinSize = 120;
            // 
            // splitSidebar.Panel2
            // 
            splitSidebar.Panel2.Controls.Add(groupBox_PropertyPanel);
            splitSidebar.Panel2MinSize = 160;
            splitSidebar.Size = new Size(300, 527);
            splitSidebar.SplitterDistance = 280;
            splitSidebar.TabIndex = 0;
            // 
            // panelToolsScroll
            // 
            panelToolsScroll.AutoScroll = true;
            panelToolsScroll.Controls.Add(flowToolsStack);
            panelToolsScroll.Dock = DockStyle.Fill;
            panelToolsScroll.Location = new Point(0, 0);
            panelToolsScroll.Name = "panelToolsScroll";
            panelToolsScroll.Padding = new Padding(0, 0, 0, 2);
            panelToolsScroll.Size = new Size(300, 280);
            panelToolsScroll.TabIndex = 0;
            // 
            // flowToolsStack
            // 
            flowToolsStack.AutoSize = true;
            flowToolsStack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flowToolsStack.Controls.Add(groupBox1);
            flowToolsStack.Controls.Add(groupBox3);
            flowToolsStack.Controls.Add(groupBox_Action);
            flowToolsStack.Controls.Add(groupBox_Layers);
            flowToolsStack.Dock = DockStyle.Top;
            flowToolsStack.FlowDirection = FlowDirection.TopDown;
            flowToolsStack.Location = new Point(0, 0);
            flowToolsStack.Name = "flowToolsStack";
            flowToolsStack.Size = new Size(283, 400);
            flowToolsStack.TabIndex = 0;
            flowToolsStack.WrapContents = false;
            // 
            // groupBox_Layers
            // 
            groupBox_Layers.AutoSize = true;
            groupBox_Layers.Controls.Add(tableLayoutPanel_Layers);
            groupBox_Layers.Location = new Point(3, 313);
            groupBox_Layers.Margin = new Padding(0, 0, 0, 4);
            groupBox_Layers.MaximumSize = new Size(320, 0);
            groupBox_Layers.MinimumSize = new Size(280, 0);
            groupBox_Layers.Name = "groupBox_Layers";
            groupBox_Layers.Padding = new Padding(4);
            groupBox_Layers.Size = new Size(280, 108);
            groupBox_Layers.TabIndex = 3;
            groupBox_Layers.TabStop = false;
            groupBox_Layers.Text = "圖層";
            // 
            // tableLayoutPanel_Layers
            // 
            tableLayoutPanel_Layers.AutoSize = true;
            tableLayoutPanel_Layers.ColumnCount = 2;
            tableLayoutPanel_Layers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Layers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Layers.Controls.Add(chk_LayerPlatforms, 0, 0);
            tableLayoutPanel_Layers.Controls.Add(chk_LayerRopes, 1, 0);
            tableLayoutPanel_Layers.Controls.Add(chk_LayerManualAnchors, 0, 1);
            tableLayoutPanel_Layers.Controls.Add(chk_LayerJumpLinks, 1, 1);
            tableLayoutPanel_Layers.Controls.Add(chk_LayerNodes, 0, 2);
            tableLayoutPanel_Layers.Controls.Add(chk_LayerEdges, 1, 2);
            tableLayoutPanel_Layers.Controls.Add(chk_LayerValidation, 0, 3);
            tableLayoutPanel_Layers.Dock = DockStyle.Fill;
            tableLayoutPanel_Layers.Location = new Point(4, 20);
            tableLayoutPanel_Layers.Name = "tableLayoutPanel_Layers";
            tableLayoutPanel_Layers.RowCount = 4;
            tableLayoutPanel_Layers.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayoutPanel_Layers.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayoutPanel_Layers.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayoutPanel_Layers.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tableLayoutPanel_Layers.Size = new Size(272, 60);
            tableLayoutPanel_Layers.TabIndex = 0;
            // 
            // chk_LayerPlatforms
            // 
            chk_LayerPlatforms.AutoSize = true;
            chk_LayerPlatforms.Checked = true;
            chk_LayerPlatforms.CheckState = CheckState.Checked;
            chk_LayerPlatforms.Location = new Point(3, 3);
            chk_LayerPlatforms.Name = "chk_LayerPlatforms";
            chk_LayerPlatforms.Size = new Size(50, 19);
            chk_LayerPlatforms.TabIndex = 0;
            chk_LayerPlatforms.Text = "平台";
            chk_LayerPlatforms.UseVisualStyleBackColor = true;
            chk_LayerPlatforms.CheckedChanged += OnLayerCheckboxChanged;
            // 
            // chk_LayerRopes
            // 
            chk_LayerRopes.AutoSize = true;
            chk_LayerRopes.Checked = true;
            chk_LayerRopes.CheckState = CheckState.Checked;
            chk_LayerRopes.Location = new Point(139, 3);
            chk_LayerRopes.Name = "chk_LayerRopes";
            chk_LayerRopes.Size = new Size(50, 19);
            chk_LayerRopes.TabIndex = 1;
            chk_LayerRopes.Text = "繩索";
            chk_LayerRopes.UseVisualStyleBackColor = true;
            chk_LayerRopes.CheckedChanged += OnLayerCheckboxChanged;
            // 
            // chk_LayerJumpLinks
            // 
            chk_LayerJumpLinks.AutoSize = true;
            chk_LayerJumpLinks.Checked = true;
            chk_LayerJumpLinks.CheckState = CheckState.Checked;
            chk_LayerJumpLinks.Location = new Point(139, 28);
            chk_LayerJumpLinks.Name = "chk_LayerJumpLinks";
            chk_LayerJumpLinks.Size = new Size(50, 19);
            chk_LayerJumpLinks.TabIndex = 6;
            chk_LayerJumpLinks.Text = "跳點";
            chk_LayerJumpLinks.UseVisualStyleBackColor = true;
            chk_LayerJumpLinks.CheckedChanged += OnLayerCheckboxChanged;
            // 
            // chk_LayerManualAnchors
            // 
            chk_LayerManualAnchors.AutoSize = true;
            chk_LayerManualAnchors.Checked = true;
            chk_LayerManualAnchors.CheckState = CheckState.Checked;
            chk_LayerManualAnchors.Location = new Point(3, 28);
            chk_LayerManualAnchors.Name = "chk_LayerManualAnchors";
            chk_LayerManualAnchors.Size = new Size(62, 19);
            chk_LayerManualAnchors.TabIndex = 2;
            chk_LayerManualAnchors.Text = "手動邊";
            chk_LayerManualAnchors.UseVisualStyleBackColor = true;
            chk_LayerManualAnchors.CheckedChanged += OnLayerCheckboxChanged;
            // 
            // chk_LayerNodes
            // 
            chk_LayerNodes.AutoSize = true;
            chk_LayerNodes.Checked = true;
            chk_LayerNodes.CheckState = CheckState.Checked;
            chk_LayerNodes.Location = new Point(3, 53);
            chk_LayerNodes.Name = "chk_LayerNodes";
            chk_LayerNodes.Size = new Size(50, 19);
            chk_LayerNodes.TabIndex = 3;
            chk_LayerNodes.Text = "節點";
            chk_LayerNodes.UseVisualStyleBackColor = true;
            chk_LayerNodes.CheckedChanged += OnLayerCheckboxChanged;
            // 
            // chk_LayerEdges
            // 
            chk_LayerEdges.AutoSize = true;
            chk_LayerEdges.Checked = true;
            chk_LayerEdges.CheckState = CheckState.Checked;
            chk_LayerEdges.Location = new Point(139, 53);
            chk_LayerEdges.Name = "chk_LayerEdges";
            chk_LayerEdges.Size = new Size(38, 19);
            chk_LayerEdges.TabIndex = 4;
            chk_LayerEdges.Text = "邊";
            chk_LayerEdges.UseVisualStyleBackColor = true;
            chk_LayerEdges.CheckedChanged += OnLayerCheckboxChanged;
            // 
            // chk_LayerValidation
            // 
            chk_LayerValidation.AutoSize = true;
            chk_LayerValidation.Checked = true;
            chk_LayerValidation.CheckState = CheckState.Checked;
            chk_LayerValidation.Location = new Point(3, 78);
            chk_LayerValidation.Name = "chk_LayerValidation";
            chk_LayerValidation.Size = new Size(62, 19);
            chk_LayerValidation.TabIndex = 5;
            chk_LayerValidation.Text = "驗證層";
            chk_LayerValidation.UseVisualStyleBackColor = true;
            chk_LayerValidation.CheckedChanged += OnLayerCheckboxChanged;
            // 
            // groupBox_PropertyPanel
            // 
            groupBox_PropertyPanel.Dock = DockStyle.Fill;
            groupBox_PropertyPanel.Location = new Point(0, 0);
            groupBox_PropertyPanel.Name = "groupBox_PropertyPanel";
            groupBox_PropertyPanel.Padding = new Padding(4);
            groupBox_PropertyPanel.Size = new Size(300, 243);
            groupBox_PropertyPanel.TabIndex = 0;
            groupBox_PropertyPanel.TabStop = false;
            groupBox_PropertyPanel.Text = "屬性面板";
            // 
            // lbl_MouseCoords
            // 
            lbl_MouseCoords.AutoSize = false;
            lbl_MouseCoords.BackColor = Color.FromArgb(40, 40, 40);
            lbl_MouseCoords.Dock = DockStyle.Fill;
            lbl_MouseCoords.ForeColor = Color.White;
            lbl_MouseCoords.Location = new Point(0, 20);
            lbl_MouseCoords.Name = "lbl_MouseCoords";
            lbl_MouseCoords.Padding = new Padding(4, 4, 3, 3);
            lbl_MouseCoords.Size = new Size(300, 28);
            lbl_MouseCoords.TabIndex = 1;
            lbl_MouseCoords.Text = "座標: (-, -)";
            lbl_MouseCoords.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // groupBox_Action
            // 
            groupBox_Action.AutoSize = true;
            groupBox_Action.Controls.Add(cbo_ActionType);
            groupBox_Action.Controls.Add(lbl_Action);
            groupBox_Action.Location = new Point(3, 242);
            groupBox_Action.Margin = new Padding(0, 0, 0, 4);
            groupBox_Action.MaximumSize = new Size(320, 0);
            groupBox_Action.MinimumSize = new Size(280, 0);
            groupBox_Action.Name = "groupBox_Action";
            groupBox_Action.Size = new Size(280, 67);
            groupBox_Action.TabIndex = 2;
            groupBox_Action.TabStop = false;
            groupBox_Action.Text = "動作類型（ManualEdge）";
            // 
            // cbo_ActionType
            // 
            cbo_ActionType.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo_ActionType.FormattingEnabled = true;
            cbo_ActionType.Location = new Point(50, 22);
            cbo_ActionType.Name = "cbo_ActionType";
            cbo_ActionType.Size = new Size(140, 23);
            cbo_ActionType.TabIndex = 1;
            cbo_ActionType.SelectedIndexChanged += cbo_ActionType_SelectedIndexChanged;
            // 
            // lbl_Action
            // 
            lbl_Action.AutoSize = true;
            lbl_Action.Location = new Point(10, 25);
            lbl_Action.Name = "lbl_Action";
            lbl_Action.Size = new Size(34, 15);
            lbl_Action.TabIndex = 0;
            lbl_Action.Text = "動作:";
            // 
            // groupBox3
            // 
            groupBox3.AutoSize = true;
            groupBox3.Controls.Add(chk_AdvancedMode);
            groupBox3.Controls.Add(rdo_TwoPointLink);
            groupBox3.Controls.Add(rdo_DeleteMarker);
            groupBox3.Controls.Add(rdo_JumpLinkMarker);
            groupBox3.Controls.Add(rdo_RopeMarker);
            groupBox3.Controls.Add(rdo_PathMarker);
            groupBox3.Controls.Add(rdo_SelectMode);
            groupBox3.Location = new Point(3, 115);
            groupBox3.Margin = new Padding(0, 0, 0, 4);
            groupBox3.MaximumSize = new Size(320, 0);
            groupBox3.MinimumSize = new Size(280, 0);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(280, 145);
            groupBox3.TabIndex = 1;
            groupBox3.TabStop = false;
            groupBox3.Text = "標記編輯模式";
            // 
            // chk_AdvancedMode
            // 
            chk_AdvancedMode.AutoSize = true;
            chk_AdvancedMode.Location = new Point(8, 112);
            chk_AdvancedMode.Name = "chk_AdvancedMode";
            chk_AdvancedMode.Size = new Size(134, 19);
            chk_AdvancedMode.TabIndex = 7;
            chk_AdvancedMode.Text = "啟用進階例外邊模式";
            chk_AdvancedMode.UseVisualStyleBackColor = true;
            chk_AdvancedMode.CheckedChanged += chk_AdvancedMode_CheckedChanged;
            // 
            // rdo_TwoPointLink
            // 
            rdo_TwoPointLink.AutoSize = true;
            rdo_TwoPointLink.Location = new Point(119, 72);
            rdo_TwoPointLink.Name = "rdo_TwoPointLink";
            rdo_TwoPointLink.Size = new Size(73, 19);
            rdo_TwoPointLink.TabIndex = 6;
            rdo_TwoPointLink.TabStop = true;
            rdo_TwoPointLink.Text = "兩點連線";
            rdo_TwoPointLink.UseVisualStyleBackColor = true;
            rdo_TwoPointLink.CheckedChanged += OnEditModeChanged;
            // 
            // rdo_DeleteMarker
            // 
            rdo_DeleteMarker.AutoSize = true;
            rdo_DeleteMarker.Location = new Point(119, 47);
            rdo_DeleteMarker.Name = "rdo_DeleteMarker";
            rdo_DeleteMarker.Size = new Size(73, 19);
            rdo_DeleteMarker.TabIndex = 4;
            rdo_DeleteMarker.TabStop = true;
            rdo_DeleteMarker.Text = "刪除標記";
            rdo_DeleteMarker.UseVisualStyleBackColor = true;
            // 
            // rdo_JumpLinkMarker
            // 
            rdo_JumpLinkMarker.AutoSize = true;
            rdo_JumpLinkMarker.Location = new Point(8, 47);
            rdo_JumpLinkMarker.Name = "rdo_JumpLinkMarker";
            rdo_JumpLinkMarker.Size = new Size(73, 19);
            rdo_JumpLinkMarker.TabIndex = 8;
            rdo_JumpLinkMarker.TabStop = true;
            rdo_JumpLinkMarker.Text = "跳點標記";
            rdo_JumpLinkMarker.UseVisualStyleBackColor = true;
            // 
            // rdo_RopeMarker
            // 
            rdo_RopeMarker.AutoSize = true;
            rdo_RopeMarker.Location = new Point(119, 22);
            rdo_RopeMarker.Name = "rdo_RopeMarker";
            rdo_RopeMarker.Size = new Size(73, 19);
            rdo_RopeMarker.TabIndex = 3;
            rdo_RopeMarker.TabStop = true;
            rdo_RopeMarker.Text = "繩索標記";
            rdo_RopeMarker.UseVisualStyleBackColor = true;
            // 
            // rdo_PathMarker
            // 
            rdo_PathMarker.AutoSize = true;
            rdo_PathMarker.Location = new Point(8, 22);
            rdo_PathMarker.Name = "rdo_PathMarker";
            rdo_PathMarker.Size = new Size(73, 19);
            rdo_PathMarker.TabIndex = 0;
            rdo_PathMarker.TabStop = true;
            rdo_PathMarker.Text = "路線標記";
            rdo_PathMarker.UseVisualStyleBackColor = true;
            // 
            // rdo_SelectMode
            // 
            rdo_SelectMode.AutoSize = true;
            rdo_SelectMode.Location = new Point(8, 72);
            rdo_SelectMode.Name = "rdo_SelectMode";
            rdo_SelectMode.Size = new Size(49, 19);
            rdo_SelectMode.TabIndex = 5;
            rdo_SelectMode.TabStop = true;
            rdo_SelectMode.Text = "選取";
            rdo_SelectMode.UseVisualStyleBackColor = true;
            rdo_SelectMode.CheckedChanged += rdo_SelectMode_CheckedChanged;
            // 
            // groupBox1
            // 
            groupBox1.AutoSize = true;
            groupBox1.Controls.Add(btn_New);
            groupBox1.Controls.Add(btn_SaveMap);
            groupBox1.Controls.Add(cbo_MapFiles);
            groupBox1.Location = new Point(3, 3);
            groupBox1.Margin = new Padding(0, 0, 0, 4);
            groupBox1.MaximumSize = new Size(320, 0);
            groupBox1.MinimumSize = new Size(280, 0);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(280, 108);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "路徑檔管理";
            // 
            // btn_New
            // 
            btn_New.AutoSize = true;
            btn_New.Location = new Point(107, 61);
            btn_New.Name = "btn_New";
            btn_New.Size = new Size(75, 25);
            btn_New.TabIndex = 2;
            btn_New.Text = "新建";
            btn_New.UseVisualStyleBackColor = true;
            btn_New.Click += btn_New_Click;
            // 
            // btn_SaveMap
            // 
            btn_SaveMap.AutoSize = true;
            btn_SaveMap.Location = new Point(6, 61);
            btn_SaveMap.Name = "btn_SaveMap";
            btn_SaveMap.Size = new Size(75, 25);
            btn_SaveMap.TabIndex = 1;
            btn_SaveMap.Text = "保存";
            btn_SaveMap.UseVisualStyleBackColor = true;
            btn_SaveMap.Click += btn_SaveMap_Click;
            // 
            // cbo_MapFiles
            // 
            cbo_MapFiles.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo_MapFiles.FormattingEnabled = true;
            cbo_MapFiles.Location = new Point(25, 32);
            cbo_MapFiles.Name = "cbo_MapFiles";
            cbo_MapFiles.Size = new Size(141, 23);
            cbo_MapFiles.TabIndex = 0;
            cbo_MapFiles.SelectedIndexChanged += cbo_MapFiles_SelectedIndexChanged;
            // 
            // tabPage3
            // 
            tabPage3.Controls.Add(pictureBoxLiveView);
            tabPage3.Location = new Point(4, 24);
            tabPage3.Name = "tabPage3";
            tabPage3.Padding = new Padding(3);
            tabPage3.Size = new Size(908, 581);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "即時顯示";
            tabPage3.UseVisualStyleBackColor = true;
            // 
            // pictureBoxLiveView
            // 
            pictureBoxLiveView.Dock = DockStyle.Fill;
            pictureBoxLiveView.Location = new Point(3, 3);
            pictureBoxLiveView.Name = "pictureBoxLiveView";
            pictureBoxLiveView.Size = new Size(902, 575);
            pictureBoxLiveView.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxLiveView.TabIndex = 0;
            pictureBoxLiveView.TabStop = false;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(916, 609);
            Controls.Add(tabControl1);
            Name = "MainForm";
            Text = "ArtaleAI";
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            groupBox7.ResumeLayout(false);
            groupBox7.PerformLayout();
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            tabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage2.ResumeLayout(false);
            panel3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBoxMinimap).EndInit();
            panel4.ResumeLayout(false);
            panel4BottomHost.ResumeLayout(false);
            splitSidebar.Panel1.ResumeLayout(false);
            splitSidebar.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitSidebar).EndInit();
            splitSidebar.ResumeLayout(false);
            panelToolsScroll.ResumeLayout(false);
            flowToolsStack.ResumeLayout(false);
            flowToolsStack.PerformLayout();
            groupBox_Layers.ResumeLayout(false);
            groupBox_Layers.PerformLayout();
            tableLayoutPanel_Layers.ResumeLayout(false);
            tableLayoutPanel_Layers.PerformLayout();
            groupBox_PropertyPanel.ResumeLayout(false);
            groupBox_Action.ResumeLayout(false);
            groupBox_Action.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            tabPage3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBoxLiveView).EndInit();
            ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Splitter splitter1;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        public System.Windows.Forms.TextBox textBox1;
        public System.Windows.Forms.PictureBox pictureBoxMinimap;
        private System.Windows.Forms.Panel panel4;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ComboBox cbo_MapFiles;
        private GroupBox groupBox3;
        private RadioButton rdo_DeleteMarker;
        private RadioButton rdo_JumpLinkMarker;
        private RadioButton rdo_RopeMarker;
        private RadioButton rdo_PathMarker;
        private Button btn_New;
        private Button btn_SaveMap;
        private GroupBox groupBox5;
        private ComboBox cbo_MonsterTemplates;
        private GroupBox groupBox8;
        private GroupBox groupBox7;
        private GroupBox groupBox6;
        private Label label3;
        private Label label2;
        private GroupBox groupBox9;
        private CheckBox checkBox2;
        private CheckBox checkBox1;
        private TextBox textBox3;
        private TextBox textBox2;
        private Label label4;
        private Label label5;
        private TabPage tabPage3;
        private Button btn_DownloadMonster;
        private ComboBox cbo_DetectMode;
        private PictureBox pictureBoxLiveView;
        private CheckBox ckB_Start;
        private ComboBox cbo_LoadPathFile;

        private RadioButton rdo_SelectMode;
        private RadioButton rdo_TwoPointLink;
        private GroupBox groupBox_Action;
        private ComboBox cbo_ActionType;
        private Label lbl_Action;
        private Label lbl_MapStatus;
        private SplitContainer splitSidebar;
        private Panel panelToolsScroll;
        private FlowLayoutPanel flowToolsStack;
        private GroupBox groupBox_Layers;
        private TableLayoutPanel tableLayoutPanel_Layers;
        private CheckBox chk_LayerPlatforms;
        private CheckBox chk_LayerRopes;
        private CheckBox chk_LayerJumpLinks;
        private CheckBox chk_LayerManualAnchors;
        private CheckBox chk_LayerNodes;
        private CheckBox chk_LayerEdges;
        private CheckBox chk_LayerValidation;
        private GroupBox groupBox_PropertyPanel;
        private Panel panel4BottomHost;
        private CheckBox chk_AdvancedMode;
        private Label lbl_MouseCoords;
    }
}
