using ArtaleAI.Config;
using ArtaleAI.Core;
using ArtaleAI.GameCapture;
using ArtaleAI.Minimap;
using ArtaleAI.Monster;
using ArtaleAI.UI;
using ArtaleAI.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Capture;

namespace ArtaleAI
{
    public partial class MainForm : Form,
        IConfigEventHandler,           // 配置管理
        IMapFileEventHandler,          // 地圖檔案管理  
        IApplicationEventHandler       // 統一應用事件
    {
        #region Private Fields

        private ConfigManager? _configurationManager;
        private readonly MinimapEditor _editorMinimap = new();
        private GraphicsCaptureItem? _selectedCaptureItem;
        private readonly MapEditor _mapEditor = new();
        private readonly MapData _mapData = new();
        private LiveViewController? _liveViewController;
        private FloatingMagnifier? _floatingMagnifier;
        private MonsterService? _monsterService;
        private MapFileManager? _mapFileManager;

        #endregion

        #region Constructor & Initialization

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            BindEvents();
        }

        private void InitializeServices()
        {
            // 配置管理統一化
            _configurationManager = new ConfigManager(this);
            _configurationManager.Load();

            // 初始化各項服務
            _liveViewController = new LiveViewController(pictureBoxLiveView, textBox1, this);
            _floatingMagnifier = new FloatingMagnifier(this);

            // ✅ 使用 MonsterService 而非 TemplateManager
            _monsterService = new MonsterService(cbo_MonsterTemplates, this);
            _monsterService.InitializeMonsterDropdown();

            _mapFileManager = new MapFileManager(cbo_MapFiles, _mapEditor, this);
            _mapFileManager.InitializeMapFilesDropdown();
        }

        private void BindEvents()
        {
            // UI 控制項事件綁定
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;
            numericUpDownZoom.ValueChanged += numericUpDownZoom_ValueChanged;

            // 地圖編輯模式事件
            rdo_PathMarker.CheckedChanged += OnEditModeChanged;
            rdo_SafeZone.CheckedChanged += OnEditModeChanged;
            rdo_RestrictedZone.CheckedChanged += OnEditModeChanged;
            rdo_RopeMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;

            // 小地圖滑鼠事件
            pictureBoxMinimap.Paint += pictureBoxMinimap_Paint;
            pictureBoxMinimap.MouseDown += pictureBoxMinimap_MouseDown;
            pictureBoxMinimap.MouseUp += pictureBoxMinimap_MouseUp;
            pictureBoxMinimap.MouseMove += pictureBoxMinimap_MouseMove;
            pictureBoxMinimap.MouseLeave += pictureBoxMinimap_MouseLeave;
            pictureBoxMinimap.MouseClick += pictureBoxMinimap_MouseClick;

            // 按鈕事件
            btn_SaveMap.Click += btn_SaveMap_Click;
            btn_New.Click += btn_New_Click;
        }

        #endregion

        #region IConfigEventHandler 實作

