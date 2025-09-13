using ArtaleAI.API;
using ArtaleAI.Config;
using ArtaleAI.Detection;
using ArtaleAI.Display;
using ArtaleAI.GameWindow;
using ArtaleAI.Minimap;
using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdRect = System.Drawing.Rectangle;
using SdSize = System.Drawing.Size;

namespace ArtaleAI
{
    public partial class MainForm : Form
    {

        #region Private Fields - 完整版
        private ConfigManager? _configurationManager;

        public ConfigManager? ConfigurationManager => _configurationManager;
        private MapDetector? _mapDetector;
        private GraphicsCaptureItem? _selectedCaptureItem;
        private MapEditor? _mapEditor;
        private List<Bitmap> _currentMonsterTemplates = new();
        private string? _currentMonsterName;

        private int _consecutiveSkippedFrames = 0;
        // 檢測狀態管理
        private Rectangle? _currentMinimapRect;
        private List<Rectangle> _currentBloodBars = new();
        private List<Rectangle> _currentDetectionBoxes = new();
        private List<Rectangle> _currentAttackRangeBoxes = new();
        private List<MonsterRenderInfo> _currentMonsters = new();
        private DateTime _lastBloodBarDetection = DateTime.MinValue;
        private DateTime _lastMonsterDetection = DateTime.MinValue;

        private GraphicsCapturer? _capturer;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _captureTask;
        private bool _isLiveViewRunning = false;

        // 圖像同步鎖
        private Bitmap? _currentDisplayFrame;

        // 其他服務
        private FloatingMagnifier? _floatingMagnifier;
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
            _configurationManager = new ConfigManager(this);
            _configurationManager.Load();

            var mapEditorSettings = _configurationManager?.CurrentConfig?.MapEditor;
            var trajectorySettings = _configurationManager?.CurrentConfig?.Trajectory;
            _mapEditor = new MapEditor(mapEditorSettings, trajectorySettings);

            // 初始化檢測服務
            var detectionSettings = _configurationManager?.CurrentConfig?.Templates?.MonsterDetection;
            var templateMatchingSettings = _configurationManager?.CurrentConfig?.TemplateMatching;

            TemplateMatcher.Initialize(detectionSettings, templateMatchingSettings, _configurationManager?.CurrentConfig);
            _mapDetector = new MapDetector(_configurationManager?.CurrentConfig ?? new AppConfig());

            // 其他服務
            var uiSettings = _configurationManager?.CurrentConfig?.Ui;
            _floatingMagnifier = new FloatingMagnifier(this, uiSettings);
            _mapFileManager = new MapFileManager(cbo_MapFiles, _mapEditor, this);
            _mapFileManager.InitializeMapFilesDropdown();
            _monsterDownloader = new MonsterImageFetcher(this);

            InitializeMonsterTemplateSystem();

            InitializeDetectionModeDropdown();
        }

        private void InitializeMonsterTemplateSystem()
        {
            var monsterNames = MonsterTemplateStore.GetAvailableMonsterNames(GetMonstersDirectory());
            cbo_MonsterTemplates.Items.Clear();
            foreach (var name in monsterNames)
                cbo_MonsterTemplates.Items.Add(name);

            cbo_MonsterTemplates.SelectedIndexChanged += OnMonsterSelectionChanged;
            OnStatusMessage($"成功載入 {monsterNames.Count} 種怪物模板選項");
        }

        // 血條檢測條件判斷
        private bool ShouldDetectBloodBar(DateTime now, DetectionPerformanceSettings config)
        {
            var elapsed = (now - _lastBloodBarDetection).TotalMilliseconds;
            return elapsed >= config.BloodBarDetectIntervalMs || _currentBloodBars.Count == 0;
        }

        // 怪物檢測條件判斷
        private bool ShouldDetectMonster(DateTime now, DetectionPerformanceSettings config)
        {
            var elapsed = (now - _lastMonsterDetection).TotalMilliseconds;
            return elapsed >= config.MonsterDetectIntervalMs || _currentMonsters.Count == 0;
        }

        private void UpdateDisplaySafely(Bitmap newFrame)
        {
            if (newFrame == null) return;

            if (pictureBoxLiveView.InvokeRequired)
            {
                pictureBoxLiveView.BeginInvoke(() => {
                    UpdateFrameInternal(newFrame);
                });
            }
            else
            {
                UpdateFrameInternal(newFrame);
            }
        }

