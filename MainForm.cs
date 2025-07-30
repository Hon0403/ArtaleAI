using ArtaleAI.Config;
using ArtaleAI.GameWindow;
using ArtaleAI.Minimap;
using ArtaleAI.PathEditor;
using ArtaleAI.Services;
using ArtaleAI.LiveView;
using ArtaleAI.Magnifier;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Capture;

namespace ArtaleAI
{
    public partial class MainForm : Form,
        IMagnifierEventHandler,
        IMonsterTemplateEventHandler,
        IMapFileEventHandler,
        IConfigEventHandler
    {
        private ConfigurationManager? _configurationManager;
        private readonly EditorMinimap _editorMinimap = new();
        private GraphicsCaptureItem? _selectedCaptureItem;
        private readonly MapEditor _mapEditor = new();
        private readonly MapData _mapData = new();
        private LiveViewController? _liveViewController;
        private FloatingMagnifier? _floatingMagnifier;
        private MonsterTemplateService? _monsterTemplateService;
        private MapFileManager? _mapFileManager;

        public MainForm()
        {
            InitializeComponent();

            // 配置管理統一化
            _configurationManager = new ConfigurationManager(this);
            _configurationManager.Load();

            _liveViewController = new LiveViewController(pictureBoxLiveView, textBox1, this);
            _floatingMagnifier = new FloatingMagnifier(this);
            _monsterTemplateService = new MonsterTemplateService(cbo_MonsterTemplates, this);
            _monsterTemplateService.InitializeMonsterDropdown();
            _mapFileManager = new MapFileManager(cbo_MapFiles, _mapEditor, this);
            _mapFileManager.InitializeMapFilesDropdown();

            // 事件綁定
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;
            numericUpDownZoom.ValueChanged += numericUpDownZoom_ValueChanged;
            rdo_PathMarker.CheckedChanged += OnEditModeChanged;
            rdo_SafeZone.CheckedChanged += OnEditModeChanged;
            rdo_RestrictedZone.CheckedChanged += OnEditModeChanged;
            rdo_RopeMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;
            pictureBoxMinimap.Paint += pictureBoxMinimap_Paint;
            pictureBoxMinimap.MouseDown += pictureBoxMinimap_MouseDown;
            pictureBoxMinimap.MouseUp += pictureBoxMinimap_MouseUp;
            pictureBoxMinimap.MouseMove += pictureBoxMinimap_MouseMove;
            pictureBoxMinimap.MouseLeave += pictureBoxMinimap_MouseLeave;
            pictureBoxMinimap.MouseClick += pictureBoxMinimap_MouseClick;
        }

        #region 設定/地圖/模板介面事件

        // ================== 配置相關 ==================
        public void OnConfigLoaded(AppConfig config)
        {
            if (InvokeRequired) { Invoke(new Action<AppConfig>(OnConfigLoaded), config); return; }
            numericUpDownZoom.Value = config.General.ZoomFactor;
        }
        public void OnConfigSaved(AppConfig config)
        {
            if (InvokeRequired) { Invoke(new Action<AppConfig>(OnConfigSaved), config); return; }
            textBox1.AppendText("✅ 設定已儲存\r\n");
            textBox1.ScrollToCaret();
        }
        public void OnConfigError(string errorMessage)
        {
            if (InvokeRequired) { Invoke(new Action<string>(OnConfigError), errorMessage); return; }
            textBox1.AppendText($"❌ 設定錯誤: {errorMessage}\r\n");
            textBox1.ScrollToCaret();
            MessageBox.Show(errorMessage, "設定檔錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ================== 狀態訊息與錯誤 ==================
        // **只留一份主public方法，介面全部明確實作轉給這裡**
        public void OnStatusMessage(string message)
        {
            if (InvokeRequired) { Invoke(new Action<string>(OnStatusMessage), message); return; }
            textBox1.AppendText(message + "\r\n");
            textBox1.ScrollToCaret();
        }
        public void OnError(string errorMessage)
        {
            if (InvokeRequired) { Invoke(new Action<string>(OnError), errorMessage); return; }
            textBox1.AppendText($"❌ {errorMessage}\r\n");
            textBox1.ScrollToCaret();
            MessageBox.Show(errorMessage, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        // ========== 明確介面實作 ==========
        void IMonsterTemplateEventHandler.OnStatusMessage(string message) => OnStatusMessage(message);
        void IMapFileEventHandler.OnStatusMessage(string message) => OnStatusMessage(message);
        void IMonsterTemplateEventHandler.OnError(string msg) => OnError(msg);
        void IMapFileEventHandler.OnError(string msg) => OnError(msg);

        // ================== 怪物模板 ==================
        public string GetMonstersDirectory() => ProjectMonstersDirectory;
        public void OnTemplatesLoaded(string monsterName, int templateCount)
        {
            if (InvokeRequired) { Invoke(new Action<string, int>(OnTemplatesLoaded), monsterName, templateCount); return; }
            textBox1.AppendText($"✅ 成功載入 {templateCount} 個 '{monsterName}' 的模板。\r\n");
            textBox1.ScrollToCaret();
        }

        // ================== 地圖檔案 ==================
        public string GetMapDataDirectory() => ProjectMapDataDirectory;
        public void OnMapLoaded(string mapFileName) { /* 若要附加動作可加在此 */ }
        public void OnNewMapCreated() { }
        public void OnMapSaved(string mapFileName, bool isNewFile)
        {
            if (InvokeRequired) { Invoke(new Action<string, bool>(OnMapSaved), mapFileName, isNewFile); return; }
            string message = isNewFile ? "新地圖儲存成功！" : "儲存成功！";
            MessageBox.Show(message, "地圖檔案管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        public void UpdateWindowTitle(string title)
        {
            if (InvokeRequired) { Invoke(new Action<string>(UpdateWindowTitle), title); return; }
            this.Text = title;
        }
        public void RefreshMinimap()
        {
            if (InvokeRequired) { Invoke(new Action(RefreshMinimap)); return; }
            pictureBoxMinimap.Invalidate();
        }
        #endregion

        #region 放大鏡/滑鼠事件/地圖編輯
        public Bitmap? GetSourceImage() => pictureBoxMinimap.Image as Bitmap;
        public decimal GetZoomFactor() => _configurationManager?.CurrentConfig?.General.ZoomFactor ?? numericUpDownZoom.Value;
        public Point? ConvertToImageCoordinates(Point mouseLocation) =>
            _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, mouseLocation);

        private void OnEditModeChanged(object? sender, EventArgs e)
        {
            if (sender is not RadioButton checkedButton || !checkedButton.Checked) return;
            EditMode selectedMode =
                checkedButton == rdo_PathMarker ? EditMode.Waypoint :
                checkedButton == rdo_SafeZone ? EditMode.SafeZone :
                checkedButton == rdo_RestrictedZone ? EditMode.RestrictedZone :
                checkedButton == rdo_RopeMarker ? EditMode.Rope :
                checkedButton == rdo_DeleteMarker ? EditMode.Delete :
                EditMode.None;
            _mapEditor.SetEditMode(selectedMode);
            pictureBoxMinimap.Invalidate();
        }
        private void pictureBoxMinimap_MouseDown(object sender, MouseEventArgs e)
        {
            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (!imgPoint.HasValue) return;
            if (e.Button == MouseButtons.Left) _mapEditor.HandleMouseClick(imgPoint.Value);
            else if (e.Button == MouseButtons.Right) _mapEditor.HandleRightClick();
            pictureBoxMinimap.Invalidate();
        }
        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            _floatingMagnifier?.UpdateMagnifier(e.Location, pictureBoxMinimap);
            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (!imgPoint.HasValue) return;
            _mapEditor.HandleMouseMove(imgPoint.Value);
            pictureBoxMinimap.Invalidate();
        }
        private void pictureBoxMinimap_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            pictureBoxMinimap.Invalidate();
        }
        private void pictureBoxMinimap_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (!imgPoint.HasValue) return;
            _mapEditor.HandleMouseClick(imgPoint.Value);
            pictureBoxMinimap.Invalidate();
        }
        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            _mapEditor.Render(
                e.Graphics,
                pointF => _editorMinimap.ConvertToDisplayCoordinates(pictureBoxMinimap, Point.Round(pointF)));
        }
        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e) => _floatingMagnifier?.Hide();
        #endregion

        #region UI/設定-分頁切換
        private void numericUpDownZoom_ValueChanged(object? sender, EventArgs e)
        {
            _configurationManager?.SetValue(cfg => cfg.General.ZoomFactor = numericUpDownZoom.Value, autoSave: true);
        }
        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_liveViewController != null && _liveViewController.IsRunning)
                await _liveViewController.StopAsync();

