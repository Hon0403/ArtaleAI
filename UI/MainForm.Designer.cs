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
            panelConsoleStack = new Panel();
            groupBox_Prereq = new GroupBox();
            lbl_GameWindowStatus = new Label();
            groupBox_Settings = new GroupBox();
            lbl_LoadPathFile = new Label();
            cbo_LoadPathFile = new ComboBox();
            lbl_DetectMode = new Label();
            cbo_DetectMode = new ComboBox();
            lbl_MonsterTemplate = new Label();
            clb_MonsterTemplates = new CheckedListBox();
            lbl_MonsterHint = new Label();
            btn_DownloadMonster = new Button();
            groupBox_Execute = new GroupBox();
            ckB_Start = new CheckBox();
            lbl_Prerequisites = new Label();
            groupBox6 = new GroupBox();
            label4 = new Label();
            label5 = new Label();
            prg_Hp = new ProgressBar();
            prg_Mp = new ProgressBar();
            lbl_HpPercent = new Label();
            lbl_MpPercent = new Label();
            groupBox7 = new GroupBox();
            chk_AutoHealHp = new CheckBox();
            lbl_HealHpThreshold = new Label();
            txt_HealHpThreshold = new TextBox();
            lbl_HealHpHotkey = new Label();
            txt_HealHpHotkey = new TextBox();
            chk_AutoHealMp = new CheckBox();
            lbl_HealMpThreshold = new Label();
            txt_HealMpThreshold = new TextBox();
            lbl_HealMpHotkey = new Label();
            txt_HealMpHotkey = new TextBox();
            lbl_HealHint = new Label();
            groupBox8 = new GroupBox();
            lbl_RestInterval = new Label();
            txt_RestIntervalMinutes = new TextBox();
            lbl_RestDuration = new Label();
            txt_RestDurationSeconds = new TextBox();
            lbl_RestJitter = new Label();
            txt_RestJitterPercent = new TextBox();
            lbl_RestHint = new Label();
            groupBox9 = new GroupBox();
            panel2 = new Panel();
            groupBox_Log = new GroupBox();
            panel_StatusBar = new Panel();
            tableLayoutPanel_Ops = new TableLayoutPanel();
            tableLayoutPanel_Status = new TableLayoutPanel();
            lbl_Status_Game = new Label();
            lbl_Status_Capture = new Label();
            lbl_Status_Fsm = new Label();
            lbl_Status_Vitals = new Label();
            lbl_Status_Path = new Label();
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
            panelConsoleStack.SuspendLayout();
            groupBox_Prereq.SuspendLayout();
            groupBox_Settings.SuspendLayout();
            groupBox_Execute.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox7.SuspendLayout();
            groupBox8.SuspendLayout();
            panel2.SuspendLayout();
            groupBox_Log.SuspendLayout();
            panel_StatusBar.SuspendLayout();
            tableLayoutPanel_Ops.SuspendLayout();
            tableLayoutPanel_Status.SuspendLayout();
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
            // panel1 — 左側只留「啟動主流程」；營運設定移到右側日誌上方
            // 
            panel1.AutoScroll = true;
            panel1.Controls.Add(panelConsoleStack);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new Point(3, 3);
            panel1.Name = "panel1";
            panel1.Padding = new Padding(0, 0, 2, 0);
            panel1.Size = new Size(280, 640);
            panel1.TabIndex = 2;
            // 
            // panelConsoleStack
            // 
            panelConsoleStack.Controls.Add(groupBox9);
            panelConsoleStack.Controls.Add(groupBox_Execute);
            panelConsoleStack.Controls.Add(groupBox_Settings);
            panelConsoleStack.Controls.Add(groupBox_Prereq);
            panelConsoleStack.Dock = DockStyle.Top;
            panelConsoleStack.Location = new Point(0, 0);
            panelConsoleStack.Name = "panelConsoleStack";
            panelConsoleStack.Size = new Size(260, 470);
            panelConsoleStack.TabIndex = 0;
            // 
            // groupBox_Prereq
            // 
            groupBox_Prereq.Controls.Add(lbl_GameWindowStatus);
            groupBox_Prereq.Dock = DockStyle.Top;
            groupBox_Prereq.Location = new Point(0, 0);
            groupBox_Prereq.Name = "groupBox_Prereq";
            groupBox_Prereq.Padding = new Padding(4);
            groupBox_Prereq.Size = new Size(260, 52);
            groupBox_Prereq.TabIndex = 0;
            groupBox_Prereq.TabStop = false;
            groupBox_Prereq.Text = "前置狀態";
            // 
            // lbl_GameWindowStatus
            // 
            lbl_GameWindowStatus.Dock = DockStyle.Fill;
            lbl_GameWindowStatus.Location = new Point(4, 20);
            lbl_GameWindowStatus.Name = "lbl_GameWindowStatus";
            lbl_GameWindowStatus.Padding = new Padding(0, 2, 0, 0);
            lbl_GameWindowStatus.Size = new Size(252, 28);
            lbl_GameWindowStatus.TabIndex = 0;
            lbl_GameWindowStatus.Text = "遊戲視窗：檢查中…";
            lbl_GameWindowStatus.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // groupBox_Settings
            // 
            groupBox_Settings.Controls.Add(lbl_LoadPathFile);
            groupBox_Settings.Controls.Add(cbo_LoadPathFile);
            groupBox_Settings.Controls.Add(lbl_DetectMode);
            groupBox_Settings.Controls.Add(cbo_DetectMode);
            groupBox_Settings.Controls.Add(lbl_MonsterTemplate);
            groupBox_Settings.Controls.Add(clb_MonsterTemplates);
            groupBox_Settings.Controls.Add(lbl_MonsterHint);
            groupBox_Settings.Controls.Add(btn_DownloadMonster);
            groupBox_Settings.Dock = DockStyle.Top;
            groupBox_Settings.Location = new Point(0, 52);
            groupBox_Settings.Name = "groupBox_Settings";
            groupBox_Settings.Padding = new Padding(4);
            groupBox_Settings.Size = new Size(260, 286);
            groupBox_Settings.TabIndex = 1;
            groupBox_Settings.TabStop = false;
            groupBox_Settings.Text = "設定";
            // 
            // lbl_LoadPathFile
            // 
            lbl_LoadPathFile.AutoSize = true;
            lbl_LoadPathFile.Location = new Point(8, 22);
            lbl_LoadPathFile.Name = "lbl_LoadPathFile";
            lbl_LoadPathFile.Size = new Size(43, 15);
            lbl_LoadPathFile.TabIndex = 0;
            lbl_LoadPathFile.Text = "路徑檔";
            // 
            // cbo_LoadPathFile
            // 
            cbo_LoadPathFile.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cbo_LoadPathFile.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo_LoadPathFile.FormattingEnabled = true;
            cbo_LoadPathFile.Location = new Point(8, 40);
            cbo_LoadPathFile.Name = "cbo_LoadPathFile";
            cbo_LoadPathFile.Size = new Size(244, 23);
            cbo_LoadPathFile.TabIndex = 1;
            cbo_LoadPathFile.SelectedIndexChanged += cbo_LoadPathFile_SelectedIndexChanged;
            // 
            // lbl_DetectMode
            // 
            lbl_DetectMode.AutoSize = true;
            lbl_DetectMode.Location = new Point(8, 68);
            lbl_DetectMode.Name = "lbl_DetectMode";
            lbl_DetectMode.Size = new Size(55, 15);
            lbl_DetectMode.TabIndex = 2;
            lbl_DetectMode.Text = "偵測模式";
            // 
            // cbo_DetectMode
            // 
            cbo_DetectMode.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cbo_DetectMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cbo_DetectMode.FormattingEnabled = true;
            cbo_DetectMode.Location = new Point(8, 86);
            cbo_DetectMode.Name = "cbo_DetectMode";
            cbo_DetectMode.Size = new Size(244, 23);
            cbo_DetectMode.TabIndex = 3;
            // 
            // lbl_MonsterTemplate
            // 
            lbl_MonsterTemplate.AutoSize = true;
            lbl_MonsterTemplate.Location = new Point(8, 114);
            lbl_MonsterTemplate.Name = "lbl_MonsterTemplate";
            lbl_MonsterTemplate.Size = new Size(151, 15);
            lbl_MonsterTemplate.TabIndex = 4;
            lbl_MonsterTemplate.Text = "要打哪些怪（最多 3 種）";
            // 
            // clb_MonsterTemplates
            // 
            clb_MonsterTemplates.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            clb_MonsterTemplates.CheckOnClick = true;
            clb_MonsterTemplates.FormattingEnabled = true;
            clb_MonsterTemplates.Location = new Point(8, 132);
            clb_MonsterTemplates.Name = "clb_MonsterTemplates";
            clb_MonsterTemplates.Size = new Size(244, 94);
            clb_MonsterTemplates.TabIndex = 5;
            // 
            // lbl_MonsterHint
            // 
            lbl_MonsterHint.AutoSize = true;
            lbl_MonsterHint.ForeColor = SystemColors.GrayText;
            lbl_MonsterHint.Location = new Point(8, 230);
            lbl_MonsterHint.Name = "lbl_MonsterHint";
            lbl_MonsterHint.Size = new Size(187, 15);
            lbl_MonsterHint.TabIndex = 6;
            lbl_MonsterHint.Text = "可多選；勾太多會變慢、容易打錯";
            // 
            // btn_DownloadMonster
            // 
            btn_DownloadMonster.Location = new Point(8, 250);
            btn_DownloadMonster.Name = "btn_DownloadMonster";
            btn_DownloadMonster.Size = new Size(78, 25);
            btn_DownloadMonster.TabIndex = 7;
            btn_DownloadMonster.Text = "下載怪物";
            btn_DownloadMonster.UseVisualStyleBackColor = true;
            btn_DownloadMonster.Click += btn_DownloadMonster_Click;
            // 
            // groupBox_Execute
            // 
            groupBox_Execute.Controls.Add(lbl_Prerequisites);
            groupBox_Execute.Controls.Add(ckB_Start);
            groupBox_Execute.Dock = DockStyle.Top;
            groupBox_Execute.Location = new Point(0, 300);
            groupBox_Execute.Name = "groupBox_Execute";
            groupBox_Execute.Padding = new Padding(4);
            groupBox_Execute.Size = new Size(260, 120);
            groupBox_Execute.TabIndex = 2;
            groupBox_Execute.TabStop = false;
            groupBox_Execute.Text = "執行";
            // 
            // ckB_Start
            // 
            ckB_Start.AutoSize = true;
            ckB_Start.Location = new Point(8, 22);
            ckB_Start.Name = "ckB_Start";
            ckB_Start.Size = new Size(74, 19);
            ckB_Start.TabIndex = 0;
            ckB_Start.Text = "自動打怪";
            ckB_Start.UseVisualStyleBackColor = true;
            ckB_Start.CheckedChanged += ckB_Start_CheckedChanged;
            // 
            // lbl_Prerequisites
            // 
            lbl_Prerequisites.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lbl_Prerequisites.ForeColor = SystemColors.GrayText;
            lbl_Prerequisites.Location = new Point(8, 44);
            lbl_Prerequisites.Name = "lbl_Prerequisites";
            lbl_Prerequisites.Size = new Size(244, 68);
            lbl_Prerequisites.TabIndex = 1;
            lbl_Prerequisites.Text = "尚未啟動";
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(lbl_MpPercent);
            groupBox6.Controls.Add(prg_Mp);
            groupBox6.Controls.Add(label4);
            groupBox6.Controls.Add(lbl_HpPercent);
            groupBox6.Controls.Add(prg_Hp);
            groupBox6.Controls.Add(label5);
            groupBox6.Dock = DockStyle.Fill;
            groupBox6.Location = new Point(3, 4);
            groupBox6.Name = "groupBox6";
            groupBox6.Padding = new Padding(4);
            groupBox6.Size = new Size(182, 142);
            groupBox6.TabIndex = 3;
            groupBox6.TabStop = false;
            groupBox6.Text = "角色資訊";
            // 
            // lbl_MpPercent
            // 
            lbl_MpPercent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lbl_MpPercent.AutoSize = true;
            lbl_MpPercent.Location = new Point(152, 58);
            lbl_MpPercent.Name = "lbl_MpPercent";
            lbl_MpPercent.Size = new Size(13, 15);
            lbl_MpPercent.TabIndex = 5;
            lbl_MpPercent.Text = "—";
            // 
            // prg_Mp
            // 
            prg_Mp.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            prg_Mp.Location = new Point(40, 54);
            prg_Mp.Name = "prg_Mp";
            prg_Mp.Size = new Size(108, 18);
            prg_Mp.TabIndex = 4;
            // 
            // lbl_HpPercent
            // 
            lbl_HpPercent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lbl_HpPercent.AutoSize = true;
            lbl_HpPercent.Location = new Point(152, 28);
            lbl_HpPercent.Name = "lbl_HpPercent";
            lbl_HpPercent.Size = new Size(13, 15);
            lbl_HpPercent.TabIndex = 3;
            lbl_HpPercent.Text = "—";
            // 
            // prg_Hp
            // 
            prg_Hp.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            prg_Hp.Location = new Point(40, 24);
            prg_Hp.Name = "prg_Hp";
            prg_Hp.Size = new Size(108, 18);
            prg_Hp.TabIndex = 2;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(8, 56);
            label4.Name = "label4";
            label4.Size = new Size(29, 15);
            label4.TabIndex = 1;
            label4.Text = "MP:";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(8, 26);
            label5.Name = "label5";
            label5.Size = new Size(35, 15);
            label5.TabIndex = 0;
            label5.Text = "HP：";
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(lbl_HealHint);
            groupBox7.Controls.Add(txt_HealMpHotkey);
            groupBox7.Controls.Add(lbl_HealMpHotkey);
            groupBox7.Controls.Add(txt_HealMpThreshold);
            groupBox7.Controls.Add(lbl_HealMpThreshold);
            groupBox7.Controls.Add(chk_AutoHealMp);
            groupBox7.Controls.Add(txt_HealHpHotkey);
            groupBox7.Controls.Add(lbl_HealHpHotkey);
            groupBox7.Controls.Add(txt_HealHpThreshold);
            groupBox7.Controls.Add(lbl_HealHpThreshold);
            groupBox7.Controls.Add(chk_AutoHealHp);
            groupBox7.Dock = DockStyle.Fill;
            groupBox7.Location = new Point(215, 4);
            groupBox7.Name = "groupBox7";
            groupBox7.Padding = new Padding(4);
            groupBox7.Size = new Size(218, 142);
            groupBox7.TabIndex = 5;
            groupBox7.TabStop = false;
            groupBox7.Text = "自動喝水";
            // 
            // chk_AutoHealHp
            // 
            chk_AutoHealHp.AutoSize = true;
            chk_AutoHealHp.Location = new Point(6, 20);
            chk_AutoHealHp.Name = "chk_AutoHealHp";
            chk_AutoHealHp.Size = new Size(68, 19);
            chk_AutoHealHp.TabIndex = 0;
            chk_AutoHealHp.Text = "補 HP";
            chk_AutoHealHp.UseVisualStyleBackColor = true;
            // 
            // lbl_HealHpThreshold
            // 
            lbl_HealHpThreshold.AutoSize = true;
            lbl_HealHpThreshold.Location = new Point(6, 44);
            lbl_HealHpThreshold.Name = "lbl_HealHpThreshold";
            lbl_HealHpThreshold.Size = new Size(39, 15);
            lbl_HealHpThreshold.TabIndex = 1;
            lbl_HealHpThreshold.Text = "低於%";
            // 
            // txt_HealHpThreshold
            // 
            txt_HealHpThreshold.Location = new Point(48, 40);
            txt_HealHpThreshold.MaxLength = 2;
            txt_HealHpThreshold.Name = "txt_HealHpThreshold";
            txt_HealHpThreshold.Size = new Size(28, 23);
            txt_HealHpThreshold.TabIndex = 2;
            txt_HealHpThreshold.Text = "40";
            txt_HealHpThreshold.TextAlign = HorizontalAlignment.Right;
            // 
            // lbl_HealHpHotkey
            // 
            lbl_HealHpHotkey.AutoSize = true;
            lbl_HealHpHotkey.Location = new Point(82, 44);
            lbl_HealHpHotkey.Name = "lbl_HealHpHotkey";
            lbl_HealHpHotkey.Size = new Size(19, 15);
            lbl_HealHpHotkey.TabIndex = 3;
            lbl_HealHpHotkey.Text = "鍵";
            // 
            // txt_HealHpHotkey
            // 
            txt_HealHpHotkey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txt_HealHpHotkey.Location = new Point(104, 40);
            txt_HealHpHotkey.MaxLength = 32;
            txt_HealHpHotkey.Name = "txt_HealHpHotkey";
            txt_HealHpHotkey.Size = new Size(100, 23);
            txt_HealHpHotkey.TabIndex = 4;
            txt_HealHpHotkey.Text = "Insert";
            // 
            // chk_AutoHealMp
            // 
            chk_AutoHealMp.AutoSize = true;
            chk_AutoHealMp.Location = new Point(6, 68);
            chk_AutoHealMp.Name = "chk_AutoHealMp";
            chk_AutoHealMp.Size = new Size(70, 19);
            chk_AutoHealMp.TabIndex = 5;
            chk_AutoHealMp.Text = "補 MP";
            chk_AutoHealMp.UseVisualStyleBackColor = true;
            // 
            // lbl_HealMpThreshold
            // 
            lbl_HealMpThreshold.AutoSize = true;
            lbl_HealMpThreshold.Location = new Point(6, 92);
            lbl_HealMpThreshold.Name = "lbl_HealMpThreshold";
            lbl_HealMpThreshold.Size = new Size(39, 15);
            lbl_HealMpThreshold.TabIndex = 6;
            lbl_HealMpThreshold.Text = "低於%";
            // 
            // txt_HealMpThreshold
            // 
            txt_HealMpThreshold.Location = new Point(48, 88);
            txt_HealMpThreshold.MaxLength = 2;
            txt_HealMpThreshold.Name = "txt_HealMpThreshold";
            txt_HealMpThreshold.Size = new Size(28, 23);
            txt_HealMpThreshold.TabIndex = 7;
            txt_HealMpThreshold.Text = "30";
            txt_HealMpThreshold.TextAlign = HorizontalAlignment.Right;
            // 
            // lbl_HealMpHotkey
            // 
            lbl_HealMpHotkey.AutoSize = true;
            lbl_HealMpHotkey.Location = new Point(82, 92);
            lbl_HealMpHotkey.Name = "lbl_HealMpHotkey";
            lbl_HealMpHotkey.Size = new Size(19, 15);
            lbl_HealMpHotkey.TabIndex = 8;
            lbl_HealMpHotkey.Text = "鍵";
            // 
            // txt_HealMpHotkey
            // 
            txt_HealMpHotkey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txt_HealMpHotkey.Location = new Point(104, 88);
            txt_HealMpHotkey.MaxLength = 32;
            txt_HealMpHotkey.Name = "txt_HealMpHotkey";
            txt_HealMpHotkey.Size = new Size(100, 23);
            txt_HealMpHotkey.TabIndex = 9;
            txt_HealMpHotkey.Text = "Delete";
            // 
            // lbl_HealHint
            // 
            lbl_HealHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lbl_HealHint.ForeColor = SystemColors.GrayText;
            lbl_HealHint.Location = new Point(6, 116);
            lbl_HealHint.Name = "lbl_HealHint";
            lbl_HealHint.Size = new Size(198, 20);
            lbl_HealHint.TabIndex = 10;
            lbl_HealHint.Text = "點「鍵」錄製任一鍵盤鍵；Esc 取消";
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(lbl_RestHint);
            groupBox8.Controls.Add(txt_RestJitterPercent);
            groupBox8.Controls.Add(lbl_RestJitter);
            groupBox8.Controls.Add(txt_RestDurationSeconds);
            groupBox8.Controls.Add(lbl_RestDuration);
            groupBox8.Controls.Add(txt_RestIntervalMinutes);
            groupBox8.Controls.Add(lbl_RestInterval);
            groupBox8.Dock = DockStyle.Fill;
            groupBox8.Location = new Point(3, 4);
            groupBox8.Name = "groupBox8";
            groupBox8.Padding = new Padding(4);
            groupBox8.Size = new Size(206, 142);
            groupBox8.TabIndex = 4;
            groupBox8.TabStop = false;
            groupBox8.Text = "定時休息";
            // 
            // lbl_RestInterval
            // 
            lbl_RestInterval.AutoSize = true;
            lbl_RestInterval.Location = new Point(6, 24);
            lbl_RestInterval.Name = "lbl_RestInterval";
            lbl_RestInterval.Size = new Size(91, 15);
            lbl_RestInterval.TabIndex = 0;
            lbl_RestInterval.Text = "每隔幾分鐘";
            // 
            // txt_RestIntervalMinutes
            // 
            txt_RestIntervalMinutes.Location = new Point(108, 20);
            txt_RestIntervalMinutes.MaxLength = 3;
            txt_RestIntervalMinutes.Name = "txt_RestIntervalMinutes";
            txt_RestIntervalMinutes.Size = new Size(40, 23);
            txt_RestIntervalMinutes.TabIndex = 1;
            txt_RestIntervalMinutes.Text = "0";
            txt_RestIntervalMinutes.TextAlign = HorizontalAlignment.Right;
            txt_RestIntervalMinutes.KeyPress += txt_RestNumeric_KeyPress;
            txt_RestIntervalMinutes.Leave += txt_RestSettings_Leave;
            // 
            // lbl_RestDuration
            // 
            lbl_RestDuration.AutoSize = true;
            lbl_RestDuration.Location = new Point(6, 52);
            lbl_RestDuration.Name = "lbl_RestDuration";
            lbl_RestDuration.Size = new Size(91, 15);
            lbl_RestDuration.TabIndex = 2;
            lbl_RestDuration.Text = "每次休息幾秒";
            // 
            // txt_RestDurationSeconds
            // 
            txt_RestDurationSeconds.Location = new Point(108, 48);
            txt_RestDurationSeconds.MaxLength = 4;
            txt_RestDurationSeconds.Name = "txt_RestDurationSeconds";
            txt_RestDurationSeconds.Size = new Size(40, 23);
            txt_RestDurationSeconds.TabIndex = 3;
            txt_RestDurationSeconds.Text = "60";
            txt_RestDurationSeconds.TextAlign = HorizontalAlignment.Right;
            txt_RestDurationSeconds.KeyPress += txt_RestNumeric_KeyPress;
            txt_RestDurationSeconds.Leave += txt_RestSettings_Leave;
            // 
            // lbl_RestJitter
            // 
            lbl_RestJitter.AutoSize = true;
            lbl_RestJitter.Location = new Point(6, 80);
            lbl_RestJitter.Name = "lbl_RestJitter";
            lbl_RestJitter.Size = new Size(91, 15);
            lbl_RestJitter.TabIndex = 4;
            lbl_RestJitter.Text = "時間略為隨機";
            // 
            // txt_RestJitterPercent
            // 
            txt_RestJitterPercent.Location = new Point(108, 76);
            txt_RestJitterPercent.MaxLength = 2;
            txt_RestJitterPercent.Name = "txt_RestJitterPercent";
            txt_RestJitterPercent.Size = new Size(40, 23);
            txt_RestJitterPercent.TabIndex = 5;
            txt_RestJitterPercent.Text = "20";
            txt_RestJitterPercent.TextAlign = HorizontalAlignment.Right;
            txt_RestJitterPercent.KeyPress += txt_RestNumeric_KeyPress;
            txt_RestJitterPercent.Leave += txt_RestSettings_Leave;
            // 
            // lbl_RestHint
            // 
            lbl_RestHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lbl_RestHint.ForeColor = SystemColors.GrayText;
            lbl_RestHint.Location = new Point(6, 104);
            lbl_RestHint.Name = "lbl_RestHint";
            lbl_RestHint.Size = new Size(188, 32);
            lbl_RestHint.TabIndex = 6;
            lbl_RestHint.Text = "0＝不休息　建議 30／60／20";
            // 
            // groupBox9
            // 
            groupBox9.Dock = DockStyle.Bottom;
            groupBox9.Location = new Point(0, 486);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(260, 89);
            groupBox9.TabIndex = 6;
            groupBox9.TabStop = false;
            groupBox9.Text = "輔助技能設定";
            groupBox9.Visible = false;
            // 
            // panel2 — StatusBar → 營運列（休息／喝水／角色）→ 日誌
            // 
            panel2.Controls.Add(groupBox_Log);
            panel2.Controls.Add(tableLayoutPanel_Ops);
            panel2.Controls.Add(panel_StatusBar);
            panel2.Dock = DockStyle.Fill;
            panel2.Location = new Point(283, 3);
            panel2.Name = "panel2";
            panel2.Size = new Size(732, 640);
            panel2.TabIndex = 3;
            // 
            // groupBox_Log
            // 
            groupBox_Log.Controls.Add(textBox1);
            groupBox_Log.Dock = DockStyle.Fill;
            groupBox_Log.Location = new Point(0, 228);
            groupBox_Log.Name = "groupBox_Log";
            groupBox_Log.Padding = new Padding(4);
            groupBox_Log.Size = new Size(732, 424);
            groupBox_Log.TabIndex = 2;
            groupBox_Log.TabStop = false;
            groupBox_Log.Text = "日誌";
            // 
            // textBox1
            // 
            textBox1.Dock = DockStyle.Fill;
            textBox1.Font = new Font("Consolas", 9F);
            textBox1.Location = new Point(4, 20);
            textBox1.Multiline = true;
            textBox1.Name = "textBox1";
            textBox1.ReadOnly = true;
            textBox1.ScrollBars = ScrollBars.Vertical;
            textBox1.Size = new Size(724, 400);
            textBox1.TabIndex = 0;
            // 
            // tableLayoutPanel_Ops — 承接原左側的休息／喝水／角色資訊
            // 
            tableLayoutPanel_Ops.ColumnCount = 3;
            tableLayoutPanel_Ops.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            tableLayoutPanel_Ops.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            tableLayoutPanel_Ops.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29F));
            tableLayoutPanel_Ops.Controls.Add(groupBox8, 0, 0);
            tableLayoutPanel_Ops.Controls.Add(groupBox7, 1, 0);
            tableLayoutPanel_Ops.Controls.Add(groupBox6, 2, 0);
            tableLayoutPanel_Ops.Dock = DockStyle.Top;
            tableLayoutPanel_Ops.Location = new Point(0, 56);
            tableLayoutPanel_Ops.Name = "tableLayoutPanel_Ops";
            tableLayoutPanel_Ops.Padding = new Padding(2, 4, 2, 4);
            tableLayoutPanel_Ops.RowCount = 1;
            tableLayoutPanel_Ops.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel_Ops.Size = new Size(732, 164);
            tableLayoutPanel_Ops.TabIndex = 3;
            // 
            // panel_StatusBar
            // 
            panel_StatusBar.BackColor = Color.FromArgb(45, 45, 45);
            panel_StatusBar.Controls.Add(tableLayoutPanel_Status);
            panel_StatusBar.Dock = DockStyle.Top;
            panel_StatusBar.Location = new Point(0, 0);
            panel_StatusBar.Name = "panel_StatusBar";
            panel_StatusBar.Padding = new Padding(2, 0, 2, 2);
            panel_StatusBar.Size = new Size(732, 56);
            panel_StatusBar.TabIndex = 1;
            // 
            // tableLayoutPanel_Status
            // 
            tableLayoutPanel_Status.ColumnCount = 3;
            tableLayoutPanel_Status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            tableLayoutPanel_Status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33334F));
            tableLayoutPanel_Status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33334F));
            tableLayoutPanel_Status.Controls.Add(lbl_Status_Game, 0, 0);
            tableLayoutPanel_Status.Controls.Add(lbl_Status_Capture, 1, 0);
            tableLayoutPanel_Status.Controls.Add(lbl_Status_Fsm, 2, 0);
            tableLayoutPanel_Status.Controls.Add(lbl_Status_Vitals, 0, 1);
            tableLayoutPanel_Status.Controls.Add(lbl_Status_Path, 1, 1);
            tableLayoutPanel_Status.Dock = DockStyle.Fill;
            tableLayoutPanel_Status.Location = new Point(2, 0);
            tableLayoutPanel_Status.Name = "tableLayoutPanel_Status";
            tableLayoutPanel_Status.RowCount = 2;
            tableLayoutPanel_Status.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Status.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel_Status.Size = new Size(728, 54);
            tableLayoutPanel_Status.TabIndex = 0;
            tableLayoutPanel_Status.SetColumnSpan(lbl_Status_Path, 2);
            // 
            // lbl_Status_Game
            // 
            lbl_Status_Game.AutoSize = true;
            lbl_Status_Game.Location = new Point(3, 0);
            lbl_Status_Game.Name = "lbl_Status_Game";
            lbl_Status_Game.Size = new Size(67, 15);
            lbl_Status_Game.TabIndex = 0;
            lbl_Status_Game.Text = "遊戲：—";
            // 
            // lbl_Status_Capture
            // 
            lbl_Status_Capture.AutoSize = true;
            lbl_Status_Capture.Location = new Point(217, 0);
            lbl_Status_Capture.Name = "lbl_Status_Capture";
            lbl_Status_Capture.Size = new Size(67, 15);
            lbl_Status_Capture.TabIndex = 1;
            lbl_Status_Capture.Text = "擷取：—";
            // 
            // lbl_Status_Fsm
            // 
            lbl_Status_Fsm.AutoSize = true;
            lbl_Status_Fsm.Location = new Point(431, 0);
            lbl_Status_Fsm.Name = "lbl_Status_Fsm";
            lbl_Status_Fsm.Size = new Size(67, 15);
            lbl_Status_Fsm.TabIndex = 2;
            lbl_Status_Fsm.Text = "導航：—";
            // 
            // lbl_Status_Vitals
            // 
            lbl_Status_Vitals.AutoSize = true;
            lbl_Status_Vitals.Location = new Point(3, 26);
            lbl_Status_Vitals.Name = "lbl_Status_Vitals";
            lbl_Status_Vitals.Size = new Size(88, 15);
            lbl_Status_Vitals.TabIndex = 3;
            lbl_Status_Vitals.Text = "HP — · MP —";
            // 
            // lbl_Status_Path
            // 
            lbl_Status_Path.AutoSize = true;
            lbl_Status_Path.Location = new Point(217, 26);
            lbl_Status_Path.Name = "lbl_Status_Path";
            lbl_Status_Path.Size = new Size(43, 15);
            lbl_Status_Path.TabIndex = 4;
            lbl_Status_Path.Text = "路徑：—";
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
            ClientSize = new Size(1024, 680);
            Controls.Add(tabControl1);
            MinimumSize = new Size(900, 600);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "ArtaleAI";
            panel1.ResumeLayout(false);
            panelConsoleStack.ResumeLayout(false);
            groupBox_Prereq.ResumeLayout(false);
            groupBox_Settings.ResumeLayout(false);
            groupBox_Settings.PerformLayout();
            groupBox_Execute.ResumeLayout(false);
            groupBox_Execute.PerformLayout();
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            groupBox7.ResumeLayout(false);
            groupBox7.PerformLayout();
            groupBox8.ResumeLayout(false);
            groupBox8.PerformLayout();
            panel2.ResumeLayout(false);
            groupBox_Log.ResumeLayout(false);
            groupBox_Log.PerformLayout();
            tableLayoutPanel_Ops.ResumeLayout(false);
            panel_StatusBar.ResumeLayout(false);
            tableLayoutPanel_Status.ResumeLayout(false);
            tableLayoutPanel_Status.PerformLayout();
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
        private System.Windows.Forms.Panel panelConsoleStack;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.GroupBox groupBox_Log;
        private System.Windows.Forms.Panel panel_StatusBar;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel_Ops;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel_Status;
        private System.Windows.Forms.Label lbl_Status_Game;
        private System.Windows.Forms.Label lbl_Status_Capture;
        private System.Windows.Forms.Label lbl_Status_Fsm;
        private System.Windows.Forms.Label lbl_Status_Vitals;
        private System.Windows.Forms.Label lbl_Status_Path;
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
        private GroupBox groupBox_Prereq;
        private Label lbl_GameWindowStatus;
        private GroupBox groupBox_Settings;
        private Label lbl_LoadPathFile;
        private Label lbl_DetectMode;
        private Label lbl_MonsterTemplate;
        private CheckedListBox clb_MonsterTemplates;
        private Label lbl_MonsterHint;
        private GroupBox groupBox_Execute;
        private Label lbl_Prerequisites;
        private GroupBox groupBox8;
        private Label lbl_RestInterval;
        private TextBox txt_RestIntervalMinutes;
        private Label lbl_RestDuration;
        private TextBox txt_RestDurationSeconds;
        private Label lbl_RestJitter;
        private TextBox txt_RestJitterPercent;
        private Label lbl_RestHint;
        private GroupBox groupBox7;
        private CheckBox chk_AutoHealHp;
        private Label lbl_HealHpThreshold;
        private TextBox txt_HealHpThreshold;
        private Label lbl_HealHpHotkey;
        private TextBox txt_HealHpHotkey;
        private CheckBox chk_AutoHealMp;
        private Label lbl_HealMpThreshold;
        private TextBox txt_HealMpThreshold;
        private Label lbl_HealMpHotkey;
        private TextBox txt_HealMpHotkey;
        private Label lbl_HealHint;
        private GroupBox groupBox6;
        private ProgressBar prg_Hp;
        private ProgressBar prg_Mp;
        private Label lbl_HpPercent;
        private Label lbl_MpPercent;
        private GroupBox groupBox9;
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