        private void UpdateFrameInternal(Bitmap newFrame)
        {
            if (pictureBoxLiveView.IsDisposed)
            {
                newFrame?.Dispose();
                return;
            }

            var oldImage = pictureBoxLiveView.Image;
            var oldFrame = _currentDisplayFrame;

            _currentDisplayFrame = newFrame;
            pictureBoxLiveView.Image = newFrame;

            // 只釋放舊的資源
            if (oldImage != newFrame)
            {
                oldImage?.Dispose();
            }

            if (oldFrame != newFrame)
            {
                oldFrame?.Dispose();
            }
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

            var config = _configurationManager.CurrentConfig;
            var detectionModes = config.DetectionModes;

            if (detectionModes?.DisplayOrder != null && detectionModes.DisplayNames != null)
            {
                try
                {
                    // 按設定檔順序添加項目
                    foreach (var mode in detectionModes.DisplayOrder)
                    {
                        if (detectionModes.DisplayNames.TryGetValue(mode, out var displayName))
                        {
                            cbo_DetectMode.Items.Add(displayName);
                        }
                    }

                    // 設置預設選擇
                    var defaultMode = detectionModes.DefaultMode;
                    if (detectionModes.DisplayNames.TryGetValue(defaultMode, out var defaultDisplay))
                    {
                        cbo_DetectMode.SelectedItem = defaultDisplay;
                    }

                    OnStatusMessage($"智慧辨識模式初始化完成，預設：{defaultMode}");
                }
                catch (Exception ex)
                {
                    OnError($"辨識模式設定檔格式錯誤: {ex.Message}");
                }
            }
            else
            {
                OnError("辨識模式設定檔缺少必要配置，使用預設模式");
            }

            // 綁定事件
            cbo_DetectMode.SelectedIndexChanged += OnDetectionModeChanged;
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
        /// 從顯示文字提取模式
        /// </summary>
        private string ExtractModeFromDisplayText(string displayText)
        {
            var config = _configurationManager.CurrentConfig;
            var detectionModes = config.DetectionModes;

            //  優先使用設定檔映射
            if (detectionModes?.DisplayNames != null)
            {
                var mode = detectionModes.DisplayNames.FirstOrDefault(kvp => kvp.Value == displayText).Key;
                if (!string.IsNullOrEmpty(mode))
                {
                    return mode;
                }
            }

            OnError($"辨識模式設定檔讀取失敗或格式錯誤：'{displayText}'");
            return config.DetectionModes.DefaultMode;
        }

        /// <summary>
        /// 獲取模式的最佳遮擋處理 - 基於設定檔
        /// </summary>
        private OcclusionHandling GetOptimalOcclusionForMode(string mode)
        {
            var config = _configurationManager?.CurrentConfig;
            var occlusionMappings = config?.DetectionModes?.OcclusionMappings;

            if (occlusionMappings?.TryGetValue(mode, out var occlusionString) == true)
            {
                return Enum.TryParse<OcclusionHandling>(occlusionString, out var result)
                    ? result
                    : OcclusionHandling.None;
            }

            OnError($"找不到模式 '{mode}' 的遮擋處理設定");
            return OcclusionHandling.None;
        }

        #endregion

        #region IMapFileEventHandler 實作

        public string GetMapDataDirectory() => UtilityHelper.GetMapDataDirectory();

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
            _configurationManager.CurrentConfig.General.ZoomFactor;

        public PointF? ConvertToImageCoordinates(SdPoint mouseLocation)
        {
            if (pictureBoxMinimap.Image == null) return null;

            var clientSize = pictureBoxMinimap.ClientSize;
            var imageSize = pictureBoxMinimap.Image.Size;

            // 🔍 檢查這裡的比例計算是否正確
            float ratioX = (float)clientSize.Width / imageSize.Width;
            float ratioY = (float)clientSize.Height / imageSize.Height;
            float ratio = Math.Min(ratioX, ratioY); // 保持比例

            // 計算實際顯示區域
            int displayWidth = (int)(imageSize.Width * ratio);
            int displayHeight = (int)(imageSize.Height * ratio);
            int offsetX = (clientSize.Width - displayWidth) / 2;
            int offsetY = (clientSize.Height - displayHeight) / 2;

            var displayRect = new Rectangle(offsetX, offsetY, displayWidth, displayHeight);
            if (!displayRect.Contains(mouseLocation)) return null;

            // 🎯 修正：確保座標轉換的精確性
            float imageX = (mouseLocation.X - offsetX) / ratio;
            float imageY = (mouseLocation.Y - offsetY) / ratio;

            return new PointF(imageX, imageY);
        }


        // 怪物模板功能
        public string GetMonstersDirectory() => UtilityHelper.GetMonstersDirectory();

        public void OnTemplatesLoaded(string monsterName, int templateCount)
        {
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
            //  完全停止並釋放所有分頁資源
            await StopAndReleaseAllResources();

            //  根據當前分頁啟動對應功能
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
            OnStatusMessage("🗺️ 路徑編輯模式：載入靜態小地圖（Mat域優化）");
            tabControl1.Enabled = false;

            try
            {
                // 🚀 使用修改後的Mat域處理流程
                var result = await LoadMinimapWithMatOptimized(MinimapUsage.PathEditing);

                if (result?.MinimapImage != null)
                {
                    // 📷 設置小地圖到 UI
                    pictureBoxMinimap.Image?.Dispose();
                    pictureBoxMinimap.Image = result.MinimapImage;

                    OnStatusMessage("✅ 路徑編輯模式就緒（Mat域無損精度）");
                }
                else
                {
                    OnError("無法載入小地圖");
                }
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

        private async Task<MinimapSnapshotResult?> LoadMinimapWithMatOptimized(MinimapUsage usage)
        {
            var config = _configurationManager?.CurrentConfig ?? new AppConfig();

            try
            {
                OnStatusMessage("🎯 建立捕捉器");

                // 🎯 建立捕捉器
                var captureItem = WindowFinder.TryCreateItemForWindow(config.General.GameWindowTitle);
                if (captureItem == null)
                {
                    OnError($"找不到遊戲視窗: {config.General.GameWindowTitle}");
                    return null;
                }

                OnStatusMessage("📸 執行Mat域小地圖處理");

                // 🚀 使用修改後的Mat域處理方法
                return await _mapDetector?.GetSnapshotAsync(this.Handle, config, captureItem, OnStatusMessage);
            }
            catch (Exception ex)
            {
                OnError($"Mat域小地圖載入失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 即時顯示模式：啟動所有即時處理功能
        /// </summary>
        private async Task StartLiveViewModeAsync()
        {
            OnStatusMessage("📺 即時顯示模式：啟動");
            var config = _configurationManager?.CurrentConfig ?? new AppConfig();

            try
            {
                await StartLiveViewAsync(config);
                OnStatusMessage("✅ 即時顯示模式就緒");
            }
            catch (Exception ex)
            {
                OnError($"即時顯示模式啟動失敗: {ex.Message}");
            }
        }
        public bool IsLiveViewRunning => _isLiveViewRunning;

        public async Task StartLiveViewAsync(AppConfig config)
        {
            if (_isLiveViewRunning)
            {
                OnStatusMessage("即時顯示已經在運行中");
                return;
            }

            try
            {
                OnStatusMessage("正在尋找遊戲視窗...");

                // 尋找遊戲視窗
                var captureItem = WindowFinder.TryCreateItemForWindow(config.General.GameWindowTitle);
                if (captureItem == null)
                {
                    OnError($"找不到名為 '{config.General.GameWindowTitle}' 的遊戲視窗");
                    return;
                }

                OnStatusMessage("✅ 成功找到遊戲視窗");

                // 建立捕捉器
                _capturer = new GraphicsCapturer(captureItem);
                _cancellationTokenSource = new CancellationTokenSource();
                OnStatusMessage("🎥 即時顯示已啟動");
                _isLiveViewRunning = true;

                // 開始捕捉任務
                _captureTask = CaptureLoopAsync(_cancellationTokenSource.Token);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                OnError($"啟動即時顯示失敗: {ex.Message}");
                await StopLiveViewAsync();
            }
        }

        public async Task StopLiveViewAsync()
        {
            if (!_isLiveViewRunning) return;

            try
            {
                _isLiveViewRunning = false;
                _cancellationTokenSource?.Cancel();

                if (_captureTask != null && !_captureTask.IsCompleted)
                {
                    await _captureTask;
                }

                OnStatusMessage("🛑 即時顯示已停止");
            }
            catch (TaskCanceledException)
            {
                // 正常的取消操作，忽略
            }
            catch (Exception ex)
            {
                OnError($"停止即時顯示時發生錯誤: {ex.Message}");
            }
            finally
            {
                _capturer?.Dispose();
                _capturer = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _captureTask = null;
            }
        }

        private async Task CaptureLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                var config = _configurationManager?.CurrentConfig;
                int targetFPS = config.WindowCapture.CaptureFrameRate;
                int captureDelayMs = 1000 / targetFPS;

                OnStatusMessage($"🎥 BGR優化捕捉設定: {targetFPS} FPS (間隔 {captureDelayMs}ms)");

                await Task.Yield();

                while (!cancellationToken.IsCancellationRequested && _capturer != null)
                {
                    // 🚀 直接獲取最高效的 BGR Mat
                    using var frameMat = _capturer.TryGetNextMat();
                    if (frameMat != null)
                    {
                        try
                        {
                            // 🎯 直接使用 Mat 進行所有處理，這是最高效能的做法
                            OnFrameAvailableOptimized(frameMat);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"BGR處理幀失敗: {ex.Message}");
                        }
                    }

                    await Task.Delay(captureDelayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnError($"BGR捕捉過程發生錯誤: {ex.Message}");
            }
        }

        // 🚀 新增：優化版的幀處理方法
        public void OnFrameAvailableOptimized(Mat frameMat)
        {
            if (frameMat?.Empty() != false) return;

            // 🔍 驗證格式正確性
            Debug.Assert(frameMat.Channels() == 3, "期望 BGR 三通道格式");
            Debug.Assert(frameMat.Type() == MatType.CV_8UC3, "期望 CV_8UC3 類型");

            try
            {
                var config = _configurationManager?.CurrentConfig;
                if (config?.DetectionPerformance == null) return;

                // 🎯 只保留必要的處理，移除角色檢測
                ProcessBloodBarsOptimized(frameMat);
                ProcessMonstersOptimized(frameMat);
                RenderAndDisplayOverlaysOptimized(frameMat);
            }
            catch (Exception ex)
            {
                OnStatusMessage($"BGR幀處理失敗: {ex.Message}");
            }
        }

        private void ProcessBloodBarsOptimized(Mat frameMat)
        {
            try
            {
                var config = _configurationManager?.CurrentConfig;
                if (!ShouldDetectBloodBar(DateTime.UtcNow, config.DetectionPerformance)) return;

                // 🎯 直接使用BGR Mat
                using var cameraArea = BloodBarDetector.ExtractCameraArea(frameMat, null, config.PartyRedBar, out int cameraOffsetY);

                // 🚀 關鍵：從RGB轉HSV
                using var hsvImage = UtilityHelper.ConvertToHSV(cameraArea); // 這裡已經是RGB2HSV
                using var redMask = BloodBarDetector.CreateRedMask(hsvImage, config.PartyRedBar);

                var bloodBarRect = BloodBarDetector.FindBestRedBar(redMask, config.PartyRedBar);
                if (bloodBarRect.HasValue)
                {
                    var screenBloodBar = BloodBarDetector.ToScreenCoordinates(bloodBarRect.Value, cameraOffsetY);
                    _currentBloodBars = new List<Rectangle> { screenBloodBar };
                    _currentDetectionBoxes = BloodBarDetector.CalculateDetectionBoxes(screenBloodBar, config.PartyRedBar);
                    _currentAttackRangeBoxes = BloodBarDetector.CalculateAttackRangeBoxes(screenBloodBar, config.AttackRange);
                    _lastBloodBarDetection = DateTime.UtcNow;

                    OnStatusMessage($"✅ BGR優化血條檢測成功: {screenBloodBar}");
                }
            }
            catch (Exception ex)
            {
                OnStatusMessage($"❌ BGR血條檢測異常: {ex.Message}");
            }
        }

        // 🚀 優化版怪物檢測
        private void ProcessMonstersOptimized(Mat frameMat)
        {
            try
            {
                var config = _configurationManager?.CurrentConfig;
                if (!ShouldDetectMonster(DateTime.UtcNow, config.DetectionPerformance)) return;

                if (!_currentDetectionBoxes.Any())
                {
                    OnStatusMessage("❌ 無檢測區域，等待血條檢測成功");
                    return;
                }

                OnStatusMessage($"🎯 開始BGR優化怪物檢測，使用 {_currentDetectionBoxes.Count} 個檢測框");

                var templateData = GetTemplateDataSafely();
                if (string.IsNullOrEmpty(templateData.SelectedMonsterName) || !templateData.Templates.Any())
                {
                    OnStatusMessage("❌ 沒有可用模板!");
                    return;
                }

                var allResults = new List<MonsterRenderInfo>();

                foreach (var detectionBox in _currentDetectionBoxes)
                {
                    // 裁切BGR檢測區域
                    var frameBounds = new Rect(0, 0, frameMat.Width, frameMat.Height);
                    var cropRect = new Rect(detectionBox.X, detectionBox.Y, detectionBox.Width, detectionBox.Height);
                    var validCropRect = frameBounds & cropRect; // 計算交集

                    if (validCropRect.Width < 10 || validCropRect.Height < 10) continue;

                    using var croppedMat = frameMat[validCropRect].Clone();
                    OnStatusMessage($"🎯 BGR Mat直接優化檢測: {croppedMat.Width}x{croppedMat.Height}");

                    // 🚀 使用優化版Mat檢測
                    var results = TemplateMatcher.FindMonstersWithMatOptimized(
                        croppedMat,
                        templateData.Templates,
                        Enum.Parse<MonsterDetectionMode>(templateData.DetectionMode),
                        templateData.Threshold,
                        templateData.SelectedMonsterName);

                    // 轉換座標到全局
                    foreach (var result in results)
                    {
                        var monster = new MonsterRenderInfo
                        {
                            MonsterName = result.Name,
                            Location = new SdPoint(result.Position.X + validCropRect.X, result.Position.Y + validCropRect.Y),
                            Size = result.Size,
                            Confidence = result.Confidence
                        };
                        allResults.Add(monster);
                        OnStatusMessage($"✅ BGR檢測到怪物: {monster.MonsterName} (信心度: {monster.Confidence:F4})");
                    }
                }

                _currentMonsters = allResults;
                _lastMonsterDetection = DateTime.UtcNow;

                if (allResults.Any())
                    OnStatusMessage($"🎯 BGR優化檢測完成，發現 {allResults.Count} 個怪物");
                else
                    OnStatusMessage("⚠️ BGR優化檢測未發現怪物");
            }
            catch (Exception ex)
            {
                OnStatusMessage($"❌ BGR怪物檢測異常: {ex.Message}");
            }
        }

        // 🚀 優化版渲染方法
        private void RenderAndDisplayOverlaysOptimized(Mat frameMat)
        {
            try
            {
                using var displayBitmap = frameMat.ToBitmap();

                // 🚀 直接內嵌渲染邏輯，避免調用已刪除的BGR方法
                var config = _configurationManager?.CurrentConfig;
                if (config?.OverlayStyle == null)
                {
                    UpdateDisplaySafely(new Bitmap(displayBitmap));
                    return;
                }

                var monsterItems = new List<IRenderItem>();
                var partyRedBarItems = new List<IRenderItem>();
                var detectionBoxItems = new List<IRenderItem>();
                var attackRangeItems = new List<IRenderItem>();

                if (_currentBloodBars.Any())
                {
                    partyRedBarItems.AddRange(_currentBloodBars.Select(rect =>
                        new PartyRedBarRenderItem(config.OverlayStyle.PartyRedBar) { BoundingBox = rect }));
                }

                if (_currentDetectionBoxes.Any())
                {
                    detectionBoxItems.AddRange(_currentDetectionBoxes.Select(rect =>
                        new DetectionBoxRenderItem(config.OverlayStyle.DetectionBox) { BoundingBox = rect }));
                }

                if (_currentAttackRangeBoxes.Any())
                {
                    attackRangeItems.AddRange(_currentAttackRangeBoxes.Select(rect =>
                        new AttackRangeRenderItem(config.OverlayStyle.AttackRange) { BoundingBox = rect }));
                }

                if (_currentMonsters.Any())
                {
                    monsterItems.AddRange(_currentMonsters.Select(m =>
                        new MonsterRenderItem(config.OverlayStyle.Monster)
                        {
                            BoundingBox = new Rectangle(m.Location.X, m.Location.Y, m.Size.Width, m.Size.Height),
                            MonsterName = m.MonsterName,
                            Confidence = m.Confidence
                        }));
                }

                var allDetectionItems = new List<IRenderItem>();
                allDetectionItems.AddRange(detectionBoxItems);
                allDetectionItems.AddRange(attackRangeItems);

                var renderedFrame = SimpleRenderer.RenderOverlays(
                    displayBitmap,
                    monsterItems,
                    null,
                    null,
                    partyRedBarItems,
                    allDetectionItems
                );

                if (renderedFrame != null)
                {
                    UpdateDisplaySafely(renderedFrame);
                }
            }
            catch (Exception ex)
            {
                OnError($"BGR優化渲染失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 完全停止並釋放所有分頁處理資源
        /// </summary>
        private async Task StopAndReleaseAllResources()
        {
            try
            {
                // 停止即時顯示
                if (_isLiveViewRunning)
                {
                    await StopLiveViewAsync();
                }

                // 清理顯示畫面
                var oldLiveImage = pictureBoxLiveView.Image;
                pictureBoxLiveView.Image = null;
                oldLiveImage?.Dispose();

                OnStatusMessage("🗑️ 資源已清理");
            }
            catch (Exception ex)
            {
                OnError($"清理資源時發生錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 統一的小地圖載入方法 - 重構版
        /// </summary>
        private async Task LoadMinimapAsync(MinimapUsage usage)
        {
            try
            {
                OnStatusMessage($"🗺️ 正在載入小地圖 ({usage} 模式)...");

                var snapshot = await _mapDetector!.GetSnapshotAsync(
                    this.Handle,
                    _configurationManager!.CurrentConfig!,
                    _selectedCaptureItem,
                    OnStatusMessage
                );

                if (snapshot?.MinimapImage != null)
                {
                    // 🔍 調試：保存路徑編輯模式的小地圖
                    snapshot.MinimapImage.Save($"debug_PathEditing_minimap_{DateTime.Now:HHmmss}.png");

                    pictureBoxMinimap.Image?.Dispose();
                    pictureBoxMinimap.Image = snapshot.MinimapImage;

                    OnStatusMessage($"✅ 路徑編輯小地圖尺寸: {snapshot.MinimapImage.Width}x{snapshot.MinimapImage.Height}");
                }
            }
            catch (Exception ex)
            {
                OnError($"載入小地圖失敗: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// 設置即時顯示的小地圖疊加層
        /// </summary>
        private void SetupLiveViewOverlay(MinimapSnapshotResult result)
        {
            if (result?.MinimapImage == null) return;

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

                //  直接使用動態偵測到的小地圖螢幕位置
                Rectangle minimapOnScreen = result.MinimapScreenRect.Value;


                OnStatusMessage($" 小地圖疊加層已設置 ({minimapOnScreen.Width}x{minimapOnScreen.Height})");
            }
            catch (Exception ex)
            {
                OnError($"設置小地圖疊加層失敗: {ex.Message}");
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
            if (e.Button == MouseButtons.Left)
            {
                var imgPoint = ConvertToImageCoordinates(e.Location);
                if (imgPoint.HasValue)
                {
                    _mapEditor?.StartDrawing(imgPoint.Value);
                    pictureBoxMinimap.Invalidate();
                }
            }
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            // 放大鏡功能
            _floatingMagnifier?.UpdateMagnifier(e.Location, pictureBoxMinimap);

            var imgPoint = ConvertToImageCoordinates(e.Location);
            if (imgPoint.HasValue)
            {
                _mapEditor?.UpdatePreview(imgPoint.Value);
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var imgPoint = ConvertToImageCoordinates(e.Location);
                if (imgPoint.HasValue)
                {
                    _mapEditor?.FinishDrawing(imgPoint.Value);
                    pictureBoxMinimap.Invalidate();
                }
            }
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            _mapEditor?.Render(e.Graphics, pointF => ConvertToDisplayCoordinates(SdPoint.Round(pointF)));
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


        #region 清理與釋放


        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                _currentDisplayFrame?.Dispose();
                _currentDisplayFrame = null;

                ClearCurrentMonsterTemplates();
                UtilityHelper.ClearMonsterTemplateCache();

                // 清理其他資源
                _floatingMagnifier?.Dispose();
                _monsterDownloader?.Dispose();
                _mapDetector?.Dispose();
                pictureBoxMinimap.Image?.Dispose();
                pictureBoxLiveView.Image?.Dispose();

                OnStatusMessage("應用程式已清理完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清理資源時發生錯誤: {ex.Message}");
            }

            base.OnFormClosed(e);
        }

        #endregion

        /// <summary>
        /// 安全獲取模板資料的方法 - UI執行緒安全
        /// </summary>
        private TemplateData GetTemplateDataSafely()
        {
            if (InvokeRequired)
                return (TemplateData)Invoke(() => GetTemplateDataSafely());

            var selectedMonster = cbo_MonsterTemplates.SelectedItem?.ToString();
            var selectedModeDisplay = cbo_DetectMode.SelectedItem?.ToString() ;

            return new TemplateData
            {
                SelectedMonsterName = selectedMonster,                 // 空字串代表未選擇
                Templates = GetCurrentMonsterTemplates(),              // 可能為空清單
                DetectionMode = ExtractModeFromDisplayText(selectedModeDisplay), // 內部已回退到預設
                Threshold = _configurationManager.CurrentConfig.Templates.MonsterDetection.DefaultThreshold,
                TemplateCount = GetMonsterTemplateCount()
            };
        }

        public SdPoint ConvertToDisplayCoordinates(SdPoint imagePoint)
        {
            if (pictureBoxMinimap.Image == null) return SdPoint.Empty;

            var clientSize = pictureBoxMinimap.ClientSize;
            var imageSize = pictureBoxMinimap.Image.Size;
            float ratioX = (float)clientSize.Width / imageSize.Width;
            float ratioY = (float)clientSize.Height / imageSize.Height;
            float ratio = Math.Min(ratioX, ratioY);

            int displayWidth = (int)(imageSize.Width * ratio);
            int displayHeight = (int)(imageSize.Height * ratio);
            int offsetX = (clientSize.Width - displayWidth) / 2;
            int offsetY = (clientSize.Height - displayHeight) / 2;

            int controlX = (int)(imagePoint.X * ratio) + offsetX;
            int controlY = (int)(imagePoint.Y * ratio) + offsetY;

            return new SdPoint(controlX, controlY);
        }

        private async void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            if (cbo_MonsterTemplates.SelectedItem == null) return;

            string selectedMonster = cbo_MonsterTemplates.SelectedItem.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(selectedMonster))
            {
                OnStatusMessage($"🔄 切換怪物模板：{selectedMonster}（BGR格式）");
                ClearCurrentMonsterTemplates();

                // 🚀 確保模板以BGR格式載入
                _currentMonsterTemplates = await MonsterTemplateStore.LoadMonsterTemplatesAsync(
                    selectedMonster, GetMonstersDirectory(), OnStatusMessage);

                _currentMonsterName = selectedMonster;
                OnTemplatesLoaded(selectedMonster, _currentMonsterTemplates.Count);
                OnStatusMessage($"✅ 所有模板已轉換為BGR格式");
            }
        }

        private void ClearCurrentMonsterTemplates()
        {
            foreach (var template in _currentMonsterTemplates)
            {
                template?.Dispose();
            }
            _currentMonsterTemplates.Clear();
            _currentMonsterName = null;
        }

        public List<Bitmap> GetCurrentMonsterTemplates()
        {
            return _currentMonsterTemplates.ToList(); // 返回副本
        }

        public int GetMonsterTemplateCount()
        {
            return _currentMonsterTemplates.Count;
        }

        private async void btn_DownloadMonster_Click(object sender, EventArgs e)
        {
            try
            {
                string monsterName = Microsoft.VisualBasic.Interaction.InputBox(
                    "請輸入怪物名稱:", "下載怪物模板", "");
                if (string.IsNullOrWhiteSpace(monsterName))
                    return;

                btn_DownloadMonster.Enabled = false;
                btn_DownloadMonster.Text = "下載中...";
                var result = await _monsterDownloader.DownloadMonsterAsync(monsterName);
                if (result?.Success == true)
                {
                    var monsterNames = MonsterTemplateStore.GetAvailableMonsterNames(GetMonstersDirectory());
                    cbo_MonsterTemplates.Items.Clear();
                    foreach (var name in monsterNames)
                        cbo_MonsterTemplates.Items.Add(name);

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