            if (tabControl1.SelectedIndex == 1)
                await UpdateMinimapSnapshotAsync();
            else if (tabControl1.SelectedIndex == 2)
                await _liveViewController.StartAsync(_configurationManager?.CurrentConfig ?? new AppConfig());
        }
        private async Task UpdateMinimapSnapshotAsync()
        {
            tabControl1.Enabled = false;
            pictureBoxMinimap.Image = null;
            textBox1.AppendText("正在載入小地圖快照...\r\n");
            try
            {
                var config = _configurationManager?.CurrentConfig ?? new AppConfig();
                Action<string> reporter = message => this.Invoke((Action)(() => textBox1.AppendText(message + "\r\n")));
                var result = await _editorMinimap.LoadSnapshotAsync(this.Handle, config, reporter);
                if (result?.MinimapImage != null)
                {
                    pictureBoxMinimap.Image?.Dispose();
                    pictureBoxMinimap.Image = result.MinimapImage;
                    textBox1.AppendText("✅ 小地圖快照載入成功。\r\n");
                }
                else
                    textBox1.AppendText("⚠️ 小地圖載入操作已取消或失敗。\r\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                textBox1.AppendText($"錯誤: {ex.Message}\r\n");
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }
        #endregion

        #region 路徑檔案管理事件/清理
        private void btn_SaveMap_Click(object sender, EventArgs e) => _mapFileManager?.SaveCurrentMap();
        private void btn_New_Click(object sender, EventArgs e) => _mapFileManager?.CreateNewMap();
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _floatingMagnifier?.Dispose();
            _liveViewController?.Dispose();
            _monsterTemplateService?.Dispose();
            _mapFileManager?.Dispose();
            base.OnFormClosed(e);
        }
        #endregion

        #region 專案目錄屬性/輔助方法
        private string ProjectDirectory
        {
            get
            {
                string currentDir = Application.StartupPath;
                DirectoryInfo? projectDir = Directory.GetParent(currentDir)?.Parent?.Parent?.Parent;
                return projectDir?.FullName ?? currentDir;
            }
        }
        private string ProjectMapDataDirectory => Path.Combine(ProjectDirectory, "MapData");
        private string ProjectMonstersDirectory => Path.Combine(ProjectDirectory, "Templates", "Monsters");
        #endregion
    }
}
