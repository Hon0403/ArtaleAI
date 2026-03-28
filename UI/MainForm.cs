using ArtaleAI.API;
using ArtaleAI.API.Config;
using ArtaleAI.Models.Config;
using ArtaleAI.Core;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Map;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Services;
using ArtaleAI.UI;
using ArtaleAI.UI.MapEditing;
using ArtaleAI.Models.Visualization;
using ArtaleAI.Utils;
using ArtaleAI.Core.Domain.Navigation;  // 邊驅動動作架構
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using SdRect = System.Drawing.Rectangle;
using SdSize = System.Drawing.Size;
using Timer = System.Threading.Timer;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArtaleAI
{
    public partial class MainForm : Form
    {

        #region Private Fields
        private MapEditor? _mapEditor;
        private Rectangle minimapBounds = Rectangle.Empty;
        private GameVisionCore? gameVision;
        private AppConfig Config => AppConfig.Instance;

        // 檢測狀態管理
        private List<Rectangle> _currentMinimapBoxes = new();
        private List<Rectangle> _currentMinimapMarkers = new();
        private List<Mat> currentMonsterMatTemplates = new();

        private string _selectedMonsterName = string.Empty;
        private LiveViewManager? liveViewManager;

        // 圖像同步鎖
        private Bitmap? _currentDisplayFrame;
        private readonly object imageUpdateLock = new object();

        // 其他服務
        private MinimapViewer? _minimapViewer;
        private MapFileManager? _mapFileManager;
        private MonsterImageFetcher? _monsterDownloader;
        private MapData? loadedPathData = null;
        private PathPlanningManager? _pathPlanningManager;
        private CharacterMovementController? _movementController;
        private NavigationExecutor? _navigationExecutor;
        private INavigationStateMachine? _fsm;
        private GamePipeline? _gamePipeline;
        private OverlayRenderer? _overlayRenderer;



        // 狀態訊息輸出控制
        private DateTime _lastStatusUpdate = DateTime.MinValue;
        private const int StatusUpdateIntervalMs = 500; // 狀態訊息更新間隔（500ms）

        // 🔧 UI 更新節流控制（避免 UI 更新太頻繁造成卡頓）
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private const int UIUpdateIntervalMs = 33; // UI 更新間隔（33ms = 約 30Hz，人眼幾乎察覺不到）
        private volatile bool _isUIUpdatePending = false; // 是否有 UI 更新在等待執行
        private volatile bool _isLiveViewTabActive = false; // 🔧 快取的 Tab 狀態（避免每幀都 Invoke）
        private volatile bool _isPathEditingTabActive = false; // 🔧 快取的路徑編輯 Tab 狀態

        // 視覺與導航狀態旗標
        private volatile bool _visionDataReady = false; // 🔧 幀驅動同步旗標：視覺處理完成後設為 true

        // 訊息輸出去重（避免重複輸出相同訊息）
        private float _lastReportedDistance = -1;
        // 🔗 核心組件橋接 (為了向下相容)
        private string _lastReportedAction = "";

        /// <summary>
        /// 安全報告路徑規劃動作（避免重複，線程安全）
        /// </summary>
        private void ReportAction(string action)
        {
            if (action == _lastReportedAction) return;
            _lastReportedAction = action;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => MsgLog.ShowStatus(textBox1, $"🎯 {action}")));
            }
            else
            {
                MsgLog.ShowStatus(textBox1, $"🎯 {action}");
            }
        }

        #endregion

        #region Constructor & Initialization

        public MainForm()
        {
            // 🔧 初始化日誌系統（在所有初始化之前）
            Logger.Initialize("Logs", enableConsole: true);
            Logger.Info("[系統] ArtaleAI 正在啟動...");

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

                // 架構考量：MapFileManager 不再接收 UI 引用，只接收 MapEditor
                _mapFileManager = new MapFileManager(_mapEditor);
                _minimapViewer = new MinimapViewer(this, config);
                _monsterDownloader = new MonsterImageFetcher(this);

                // ComboBox 由 MainForm 自行管理（不再由 MapFileManager 操作）
                PopulateMapFilesComboBox();
                cbo_MapFiles.DropDown += (s, e) => PopulateMapFilesComboBox();

                InitializeMonsterTemplateSystem();
                InitializeDetectionModeDropdown();

                RefreshLoadPathFileOptions(false);
                cbo_LoadPathFile.DropDown += (s, e) => RefreshLoadPathFileOptions(true);

                InitializeActionComboBox();
                _mapEditor.OnNodeSelected += (action) =>
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => UpdateActionComboBoxSelection(action)));
                    else
                        UpdateActionComboBoxSelection(action);
                };

                var tracker = new PathPlanningTracker(gameVision);
                _pathPlanningManager = new PathPlanningManager(tracker, Config);
                _movementController = new CharacterMovementController();
                _movementController.SetGameWindowTitle(Config.General.GameWindowTitle);

                IPlayerPositionProvider positionProvider = new LambdaPositionProvider(
                    () => _pathPlanningManager?.CurrentState?.CurrentPlayerPosition);
                _navigationExecutor = new NavigationExecutor(
                    _movementController, positionProvider, _movementController);
                _fsm = new NavigationStateMachine(_navigationExecutor, tracker);
                _pathPlanningManager.Tracker.BindStateMachine(_fsm);

                // 架構考量：GamePipeline 封裝每幀處理流程，OverlayRenderer 負責繪製
                _gamePipeline = new GamePipeline(gameVision, _pathPlanningManager, _movementController);
                _movementController.SetSyncProvider(_gamePipeline); // 🔗 核心同步：移動層
                _navigationExecutor.SetSyncProvider(_gamePipeline); // 🔗 核心同步：執行層
                _overlayRenderer = new OverlayRenderer();

                // ── 事件訂閱 ──

                // MapFileManager 事件 → UI 更新
                _mapFileManager.MapSaved += OnMapSaved;
                _mapFileManager.MapLoaded += OnMapFileLoaded;
                _mapFileManager.StatusMessage += OnMapFileManagerStatusMessage;
                _mapFileManager.ErrorMessage += OnMapFileManagerErrorMessage;
                _mapFileManager.FileListChanged += OnMapFileListChanged;

                // GamePipeline 事件 → UI 更新
                _gamePipeline.OnFrameProcessed += OnGamePipelineFrameProcessed;
                _gamePipeline.OnStatusMessage += msg =>
                {
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => MsgLog.ShowStatus(textBox1, msg)));
                    else
                        MsgLog.ShowStatus(textBox1, msg);
                };
                _gamePipeline.OnPathTrackingResult += OnPathTrackingUpdated;

                // PathPlanningManager 事件
                // Bug Fix：移除對 OnTrackingUpdated 的越級訂閱
                // 原因：GamePipeline 內部已呼叫 ProcessTrackingResult 並透過
                // OnPathTrackingResult 事件傳遞結果，重複訂閱會導致每幀呼叫兩次
                // _pathPlanningManager.OnTrackingUpdated += OnPathTrackingUpdated; // 已由 GamePipeline 統一管理
                liveViewManager = new LiveViewManager(config);
                liveViewManager.OnFrameReady += OnFrameAvailable;
                numericUpDownZoom.Value = Config.General.ZoomFactor;

                MsgLog.ShowStatus(textBox1, " 所有服務初始化完成");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"初始化失敗: {ex.Message}");
                Logger.Error($"[系統] InitializeServices 失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 刷新主控台的路徑檔案下拉選單
        /// </summary>
        /// <param name="suppressLog">是否隱藏Log</param>
        private void RefreshLoadPathFileOptions(bool suppressLog = false)
        {
            try
            {
                var currentSelection = cbo_LoadPathFile.Text;
                cbo_LoadPathFile.Items.Clear();
                string mapDataDirectory = PathManager.MapDataDirectory;

                if (!Directory.Exists(mapDataDirectory))
                {
                    Directory.CreateDirectory(mapDataDirectory);
                }

                if (!Directory.Exists(mapDataDirectory))
                {
                    Directory.CreateDirectory(mapDataDirectory);
                }

                // Add default None option
                cbo_LoadPathFile.Items.Add("null");

                var mapFiles = Directory.GetFiles(mapDataDirectory, "*.json");

                foreach (var file in mapFiles)
                {
                    cbo_LoadPathFile.Items.Add(Path.GetFileNameWithoutExtension(file));
                }

                // 還原選擇
                if (!string.IsNullOrEmpty(currentSelection) && cbo_LoadPathFile.Items.Contains(currentSelection))
                {
                    cbo_LoadPathFile.Text = currentSelection;
                }

                if (!suppressLog)
                    MsgLog.ShowStatus(textBox1, $"載入 {mapFiles.Length} 個路徑檔案到路徑規劃下拉選單");
            }
            catch (Exception ex)
            {
                if (!suppressLog)
                    MsgLog.ShowError(textBox1, $"刷新路徑列表失敗: {ex.Message}");
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
                    monsterNames = subDirectories.Select(p => Path.GetFileName(p) ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToList();
                }

                cbo_MonsterTemplates.Items.Clear();
                // Add default None option
                cbo_MonsterTemplates.Items.Add("null");

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

        // 🔧 性能優化：使用非阻塞 UI 更新 + 節流控制
        private void UpdateDisplay(Bitmap newFrame)
        {
            if (newFrame?.Width <= 0 || newFrame?.Height <= 0)
            {
                newFrame?.Dispose();
                return;
            }

            // 🔧 UI 節流：如果距離上次更新時間太短，或有更新正在等待，則跳過此幀
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastUIUpdate).TotalMilliseconds;

            if (elapsed < UIUpdateIntervalMs || _isUIUpdatePending)
            {
                // 跳過此幀以避免 UI 堆積，但記錄以便調試
                newFrame?.Dispose();
                return;
            }

            _lastUIUpdate = now;
            _isUIUpdatePending = true;

            Action updateAction = () =>
            {
                try
                {
                    lock (imageUpdateLock)
                    {
                        var oldImage = pictureBoxLiveView.Image;
                        pictureBoxLiveView.Image = newFrame;
                        _currentDisplayFrame = newFrame;
                        oldImage?.Dispose();
                    }
                }
                finally
                {
                    _isUIUpdatePending = false;
                }
            };

            if (InvokeRequired)
            {
                // 🔧 使用非阻塞的 BeginInvoke，不會造成背景執行緒等待
                BeginInvoke(updateAction);
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
            rdo_RopeMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;

            // 小地圖滑鼠事件
            // Event bindings for Paint, MouseMove, MouseLeave are already in Designer.cs

            // 🎨 設定深色主題背景
            pictureBoxMinimap.BackColor = Color.FromArgb(45, 45, 48);

            // 按鈕事件
            // Event bindings for btn_SaveMap and btn_New are already in Designer.cs

            // 🔧 自動攻擊相關事件綁定（更新快取狀態）
            ckB_Start.CheckedChanged += (s, e) => UpdateAutoAttackState();
            cbo_LoadPathFile.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
            cbo_DetectMode.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
            // cbo_MonsterTemplates 已在 InitializeMonsterTemplateSystem 中綁定 OnMonsterSelectionChanged
            // 我們需要額外呼叫 UpdateAutoAttackState
            cbo_MonsterTemplates.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();

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

            numericUpDownZoom.Value = Config.General.ZoomFactor;
            MsgLog.ShowStatus(textBox1, "配置檔案載入完成");
        }

        public void OnMapSaved(string fileName, bool isNewFile)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnMapSaved(fileName, isNewFile));
                return;
            }

            UpdateWindowTitle($"地圖編輯器 - {fileName}");

            if (isNewFile)
            {
                PopulateMapFilesComboBox(); // 刷新下拉清單以包含新檔案
            }

            RefreshMinimap();
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

            if (config.Vision.DisplayOrder?.Any() == true && config.Vision.DetectionModes?.Any() == true)
            {
                try
                {
                    foreach (var mode in config.Vision.DisplayOrder)
                    {
                        if (config.Vision.DetectionModes.TryGetValue(mode, out var modeConfig))
                        {
                            cbo_DetectMode.Items.Add(modeConfig.DisplayName);
                        }
                    }

                    var defaultMode = config.Vision.DefaultMode;
                    if (config.Vision.DetectionModes.TryGetValue(defaultMode, out var defaultModeConfig))
                    {
                        cbo_DetectMode.SelectedItem = defaultModeConfig.DisplayName;
                    }

                    MsgLog.ShowStatus(textBox1, $"檢測模式已載入：{config.Vision.DisplayOrder.Count} 個模式，預設：{defaultMode}");
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
            var selectedMode = config.Vision.DetectionModes?
                .FirstOrDefault(kvp => kvp.Value.DisplayName == selectedDisplayText).Key
                ?? config.Vision.DefaultMode ?? "Normal";

            // 2. 直接找到最佳遮擋設定（內嵌邏輯）
            var optimalOcclusion = "None";
            if (config.Vision.DetectionModes?.TryGetValue(selectedMode, out var modeConfig) == true)
            {
                optimalOcclusion = modeConfig.Occlusion;
            }

            // 3. 套用設定
            config.Vision.DetectionMode = selectedMode;
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
            Config.General.ZoomFactor = numericUpDownZoom.Value;
            Config.Save();

        }

        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // 🔧 性能優化：快取 Tab 狀態，避免每幀都進行 Invoke 檢查
            _isLiveViewTabActive = tabControl1.SelectedIndex == 2;
            _isPathEditingTabActive = tabControl1.SelectedIndex == 1; // 路徑編輯分頁

            // 🔧 修復：當切換到即時顯示分頁且 LiveView 已在運行時，不要中斷它
            // 這可以避免路徑追蹤期間因為 LiveView 重啟導致的位置資料遺失
            bool isLiveViewRunning = liveViewManager?.IsRunning == true;
            bool isSwitchingToLiveView = _isLiveViewTabActive;

            // 只有在非切換到即時顯示分頁，或 LiveView 未運行時才停止資源
            if (!isSwitchingToLiveView || !isLiveViewRunning)
            {
                StopAndReleaseAllResources();
            }
            else
            {
                Logger.Debug("[系統] 切換到即時顯示分頁，保持 LiveView 運行以避免路徑追蹤中斷");
            }

            switch (tabControl1.SelectedIndex)
            {
                case 0: // 主控台頁面
                    UpdateWindowTitle("ArtaleAI");
                    // 🔧 修復：若自動打怪已啟動，保持顯示小地圖視窗
                    if (ckB_Start.Checked)
                    {
                        _minimapViewer?.Show();
                    }
                    else
                    {
                        _minimapViewer?.Hide();
                    }
                    break;
                case 1: // 路徑編輯頁面
                    StartPathEditingModeAsync();
                    _minimapViewer?.Hide(); // 隱藏小地圖視窗
                    // 標題會在載入地圖檔案時更新
                    break;
                case 2: // 即時顯示頁面
                    UpdateWindowTitle("ArtaleAI - 即時顯示");

                    // 🔧 只有當 LiveView 未運行時才啟動它
                    if (!isLiveViewRunning)
                    {
                        await StartLiveViewModeAsync();
                    }


                    // 顯示小地圖放大視窗
                    _minimapViewer?.Show();
                    _minimapViewer?.Show();
                    break;
                default:
                    UpdateWindowTitle("ArtaleAI");
                    _minimapViewer?.Hide(); // 隱藏小地圖視窗
                    break;
            }
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
                        _gamePipeline?.SetMinimapBoxes(new List<Rectangle> { result.MinimapScreenRect.Value });
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
                Logger.Debug("[小地圖] 開始 LoadMinimapWithMat");
                MsgLog.ShowStatus(textBox1, "正在載入小地圖...");

                var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
                if (captureItem == null)
                {
                    MsgLog.ShowError(textBox1, $"無法建立捕獲項目: {Config.General.GameWindowTitle}");
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
                Logger.Error($"[小地圖] LoadMinimapWithMat 錯誤: {ex.Message}");
                MsgLog.ShowError(textBox1, $"載入小地圖失敗: {ex.Message}");
                return null;
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
                    _gamePipeline?.SetMinimapBoxes(new List<Rectangle> { result.MinimapScreenRect.Value });
                    MsgLog.ShowStatus(textBox1, "小地圖位置已定位");

                    // 使用LiveViewManager啟動Timer
                    var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
                    if (captureItem != null)
                    {
                        liveViewManager?.StartLiveView(captureItem);
                        MsgLog.ShowStatus(textBox1, "即時畫面已啟動");
                    }
                    else
                    {
                        MsgLog.ShowError(textBox1, $"找不到遊戲視窗：{Config.General.GameWindowTitle}");
                    }
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"啟動失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 當 LiveViewManager 傳來新畫面時，委派給 GamePipeline 處理
        /// 架構考量：MainForm 只負責 Dispose 檢查和 UI 更新觸發，所有偵測邏輯已移至 GamePipeline
        /// </summary>
        private void OnFrameAvailable(Mat frameMat, DateTime captureTime)
        {
            if (IsDisposed || Disposing)
            {
                frameMat?.Dispose();
                return;
            }

            if (frameMat == null || frameMat.Empty()) return;

            try
            {
                using (frameMat)
                {
                    var config = Config;
                    if (config == null) return;

                    // 同步可變狀態到 GamePipeline
                    if (_gamePipeline != null)
                    {
                        _gamePipeline.AutoAttackEnabled = _autoAttackEnabled;
                        _gamePipeline.SelectedMonsterName = _selectedMonsterName;
                        _gamePipeline.MonsterTemplates = currentMonsterMatTemplates;

                        // 委派過去的 195 行邏輯
                        _gamePipeline.ProcessFrame(frameMat, captureTime, config);

                        // Bug Fix：同步 VisionDataReady 旗標回 MainForm
                        // 原因：ProcessPathPlanningUpdate 現在在 GamePipeline 內設定此旗標，
                        // 但 OnPathTrackingUpdated 仍檢查 MainForm._visionDataReady
                        _visionDataReady = _gamePipeline.VisionDataReady;

                        // [修復]：補回即時顯示畫面渲染
                        // 因為 frameMat 將在 using 區塊結束後自動 Dispose，故必須在此提早繪製
                        if (_isLiveViewTabActive && _overlayRenderer != null)
                        {
                            var resultSnapshot = _gamePipeline.GetCurrentSnapshot();
                            using var bmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frameMat);
                            using var drawnBmp = _overlayRenderer.Render(bmp, resultSnapshot, config);
                            UpdateDisplay((Bitmap)drawnBmp.Clone());
                        }
                    }

                    // UI 更新：路徑編輯分頁的紅點跟隨
                    var now = DateTime.UtcNow;
                    if (_isPathEditingTabActive)
                    {
                        if (!_isUIUpdatePending && (now - _lastUIUpdate).TotalMilliseconds >= UIUpdateIntervalMs)
                        {
                            _isUIUpdatePending = true;
                            _lastUIUpdate = now;
                            BeginInvoke(new Action(() =>
                            {
                                try { pictureBoxMinimap.Invalidate(); }
                                finally { _isUIUpdatePending = false; }
                            }));
                        }
                    }

                    // 獨立視窗更新：僅在視窗可見時推送影像，避免不必要的 clone/繪製成本。
                    if (_minimapViewer?.IsVisible == true)
                    {
                        using var minimapClone = gameVision?.GetLastMinimapMatClone();
                        if (minimapClone != null)
                        {
                            PathVisualizationData? pathData = null;
                            if (_pathPlanningManager?.IsRunning == true)
                            {
                                pathData = BuildPathVisualizationData();
                            }
                            _minimapViewer.UpdateMinimapWithPath(minimapClone, pathData);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[系統] 處理畫面錯誤: {ex.Message}");
            }
        }


        // ============================================================
        // 已遷移至 GamePipeline 的方法：
        //   ProcessMonsters → GamePipeline.ProcessMonsterDetection
        //   CheckAutoAttackCondition → GamePipeline.CheckAutoAttackCondition
        //   PerformAutoAttackAsync → GamePipeline.PerformAutoAttackSequenceAsync
        //   RenderAndDisplayOverlays → OverlayRenderer.Render
        // ============================================================


        // 🔧 快取的自動攻擊啟用狀態（由 UI 執行緒更新，避免跨執行緒存取）
        private volatile bool _autoAttackEnabled = false;

        /// <summary>更新自動攻擊啟用狀態</summary>
        private void UpdateAutoAttackState()
        {
            _autoAttackEnabled = ckB_Start.Checked &&
                                 cbo_LoadPathFile.SelectedIndex > 0 &&
                                 cbo_DetectMode.SelectedItem != null &&
                                 cbo_MonsterTemplates.SelectedIndex > 0;
        }

        // ============================================================
        // GamePipeline 事件處理
        // ============================================================

        /// <summary>
        /// GamePipeline 每幀處理完成後的事件處理
        /// 將偵測結果透過 OverlayRenderer 繪製到畫面上
        /// </summary>
        private void OnGamePipelineFrameProcessed(FrameProcessingResult result)
        {
            if (result == null) return;

            // 同步偵測結果到 MainForm 的共享狀態（供其他方法使用）
            _currentMinimapBoxes = result.MinimapBoxes;
            _currentMinimapMarkers = result.MinimapMarkers;

            // 注意：即時顯示分頁 (LiveView) 的疊加層繪製，
            // 已經移至 OnFrameAvailable 函式內，以確保能拿到尚未 Dispose 的 frameMat。
        }

        // ============================================================
        // MapFileManager 事件處理 + ComboBox 管理
        // ============================================================

        /// <summary>填充地圖檔案下拉選單（從 MapFileManager 取得檔案清單）</summary>
        private void PopulateMapFilesComboBox()
        {
            try
            {
                var currentSelection = cbo_MapFiles.SelectedItem?.ToString();
                cbo_MapFiles.Items.Clear();
                cbo_MapFiles.Items.Add("null");

                var files = _mapFileManager?.GetAvailableMapFiles() ?? Array.Empty<string>();
                foreach (var file in files)
                    cbo_MapFiles.Items.Add(file);

                // 恢復之前的選擇
                if (!string.IsNullOrEmpty(currentSelection) && cbo_MapFiles.Items.Contains(currentSelection))
                    cbo_MapFiles.SelectedItem = currentSelection;
            }
            catch (Exception ex)
            {
                Logger.Error($"[地圖] 填充檔案清單失敗: {ex.Message}");
            }
        }



        /// <summary>地圖載入完成事件處理 — 更新標題列和小地圖</summary>
        private void OnMapFileLoaded(string fileName)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnMapFileLoaded(fileName)));
                return;
            }

            UpdateWindowTitle($"地圖編輯器 - {fileName}");

            // 如果是新地圖，清空下拉選單選取，避免混淆
            if (fileName == "(新地圖)")
            {
                cbo_MapFiles.SelectedIndex = -1;
            }

            RefreshMinimap();
            MsgLog.ShowStatus(textBox1, $"載入地圖: {fileName}");
        }

        private void OnMapFileManagerStatusMessage(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnMapFileManagerStatusMessage), message);
                return;
            }

            MsgLog.ShowStatus(textBox1, message);
        }

        private void OnMapFileManagerErrorMessage(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnMapFileManagerErrorMessage), message);
                return;
            }

            MsgLog.ShowError(textBox1, message);
        }

        private void OnMapFileListChanged()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnMapFileListChanged));
                return;
            }

            PopulateMapFilesComboBox();
        }


        /// <summary>
        /// 建立路徑可視化資料（供 MinimapViewer 使用）
        /// </summary>
        private PathVisualizationData? BuildPathVisualizationData()
        {
            try
            {
                var pathData = new PathVisualizationData();

                // 取得路徑節點與連線資料（從新版 NavigationGraph）
                var graph = _pathPlanningManager?.Tracker?.NavGraph;
                if (graph != null && graph.NodeCount > 0)
                {
                    // 1. 取得所有節點 (區分平台與繩索)
                    var allNodes = graph.GetAllNodes();

                    var platformNodes = allNodes.Where(n => n.Type == ArtaleAI.Core.Domain.Navigation.NavigationNodeType.Platform).ToList();
                    var ropeNodes = allNodes.Where(n => n.Type == ArtaleAI.Core.Domain.Navigation.NavigationNodeType.Rope).ToList();

                    // 將平台節點轉換為視覺化用的 WaypointWithPriority
                    pathData.WaypointPaths = platformNodes
                        .Select(n => new WaypointWithPriority(
                            new SdPointF(n.Position.X, n.Position.Y),
                            0f, // 暫時給預設值，因為優先權系統可能已重構
                            false,
                            _pathPlanningManager?.Tracker?.CurrentTarget?.Position.X == n.Position.X && _pathPlanningManager?.Tracker?.CurrentTarget?.Position.Y == n.Position.Y))
                        .ToList();

                    // 2. 將繩索節點轉換為 RopeWithAccessibility
                    // 注意：舊版資料有 TopY 和 BottomY，新版目前只有單點
                    // 這裡暫時用上下延伸一點範圍來繪圖，如果有新版 Edge 資料可再優化
                    pathData.Ropes = ropeNodes
                        .Select(n => new RopeWithAccessibility(
                            n.Position.X,
                            n.Position.Y - 50, // 假設往上延伸 50px
                            n.Position.Y + 50, // 假設往下延伸 50px
                            0f,
                            false,
                            false))
                        .ToList();
                }

                // 玩家位置（從當前小地圖標記取得中心點）
                if (_currentMinimapMarkers.Any() && !minimapBounds.IsEmpty)
                {
                    var marker = _currentMinimapMarkers.First();
                    pathData.PlayerPosition = new SdPointF(
                        marker.X + marker.Width / 2f - minimapBounds.X,
                        marker.Y + marker.Height / 2f - minimapBounds.Y);
                }
                // 目標位置（從路徑系統取得）
                var nextWp = _pathPlanningManager?.CurrentState?.NextWaypoint;
                if (nextWp.HasValue && !minimapBounds.IsEmpty)
                {
                    pathData.TargetPosition = new SdPointF(
                        nextWp.Value.X - minimapBounds.X,
                        nextWp.Value.Y - minimapBounds.Y);
                }


                // 🎯 臨時目標位置 (動作點/中間點)
                // 修正：已經是小地圖相對座標，不需減去 minimapBounds
                var tempTarget = _pathPlanningManager?.Tracker?.CurrentPathState?.TemporaryTarget;
                if (tempTarget.HasValue && !minimapBounds.IsEmpty)
                {
                    pathData.TemporaryTarget = new SdPointF(
                        tempTarget.Value.X,
                        tempTarget.Value.Y);
                }

                // 🏁 下一個即將到達的節點（立即目標）
                if (nextWp.HasValue)
                {
                    pathData.FinalDestination = new SdPointF(nextWp.Value.X, nextWp.Value.Y);
                }

                // === 診斷層：SSOT Hitbox + 繩索對位帶 + 當前動作 ===
                var tracker = _pathPlanningManager?.Tracker;
                var currentTargetNode = tracker?.CurrentTarget;
                if (currentTargetNode?.Hitbox is BoundingBox hitbox)
                {
                    pathData.TargetHitbox = new RectangleF(
                        hitbox.MinX,
                        hitbox.MinY,
                        hitbox.MaxX - hitbox.MinX,
                        hitbox.MaxY - hitbox.MinY);
                }

                if (pathData.PlayerPosition.HasValue)
                {
                    pathData.IsPlayerInsideTargetHitbox = tracker?.IsPlayerAtTarget() ?? false;
                }

                var currentEdge = tracker?.CurrentNavigationEdge;
                if (currentEdge != null)
                {
                    pathData.CurrentAction = currentEdge.ActionType.ToString();
                    if (currentEdge.ActionType == NavigationActionType.ClimbUp ||
                        currentEdge.ActionType == NavigationActionType.ClimbDown)
                    {
                        float ropeX = float.NaN;
                        foreach (var seq in currentEdge.InputSequence)
                        {
                            if (seq.StartsWith("ropeX:") &&
                                float.TryParse(seq.Substring(6), out var parsedRopeX))
                            {
                                ropeX = parsedRopeX;
                                break;
                            }
                        }

                        if (!float.IsNaN(ropeX))
                        {
                            pathData.RopeAlignCenterX = ropeX;
                            // 與執行層一致：對位容許量上限為 2.0f
                            pathData.RopeAlignTolerance = Math.Min((float)AppConfig.Instance.Navigation.WaypointReachDistance, 2.0f);
                        }
                    }
                }
                // [移除] 動態插值已廢除，不再顯示 intermediate points

                return pathData;
            }
            catch (Exception ex)
            {
                Logger.Error($"[MinimapViewer] BuildPathVisualizationData 錯誤: {ex.Message}");
                return null;
            }
        }

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
                nameof(rdo_RopeMarker) => EditMode.Rope,
                nameof(rdo_DeleteMarker) => EditMode.Delete,
                nameof(rdo_SelectMode) => EditMode.Select,
                nameof(rdo_TwoPointLink) => EditMode.Link,
                _ => EditMode.None
            };

            // 路線標記／兩點連線／選取：皆需「動作類型」語意（連線動作或批次套用）。
            groupBox_Action.Enabled = selectedMode is EditMode.Waypoint or EditMode.Select or EditMode.Link;

            _mapEditor.SetEditMode(selectedMode);
            pictureBoxMinimap.Invalidate();

            MsgLog.ShowStatus(textBox1, $"編輯模式切換至: {selectedMode}");
        }

        private void rdo_SelectMode_CheckedChanged(object sender, EventArgs e)
        {
            OnEditModeChanged(sender, e);
        }

        #region Merged UI Events (from partial classes)

        /// <summary>
        /// 更新獨立小地圖視窗的可見性
        /// </summary>
        private void UpdateMinimapViewerVisibility()
        {
            try
            {
                if (_minimapViewer == null) return;
                bool pathLoaded = loadedPathData != null && (loadedPathData.Nodes?.Count ?? 0) > 0;
                bool autoStartChecked = ckB_Start.Checked;
                bool liveViewReady = liveViewManager?.IsRunning == true && _isLiveViewTabActive;

                // 即時顯示分頁可直接預覽；若是自動導航流程，仍維持原先可見規則。
                if (liveViewReady || (pathLoaded && autoStartChecked))
                {
                    _minimapViewer.Show();
                }
                else
                {
                    _minimapViewer.Hide();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[獨立視窗] 更新視窗可見性失敗: {ex.Message}");
            }
        }

        private void cbo_MapFiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbo_MapFiles.SelectedItem == null) return;
            string selectedFile = cbo_MapFiles.SelectedItem.ToString() ?? "";
            if (selectedFile == "null" || string.IsNullOrEmpty(selectedFile)) return;

            MsgLog.ShowStatus(textBox1, $"載入地圖檔案: {selectedFile}");
            _mapFileManager?.LoadMapFile(selectedFile);
        }

        private void cbo_LoadPathFile_SelectedIndexChanged(object sender, EventArgs e)
        {
            string? selectedFile = cbo_LoadPathFile.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedFile) || selectedFile == "null")
            {
                loadedPathData = null;
                _mapEditor?.LoadMapData(new MapData());
                pictureBoxMinimap.Invalidate();
                return;
            }
            try
            {
                var mapFilePath = System.IO.Path.Combine(
                    PathManager.MapDataDirectory, $"{selectedFile}.json");
                var mapData = MapFileManager.LoadMapFromFile(mapFilePath);
                if (mapData != null)
                {
                    loadedPathData = mapData;
                    _mapEditor?.LoadMapData(mapData);
                    _pathPlanningManager?.LoadMap(mapData);
                    pictureBoxMinimap.Invalidate();
                    MsgLog.ShowStatus(textBox1, $"已載入路徑檔: {selectedFile}");
                }
                else
                {
                    loadedPathData = null;
                    _mapEditor?.LoadMapData(new MapData());
                    pictureBoxMinimap.Invalidate();
                    MsgLog.ShowError(textBox1, $"無法載入路徑檔: {selectedFile}");
                }
            }
            catch (Exception ex)
            {
                loadedPathData = null;
                _mapEditor?.LoadMapData(new MapData());
                pictureBoxMinimap.Invalidate();
                MsgLog.ShowError(textBox1, $"路徑檔載入失敗: {ex.Message}");
            }
        }

        #endregion

        private void InitializeActionComboBox()
        {
            cbo_ActionType.Items.Clear();
            cbo_ActionType.Items.Add(new ComboBoxItem("Walk (走路)", 0));
            cbo_ActionType.Items.Add(new ComboBoxItem("SmartSideJump (智慧側跳)", 13));
            cbo_ActionType.Items.Add(new ComboBoxItem("Jump (原地跳)", 8));
            cbo_ActionType.Items.Add(new ComboBoxItem("JumpDown (下跳)", 4));
            cbo_ActionType.Items.Add(new ComboBoxItem("ClimbUp (上爬)", 11));
            cbo_ActionType.Items.Add(new ComboBoxItem("ClimbDown (下爬)", 12));

            cbo_ActionType.SelectedIndex = 0; // Default Walk
        }

        private void cbo_ActionType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbo_ActionType.SelectedItem is ComboBoxItem item)
            {
                _mapEditor.SetCurrentActionType(item.Value);
                pictureBoxMinimap.Invalidate();
            }
        }

        private void UpdateActionComboBoxSelection(int action)
        {
            if (action == -1) return;
            // 舊資料中的 9/10（左/右跳）在 UI 下拉統一顯示為智慧側跳
            int targetAction = (action == 9 || action == 10) ? 13 : action;

            foreach (ComboBoxItem item in cbo_ActionType.Items)
            {
                if (item.Value == targetAction)
                {
                    cbo_ActionType.SelectedItem = item;
                    break;
                }
            }
        }
        private class ComboBoxItem
        {
            public string Text { get; }
            public int Value { get; }
            public ComboBoxItem(string text, int value) { Text = text; Value = value; }
            public override string ToString() => Text;
        }

        #endregion

        #region PictureBox 滑鼠事件

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {

            //  3. 簡化：更新預覽位置
            if (_mapEditor != null && !minimapBounds.IsEmpty && pictureBoxMinimap.Image != null)
            {
                var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
                var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);

                _mapEditor.UpdateMousePosition(screenPoint);
                _mapEditor.UpdateHoveredNode(screenPoint); // 更新懸停節點高亮
                pictureBoxMinimap.Invalidate();

                // 🔧 更新座標顯示標籤（使用小地圖相對座標，與路徑檔一致）
                lbl_MouseCoords.Text = $"座標: ({imagePoint.X:F1}, {imagePoint.Y:F1})";
            }
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            if (_mapEditor == null || minimapBounds.IsEmpty || pictureBoxMinimap.Image == null)
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

            // 🕸️ 繪製網格線 (在路徑之前繪製)
            DrawPathEditorGrid(e.Graphics, imgWidth, imgHeight, scaleX, scaleY, offsetX, offsetY);

            //  呼叫 MapEditor 的 Render (繪製所有路徑和預覽線)
            _mapEditor.Render(e.Graphics, ConvertScreenToDisplay);

            // 📏 繪製座標刻度尺（上方和左側）
            DrawPathEditorRuler(e.Graphics, imgWidth, imgHeight, scaleX, scaleY, offsetX, offsetY);

            // 繪製錄製中的路徑（即時顯示）

        }



        /// <summary>
        /// 🕸️ 在路徑編輯的小地圖上繪製網格線
        /// </summary>
        private void DrawPathEditorGrid(Graphics g, float imgWidth, float imgHeight,
            float scaleX, float scaleY, float offsetX, float offsetY)
        {
            const int MajorTickInterval = 10;   // 主刻度間距 (座標)
            const int RulerSize = 18;           // 避開刻度尺區域

            // 網格樣式 (非常淡的白色)
            using var gridPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1);

            // X 軸網格 (垂直線)
            for (int x = 0; x <= (int)imgWidth; x++)
            {
                if (x % MajorTickInterval == 0 && x != 0)
                {
                    float screenX = offsetX + x * scaleX;
                    // 從刻度尺下方開始畫
                    g.DrawLine(gridPen, screenX, offsetY + RulerSize, screenX, offsetY + imgHeight * scaleY);
                }
            }

            // Y 軸網格 (水平線)
            for (int y = 0; y <= (int)imgHeight; y++)
            {
                if (y % MajorTickInterval == 0 && y != 0)
                {
                    float screenY = offsetY + y * scaleY;
                    // 從刻度尺右方開始畫
                    g.DrawLine(gridPen, offsetX + RulerSize, screenY, offsetX + imgWidth * scaleX, screenY);
                }
            }
        }

        /// <summary>
        /// 📏 在路徑編輯的小地圖上繪製座標刻度尺（上方和左側）
        /// </summary>
        private void DrawPathEditorRuler(Graphics g, float imgWidth, float imgHeight,
            float scaleX, float scaleY, float offsetX, float offsetY)
        {
            // 刻度尺參數
            const int RulerSize = 18;           // 刻度尺寬度/高度
            const int MajorTickInterval = 10;   // 主刻度間距 (座標)
            const int MinorTickInterval = 5;    // 次刻度間距 (座標)

            // 🎨 深色主題配色
            var bgColor = Color.FromArgb(30, 30, 30);            // 刻度尺背景（更深）
            var tickColor = Color.FromArgb(100, 100, 100);       // 刻度線（柔和灰）
            var textColor = Color.FromArgb(200, 200, 200);       // 文字（亮灰）
            var majorTickLength = RulerSize - 4;
            var minorTickLength = RulerSize / 2;

            using var bgBrush = new SolidBrush(bgColor);
            using var tickPen = new Pen(tickColor, 1);
            using var textBrush = new SolidBrush(textColor);
            using var font = new Font("Consolas", 7f, FontStyle.Regular);

            // ===== 上方刻度尺 (X 軸) =====
            var topRulerRect = new RectangleF(offsetX, 0, imgWidth * scaleX, RulerSize);
            g.FillRectangle(bgBrush, topRulerRect);

            for (int x = 0; x <= (int)imgWidth; x++)
            {
                if (x % MajorTickInterval == 0 || x % MinorTickInterval == 0)
                {
                    float screenX = offsetX + x * scaleX;

                    if (x % MajorTickInterval == 0)
                    {
                        g.DrawLine(tickPen, screenX, RulerSize - majorTickLength, screenX, RulerSize);
                        var text = x.ToString();
                        var textSize = g.MeasureString(text, font);
                        g.DrawString(text, font, textBrush, screenX - textSize.Width / 2, 1);
                    }
                    else
                    {
                        g.DrawLine(tickPen, screenX, RulerSize - minorTickLength, screenX, RulerSize);
                    }
                }
            }

            // ===== 左側刻度尺 (Y 軸) =====
            var leftRulerRect = new RectangleF(0, offsetY + RulerSize, RulerSize, imgHeight * scaleY - RulerSize);
            g.FillRectangle(bgBrush, leftRulerRect);

            for (int y = 0; y <= (int)imgHeight; y++)
            {
                if (y % MajorTickInterval == 0 || y % MinorTickInterval == 0)
                {
                    float screenY = offsetY + y * scaleY;

                    if (y % MajorTickInterval == 0)
                    {
                        g.DrawLine(tickPen, RulerSize - majorTickLength, screenY, RulerSize, screenY);
                        var text = y.ToString();
                        var textSize = g.MeasureString(text, font);
                        g.DrawString(text, font, textBrush, 1, screenY - textSize.Height / 2);
                    }
                    else
                    {
                        g.DrawLine(tickPen, RulerSize - minorTickLength, screenY, RulerSize, screenY);
                    }
                }
            }

            // 左上角方塊（交接處）
            g.FillRectangle(bgBrush, 0, 0, RulerSize, RulerSize);
        }


        private void pictureBoxMinimap_Click(object sender, MouseEventArgs e)
        {
            if (_mapEditor == null || minimapBounds.IsEmpty) return;
            var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
            var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);
            _mapEditor.HandleClick(screenPoint, e.Button);
            pictureBoxMinimap.Invalidate();
        }

        private static PointF TranslatePictureBoxPointToImage(PointF pbPoint, PictureBox pb)
        {
            if (pb.Image == null) return pbPoint;
            float pbW = pb.ClientSize.Width, pbH = pb.ClientSize.Height;
            float imgW = pb.Image.Width, imgH = pb.Image.Height;
            float scale, offsetX = 0f, offsetY = 0f;
            if (pbW / pbH > imgW / imgH)
            {
                scale = pbH / imgH;
                offsetX = (pbW - imgW * scale) / 2f;
            }
            else
            {
                scale = pbW / imgW;
                offsetY = (pbH - imgH * scale) / 2f;
            }
            if (scale <= 0f) return pbPoint;
            return new PointF((pbPoint.X - offsetX) / scale, (pbPoint.Y - offsetY) / scale);
        }

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            try
            {

                lbl_MouseCoords.Text = "座標: (-, -)";

                // 🔧 新增：清除編輯器相關狀態
                if (_mapEditor != null)
                {
                    _mapEditor.UpdateMousePosition(new PointF(-1000, -1000));
                    _mapEditor.UpdateHoveredNode(new PointF(-1000, -1000));
                }

                // 重繪畫布
                pictureBoxMinimap.Invalidate();
            }
            catch (Exception ex)
            {
                Logger.Debug($"[UI] MouseLeave 錯誤: {ex.Message}");
            }
        }

        #endregion

        #region 按鈕事件

        private void btn_SaveMap_Click(object sender, EventArgs e)
        {
            try
            {
                if (_mapFileManager == null) return;

                if (!_mapFileManager.HasCurrentMap)
                {
                    // 如果是新地圖且無路徑，先給提示
                    using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                    {
                        saveFileDialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                        saveFileDialog.InitialDirectory = PathManager.MapDataDirectory;
                        saveFileDialog.Title = "另存新地圖檔案";

                        if (saveFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            _mapFileManager.SaveMapToPath(saveFileDialog.FileName);
                        }
                    }
                }
                else
                {
                    _mapFileManager.SaveCurrentMap();
                }
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
                var result = MessageBox.Show("確定要清空當前地圖並建立新檔案嗎？\n(未儲存的變更將會遺失)",
                    "建立新地圖", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    _mapFileManager?.CreateNewMap();
                    MessageBox.Show("已建立新地圖，您可以開始進行錄製。", "地圖編輯器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        #endregion

        #region 清理與釋放

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                // 在視窗與控制項仍有效時先行持久化，避免關閉後狀態遺失。
                AppConfig.Instance.Save();
            }
            catch (Exception ex)
            {
                Logger.Error($"[系統] OnFormClosing 存檔失敗: {ex.Message}");
            }

            base.OnFormClosing(e);
        }

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
                // 修復：取消事件訂閱以避免記憶體洩漏
                // 注意：Lambda 訂閱無法直接取消，但會在 Dispose 時自動清理
                if (_mapFileManager != null)
                {
                    _mapFileManager.MapSaved -= OnMapSaved;
                    _mapFileManager.MapLoaded -= OnMapFileLoaded;
                    _mapFileManager.StatusMessage -= OnMapFileManagerStatusMessage;
                    _mapFileManager.ErrorMessage -= OnMapFileManagerErrorMessage;
                    _mapFileManager.FileListChanged -= OnMapFileListChanged;
                }

                if (_pathPlanningManager != null)
                {
                    // OnTrackingUpdated 已不直接訂閱（由 GamePipeline 統一管理）
                }

                if (liveViewManager != null)
                {
                    liveViewManager.OnFrameReady -= OnFrameAvailable;
                }

                _minimapViewer?.Dispose();
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
                Logger.Error("[系統] Form關閉錯誤", ex);
            }

            // 🔧 關閉日誌系統（確保所有日誌寫入檔案）
            Logger.Shutdown();

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

                if (selectedMonster == "null")
                {
                    foreach (var template in currentMonsterMatTemplates) template?.Dispose();
                    currentMonsterMatTemplates.Clear();
                    _selectedMonsterName = null;
                    MsgLog.ShowStatus(textBox1, "已清除怪物模板選擇");
                    return;
                }

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
                        var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
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

                    await _pathPlanningManager.StartAsync(Config.General.GameWindowTitle);
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

            // 小地圖框與玩家標記統一由 GamePipeline 快照更新（OnGamePipelineFrameProcessed）。
            // 這個回調只處理路徑追蹤狀態與導航驅動，避免多來源覆寫造成競態。

            var playerPosOpt = result.PlayerPosition;
            if (playerPosOpt.HasValue && playerPosOpt.Value != SdPointF.Empty)
            {
                var playerPos = playerPosOpt.Value;



                // 路徑規劃狀態顯示和自動移動控制（僅在有規劃路徑時）
                if (_pathPlanningManager?.CurrentState != null)
                {
                    var pathState = _pathPlanningManager.CurrentState;
                    var progress = $"{pathState.CurrentWaypointIndex + 1}/{pathState.PlannedPath.Count}";
                    var distance = pathState.DistanceToNextWaypoint;

                    // 優先使用 TemporaryTarget (例如繩索點)，否則使用 NextWaypoint
                    var nextWaypointOpt = pathState.TemporaryTarget ?? pathState.NextWaypoint;

                    if (nextWaypointOpt.HasValue)
                    {
                        var nextWaypoint = nextWaypointOpt.Value;

                        // 優化：限制狀態訊息輸出頻率，且只在距離變化時輸出
                        var now = DateTime.UtcNow;
                        var elapsed = (now - _lastStatusUpdate).TotalMilliseconds;
                        bool distanceChanged = Math.Abs(distance - _lastReportedDistance) > 1.0f;

                        if (elapsed >= StatusUpdateIntervalMs && distanceChanged)
                        {
                            MsgLog.ShowStatus(textBox1, $"進度: {progress} 距離: {distance:F1} 目標: ({nextWaypoint.X},{nextWaypoint.Y})");
                            _lastStatusUpdate = now;
                            _lastReportedDistance = (float)distance;
                        }

                        // 自動移動控制（長按模式）- 加入冷卻時間和方向偵測
                        if (Config.Navigation.EnableAutoMovement && _movementController != null && _pathPlanningManager.IsRunning)
                        {
                            // 🔧 幀驅動同步：檢查旗標
                            if (!_visionDataReady)
                            {
                                return; // 還沒有新的視覺數據，跳過控制
                            }
                            _visionDataReady = false; // 消費旗標，等待下一幀

                            // 架構考量：從當前導航邊讀取動作，直接使用 NavigationActionType，不再轉換
                            NavigationEdge? currentEdge = _pathPlanningManager?.Tracker?.CurrentNavigationEdge;

                            // 判斷是否有特殊動作（非普通 Walk）
                            bool hasAction = currentEdge != null &&
                                           currentEdge.ActionType != NavigationActionType.Walk;

                            if (currentEdge != null)
                            {
                                Logger.Info($"[導航狀態] 玩家=({playerPos.X:F1},{playerPos.Y:F1}) 目標=({nextWaypoint.X},{nextWaypoint.Y}) Edge={currentEdge.FromNodeId}->{currentEdge.ToNodeId} Action={currentEdge.ActionType}");
                            }

                            // 1. 決定是否已經到達 (SSOT: 優先使用 Hitbox 判定)
                            bool isAtTarget = _pathPlanningManager?.Tracker?.IsPlayerAtTarget() ?? false;
                            
                            // 物理引擎在 Walk 邊會自行停穩並達成 Success；
                            // 此處 UI 僅負責啟動 (Start) 或在中斷後重啟。
                            bool walkEdge = currentEdge != null && currentEdge.ActionType == NavigationActionType.Walk;

                            if (isAtTarget && !hasAction)
                            {
                                // 只有在非特殊動作（單純 Walk 且已進 Hitbox）時，才主動通知到達
                                // 事實上在新的架構中，這主要是為了觸發索引推進
                                if (!walkEdge) _fsm?.NotifyTargetReached();
                            }
                            else if (currentEdge == null)
                            {
                                Logger.Error($"[導航狀態] 無合法導航邊可執行，停止自動導航。玩家=({playerPos.X:F1},{playerPos.Y:F1})");
                                _fsm?.CancelNavigation("無合法導航邊可執行");
                                return;
                            }
                            else
                            {
                                // 呼叫 FSM: 同步防護杜絕機關槍效應
                                if (_fsm != null)
                                {
                                    if (_fsm.TryStartNavigation(currentEdge, (SdPointF)playerPos, (SdPointF)nextWaypoint))
                                    {
                                        ReportAction($"{currentEdge.ActionType}");
                                    }
                                }
                            }
                        }
                        else if (!Config.Navigation.EnableAutoMovement)
                        {
                            // 調試訊息：確認自動移動是否啟用
                            Logger.Debug($"[調試] 自動移動未啟用: EnableAutoMovement={Config.Navigation.EnableAutoMovement}");
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
                if (result.OtherPlayers?.Any() == true && result.MinimapBounds.HasValue)
                {
                    MsgLog.ShowStatus(textBox1, $"其他玩家: {result.OtherPlayers.Count}");
                }

            }
        }
        #endregion

        // ShouldAddNewPoint 已廢棄 - RouteRecorderService 內部處理距離檢查 (Removed comment)

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
                        var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
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
                                _gamePipeline?.SetMinimapBoxes(new List<Rectangle> { minimapResult.MinimapScreenRect.Value });
                                MsgLog.ShowStatus(textBox1, "小地圖位置已定位");

                                // 🔧 修復：啟動時強制顯示小地圖視窗
                                _minimapViewer?.Show();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"[小地圖] 載入小地圖錯誤: {ex.Message}");
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
                    bool hasDetectionMode = !string.IsNullOrEmpty(Config.Vision.DetectionMode);

                    if (hasMonsterTemplate && hasDetectionMode)
                    {
                        MsgLog.ShowStatus(textBox1, $"怪物辨識已啟動（模板：{_selectedMonsterName}，模式：{Config.Vision.DetectionMode}）");
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
                    int platformNodeCount = 0;
                    if (loadedPathData?.Nodes != null)
                    {
                        foreach (var n in loadedPathData.Nodes)
                        {
                            if (n.Type == "Platform")
                                platformNodeCount++;
                        }
                    }

                    if (platformNodeCount > 0)
                    {
                        if (!_pathPlanningManager.IsRunning)
                        {
                            await _pathPlanningManager.StartAsync(Config.General.GameWindowTitle);
                            MsgLog.ShowStatus(textBox1, $"路徑規劃已啟動（已載入 {platformNodeCount} 個平台節點）");
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

                // 🔧 更新獨立視窗可見性（根據條件顯示/隱藏）
                UpdateMinimapViewerVisibility();
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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {

                case Keys.F4:
                    pictureBoxMinimap.Invalidate();
                    MsgLog.ShowStatus(textBox1, "已更新路徑編輯畫面");
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}