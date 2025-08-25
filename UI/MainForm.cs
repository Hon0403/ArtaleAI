using ArtaleAI.Config;
using ArtaleAI.Detection;
using ArtaleAI.Display;
using ArtaleAI.Interfaces;
using ArtaleAI.Minimap;
using ArtaleAI.Utils;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using ArtaleAI.API;

namespace ArtaleAI
{
    public partial class MainForm : Form, IMainFormEvents
    {

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr hModule);

        #region Private Fields

        private ConfigManager? _configurationManager;
        public ConfigManager? ConfigurationManager => _configurationManager;

        private readonly MinimapEditor _editorMinimap = new();
        private GraphicsCaptureItem? _selectedCaptureItem;
        private readonly MapEditor _mapEditor = new();
        private readonly MapData _mapData = new();
        private LiveViewController? _liveViewController;
        private FloatingMagnifier? _floatingMagnifier;
        private MonsterService? _monsterService;
        private MapFileManager? _mapFileManager;

        private MonsterImageFetcher? _monsterDownloader;

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

            var detectionSettings = _configurationManager?.CurrentConfig?.Templates?.MonsterDetection;
            TemplateMatcher.Initialize(detectionSettings);

            _liveViewController = new LiveViewController(textBox1, this, pictureBoxLiveView);
            _liveViewController.SetConfig(_configurationManager.CurrentConfig);

            // 🔧 只建立一次 MonsterService
            _monsterService = new MonsterService(cbo_MonsterTemplates, this);
            _monsterService.InitializeMonsterDropdown();
            _liveViewController.SetMonsterService(_monsterService);

