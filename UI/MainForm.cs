using ArtaleAI.API;
using ArtaleAI.API.Config;
using ArtaleAI.Config;
using ArtaleAI.Core;
using ArtaleAI.Core;
using ArtaleAI.Services;
using ArtaleAI.UI;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdRect = System.Drawing.Rectangle;
using SdSize = System.Drawing.Size;
using Timer = System.Threading.Timer;

namespace ArtaleAI
{
    public partial class MainForm : Form
    {

        #region Private Fields
        private GraphicsCaptureItem? _selectedCaptureItem;
        private MapEditor? _mapEditor;
        private Rectangle minimapBounds = Rectangle.Empty;
        private GameVisionCore? gameVision;
        private AppConfig Config => AppConfig.Instance;

        // 檢測狀態管理
        private List<Rectangle> _currentMinimapBoxes = new();
        private List<Rectangle> _currentBloodBars = new();
        private List<Rectangle> _currentDetectionBoxes = new();
        private List<Rectangle> _currentMinimapMarkers = new();
        private List<Rectangle> _currentAttackRangeBoxes = new();
        private List<SdPoint> _currentPathPoints = new();
        private List<DetectionResult> _currentMonsters = new();
        private List<Mat> currentMonsterMatTemplates = new();
        private DateTime _lastBloodBarDetection = DateTime.MinValue;
        private DateTime _lastMonsterDetection = DateTime.MinValue;
        private PointF _lastMousePosition = PointF.Empty;

        private string _selectedMonsterName = string.Empty;
        private LiveViewManager? liveViewManager;
        private GraphicsCapturer? _capturer;
        private readonly object lockObject = new object();

        // 圖像同步鎖
        private Bitmap? _currentDisplayFrame;
        private readonly object imageUpdateLock = new object();

        // 其他服務
        private FloatingMagnifier? _floatingMagnifier;
        private MapFileManager? _mapFileManager;
        private MonsterImageFetcher? _monsterDownloader;
        private MapData? loadedPathData = null;
        private PathPlanningManager? _pathPlanningManager;
        private CharacterMovementController? _movementController;

        private bool _isRecordingRoute = false;
        private readonly List<SdPoint> _recordedRoutePoints = new();
        private DateTime _lastRecordTime = DateTime.MinValue;
        private const double MinRecordDistance = 1.0;   // 最小移動 5 像素才記錄
        private const int MinRecordIntervalMs = 100;    // 最小間隔 100ms
        