        public void OnConfigLoaded(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigLoaded), config);
                return;
            }

            numericUpDownZoom.Value = config.General?.ZoomFactor ?? 15;
            OnStatusMessage("✅ 配置檔案載入完成");
        }

        public void OnConfigSaved(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigSaved), config);
                return;
            }

            OnStatusMessage("✅ 設定已儲存");
        }

        public void OnConfigError(string errorMessage)
        {
            OnError($"設定錯誤: {errorMessage}");
        }

        #endregion

        #region IMapFileEventHandler 實作

        public string GetMapDataDirectory() => PathUtils.GetMapDataDirectory();

        public void OnMapLoaded(string mapFileName)
        {
            OnStatusMessage($"✅ 成功載入地圖: {mapFileName}");
        }

        public void OnMapSaved(string mapFileName, bool isNewFile)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, bool>(OnMapSaved), mapFileName, isNewFile);
                return;
            }

            string message = isNewFile ? "新地圖儲存成功！" : "儲存成功！";
            MessageBox.Show(message, "地圖檔案管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OnStatusMessage($"✅ 地圖儲存: {mapFileName}");
        }

        public void OnNewMapCreated()
        {
            OnStatusMessage("✅ 已建立新地圖");
        }

        public void UpdateWindowTitle(string title)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(UpdateWindowTitle), title);
                return;
            }

            this.Text = title;
        }

        public void RefreshMinimap()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(RefreshMinimap));
                return;
            }

            pictureBoxMinimap.Invalidate();
        }

        #endregion

        #region IApplicationEventHandler 實作

        // 放大鏡功能
        public Bitmap? GetSourceImage() => pictureBoxMinimap.Image as Bitmap;

        public decimal GetZoomFactor() =>
            _configurationManager?.CurrentConfig?.General?.ZoomFactor ?? numericUpDownZoom.Value;

        public Point? ConvertToImageCoordinates(Point mouseLocation) =>
            _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, mouseLocation);

        // 怪物模板功能
        public string GetMonstersDirectory() => PathUtils.GetMonstersDirectory();

        public void OnTemplatesLoaded(string monsterName, int templateCount)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, int>(OnTemplatesLoaded), monsterName, templateCount);
                return;
            }

            OnStatusMessage($"✅ 成功載入 {templateCount} 個 '{monsterName}' 的模板");
        }

        // 即時顯示功能
        public void OnFrameAvailable(Bitmap frame)
        {
            // 這個方法會由 LiveViewController 內部處理
            // 保留此實作以符合介面需求
        }

        #endregion

        #region 統一狀態訊息處理

        public void OnStatusMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnStatusMessage), message);
                return;
            }

            textBox1.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\r\n");
            textBox1.ScrollToCaret();
        }

        public void OnError(string errorMessage)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnError), errorMessage);
                return;
            }

            textBox1.AppendText($"{DateTime.Now:HH:mm:ss} - ❌ {errorMessage}\r\n");
            textBox1.ScrollToCaret();
            MessageBox.Show(errorMessage, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion

        #region UI 事件處理

        private void numericUpDownZoom_ValueChanged(object? sender, EventArgs e)
        {
            _configurationManager?.SetValue(cfg =>
            {
                if (cfg.General != null)
                    cfg.General.ZoomFactor = numericUpDownZoom.Value;
            }, autoSave: true);
        }

        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // 停止當前的即時顯示
            if (_liveViewController != null && _liveViewController.IsRunning)
                await _liveViewController.StopAsync();

            switch (tabControl1.SelectedIndex)
            {
                case 1: // 小地圖編輯分頁
                    await UpdateMinimapSnapshotAsync();
                    break;

                case 2: // 即時顯示分頁
                    var config = _configurationManager?.CurrentConfig ?? new AppConfig();
                    await _liveViewController.StartAsync(config);
                    tmr_MonsterMatch.Start();
                    break;
            }
        }

        private async Task UpdateMinimapSnapshotAsync()
        {
            tabControl1.Enabled = false;
            pictureBoxMinimap.Image?.Dispose();
            pictureBoxMinimap.Image = null;

            OnStatusMessage("正在載入小地圖快照...");

            try
            {
                var config = _configurationManager?.CurrentConfig ?? new AppConfig();
                Action<string> reporter = message => OnStatusMessage(message);

                var result = await _editorMinimap.LoadSnapshotAsync(this.Handle, config, reporter);

                if (result?.MinimapImage != null)
                {
                    pictureBoxMinimap.Image = result.MinimapImage;
                    OnStatusMessage("✅ 小地圖快照載入成功");

                    // 更新選中的捕捉項目
                    _selectedCaptureItem = result.CaptureItem;
                }
                else
                {
                    OnStatusMessage("⚠️ 小地圖載入操作已取消或失敗");
                }
            }
            catch (Exception ex)
            {
                OnError($"載入小地圖時發生錯誤: {ex.Message}");
                MessageBox.Show(ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }

        #endregion

        #region 地圖編輯事件

        private void OnEditModeChanged(object? sender, EventArgs e)
        {
            if (sender is not RadioButton checkedButton || !checkedButton.Checked)
                return;

            EditMode selectedMode = checkedButton.Name switch
            {
                nameof(rdo_PathMarker) => EditMode.Waypoint,
                nameof(rdo_SafeZone) => EditMode.SafeZone,
                nameof(rdo_RestrictedZone) => EditMode.RestrictedZone,
                nameof(rdo_RopeMarker) => EditMode.Rope,
                nameof(rdo_DeleteMarker) => EditMode.Delete,
                _ => EditMode.None
            };

            _mapEditor.SetEditMode(selectedMode);
            pictureBoxMinimap.Invalidate();

            OnStatusMessage($"編輯模式切換至: {selectedMode}");
        }

        #endregion

        #region PictureBox 滑鼠事件

        private void pictureBoxMinimap_MouseDown(object sender, MouseEventArgs e)
        {
            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (!imgPoint.HasValue) return;

            if (e.Button == MouseButtons.Left)
                _mapEditor.HandleMouseClick(imgPoint.Value);
            else if (e.Button == MouseButtons.Right)
                _mapEditor.HandleRightClick();

            pictureBoxMinimap.Invalidate();
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            // 更新放大鏡
            _floatingMagnifier?.UpdateMagnifier(e.Location, pictureBoxMinimap);

            // 更新地圖編輯器的滑鼠位置
            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (imgPoint.HasValue)
            {
                _mapEditor.HandleMouseMove(imgPoint.Value);
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                pictureBoxMinimap.Invalidate();
        }

        private void pictureBoxMinimap_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            var imgPoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (imgPoint.HasValue)
            {
                _mapEditor.HandleMouseClick(imgPoint.Value);
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            _mapEditor.Render(
                e.Graphics,
                pointF => _editorMinimap.ConvertToDisplayCoordinates(pictureBoxMinimap, Point.Round(pointF)));
        }

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            _floatingMagnifier?.Hide();
        }

        #endregion

        #region 按鈕事件

        private void btn_SaveMap_Click(object sender, EventArgs e)
        {
            try
            {
                _mapFileManager?.SaveCurrentMap();
            }
            catch (Exception ex)
            {
                OnError($"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        private void btn_New_Click(object sender, EventArgs e)
        {
            try
            {
                _mapFileManager?.CreateNewMap();
            }
            catch (Exception ex)
            {
                OnError($"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        #endregion

        #region 怪物匹配

        private void tmr_MonsterMatch_Tick(object sender, EventArgs e)
        {
            // 1. 取得遊戲畫面截圖
            var capturer = _liveViewController?.Capturer;
            if (capturer == null) return;

            Bitmap? screenCapture = capturer.TryGetNextFrame();

            // 2. ✅ 修正：使用 MonsterService 的方法
            if (screenCapture != null && _monsterService != null && _monsterService.HasTemplates)
            {
                try
                {
                    // 3. ✅ 使用 MonsterService 的偵測方法
                    var monsterResults = _monsterService.DetectMonstersOnScreen(screenCapture);

                    // 4. 處理找到的結果
                    if (monsterResults.Any())
                    {
                        OnStatusMessage($"🎯 找到了 {monsterResults.Count} 隻怪物！");

                        // ✅ 使用 MonsterService 的模板資訊
                        var templateSize = _monsterService.GetTemplate(0)?.Size ?? new Size(32, 32);
                        var locations = monsterResults.Select(r => r.Location).ToList();

                        _liveViewController?.DrawMonsterRectangles(locations, templateSize);
                    }
                }
                catch (Exception ex)
                {
                    OnError($"怪物匹配時發生錯誤: {ex.Message}");
                }
                finally
                {
                    screenCapture.Dispose();
                }
            }
        }

        #endregion

        #region 清理與釋放

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                // 停止計時器
                tmr_MonsterMatch?.Stop();

                // 清理所有資源
                _floatingMagnifier?.Dispose();
                _liveViewController?.Dispose();
                _monsterService?.Dispose();
                _mapFileManager?.Dispose();

                // 確保圖片資源被釋放
                pictureBoxMinimap.Image?.Dispose();

                OnStatusMessage("✅ 應用程式已清理完成");
            }
            catch (Exception ex)
            {
                // 清理過程中的錯誤不應該阻止程式關閉
                System.Diagnostics.Debug.WriteLine($"清理資源時發生錯誤: {ex.Message}");
            }

            base.OnFormClosed(e);
        }

        #endregion
    }
}