            // 其他服務初始化
            _floatingMagnifier = new FloatingMagnifier(this);
            _mapFileManager = new MapFileManager(cbo_MapFiles, _mapEditor, this);
            _mapFileManager.InitializeMapFilesDropdown();
            _monsterDownloader = new MonsterImageFetcher(this);
            InitializeDetectionModeDropdown();
        }

        // 即時顯示事件
        public void OnFrameAvailable(Bitmap frame)
        {
            // 直接委托給 LiveViewController
            _liveViewController?.OnFrameAvailable(frame);
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

            numericUpDownZoom.Value = config.General.ZoomFactor;
            OnStatusMessage("配置檔案載入完成");
        }

        public void OnConfigSaved(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigSaved), config);
                return;
            }

            OnStatusMessage("設定已儲存");
        }

        public void OnConfigError(string errorMessage)
        {
            OnError($"設定錯誤: {errorMessage}");
        }

        #endregion

        #region 辨識模式控制

        /// <summary>
        /// 初始化辨識模式下拉選單
        /// </summary>
        private void InitializeDetectionModeDropdown()
        {
            cbo_DetectMode.Items.Clear();
            cbo_DetectMode.Items.Add("⚡ Basic - 基本匹配（最快）");
            cbo_DetectMode.Items.Add("🖼️ ContourOnly - 輪廓匹配（速度快）");
            cbo_DetectMode.Items.Add("⚖️ Grayscale - 灰階匹配（平衡）");
            cbo_DetectMode.Items.Add("🎯 Color - 彩色匹配（推薦）");
            cbo_DetectMode.Items.Add("🔍 TemplateFree - 自由偵測（無需模板）");

            // 從設定檔載入預設值
            var config = _configurationManager?.CurrentConfig;
            var detectionMode = config?.Templates?.MonsterDetection?.DetectionMode ?? "Color";

            // 映射到UI顯示
            var displayText = GetDisplayTextForMode(detectionMode);
            cbo_DetectMode.SelectedItem = displayText;

            // 綁定事件
            cbo_DetectMode.SelectedIndexChanged += OnDetectionModeChanged;

            OnStatusMessage($"智慧辨識模式初始化完成，預設：{detectionMode}");
        }

        /// <summary>
        /// 辨識模式變更事件 - 重構版
        /// </summary>
        private void OnDetectionModeChanged(object? sender, EventArgs e)
        {
            var selectedDisplayText = cbo_DetectMode.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedDisplayText))
            {
                var selectedMode = ExtractModeFromDisplayText(selectedDisplayText);
                var optimalOcclusion = GetOptimalOcclusionForMode(selectedMode);

                _configurationManager?.SetValue(cfg =>
                {
                    if (cfg.Templates?.MonsterDetection != null)
                    {
                        cfg.Templates.MonsterDetection.DetectionMode = selectedMode;
                        cfg.Templates.MonsterDetection.OcclusionHandling = optimalOcclusion.ToString();
                    }
                }, autoSave: true);

                OnStatusMessage($"辨識模式已切換至: {selectedMode} (自動使用 {optimalOcclusion} 遮擋處理)");
            }
        }

        /// <summary>
        /// 獲取模式的顯示文字
        /// </summary>
        private string GetDisplayTextForMode(string mode)
        {
            return mode switch
            {
                "Basic" => "⚡ Basic - 基本匹配（最快）",
                "ContourOnly" => "🖼️ ContourOnly - 輪廓匹配（速度快）",
                "Grayscale" => "⚖️ Grayscale - 灰階匹配（平衡）",
                "Color" => "🎯 Color - 彩色匹配（推薦）",
                "TemplateFree" => "🔍 TemplateFree - 自由偵測（無需模板）",
                _ => "🎯 Color - 彩色匹配（推薦）"
            };
        }

        /// <summary>
        /// 從顯示文字提取模式
        /// </summary>
        private string ExtractModeFromDisplayText(string displayText)
        {
            if (displayText.Contains("Basic")) return "Basic";
            if (displayText.Contains("ContourOnly")) return "ContourOnly";
            if (displayText.Contains("Grayscale")) return "Grayscale";
            if (displayText.Contains("Color")) return "Color";
            if (displayText.Contains("TemplateFree")) return "TemplateFree";
            return "Color";
        }

        /// <summary>
        /// 獲取模式的最佳遮擋處理
        /// </summary>
        private OcclusionHandling GetOptimalOcclusionForMode(string mode)
        {
            return mode switch
            {
                "Basic" => OcclusionHandling.None,
                "ContourOnly" => OcclusionHandling.MorphologyRepair,
                "Grayscale" => OcclusionHandling.DynamicThreshold,
                "Color" => OcclusionHandling.MultiScale,
                "TemplateFree" => OcclusionHandling.MorphologyRepair,
                _ => OcclusionHandling.None
            };
        }

        #endregion

        #region IMapFileEventHandler 實作

        public string GetMapDataDirectory() => common.GetMapDataDirectory();

        public void OnMapLoaded(string mapFileName)
        {
            OnStatusMessage($"成功載入地圖: {mapFileName}");
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
            OnStatusMessage($"地圖儲存: {mapFileName}");
        }

        public void OnNewMapCreated()
        {
            OnStatusMessage("已建立新地圖");
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
        public string GetMonstersDirectory() => common.GetMonstersDirectory();

        public void OnTemplatesLoaded(string monsterName, int templateCount)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, int>(OnTemplatesLoaded), monsterName, templateCount);
                return;
            }

            OnStatusMessage($"成功載入 {templateCount} 個 '{monsterName}' 的模板");
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
            // 🔧 完全停止並釋放所有分頁資源
            await StopAndReleaseAllResources();

            // 🔧 根據當前分頁啟動對應功能
            switch (tabControl1.SelectedIndex)
            {
                case 1: // 路徑編輯
                    await StartPathEditingModeAsync();
                    break;
                case 2: // 即時顯示
                    await StartLiveViewModeAsync();
                    break;
            }
        }

        /// <summary>
        /// 路徑編輯模式：只載入靜態小地圖
        /// </summary>
        private async Task StartPathEditingModeAsync()
        {
            OnStatusMessage("🗺️ 路徑編輯模式：載入靜態小地圖");

            tabControl1.Enabled = false;
            try
            {
                // 載入一次性的小地圖快照
                await LoadMinimapAsync(MinimapUsage.PathEditing);
                OnStatusMessage("✅ 路徑編輯模式就緒");
            }
            catch (Exception ex)
            {
                OnError($"路徑編輯模式啟動失敗: {ex.Message}");
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }

        /// <summary>
        /// 即時顯示模式：啟動所有即時處理功能
        /// </summary>
        private async Task StartLiveViewModeAsync()
        {
            OnStatusMessage("📺 即時顯示模式：啟動即時處理");

            var config = _configurationManager?.CurrentConfig ?? new AppConfig();

            try
            {
                // 1. 啟動即時視窗捕捉
                await _liveViewController.StartAsync(config);
                OnStatusMessage("   ✅ 即時視窗捕捉已啟動");

                // 2. 設置小地圖疊加層
                try
                {
                    await Task.Delay(500);
                    await LoadAndSetupMinimapOverlay();
                    OnStatusMessage("   ✅ 小地圖疊加層已設置");
                }
                catch (Exception ex)
                {
                    OnStatusMessage($"   ⚠️ 小地圖疊加層設置失敗: {ex.Message}");
                }

                // ❌ 刪除 Timer 啟動代碼
                /*
                if (_monsterService?.HasTemplates == true)
                {
                    await Task.Delay(500);
                    _backgroundMonsterTimer?.Change(0, 100); // 刪除這行
                    OnStatusMessage("   ✅ 背景偵測處理已啟動");
                }
                */

                // ✅ 改為這樣
                OnStatusMessage("🚀 即時顯示模式完全就緒！所有偵測在主執行緒中處理");
            }
            catch (Exception ex)
            {
                OnError($"即時顯示模式啟動失敗: {ex.Message}");
            }
        }


        /// <summary>
        /// 完全停止並釋放所有分頁處理資源
        /// </summary>
        private async Task StopAndReleaseAllResources()
        {
            OnStatusMessage("🛑 正在停止所有分頁處理並釋放資源...");

            // ❌ 刪除 Timer 停止代碼
            /*
            if (_backgroundMonsterTimer != null)
            {
                _backgroundMonsterTimer.Change(Timeout.Infinite, Timeout.Infinite);
                OnStatusMessage("   ✅ 背景怪物偵測已停止");
            }
            */

            // ✅ 保持即時顯示服務停止
            if (_liveViewController?.IsRunning == true)
            {
                await _liveViewController.StopAsync();
                OnStatusMessage("   ✅ 即時顯示服務已停止");
            }

            // ✅ 保持其他清理代碼
            TemplateMatcher.ClearCache();
            OnStatusMessage("   ✅ 模板匹配器快取已清理");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            OnStatusMessage("✅ 所有資源已完全釋放");
        }

        /// <summary>
        /// 即時顯示專用的載入方法
        /// </summary>
        private async Task LoadAndSetupMinimapOverlay()
        {
            try
            {
                await LoadMinimapAsync(MinimapUsage.LiveViewOverlay);
            }
            catch (Exception ex)
            {
                OnError($"設置小地圖疊加層失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 路徑編輯專用的載入方法
        /// </summary>
        private async Task UpdateMinimapSnapshotForPathEditingAsync()
        {
            tabControl1.Enabled = false;

            try
            {
                await LoadMinimapAsync(MinimapUsage.PathEditing);
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }

        /// <summary>
        /// 統一的小地圖載入方法 - 重構版
        /// </summary>
        private async Task<MinimapLoadResult?> LoadMinimapAsync(MinimapUsage usage)
        {
            var config = _configurationManager?.CurrentConfig ?? new AppConfig();
            Action<string> reporter = message => OnStatusMessage(message);

            OnStatusMessage($"正在載入小地圖快照 ({usage})...");
            var result = await _editorMinimap.LoadSnapshotAsync(this.Handle, config, reporter);

            if (result?.MinimapImage != null)
            {
                switch (usage)
                {
                    case MinimapUsage.PathEditing:
                        pictureBoxMinimap.Image?.Dispose();
                        pictureBoxMinimap.Image = result.MinimapImage;
                        _selectedCaptureItem = result.CaptureItem;
                        OnStatusMessage("✅ 路徑編輯小地圖載入完成");
                        break;

                    case MinimapUsage.LiveViewOverlay:
                        SetupLiveViewOverlay(result);
                        OnStatusMessage("✅ 即時顯示小地圖疊加層設置完成");
                        break;
                }

                return new MinimapLoadResult(result.MinimapImage, result.CaptureItem);
            }

            OnStatusMessage("❌ 小地圖載入失敗或已取消");
            return null;
        }

        /// <summary>
        /// 設置即時顯示的小地圖疊加層
        /// </summary>
        private void SetupLiveViewOverlay(MinimapSnapshotResult result)
        {
            if (result?.MinimapImage == null || _liveViewController == null) return;

            try
            {
                // 檢查動態辨識是否成功
                if (!result.MinimapScreenRect.HasValue)
                {
                    OnError("動態小地圖位置辨識失敗，無法設置疊加層");
                    return;
                }

                // 使用動態偵測到的玩家位置
                Rectangle playerRect;
                if (result.PlayerPosition.HasValue)
                {
                    var pos = result.PlayerPosition.Value;
                    playerRect = new Rectangle(pos.X - 8, pos.Y - 8, 16, 16);
                }
                else
                {
                    // 如果沒有玩家位置，使用空矩形
                    playerRect = Rectangle.Empty;
                    OnStatusMessage("⚠️ 未檢測到玩家位置");
                }

                // 🔧 直接使用動態偵測到的小地圖螢幕位置
                Rectangle minimapOnScreen = result.MinimapScreenRect.Value;

                // 設置小地圖疊加層
                _liveViewController.UpdateMinimapOverlay(
                    result.MinimapImage, minimapOnScreen, playerRect);

                OnStatusMessage($"✅ 小地圖疊加層已設置 ({minimapOnScreen.Width}x{minimapOnScreen.Height})");
            }
            catch (Exception ex)
            {
                OnError($"設置小地圖疊加層失敗: {ex.Message}");
            }
        }

        enum MinimapUsage { PathEditing, LiveViewOverlay }

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


        // 獲取當前小地圖位置
        private Rectangle? GetCurrentMinimapRect()
        {
            // 從 LiveViewController 獲取動態小地圖位置
            return _liveViewController?.GetMinimapRect();
        }

        #endregion

        #region 清理與釋放


        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                TemplateMatcher.ClearCache();

                // 清理所有資源
                _floatingMagnifier?.Dispose();
                _liveViewController?.Dispose();
                _monsterService?.Dispose();
                _monsterDownloader?.Dispose();
                pictureBoxMinimap.Image?.Dispose();

                OnStatusMessage("應用程式已清理完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清理資源時發生錯誤: {ex.Message}");
            }
            base.OnFormClosed(e);
        }

        #endregion


        private async void btn_DownloadMonster_Click(object sender, EventArgs e)
        {
            try
            {
                // 可以從文字框或其他控制項取得怪物名稱
                string monsterName = Microsoft.VisualBasic.Interaction.InputBox(
                    "請輸入怪物名稱:", "下載怪物模板", "");

                if (string.IsNullOrWhiteSpace(monsterName))
                    return;

                btn_DownloadMonster.Enabled = false;
                btn_DownloadMonster.Text = "下載中...";

                var result = await _monsterDownloader?.DownloadMonsterAsync(monsterName);

                if (result?.Success == true)
                {
                    // 重新載入怪物下拉選單
                    _monsterService?.InitializeMonsterDropdown();
                    OnStatusMessage($"下載完成！處理了 {result.DownloadedCount} 個檔案");
                }
            }
            catch (Exception ex)
            {
                OnError($"下載怪物時發生錯誤: {ex.Message}");
            }
            finally
            {
                btn_DownloadMonster.Enabled = true;
                btn_DownloadMonster.Text = "下載怪物";
            }
        }
    }
}
    