        // 狀態訊息輸出控制
        private DateTime _lastStatusUpdate = DateTime.MinValue;
        private const int StatusUpdateIntervalMs = 500; // 狀態訊息更新間隔（500ms）

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
            try
            {
                ApiConfig.Initialize();
                AppConfig.Initialize("Data/config.yaml");
                var config = AppConfig.Instance;

                if (config == null)
                {
                    MsgLog.ShowError(textBox1, "配置載入失敗");
                    return;
                }

                _mapEditor = new MapEditor(config);
                gameVision = new GameVisionCore();
                _mapFileManager = new MapFileManager(cbo_MapFiles, _mapEditor, this);
                _floatingMagnifier = new FloatingMagnifier(this, config.MagnifierSize, config.MagnifierOffset, config.CrosshairSize);
                _monsterDownloader = new MonsterImageFetcher(this);

                _mapFileManager?.InitializeMapFilesDropdown();
                InitializeMonsterTemplateSystem();
                InitializeDetectionModeDropdown();

                cbo_LoadPathFile.Items.Clear();
                string mapDataDirectory = PathManager.MapDataDirectory;
                if (Directory.Exists(mapDataDirectory))
                {
                    var mapFiles = Directory.GetFiles(mapDataDirectory, "*.json");
                    foreach (var file in mapFiles)
                        cbo_LoadPathFile.Items.Add(Path.GetFileNameWithoutExtension(file));
                    MsgLog.ShowStatus(textBox1, $"載入 {mapFiles.Length} 個路徑檔案到路徑規劃下拉選單");
                }
                else
                {
                    Directory.CreateDirectory(mapDataDirectory);
                }

                var tracker = new PathPlanningTracker(gameVision);
                _pathPlanningManager = new PathPlanningManager(tracker, Config);
                _movementController = new CharacterMovementController();
                _movementController.SetGameWindowTitle(Config.GameWindowTitle); // 設定遊戲視窗標題

                // 訂閱事件
                _mapFileManager.MapSaved += OnMapSaved;
                _mapFileManager.MapLoaded += fileName => MsgLog.ShowStatus(textBox1, $"載入地圖: {fileName}");
                _mapFileManager.ErrorOccurred += message => MsgLog.ShowError(textBox1, message);
                _mapFileManager.StatusMessage += message => MsgLog.ShowStatus(textBox1, message);
                _pathPlanningManager.OnTrackingUpdated += OnPathTrackingUpdated;
                _pathPlanningManager.OnPathStateChanged += OnPathStateChanged;
                _pathPlanningManager.OnWaypointReached += OnWaypointReached;
                liveViewManager = new LiveViewManager(config);
                liveViewManager.OnFrameReady += OnFrameAvailable;
                numericUpDownZoom.Value = Config.ZoomFactor;

                MsgLog.ShowStatus(textBox1, " 所有服務初始化完成");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"初始化失敗: {ex.Message}");
                Debug.WriteLine($"InitializeServices error: {ex}");
            }
        }

        private void InitializeMonsterTemplateSystem()
        {
            try
            {
                var monstersDirectory = PathManager.MonstersDirectory;
                var monsterNames = new List<string>();

                if (Directory.Exists(monstersDirectory))
                {
                    var subDirectories = Directory.GetDirectories(monstersDirectory);
                    monsterNames = subDirectories.Select(Path.GetFileName).ToList();
                }

                cbo_MonsterTemplates.Items.Clear();
                foreach (var name in monsterNames)
                {
                    cbo_MonsterTemplates.Items.Add(name);
                }

                cbo_MonsterTemplates.SelectedIndexChanged += OnMonsterSelectionChanged;
                MsgLog.ShowStatus(textBox1, $" 載入 {monsterNames.Count} 個怪物模板");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"初始化怪物模板系統失敗: {ex.Message}");
            }
        }

        // 怪物檢測條件判斷
        private void UpdateDisplay(Bitmap newFrame)
        {
            if (newFrame?.Width <= 0 || newFrame?.Height <= 0)
            {
                newFrame?.Dispose();
                return;
            }

            Action updateAction = () =>
            {
                lock (imageUpdateLock)
                {
                    var oldImage = pictureBoxLiveView.Image;
                    pictureBoxLiveView.Image = newFrame;
                    _currentDisplayFrame = newFrame;
                    oldImage?.Dispose();
                }
            };

            if (InvokeRequired)
            {
                // 🔥 使用同步的 Invoke 確保 UI 更新完成後才返回
                Invoke(updateAction);
            }
            else
            {
                updateAction();
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

            numericUpDownZoom.Value = Config.ZoomFactor;
            MsgLog.ShowStatus(textBox1, "配置檔案載入完成");
        }

        public void OnMapSaved(string fileName, bool isNewFile)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnMapSaved(fileName, isNewFile));
                return;
            }

            string message = isNewFile ? "新地圖儲存成功！" : "儲存成功！";
            MessageBox.Show(message, "地圖檔案管理", MessageBoxButtons.OK, MessageBoxIcon.Information);
            MsgLog.ShowStatus(textBox1, $"地圖儲存: {fileName}");
        }

        public void OnConfigSaved(AppConfig config)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<AppConfig>(OnConfigSaved), config);
                return;
            }

            MsgLog.ShowStatus(textBox1, "設定已儲存");
        }

        public void OnConfigError(string errorMessage)
        {
            MsgLog.ShowError(textBox1, $"設定錯誤: {errorMessage}");
        }

        #endregion

        #region 辨識模式控制

        /// <summary>
        /// 初始化辨識模式下拉選單
        /// </summary>
        private void InitializeDetectionModeDropdown()
        {
            cbo_DetectMode.Items.Clear();
            var config = AppConfig.Instance;

            if (config.DisplayOrder?.Any() == true && config.DisplayNames?.Any() == true)
            {
                try
                {
                    foreach (var mode in config.DisplayOrder)
                    {
                        if (config.DisplayNames.TryGetValue(mode, out var displayName))
                        {
                            cbo_DetectMode.Items.Add(displayName);
                        }
                    }

                    var defaultMode = config.DefaultMode;
                    if (config.DisplayNames.TryGetValue(defaultMode, out var defaultDisplay))
                    {
                        cbo_DetectMode.SelectedItem = defaultDisplay;
                    }

                    MsgLog.ShowStatus(textBox1, $"檢測模式已載入：{config.DisplayOrder.Count} 個模式，預設：{defaultMode}");
                }
                catch (Exception ex)
                {
                    MsgLog.ShowError(textBox1, $"檢測模式初始化失敗: {ex.Message}");
                }
            }
            else
            {
                MsgLog.ShowError(textBox1, "檢測模式配置無效");
            }

            cbo_DetectMode.SelectedIndexChanged += OnDetectionModeChanged;
        }

        /// <summary>
        /// 辨識模式變更事件 - 重構版
        /// </summary>
        private void OnDetectionModeChanged(object? sender, EventArgs e)
        {
            var selectedDisplayText = cbo_DetectMode.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDisplayText)) return;

            var config = AppConfig.Instance;

            // 1. 直接從顯示名稱找到模式 key（內嵌邏輯）
            var selectedMode = config.DisplayNames?
                .FirstOrDefault(kvp => kvp.Value == selectedDisplayText).Key
                ?? config.DefaultMode ?? "Color";

            // 2. 直接找到最佳遮擋設定（內嵌邏輯）
            var optimalOcclusion = OcclusionHandling.None;
            if (config.OcclusionMappings?.TryGetValue(selectedMode, out var occlusionString) == true)
            {
                if (Enum.TryParse<OcclusionHandling>(occlusionString, out var result))
                    optimalOcclusion = result;
            }

            // 3. 套用設定
            AppConfig.Instance.DetectionMode = selectedMode;
            //GameConfig.Instance.MonsterDetection.OcclusionHandling = optimalOcclusion.ToString();

            MsgLog.ShowStatus(textBox1, $" 偵測模式: {selectedMode} | 遮擋: {optimalOcclusion}");
        }

        #endregion

        #region IMapFileEventHandler 實作

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

        #region UI 事件處理

        private void numericUpDownZoom_ValueChanged(object? sender, EventArgs e)
        {
            Config.ZoomFactor = numericUpDownZoom.Value;
            Config.Save();

        }

        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            try
            {
                // 立即清空所有PictureBox.Image，防止OnVisibleChanged觸發錯誤
                //pictureBoxLiveView.Image = null;
                //pictureBoxMinimap.Image = null;

                // 短暫延遲確保清空生效
                //Application.DoEvents();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clear PictureBox Images error: {ex.Message}");
            }

            StopAndReleaseAllResources();

            switch (tabControl1.SelectedIndex)
            {
                case 0: // 主控台頁面
                    UpdateWindowTitle("ArtaleAI");
                    break;
                case 1: // 路徑編輯頁面
                    StartPathEditingModeAsync();
                    // 標題會在載入地圖檔案時更新
                    break;
                case 2: // 即時顯示頁面

                    UpdateWindowTitle("ArtaleAI - 即時顯示");
                    await StartLiveViewModeAsync();

                    if (loadedPathData != null && liveViewManager?.IsRunning == true && _currentMinimapBoxes.Any())
                    {
                        UpdateLiveViewPathDisplay();
                    }
                    break;
                default:
                    UpdateWindowTitle("ArtaleAI");
                    break;
            }
        }

        private void UpdateLiveViewPathDisplay()
        {
            // 修復：檢查邊界條件
            if (!_currentMinimapBoxes.Any())
                return;

            // 簡化：統一處理所有路徑類型
            var allLists = new[] {
                loadedPathData.WaypointPaths,
                loadedPathData.SafeZones,
                loadedPathData.Ropes,
                loadedPathData.RestrictedZones
            };

            var allStaticPoints = allLists
                .Where(list => list?.Any() == true)
                .SelectMany(list => list)
                .Where(coord => coord.Length == 2)
                .Select(coord => new SdPoint((int)Math.Round(coord[0]), (int)Math.Round(coord[1])))
                .ToList();

            lock (_currentPathPoints)
            {
                _currentPathPoints.Clear();
                _currentPathPoints.AddRange(allStaticPoints);
            }

            Debug.WriteLine($" 路徑點已更新: {_currentPathPoints.Count} 點");
        }



        /// <summary>
        /// 路徑編輯模式：只載入靜態小地圖
        /// </summary>
        private async Task StartPathEditingModeAsync()
        {
            MsgLog.ShowStatus(textBox1, "載入路徑編輯模式...");
            tabControl1.Enabled = false;

            try
            {
                var result = await LoadMinimapWithMat(MinimapUsage.PathEditing);
                if (result?.MinimapImage != null)
                {
                    //  直接內嵌圖像設定邏輯
                    Action setImage = () =>
                    {
                        var oldImage = pictureBoxMinimap.Image;
                        pictureBoxMinimap.Image = result.MinimapImage;
                        oldImage?.Dispose();
                    };

                    if (InvokeRequired)
                        Invoke(setImage);
                    else
                        setImage();

                    if (result.MinimapScreenRect.HasValue)
                    {
                        minimapBounds = result.MinimapScreenRect.Value;
                        _mapEditor?.SetMinimapBounds(minimapBounds);
                        _currentMinimapBoxes.Clear();
                        _currentMinimapBoxes.Add(result.MinimapScreenRect.Value);
                        MsgLog.ShowStatus(textBox1, "路徑編輯模式已啟動");
                    }
                }
                else
                {
                    MsgLog.ShowError(textBox1, "載入小地圖失敗");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"路徑編輯模式錯誤: {ex.Message}");
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }

        private async Task<MinimapResult?> LoadMinimapWithMat(MinimapUsage usage)
        {
            var config = Config;
            try
            {
                Debug.WriteLine("開始 LoadMinimapWithMat");
                MsgLog.ShowStatus(textBox1, "正在載入小地圖...");

                var captureItem = WindowFinder.TryCreateItemForWindow(Config.GameWindowTitle);
                if (captureItem == null)
                {
                    MsgLog.ShowError(textBox1, $"無法建立捕獲項目: {Config.GameWindowTitle}");
                    return null;
                }

                //  使用 GetSnapshotAsync
                var result = await gameVision.GetSnapshotAsync(
                    IntPtr.Zero,
                    config,
                    captureItem,
                    message => MsgLog.ShowStatus(textBox1, message)
                );

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadMinimapWithMat 錯誤: {ex.Message}");
                MsgLog.ShowError(textBox1, $"載入小地圖失敗: {ex.Message}");
                return null;
            }
        }


        private void ProcessMinimapPlayer(Mat frameMat)
        {
            if (!_currentMinimapBoxes.Any()) return;

            try
            {
                var trackingResult = gameVision?.GetMinimapTracking(frameMat);

                // 修復：加入 lock 保護讀取操作
                Rectangle? minimapRect = null;
                lock (_currentMinimapBoxes)
                {
                    if (_currentMinimapBoxes.Any())
                        minimapRect = _currentMinimapBoxes.First();
                }

                if (minimapRect.HasValue && trackingResult?.PlayerPosition.HasValue == true)
                {
                    lock (_currentMinimapMarkers)
                    {
                        _currentMinimapMarkers.Clear();

                        var playerPos = trackingResult.PlayerPosition.Value;

                        // 轉換為螢幕座標
                        var screenPlayerPos = new SdPoint(
                            minimapRect.Value.X + playerPos.X,
                            minimapRect.Value.Y + playerPos.Y);

                        _currentMinimapMarkers.Add(new SdRect(
                            screenPlayerPos.X - 5, screenPlayerPos.Y - 5, 10, 10));

                        Debug.WriteLine($"玩家位置: {playerPos} -> 螢幕座標: {screenPlayerPos}");
                    }
                }
                else
                {
                    Debug.WriteLine("未檢測到玩家位置或小地圖邊界");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessMinimapPlayer錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 即時顯示模式：啟動所有即時處理功能
        /// </summary>
        private async Task StartLiveViewModeAsync()
        {
            MsgLog.ShowStatus(textBox1, "正在啟動即時畫面...");
            var config = Config;

            try
            {
                // 先載入小地圖
                var result = await LoadMinimapWithMat(MinimapUsage.LiveViewOverlay);
                if (result?.MinimapScreenRect.HasValue == true)
                {
                    minimapBounds = result.MinimapScreenRect.Value;
                    lock (_currentMinimapBoxes)
                    {
                        _currentMinimapBoxes.Clear();
                        _currentMinimapBoxes.Add(result.MinimapScreenRect.Value);
                    }
                    MsgLog.ShowStatus(textBox1, "小地圖位置已定位");

                    // 使用LiveViewManager啟動Timer
                    var captureItem = WindowFinder.TryCreateItemForWindow(Config.GameWindowTitle);
                    if (captureItem != null)
                    {
                        liveViewManager?.StartLiveView(captureItem);
                        MsgLog.ShowStatus(textBox1, "即時畫面已啟動");
                    }
                    else
                    {
                        MsgLog.ShowError(textBox1, $"找不到遊戲視窗：{Config.GameWindowTitle}");
                    }
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"啟動失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 當LiveViewManager傳來新畫面時，這個方法會被呼叫
        /// 負責：血條偵測、怪物偵測、小地圖追蹤、路徑規劃處理
        /// </summary>
        private void OnFrameAvailable(Mat frameMat)
        {
            if (frameMat == null || frameMat.Empty())
                return;

            try
            {
                using (frameMat)
                {
                    var config = Config;
                    if (config == null)
                        return;

                    var now = DateTime.UtcNow;
                    
                    // 優化：優先處理路徑規劃和移動控制（減少延遲）
                    // 1. 小地圖玩家位置追蹤（路徑規劃需要）
                    if (_currentMinimapBoxes.Any())
                    {
                        try
                        {
                            var trackingResult = gameVision?.GetMinimapTracking(frameMat);
                            
                            // 修復：加入 lock 保護讀取操作
                            Rectangle? minimapRect = null;
                            lock (_currentMinimapBoxes)
                            {
                                if (_currentMinimapBoxes.Any())
                                    minimapRect = _currentMinimapBoxes.First();
                            }
                            
                            if (minimapRect.HasValue)
                            {
                                lock (_currentMinimapMarkers)
                                {
                                    _currentMinimapMarkers.Clear();
                                    if (trackingResult?.PlayerPosition.HasValue == true)
                                    {
                                        var playerPos = trackingResult.PlayerPosition.Value;
                                        var screenPlayerPos = new SdPoint(
                                            minimapRect.Value.X + playerPos.X,
                                            minimapRect.Value.Y + playerPos.Y
                                        );
                                        _currentMinimapMarkers.Add(new SdRect(
                                            screenPlayerPos.X - 5,
                                            screenPlayerPos.Y - 5,
                                            10, 10
                                        ));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // 修復：記錄錯誤訊息
                            Debug.WriteLine($"小地圖玩家位置追蹤錯誤: {ex.Message}");
                        }
                    }

                    // 2. 處理路徑規劃（優先處理，減少移動延遲）
                    if (_pathPlanningManager != null && _pathPlanningManager.IsRunning)
                    {
                        ProcessPathPlanning(frameMat);
                    }

                    // 3. 偵測血條（較低優先級）
                    var elapsed = (now - _lastBloodBarDetection).TotalMilliseconds;
                    if (elapsed >= config.BloodBarDetectIntervalMs || _currentBloodBars.Count == 0)
                    {
                        try
                        {
                            var (bloodBar, detectionBoxes, attackRangeBoxes) =
                                gameVision?.ProcessBloodBarDetection(frameMat, null)
                                ?? (null, new List<Rectangle>(), new List<Rectangle>());

                            if (bloodBar.HasValue)
                            {
                                // 修復：加入 lock 保護共享變數
                                lock (_currentBloodBars)
                                {
                                    _currentBloodBars.Clear();
                                    _currentBloodBars.Add(bloodBar.Value);
                                }
                                
                                _currentDetectionBoxes = detectionBoxes;
                                _currentAttackRangeBoxes = attackRangeBoxes;
                                _lastBloodBarDetection = now;
                            }
                        }
                        catch (Exception ex)
                        {
                            MsgLog.ShowError(textBox1, $"血條偵測錯誤: {ex.Message}");
                        }
                    }

                    // 4. 偵測怪物（較低優先級）
                    ProcessMonsters(frameMat);

                    // 5. 繪製所有偵測結果並顯示（僅在即時顯示分頁時才顯示，節省資源）
                    // 即時顯示分頁現在是純可視化工具，用於除錯和監控
                    // 修復：安全地檢查當前分頁（避免跨執行緒錯誤）
                    bool isLiveViewTab = false;
                    if (InvokeRequired)
                    {
                        Invoke(new Action(() => { isLiveViewTab = tabControl1.SelectedIndex == 2; }));
                    }
                    else
                    {
                        isLiveViewTab = tabControl1.SelectedIndex == 2;
                    }
                    
                    if (isLiveViewTab)
                    {
                        RenderAndDisplayOverlays(frameMat);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"處理畫面錯誤: {ex.Message}");
            }

            // 修復：移除重複的 ProcessPathPlanning 呼叫（已在上面第 706-709 行處理）
        }


        /// <summary>
        /// 【完整功能】怪物檢測 - 包含時間檢查、區域裁切、模板匹配、NMS去重
        /// </summary>
        private async void ProcessMonsters(Mat frameMat)
        {
            var config = Config;
            var now = DateTime.UtcNow;

            // 1. 時間間隔檢查
            var elapsed = (now - _lastMonsterDetection).TotalMilliseconds;
            if (elapsed < config.MonsterDetectIntervalMs && _currentMonsters.Count > 0)
                return;

            // 2. 前置條件檢查
            if (!_currentDetectionBoxes.Any())
            {
                MsgLog.ShowStatus(textBox1, "無血條檢測範圍");
                return;
            }

            if (string.IsNullOrEmpty(_selectedMonsterName) || !currentMonsterMatTemplates.Any())
            {
                MsgLog.ShowStatus(textBox1, $"未選擇怪物模板 (Templates={currentMonsterMatTemplates.Count})");
                return;
            }

            try
            {
                var allResults = new List<DetectionResult>();
                var frameBounds = new Rect(0, 0, frameMat.Width, frameMat.Height);

                // 3. 取得檢測模式
                var detectionModeString = config.DetectionMode ?? "Color";
                if (!Enum.TryParse<MonsterDetectionMode>(detectionModeString, out var detectionMode))
                    detectionMode = MonsterDetectionMode.Color;

                // 4. 逐個檢測框處理（內嵌邏輯）
                foreach (var detectionBox in _currentDetectionBoxes)
                {
                    var cropRect = new Rect(detectionBox.X, detectionBox.Y, detectionBox.Width, detectionBox.Height);
                    var validCropRect = frameBounds.Intersect(cropRect);

                    if (validCropRect.Width < 10 || validCropRect.Height < 10)
                        continue;

                    using var croppedMat = frameMat[validCropRect].Clone();

                    // 5. 怪物偵測
                    var results = gameVision?.FindMonsters(
                        croppedMat,
                        currentMonsterMatTemplates,
                        detectionMode,
                        config.PlayerThreshold,
                        _selectedMonsterName
                    ) ?? new List<DetectionResult>();

                    // 6. 座標轉換（內嵌邏輯）
                    foreach (var result in results)
                    {
                        var monster = new DetectionResult(
                            result.Name,
                            new System.Drawing.Point(result.Position.X + validCropRect.X, result.Position.Y + validCropRect.Y),
                            result.Size,
                            result.Confidence,
                            new Rectangle(result.Position.X + validCropRect.X, result.Position.Y + validCropRect.Y,
                                         result.Size.Width, result.Size.Height)
                        );
                        allResults.Add(monster);
                    }
                }

                // 7. NMS去重（內嵌邏輯）
                // 修復：加入 lock 保護共享變數
                if (allResults.Count > 1)
                {
                    var dedupedResults = GameVisionCore.ApplyNMS(allResults, iouThreshold: 0.3, higherIsBetter: true);
                    lock (_currentMonsters)
                    {
                        _currentMonsters = dedupedResults;
                    }
                    MsgLog.ShowStatus(textBox1, $"檢測到 {allResults.Count} 個怪物 (NMS後: {dedupedResults.Count})");
                }
                else
                {
                    lock (_currentMonsters)
                    {
                        _currentMonsters = allResults;
                    }
                }

                _lastMonsterDetection = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                MsgLog.ShowStatus(textBox1, $"怪物檢測錯誤: {ex.Message}");
            }
        }

        private void RenderAndDisplayOverlays(Mat frameMat)
        {
            if (frameMat?.IsDisposed != false || frameMat.Empty()) return;
            var config = Config;
            if (config == null) return;

            try
            {
                using var rgbMat = new Mat();
                Cv2.CvtColor(frameMat, rgbMat, ColorConversionCodes.BGR2RGB);
                using var bitmap = rgbMat.ToBitmap();
                using var graphics = Graphics.FromImage(bitmap);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                // 簡化：複製共享資料（避免執行緒競爭）
                List<Rectangle> bloodBars, detectionBoxes, attackRangeBoxes;
                List<DetectionResult> monsters;

                lock (_currentBloodBars) bloodBars = _currentBloodBars.ToList();
                lock (_currentMonsters) monsters = _currentMonsters.ToList();
                
                detectionBoxes = _currentDetectionBoxes.ToList();
                attackRangeBoxes = _currentAttackRangeBoxes.ToList();

                // 血條框
                DrawingHelper.DrawRectangles(graphics, bloodBars,
                    GameVisionCore.ParseColor(config.PartyRedBar.FrameColor),
                    config.PartyRedBar.FrameThickness,
                    GameVisionCore.ParseColor(config.PartyRedBar.TextColor),
                    config.PartyRedBar.RedBarDisplayName);

                // 偵測框
                DrawingHelper.DrawRectangles(graphics, detectionBoxes,
                    GameVisionCore.ParseColor(config.DetectionBox.FrameColor),
                    config.DetectionBox.FrameThickness,
                    GameVisionCore.ParseColor(config.DetectionBox.TextColor),
                    config.DetectionBox.BoxDisplayName);

                // 攻擊範圍框
                DrawingHelper.DrawRectangles(graphics, attackRangeBoxes,
                    GameVisionCore.ParseColor(config.AttackRange.FrameColor),
                    config.AttackRange.FrameThickness,
                    GameVisionCore.ParseColor(config.AttackRange.TextColor),
                    config.AttackRange.RangeDisplayName);

                // 小地圖框
                DrawingHelper.DrawRectangles(graphics, _currentMinimapBoxes,
                    GameVisionCore.ParseColor(config.Minimap.FrameColor),
                    config.Minimap.FrameThickness,
                    GameVisionCore.ParseColor(config.Minimap.TextColor),
                    config.Minimap.MinimapDisplayName);

                // 怪物
                if (monsters.Any())
                {
                    var style = config.Monster;
                    using var pen = new Pen(GameVisionCore.ParseColor(style.FrameColor), style.FrameThickness);
                    using var brush = new SolidBrush(GameVisionCore.ParseColor(style.TextColor));
                    using var font = SystemFonts.DefaultFont;

                    foreach (var monster in monsters)
                    {
                        var rect = new Rectangle(monster.Position.X, monster.Position.Y,
                            monster.Size.Width, monster.Size.Height);
                        graphics.DrawRectangle(pen, rect);
                        if (!string.IsNullOrEmpty(monster.Name))
                            graphics.DrawString($"{monster.Name} ({monster.Confidence:F1})",
                                font, brush, rect.X, rect.Y - 15);
                    }
                }

                // 小地圖玩家標記
                if (_currentMinimapMarkers.Any())
                {
                    var style = config.MinimapPlayer;
                    using var brush = new SolidBrush(GameVisionCore.ParseColor(style.FrameColor));
                    foreach (var marker in _currentMinimapMarkers)
                    {
                        graphics.FillRectangle(brush, marker);
                    }
                }

                // 路徑點線段
                if (_currentPathPoints.Count >= 2)
                {
                    using var pen = new Pen(Color.Yellow, 2);
                    for (int i = 0; i < _currentPathPoints.Count - 1; i++)
                    {
                        graphics.DrawLine(pen, _currentPathPoints[i], _currentPathPoints[i + 1]);
                    }
                }

                // 修復：Clone Bitmap 以避免 using 區塊結束時被釋放
                UpdateDisplay((Bitmap)bitmap.Clone());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RENDER ERROR: {ex.Message}");
            }
        }

        //private void RenderPathsDirect(Graphics graphics)
        //{
        //    var mapData = _mapEditor?.GetCurrentMapData();
        //    if (mapData == null) return;

        //    //  本地繪製函數
        //    void DrawPath(List<int[]>? coordinates, Color color, float width, DashStyle dashStyle)
        //    {
        //        if (coordinates?.Any() != true || coordinates.Count < 2) return;

        //        var screenPoints = coordinates
        //            .Where(coord => coord.Length == 2)
        //            .Select(coord => new SdPoint(coord[0], coord[1]))
        //            .Select(p => GameVisionCore.MinimapToScreenF(p, minimapBounds))
        //            .ToArray();

        //        if (screenPoints.Length < 2) return;

        //        using var pen = new Pen(color, width) { DashStyle = dashStyle };
        //        using var brush = new SolidBrush(color);

        //        for (int i = 0; i < screenPoints.Length - 1; i++)
        //        {
        //            graphics.DrawLine(pen, screenPoints[i], screenPoints[i + 1]);
        //        }

        //        foreach (var pt in screenPoints)
        //        {
        //            graphics.FillEllipse(brush, pt.X - 3, pt.Y - 3, 6, 6);
        //        }
        //    }

        //    // 繪製各種路徑
        //    DrawPath(mapData.WaypointPaths, Color.Blue, 2, DashStyle.Dash);
        //    DrawPath(mapData.SafeZones, Color.Green, 2, DashStyle.Dash);
        //    DrawPath(mapData.Ropes, Color.Yellow, 2, DashStyle.Dash);

        //    // 禁區點
        //    if (mapData.RestrictedZones?.Any() == true)
        //    {
        //        using var brush = new SolidBrush(Color.Red);
        //        foreach (var coord in mapData.RestrictedZones)
        //        {
        //            var point = new SdPoint((int)Math.Round(coord[0]), (int)Math.Round(coord[1]));
        //            var screenPoint = GameVisionCore.MinimapToScreenF(point, minimapBounds);
        //            graphics.FillEllipse(brush, screenPoint.X - 4, screenPoint.Y - 4, 8, 8);
        //        }
        //    }
        //}

        /// <summary>
        /// 完全停止並釋放所有分頁處理資源
        /// </summary>
        private void StopAndReleaseAllResources()
        {
            try
            {
                // 使用LiveViewManager停止
                liveViewManager?.StopLiveView();
                _currentDisplayFrame = null;
                MsgLog.ShowStatus(textBox1, "所有資源已釋放");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"釋放資源錯誤: {ex.Message}");
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

            MsgLog.ShowStatus(textBox1, $"編輯模式切換至: {selectedMode}");

        }

        #endregion

        #region PictureBox 滑鼠事件

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            //  1. 更新放大鏡 (使用 PictureBox 座標,不會卡)
            _floatingMagnifier?.UpdateMagnifier(e.Location, pictureBoxMinimap);

            //  2. 只存儲滑鼠位置,不做計算
            _lastMousePosition = new PointF(e.X, e.Y);

            //  3. 簡化：更新預覽位置
            if (_mapEditor != null && _currentMinimapBoxes.Any() && !minimapBounds.IsEmpty)
            {
                var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
                var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);
                
                _mapEditor.UpdateMousePosition(screenPoint);
                pictureBoxMinimap.Invalidate();
            }
        }


        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            if (_mapEditor == null || !_currentMinimapBoxes.Any() || pictureBoxMinimap.Image == null)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            //  計算 PictureBox 在 Zoom 模式下的縮放參數
            float pbWidth = pictureBoxMinimap.ClientSize.Width;
            float pbHeight = pictureBoxMinimap.ClientSize.Height;
            float imgWidth = pictureBoxMinimap.Image.Width;
            float imgHeight = pictureBoxMinimap.Image.Height;

            float imgAspect = imgWidth / imgHeight;
            float pbAspect = pbWidth / pbHeight;

            float scaleX, scaleY;
            float offsetX = 0, offsetY = 0;

            if (pbAspect > imgAspect)
            {
                // PictureBox 比較寬,上下留黑邊
                scaleY = pbHeight / imgHeight;
                scaleX = scaleY;
                offsetX = (pbWidth - imgWidth * scaleX) / 2;
            }
            else
            {
                // PictureBox 比較高,左右留黑邊
                scaleX = pbWidth / imgWidth;
                scaleY = scaleX;
                offsetY = (pbHeight - imgHeight * scaleY) / 2;
            }

            //  座標轉換函數: 螢幕絕對座標 → PictureBox 控制項座標
            PointF ConvertScreenToDisplay(PointF screenPoint)
            {
                float relX = screenPoint.X - minimapBounds.X;
                float relY = screenPoint.Y - minimapBounds.Y;
                return new PointF(
                    relX * scaleX + offsetX,
                    relY * scaleY + offsetY
                );
            }

            //  呼叫 MapEditor 的 Render (繪製所有路徑和預覽線)
            _mapEditor.Render(e.Graphics, ConvertScreenToDisplay);
        }


        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            try
            {
                // 隱藏放大鏡
                _floatingMagnifier?.Hide();

                // 清除滑鼠位置
                _lastMousePosition = PointF.Empty;

                // 重繪畫布
                pictureBoxMinimap.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MouseLeave 錯誤: {ex.Message}");
            }
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
                MsgLog.ShowError(textBox1, $"儲存地圖時發生錯誤: {ex.Message}");
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
                MsgLog.ShowError(textBox1, $"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        #endregion

        #region 清理與釋放

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                // 修復：正確釋放 PictureBox.Image
                var liveViewImage = pictureBoxLiveView.Image;
                var minimapImage = pictureBoxMinimap.Image;
                pictureBoxLiveView.Image = null;
                pictureBoxMinimap.Image = null;
                Application.DoEvents();

                liveViewImage?.Dispose();
                minimapImage?.Dispose();

                _currentDisplayFrame?.Dispose();
                _currentDisplayFrame = null;

                foreach (var template in currentMonsterMatTemplates)
                {
                    template?.Dispose();
                }
                currentMonsterMatTemplates.Clear();

                _currentMinimapMarkers.Clear();
                _currentMinimapBoxes.Clear();
                _currentBloodBars.Clear();
                _currentDetectionBoxes.Clear();
                _currentAttackRangeBoxes.Clear();
                _currentPathPoints.Clear();
                _currentMonsters.Clear();
                _recordedRoutePoints.Clear();

                // 修復：取消事件訂閱以避免記憶體洩漏
                // 注意：Lambda 訂閱無法直接取消，但會在 Dispose 時自動清理
                if (_mapFileManager != null)
                {
                    _mapFileManager.MapSaved -= OnMapSaved;
                }

                if (_pathPlanningManager != null)
                {
                    _pathPlanningManager.OnTrackingUpdated -= OnPathTrackingUpdated;
                    _pathPlanningManager.OnPathStateChanged -= OnPathStateChanged;
                    _pathPlanningManager.OnWaypointReached -= OnWaypointReached;
                }

                if (liveViewManager != null)
                {
                    liveViewManager.OnFrameReady -= OnFrameAvailable;
                }

                _floatingMagnifier?.Dispose();
                _monsterDownloader?.Dispose();
                gameVision?.Dispose();

                _pathPlanningManager?.Dispose();
                liveViewManager?.Dispose();
                _movementController?.Dispose();

                MsgLog.ShowStatus(textBox1, "所有資源已清理");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Form關閉錯誤: {ex.Message}");
            }
            base.OnFormClosed(e);
        }

        #endregion

        private async void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            try
            {
                if (cbo_MonsterTemplates.SelectedItem == null) return;

                string selectedMonster = cbo_MonsterTemplates.SelectedItem.ToString();
                if (string.IsNullOrEmpty(selectedMonster)) return;

                MsgLog.ShowStatus(textBox1, $"載入怪物模板: {selectedMonster}");

                // 清理現有模板
                foreach (var template in currentMonsterMatTemplates)
                {
                    template?.Dispose();
                }
                currentMonsterMatTemplates.Clear();

                //  使用靜態屬性載入模板
                currentMonsterMatTemplates = await gameVision?.LoadMonsterTemplatesAsync(
                    selectedMonster,
                    PathManager.MonstersDirectory
                ) ?? new List<Mat>();

                _selectedMonsterName = selectedMonster;
                MsgLog.ShowStatus(textBox1, $"已載入 {currentMonsterMatTemplates.Count} 個模板");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"載入模板錯誤: {ex.Message}");

                foreach (var template in currentMonsterMatTemplates)
                {
                    template?.Dispose();
                }
                currentMonsterMatTemplates.Clear();
            }
        }

        private async void btn_DownloadMonster_Click(object sender, EventArgs e)
        {
            try
            {
                string monsterName = Microsoft.VisualBasic.Interaction.InputBox(
                    "請輸入怪物名稱:", "下載怪物模板", "");

                if (string.IsNullOrWhiteSpace(monsterName)) return;

                btn_DownloadMonster.Enabled = false;
                btn_DownloadMonster.Text = "下載中...";

                var result = await _monsterDownloader.DownloadMonsterAsync(monsterName);

                if (result?.Success == true)
                {
                    // 🔄 重新載入怪物列表，不使用 MonsterTemplateStore
                    var monstersDirectory = PathManager.MonstersDirectory;
                    var monsterNames = new List<string>();

                    if (Directory.Exists(monstersDirectory))
                    {
                        var subDirectories = Directory.GetDirectories(monstersDirectory);
                        monsterNames = subDirectories.Select(Path.GetFileName).ToList();
                    }

                    cbo_MonsterTemplates.Items.Clear();
                    foreach (var name in monsterNames)
                    {
                        cbo_MonsterTemplates.Items.Add(name);
                    }

                    MsgLog.ShowStatus(textBox1, $" 成功下載 {result.DownloadedCount} 個模板");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"下載怪物模板失敗: {ex.Message}");
            }
            finally
            {
                btn_DownloadMonster.Enabled = true;
                btn_DownloadMonster.Text = "下載怪物";
            }
        }

        private async void rdo_Start_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is not RadioButton rdoButton) return;

            try
            {
                // 簡化：直接內嵌邏輯，減少方法層級
                if (rdoButton.Checked)
                {
                    // 自動啟動 LiveView 來獲取畫面（必須啟動才能處理路徑規劃）
                    if (liveViewManager == null || !liveViewManager.IsRunning)
                    {
                        var captureItem = WindowFinder.TryCreateItemForWindow(Config.GameWindowTitle);
                        if (captureItem == null)
                        {
                            MsgLog.ShowError(textBox1, "找不到遊戲視窗，請先開啟遊戲。");
                            rdoButton.Checked = false; // 取消選中
                            return;
                        }

                        MsgLog.ShowStatus(textBox1, "正在啟動背景擷取以處理路徑規劃...");
                        
                        // 啟動 LiveView（背景運行，不需要切換分頁）
                        if (liveViewManager != null)
                        {
                            liveViewManager.StartLiveView(captureItem);
                        }
                        await Task.Delay(500);
                    }

                    await _pathPlanningManager.StartAsync(Config.GameWindowTitle);
                    MsgLog.ShowStatus(textBox1, "路徑規劃已啟動");
                }
                else
                {
                    await _pathPlanningManager.StopAsync();
                    MsgLog.ShowStatus(textBox1, "路徑規劃已停止");
                }
            }
            catch (Exception ex)
            {
                var action = rdoButton.Checked ? "啟動" : "停止";
                MsgLog.ShowError(textBox1, $"路徑規劃{action}失敗: {ex.Message}");
                if (rdoButton.Checked)
                {
                    rdoButton.Checked = false; // 發生錯誤時取消選中
                }
            }
        }

        #region 路徑規劃專用方法

        /// <summary>
        /// 路徑追蹤更新事件處理
        /// </summary>
        private void OnPathTrackingUpdated(MinimapTrackingResult result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<MinimapTrackingResult>(OnPathTrackingUpdated), result);
                return;
            }

            // 先檢查 MinimapBounds
            if (result.MinimapBounds.HasValue)
            {
                minimapBounds = result.MinimapBounds.Value;
                _mapEditor?.SetMinimapBounds(minimapBounds);
            }

            var playerPosOpt = result.PlayerPosition;
            if (playerPosOpt.HasValue && playerPosOpt.Value != SdPoint.Empty)
            {
                var playerPos = playerPosOpt.Value;

                // 獨立的錄製邏輯 - 只要按 F1 就能錄製
                if (_isRecordingRoute)
                {
                    HandleRouteRecording(playerPos);
                }

                // 路徑規劃狀態顯示和自動移動控制（僅在有規劃路徑時）
                if (_pathPlanningManager?.CurrentState != null)
                {
                    var pathState = _pathPlanningManager.CurrentState;
                    var progress = $"{pathState.CurrentWaypointIndex + 1}/{pathState.PlannedPath.Count}";
                    var distance = pathState.DistanceToNextWaypoint;

                    var nextWaypointOpt = pathState.NextWaypoint;
                    if (nextWaypointOpt.HasValue)
                    {
                        var nextWaypoint = nextWaypointOpt.Value;
                        
                        // 優化：限制狀態訊息輸出頻率（每 500ms 更新一次）
                        var now = DateTime.UtcNow;
                        var elapsed = (now - _lastStatusUpdate).TotalMilliseconds;
                        if (elapsed >= StatusUpdateIntervalMs)
                        {
                            MsgLog.ShowStatus(textBox1, $"進度: {progress} 距離: {distance:F1}px 下一點: ({nextWaypoint.X},{nextWaypoint.Y}) 目前: ({playerPos.X},{playerPos.Y})");
                            _lastStatusUpdate = now;
                        }
                        
                        // 自動移動控制（長按模式）
                        if (Config.EnableAutoMovement && _movementController != null && _pathPlanningManager.IsRunning)
                        {
                            // 只在距離大於到達距離時才移動
                            if (distance > Config.WaypointReachDistance)
                            {
                                // 使用 Fire-and-Forget，不等待完成（長按模式）
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await _movementController.MoveToTargetAsync(
                                            playerPos,
                                            nextWaypoint,
                                            Config.WaypointReachDistance
                                        );
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"自動移動錯誤: {ex.Message}");
                                    }
                                });
                            }
                            else
                            {
                                // 已接近目標，停止移動
                                _movementController.StopMovement();
                            }
                        }
                        else if (!Config.EnableAutoMovement)
                        {
                            // 調試訊息：確認自動移動是否啟用
                            Debug.WriteLine($"[調試] 自動移動未啟用: EnableAutoMovement={Config.EnableAutoMovement}");
                        }
                    }
                    else
                    {
                        // 優化：限制狀態訊息輸出頻率
                        var now = DateTime.UtcNow;
                        var elapsed = (now - _lastStatusUpdate).TotalMilliseconds;
                        if (elapsed >= StatusUpdateIntervalMs)
                        {
                            MsgLog.ShowStatus(textBox1, $"目前: ({playerPos.X},{playerPos.Y})");
                            _lastStatusUpdate = now;
                        }
                    }
                }
                else if (_isRecordingRoute)
                {
                    // 只錄製時，顯示目前座標
                    MsgLog.ShowStatus(textBox1, $"錄製中 - 目前座標: ({playerPos.X},{playerPos.Y})");
                }

                // LiveView 的小地圖顯示
                // 修復：安全地檢查當前分頁（避免跨執行緒錯誤）
                bool isLiveViewTab = false;
                if (InvokeRequired)
                {
                    Invoke(new Action(() => { isLiveViewTab = tabControl1.SelectedIndex == 2; }));
                }
                else
                {
                    isLiveViewTab = tabControl1.SelectedIndex == 2;
                }
                
                if (liveViewManager != null && liveViewManager.IsRunning && isLiveViewTab && result.MinimapBounds.HasValue)
                    {
                        var bounds = result.MinimapBounds.Value;

                        lock (_currentMinimapBoxes)
                        {
                            _currentMinimapBoxes.Clear();
                            _currentMinimapBoxes.Add(bounds);
                        }

                        var screenPlayerPos = new SdPoint(bounds.X + playerPos.X, bounds.Y + playerPos.Y);

                        lock (_currentMinimapMarkers)
                        {
                            _currentMinimapMarkers.Clear();
                            _currentMinimapMarkers.Add(new SdRect(screenPlayerPos.X - 5, screenPlayerPos.Y - 5, 10, 10));
                        }
                    }

                    if (result.OtherPlayers?.Any() == true && result.MinimapBounds.HasValue)
                    {
                        MsgLog.ShowStatus(textBox1, $"其他玩家: {result.OtherPlayers.Count}");
                    }

                    if (_pathPlanningManager?.CurrentState != null && result.MinimapBounds.HasValue)
                    {
                        var bounds = result.MinimapBounds.Value;
                        if (_pathPlanningManager.CurrentState.PlannedPath?.Any() == true)
                        {
                            var pathScreenPoints = _pathPlanningManager.CurrentState.PlannedPath
                                .Select(p => new SdPoint(bounds.X + p.X, bounds.Y + p.Y))
                                .ToList();

                            lock (_currentPathPoints)
                            {
                                _currentPathPoints.Clear();
                                _currentPathPoints.AddRange(pathScreenPoints);
                            }

                            System.Diagnostics.Debug.WriteLine(pathScreenPoints.Count);
                        }
                        else
                        {
                            lock (_currentPathPoints) _currentPathPoints.Clear();
                        }
                    }
                }
            }
        #endregion

        private bool ShouldAddNewPoint(SdPoint newPos)
        {
            if (_recordedRoutePoints.Count == 0) return true;

            var last = _recordedRoutePoints[^1];
            var dx = newPos.X - last.X;
            var dy = newPos.Y - last.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            return dist >= MinRecordDistance;
        }

        private async void ckB_Start_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is not CheckBox chkBox) return;

            try
            {
                if (chkBox.Checked)
                {
                    // 自動啟動 LiveView 來獲取畫面（必須啟動才能處理怪物辨識和路徑規劃）
                    if (liveViewManager == null || !liveViewManager.IsRunning)
                    {
                        var captureItem = WindowFinder.TryCreateItemForWindow(Config.GameWindowTitle);
                        if (captureItem == null)
                        {
                            MsgLog.ShowError(textBox1, "找不到遊戲視窗，請先開啟遊戲。");
                            chkBox.Checked = false; // 取消選中
                            return;
                        }

                        MsgLog.ShowStatus(textBox1, "正在啟動背景擷取...");
                        
                        // 先載入小地圖（路徑規劃需要小地圖位置）
                        try
                        {
                            var minimapResult = await LoadMinimapWithMat(MinimapUsage.LiveViewOverlay);
                            if (minimapResult?.MinimapScreenRect.HasValue == true)
                            {
                                minimapBounds = minimapResult.MinimapScreenRect.Value;
                                lock (_currentMinimapBoxes)
                                {
                                    _currentMinimapBoxes.Clear();
                                    _currentMinimapBoxes.Add(minimapResult.MinimapScreenRect.Value);
                                }
                                MsgLog.ShowStatus(textBox1, "小地圖位置已定位");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"載入小地圖錯誤: {ex.Message}");
                        }
                        
                        // 啟動 LiveView（背景運行，不需要切換分頁）
                        if (liveViewManager != null)
                        {
                            liveViewManager.StartLiveView(captureItem);
                        }
                        await Task.Delay(500);
                    }

                    // 檢查並啟動怪物辨識（如果有選定模板和辨識模式）
                    bool hasMonsterTemplate = !string.IsNullOrEmpty(_selectedMonsterName) && currentMonsterMatTemplates.Any();
                    bool hasDetectionMode = !string.IsNullOrEmpty(Config.DetectionMode);
                    
                    if (hasMonsterTemplate && hasDetectionMode)
                    {
                        MsgLog.ShowStatus(textBox1, $"怪物辨識已啟動（模板：{_selectedMonsterName}，模式：{Config.DetectionMode}）");
                        // 怪物辨識會在 OnFrameAvailable 中自動處理，不需要額外啟動
                    }
                    else
                    {
                        if (!hasMonsterTemplate)
                        {
                            MsgLog.ShowStatus(textBox1, "警告：未選擇怪物模板，怪物辨識不會啟動");
                        }
                        if (!hasDetectionMode)
                        {
                            MsgLog.ShowStatus(textBox1, "警告：未選擇辨識模式，怪物辨識不會啟動");
                        }
                    }

                    // 檢查並啟動路徑規劃（如果有選定路徑檔）
                    if (loadedPathData != null && loadedPathData.WaypointPaths != null && loadedPathData.WaypointPaths.Any())
                    {
                        if (!_pathPlanningManager.IsRunning)
                        {
                            await _pathPlanningManager.StartAsync(Config.GameWindowTitle);
                            MsgLog.ShowStatus(textBox1, $"路徑規劃已啟動（已載入 {loadedPathData.WaypointPaths.Count} 個路徑點）");
                        }
                        else
                        {
                            MsgLog.ShowStatus(textBox1, "路徑規劃已在運行中");
                        }
                    }
                    else
                    {
                        MsgLog.ShowStatus(textBox1, "警告：未載入路徑檔，路徑規劃不會啟動");
                    }
                }
                else
                {
                    // 停止路徑規劃（如果正在運行）
                    if (_pathPlanningManager != null && _pathPlanningManager.IsRunning)
                    {
                        await _pathPlanningManager.StopAsync();
                        MsgLog.ShowStatus(textBox1, "路徑規劃已停止");
                    }
                    // 怪物辨識會在 LiveView 停止時自動停止（因為沒有畫面就不會處理）
                }
            }
            catch (Exception ex)
            {
                var action = chkBox.Checked ? "啟動" : "停止";
                MsgLog.ShowError(textBox1, $"自動打怪{action}失敗: {ex.Message}");
                if (chkBox.Checked)
                {
                    chkBox.Checked = false; // 發生錯誤時取消選中
                }
            }
        }
        private void cbo_MapFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                var comboBox = sender as ComboBox;
                if (comboBox?.SelectedItem == null) return;

                string selectedFileName = comboBox.SelectedItem.ToString()!;
                string mapDataDirectory = PathManager.MapDataDirectory;
                string fullPath = Path.Combine(mapDataDirectory, $"{selectedFileName}.json");

                if (!File.Exists(fullPath))
                {
                    MsgLog.ShowError(textBox1, $"找不到地圖檔案: {selectedFileName}");
                    return;
                }

                var loadedData = Config.LoadMapFromFile(fullPath);
                if (loadedData != null)
                {
                    _mapEditor?.LoadMapData(loadedData);

                    UpdateWindowTitle($"地圖編輯器 - {selectedFileName}");
                    RefreshMinimap();
                    MsgLog.ShowStatus(textBox1, $"已載入地圖檔案到編輯器: {selectedFileName}");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"載入地圖檔案時發生錯誤: {ex.Message}");
            }

        }

        private void cbo_LoadPathFile_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                var comboBox = sender as ComboBox;
                if (comboBox?.SelectedItem == null) return;

                string selectedFileName = comboBox.SelectedItem.ToString()!;
                string fullPath = Path.Combine(PathManager.MapDataDirectory, $"{selectedFileName}.json");

                if (!File.Exists(fullPath))
                {
                    MsgLog.ShowError(textBox1, $"找不到檔案: {selectedFileName}");
                    return;
                }

                // 載入路徑資料
                loadedPathData = Config.LoadMapFromFile(fullPath);
                
                // 自動將路徑點載入到路徑規劃系統（隨機模式）
                if (loadedPathData?.WaypointPaths != null && loadedPathData.WaypointPaths.Any())
                {
                    var waypoints = loadedPathData.WaypointPaths
                        .Where(coord => coord.Length == 2)
                        .Select(coord => new SdPoint((int)Math.Round(coord[0]), (int)Math.Round(coord[1])))
                        .ToList();

                    if (waypoints.Count >= 2)
                    {
                        _pathPlanningManager?.LoadPlannedPath(waypoints);
                        MsgLog.ShowStatus(textBox1, $"已載入 {waypoints.Count} 個路徑點到路徑規劃系統（隨機模式）");
                    }
                    else
                    {
                        MsgLog.ShowError(textBox1, $"路徑點數量不足（需至少2個點）");
                    }
                }
                else
                {
                    MsgLog.ShowStatus(textBox1, $"已載入: {selectedFileName}（無路徑點）");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"載入路徑檔案錯誤: {ex.Message}");
            }
        }


        private async void ProcessPathPlanning(Mat frameMat)
        {
            if (frameMat == null || frameMat.IsDisposed)
            {
                Debug.WriteLine("ProcessPathPlanning: Mat 已被釋放");
                return;
            }

            try
            {
                // 加入 Null Check
                if (gameVision == null)
                {
                    Debug.WriteLine("ProcessPathPlanning: gameVision 為 null");
                    return;
                }

                var result = gameVision.GetMinimapTracking(frameMat);

                if (result != null)
                {
                    _pathPlanningManager?.ProcessTrackingResult(result);
                    OnPathTrackingUpdated(result);
                }
            }
            catch (ObjectDisposedException ex)
            {
                Debug.WriteLine($"ProcessPathPlanning Mat已釋放: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessPathPlanning 錯誤: {ex.Message}");
            }
        }



        ///
        /// 路徑狀態變更事件處理
        ///
        private void OnPathStateChanged(PathPlanningState pathState)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<PathPlanningState>(OnPathStateChanged), pathState);
                return;
            }

            if (pathState.IsPathCompleted)
            {
                MsgLog.ShowStatus(textBox1, "🎉 恭喜！路徑規劃完成！");
            }
            else
            {
                var nextWaypoint = pathState.NextWaypoint;
                if (nextWaypoint.HasValue)
                {
                    MsgLog.ShowStatus(textBox1, $"新目標：({nextWaypoint.Value.X}, {nextWaypoint.Value.Y})");
                }
            }
        }

        ///
        /// 路徑點到達事件處理
        ///
        private void OnWaypointReached(SdPoint waypoint)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<SdPoint>(OnWaypointReached), waypoint);
                return;
            }
            MsgLog.ShowStatus(textBox1, $"已到達路徑點: ({waypoint.X}, {waypoint.Y})");
        }

        private void pictureBoxMinimap_Click(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_currentMinimapBoxes.Any()) return;

            //  1. 先將 PictureBox 座標轉換為圖片座標
            var pictureBoxPoint = new PointF(e.X, e.Y);
            var imagePoint = TranslatePictureBoxPointToImage(pictureBoxPoint, pictureBoxMinimap);

            //  2. 圖片座標轉換為螢幕座標
            var screenPoint = new PointF(
                minimapBounds.X + imagePoint.X,
                minimapBounds.Y + imagePoint.Y
            );

            //  3. 傳給 MapEditor 處理 (現在是螢幕座標)
            _mapEditor?.HandleClick(screenPoint);
            pictureBoxMinimap.Invalidate();
        }

        /// <summary>
        /// 將 PictureBox 的點擊座標轉換為圖片的實際座標 (處理 Zoom 模式)
        /// </summary>
        private PointF TranslatePictureBoxPointToImage(PointF pictureBoxPoint, PictureBox pb)
        {
            if (pb.Image == null) return PointF.Empty;

            float pbWidth = pb.ClientSize.Width;
            float pbHeight = pb.ClientSize.Height;
            float imgWidth = pb.Image.Width;
            float imgHeight = pb.Image.Height;

            // 計算縮放比例 (保持長寬比)
            float scale = Math.Min(pbWidth / imgWidth, pbHeight / imgHeight);

            // 計算居中偏移
            float scaledWidth = imgWidth * scale;
            float scaledHeight = imgHeight * scale;
            float offsetX = (pbWidth - scaledWidth) / 2;
            float offsetY = (pbHeight - scaledHeight) / 2;

            // 轉換座標
            float imageX = (pictureBoxPoint.X - offsetX) / scale;
            float imageY = (pictureBoxPoint.Y - offsetY) / scale;

            return new PointF(imageX, imageY);
        }

        /// <summary>
        /// 處理路徑錄製 - 獨立功能，不依賴路徑規劃
        /// </summary>
        private void HandleRouteRecording(SdPoint playerPos)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastRecordTime).TotalMilliseconds;

            // 檢查時間間隔和位置變化
            if (elapsed >= MinRecordIntervalMs && ShouldAddNewPoint(playerPos))
            {
                _recordedRoutePoints.Add(playerPos);
                _lastRecordTime = now;

                // 更新狀態顯示
                this.Invoke(() =>
                {
                    lbl_RecordStatus.Text = $"錄製中...({_recordedRoutePoints.Count} 點)";
                });
            }
        }

        private async void btn_RecordStart_Click(object sender, EventArgs e)
        {
            try
            {
                // 自動啟動 LiveView 來獲取畫面（必須啟動才能獲取小地圖座標）
                if (liveViewManager == null || !liveViewManager.IsRunning)
                {
                    var captureItem = WindowFinder.TryCreateItemForWindow(Config.GameWindowTitle);
                    if (captureItem == null)
                    {
                        MsgLog.ShowError(textBox1, "找不到遊戲視窗，請先開啟遊戲。");
                        return;
                    }

                    MsgLog.ShowStatus(textBox1, "正在啟動背景擷取以獲取座標...");
                    
                    // 啟動 LiveView（背景運行，不需要切換分頁）
                    if (liveViewManager != null)
                    {
                        liveViewManager.StartLiveView(captureItem);
                    }
                    await Task.Delay(500);
                }

                // 啟動 PathPlanning（如果尚未啟動）
                if (!_pathPlanningManager?.IsRunning ?? true)
                {
                    await _pathPlanningManager!.StartAsync(Config.GameWindowTitle);
                    await Task.Delay(300);
                }

                _isRecordingRoute = true;
                _recordedRoutePoints.Clear();
                _lastRecordTime = DateTime.UtcNow;

                // 更新 UI 狀態
                btn_RecordStart.Enabled = false;
                btn_RecordStop.Enabled = true;
                btn_RecordSave.Enabled = false;
                btn_RecordClear.Enabled = false;
                lbl_RecordStatus.Text = "正在錄製...";
                lbl_RecordStatus.ForeColor = Color.Green;

                MsgLog.ShowStatus(textBox1, "路徑錄製已啟動！請在遊戲中移動角色，按 F1 停止錄製。");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"啟動錄製失敗: {ex.Message}");
            }
        }

        private void btn_RecordStop_Click(object sender, EventArgs e)
        {
            try
            {
                _isRecordingRoute = false;

                btn_RecordStart.Enabled = true;
                btn_RecordStop.Enabled = false;
                btn_RecordSave.Enabled = _recordedRoutePoints.Count >= 2;
                btn_RecordClear.Enabled = _recordedRoutePoints.Count > 0;
                lbl_RecordStatus.Text = $"已錄製 {_recordedRoutePoints.Count} 點";
                lbl_RecordStatus.ForeColor = Color.Blue;

                if (_recordedRoutePoints.Count >= 2)
                {
                    MsgLog.ShowStatus(textBox1, $"停止錄製，共錄製 {_recordedRoutePoints.Count} 個點。按 F3 儲存。");
                }
                else
                {
                    MsgLog.ShowStatus(textBox1, $"停止錄製，但錄製點數不足（需至少 2 點）。");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"停止錄製失敗: {ex.Message}");
            }
        }

        private void btn_RecordSave_Click(object sender, EventArgs e)
        {
            if (_recordedRoutePoints.Count < 2)
            {
                MsgLog.ShowError(textBox1, "錄製點數不足,無法儲存路徑（需至少 2 點）");
                return;
            }

            var mapData = _mapEditor.GetCurrentMapData();
            int originalRecordCount = _recordedRoutePoints.Count;

            // 去重：移除所有重複座標
            var deduplicatedPoints = RemoveAllDuplicates(_recordedRoutePoints);
            int removedCount = originalRecordCount - deduplicatedPoints.Count;

            // 排序：先按 X 座標，再按 Y 座標排序（美觀）
            var sortedPoints = deduplicatedPoints.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

            foreach (var pt in sortedPoints)
            {
                mapData.WaypointPaths.Add(new[] {
                    MathF.Round(pt.X, 1),
                    MathF.Round(pt.Y, 1)
                });
            }

            int addedPoints = sortedPoints.Count;
            _recordedRoutePoints.Clear();
            btn_RecordSave.Enabled = false;
            btn_RecordClear.Enabled = false;
            lbl_RecordStatus.Text = "已儲存";
            lbl_RecordStatus.ForeColor = Color.Gray;
            pictureBoxMinimap.Invalidate();

            if (removedCount > 0)
            {
                MsgLog.ShowStatus(textBox1, $"已儲存 {addedPoints} 個路徑點（去重 {removedCount} 點，已排序）。總共 {mapData.WaypointPaths.Count} 點，記得儲存地圖檔案！");
            }
            else
            {
                MsgLog.ShowStatus(textBox1, $"已將 {addedPoints} 個路徑點加入地圖（已排序）。總共 {mapData.WaypointPaths.Count} 點，記得儲存地圖檔案！");
            }
        }

        /// <summary>
        /// 移除所有重複的座標點（保留首次出現的座標）
        /// 使用 LINQ DistinctBy 去重，效能更佳
        /// </summary>
        private List<SdPoint> RemoveAllDuplicates(List<SdPoint> points)
        {
            return points.DistinctBy(p => (p.X, p.Y)).ToList();
        }

        private void btn_RecordClear_Click(object sender, EventArgs e)
        {
            try
            {
                int clearedCount = _recordedRoutePoints.Count;
                _recordedRoutePoints.Clear();

                btn_RecordClear.Enabled = false;
                btn_RecordSave.Enabled = false;
                lbl_RecordStatus.Text = "已清除";
                lbl_RecordStatus.ForeColor = Color.Gray;

                MsgLog.ShowStatus(textBox1, $"已清除 {clearedCount} 個錄製點。");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"清除失敗: {ex.Message}");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F1:
                    if (btn_RecordStart.Enabled)
                        btn_RecordStart_Click(btn_RecordStart, EventArgs.Empty);
                    else if (btn_RecordStop.Enabled)
                        btn_RecordStop_Click(btn_RecordStop, EventArgs.Empty);
                    return true;

                case Keys.F3:
                    if (btn_RecordSave.Enabled)
                        btn_RecordSave_Click(btn_RecordSave, EventArgs.Empty);
                    return true;

                case Keys.F4:
                    pictureBoxMinimap.Invalidate();
                    MsgLog.ShowStatus(textBox1, "已更新路徑編輯畫面");
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}