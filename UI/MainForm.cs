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
using ArtaleAI.Core.Domain.Navigation;
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

        private List<Rectangle> _currentMinimapBoxes = new();

        private MonsterTemplateStore? _monsterTemplates;
        /// <summary>無模板時供 <see cref="GamePipeline"/> 重複指派，避免每幀配置新 <see cref="List{Mat}"/>。</summary>
        private static readonly List<Mat> s_emptyMonsterTemplates = new();
        private LiveViewManager? liveViewManager;

        private Bitmap? _currentDisplayFrame;
        private readonly object imageUpdateLock = new object();

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



        private DateTime _lastStatusUpdate = DateTime.MinValue;
        private const int StatusUpdateIntervalMs = 500;

        private DateTime _lastUIUpdate = DateTime.MinValue;
        private const int UIUpdateIntervalMs = 33;
        private volatile bool _isUIUpdatePending = false;
        private volatile bool _isLiveViewTabActive = false;
        private volatile bool _isPathEditingTabActive = false;

        private volatile bool _visionDataReady = false;


        private string _lastReportedAction = "";

        /// <summary>路徑動作狀態列去重（執行緒安全）。</summary>
        private void ReportAction(string action)
        {
            if (action == _lastReportedAction) return;
            _lastReportedAction = action;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => MsgLog.ShowStatus(textBox1, action)));
            }
            else
            {
                MsgLog.ShowStatus(textBox1, action);
            }
        }

        #endregion

        #region Constructor & Initialization

        public MainForm()
        {
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
                _monsterTemplates = new MonsterTemplateStore(gameVision);

                _mapFileManager = new MapFileManager(_mapEditor);
                _minimapViewer = new MinimapViewer(this, config);
                _monsterDownloader = new MonsterImageFetcher(this);

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

                _gamePipeline = new GamePipeline(gameVision, _pathPlanningManager, _movementController);
                _movementController.SetSyncProvider(_gamePipeline);
                _navigationExecutor.SetSyncProvider(_gamePipeline);
                _overlayRenderer = new OverlayRenderer();

                _mapFileManager.MapSaved += OnMapSaved;
                _mapFileManager.MapLoaded += OnMapFileLoaded;
                _mapFileManager.StatusMessage += OnMapFileManagerStatusMessage;
                _mapFileManager.ErrorMessage += OnMapFileManagerErrorMessage;
                _mapFileManager.FileListChanged += OnMapFileListChanged;

                _gamePipeline.OnFrameProcessed += OnGamePipelineFrameProcessed;
                _gamePipeline.OnStatusMessage += msg =>
                {
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => MsgLog.ShowStatus(textBox1, msg)));
                    else
                        MsgLog.ShowStatus(textBox1, msg);
                };
                _gamePipeline.OnPathTrackingResult += OnPathTrackingUpdated;

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

        /// <summary>重新列舉 <c>MapData</c> 內 JSON 路徑檔並填入下拉選單。</summary>
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



                cbo_LoadPathFile.Items.Add("null");

                var mapFiles = Directory.GetFiles(mapDataDirectory, "*.json");

                foreach (var file in mapFiles)
                {
                    cbo_LoadPathFile.Items.Add(Path.GetFileNameWithoutExtension(file));
                }

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
                MonsterTemplateStore.PopulateMonsterCombo(cbo_MonsterTemplates, PathManager.MonstersDirectory);
                cbo_MonsterTemplates.SelectedIndexChanged += OnMonsterSelectionChanged;
                int count = MonsterTemplateStore.EnumerateMonsterFolderNames(PathManager.MonstersDirectory).Count;
                MsgLog.ShowStatus(textBox1, $" 載入 {count} 個怪物模板");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"初始化怪物模板系統失敗: {ex.Message}");
            }
        }

        private void UpdateDisplay(Bitmap newFrame)
        {
            if (newFrame?.Width <= 0 || newFrame?.Height <= 0)
            {
                newFrame?.Dispose();
                return;
            }

            var now = DateTime.UtcNow;
            var elapsed = (now - _lastUIUpdate).TotalMilliseconds;

            if (elapsed < UIUpdateIntervalMs || _isUIUpdatePending)
            {
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
                BeginInvoke(updateAction);
            }
            else
            {
                updateAction();
            }
        }

        private void BindEvents()
        {
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;
            numericUpDownZoom.ValueChanged += numericUpDownZoom_ValueChanged;

            rdo_PathMarker.CheckedChanged += OnEditModeChanged;
            rdo_RopeMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;

            pictureBoxMinimap.BackColor = Color.FromArgb(45, 45, 48);

            ckB_Start.CheckedChanged += (s, e) => UpdateAutoAttackState();
            cbo_LoadPathFile.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
            cbo_DetectMode.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
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
                PopulateMapFilesComboBox();
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

        /// <summary>依設定檔填入怪物偵測模式下拉選單。</summary>
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

        private void OnDetectionModeChanged(object? sender, EventArgs e)
        {
            var selectedDisplayText = cbo_DetectMode.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDisplayText)) return;

            var config = AppConfig.Instance;

            var selectedMode = config.Vision.DetectionModes?
                .FirstOrDefault(kvp => kvp.Value.DisplayName == selectedDisplayText).Key
                ?? config.Vision.DefaultMode ?? "Normal";

            var optimalOcclusion = "None";
            if (config.Vision.DetectionModes?.TryGetValue(selectedMode, out var modeConfig) == true)
            {
                optimalOcclusion = modeConfig.Occlusion;
            }

            config.Vision.DetectionMode = selectedMode;

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
            _isLiveViewTabActive = tabControl1.SelectedIndex == 2;
            _isPathEditingTabActive = tabControl1.SelectedIndex == 1;

            bool isLiveViewRunning = liveViewManager?.IsRunning == true;
            bool isSwitchingToLiveView = _isLiveViewTabActive;

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
                case 0:
                    UpdateWindowTitle("ArtaleAI");
                    if (ckB_Start.Checked)
                    {
                        _minimapViewer?.Show();
                    }
                    else
                    {
                        _minimapViewer?.Hide();
                    }
                    break;
                case 1:
                    await StartPathEditingModeAsync();
                    _minimapViewer?.Hide();
                    break;
                case 2:
                    UpdateWindowTitle("ArtaleAI - 即時顯示");

                    if (!isLiveViewRunning)
                    {
                        await StartLiveViewModeAsync();
                    }

                    _minimapViewer?.Show();
                    break;
                default:
                    UpdateWindowTitle("ArtaleAI");
                    _minimapViewer?.Hide();
                    break;
            }
        }





        /// <summary>擷取並顯示小地圖底圖供路徑編輯。</summary>
        private async Task StartPathEditingModeAsync()
        {
            MsgLog.ShowStatus(textBox1, "載入路徑編輯模式...");
            tabControl1.Enabled = false;

            try
            {
                var result = await LoadMinimapWithMat(MinimapUsage.PathEditing);
                if (result?.MinimapImage != null)
                {
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
                if (gameVision == null)
                {
                    MsgLog.ShowError(textBox1, "視覺核心尚未初始化");
                    return null;
                }

                Logger.Debug("[小地圖] 開始 LoadMinimapWithMat");
                MsgLog.ShowStatus(textBox1, "正在載入小地圖...");

                var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
                if (captureItem == null)
                {
                    MsgLog.ShowError(textBox1, $"無法建立捕獲項目: {Config.General.GameWindowTitle}");
                    return null;
                }

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




        /// <summary>定位小地圖並啟動 LiveView 擷取。</summary>
        private async Task StartLiveViewModeAsync()
        {
            MsgLog.ShowStatus(textBox1, "正在啟動即時畫面...");
            var config = Config;

            try
            {
                var result = await LoadMinimapWithMat(MinimapUsage.LiveViewOverlay);
                if (result?.MinimapScreenRect.HasValue == true)
                {
                    minimapBounds = result.MinimapScreenRect.Value;
                    _gamePipeline?.SetMinimapBoxes(new List<Rectangle> { result.MinimapScreenRect.Value });
                    MsgLog.ShowStatus(textBox1, "小地圖位置已定位");

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

        /// <summary>新幀進入：交給 <see cref="GamePipeline"/> 並更新即時預覽／編輯器重繪。</summary>
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

                    if (_gamePipeline != null)
                    {
                        _gamePipeline.AutoAttackEnabled = _autoAttackEnabled;
                        _gamePipeline.SelectedMonsterName = _monsterTemplates?.SelectedMonsterName ?? string.Empty;
                        _gamePipeline.MonsterTemplates = _monsterTemplates?.Templates ?? s_emptyMonsterTemplates;

                        _gamePipeline.ProcessFrame(frameMat, captureTime, config);

                        _visionDataReady = _gamePipeline.VisionDataReady;

                        if (_isLiveViewTabActive && _overlayRenderer != null)
                        {
                            var resultSnapshot = _gamePipeline.GetCurrentSnapshot();
                            using var bmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frameMat);
                            using var drawnBmp = _overlayRenderer.Render(bmp, resultSnapshot, config);
                            UpdateDisplay((Bitmap)drawnBmp.Clone());
                        }
                    }

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


        private volatile bool _autoAttackEnabled = false;

        /// <summary>依勾選與下拉選項更新 <see cref="_autoAttackEnabled"/>。</summary>
        private void UpdateAutoAttackState()
        {
            _autoAttackEnabled = ckB_Start.Checked &&
                                 cbo_LoadPathFile.SelectedIndex > 0 &&
                                 cbo_DetectMode.SelectedItem != null &&
                                 cbo_MonsterTemplates.SelectedIndex > 0;
        }

        /// <summary>同步 GamePipeline 輸出的小地圖框與玩家標記（LiveView 疊加在 <see cref="OnFrameAvailable"/> 繪製）。</summary>
        private void OnGamePipelineFrameProcessed(FrameProcessingResult result)
        {
            if (result == null) return;

            _currentMinimapBoxes = result.MinimapBoxes;

        }

        /// <summary>由 <see cref="MapFileManager"/> 列舉檔名填入地圖下拉選單。</summary>
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

                if (!string.IsNullOrEmpty(currentSelection) && cbo_MapFiles.Items.Contains(currentSelection))
                    cbo_MapFiles.SelectedItem = currentSelection;
            }
            catch (Exception ex)
            {
                Logger.Error($"[地圖] 填充檔案清單失敗: {ex.Message}");
            }
        }



        /// <summary>地圖載入後更新標題與小地圖顯示。</summary>
        private void OnMapFileLoaded(string fileName)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnMapFileLoaded(fileName)));
                return;
            }

            UpdateWindowTitle($"地圖編輯器 - {fileName}");

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


        /// <summary>組裝獨立小地圖視窗所需的路徑／繩索／Hitbox 疊加資料。</summary>
        private PathVisualizationData? BuildPathVisualizationData()
        {
            try
            {
                var pathData = new PathVisualizationData();

                var graph = _pathPlanningManager?.Tracker?.NavGraph;
                if (graph != null && graph.NodeCount > 0)
                {
                    var allNodes = graph.GetAllNodes();

                    var platformNodes = allNodes.Where(n => n.Type == ArtaleAI.Core.Domain.Navigation.NavigationNodeType.Platform).ToList();
                    var ropeNodes = allNodes.Where(n => n.Type == ArtaleAI.Core.Domain.Navigation.NavigationNodeType.Rope).ToList();

                    pathData.WaypointPaths = platformNodes
                        .Select(n => new WaypointWithPriority(
                            new SdPointF(n.Position.X, n.Position.Y),
                            0f,
                            false,
                            _pathPlanningManager?.Tracker?.CurrentTarget?.Position.X == n.Position.X && _pathPlanningManager?.Tracker?.CurrentTarget?.Position.Y == n.Position.Y))
                        .ToList();

                    var mapRopeSegs = _pathPlanningManager?.Tracker?.MapRopeSegmentsForVisualization;
                    if (mapRopeSegs != null && mapRopeSegs.Count > 0)
                    {
                        pathData.Ropes = mapRopeSegs
                            .Select(s => new RopeWithAccessibility(s.X, s.TopY, s.BottomY, 0f, false, false))
                            .ToList();
                    }
                    else
                    {
                        pathData.Ropes = ropeNodes
                            .Select(n => new RopeWithAccessibility(
                                n.Position.X,
                                n.Position.Y - 50,
                                n.Position.Y + 50,
                                0f,
                                false,
                                false))
                            .ToList();
                    }
                }

                var playerPosOpt = _pathPlanningManager?.CurrentState?.CurrentPlayerPosition;
                if (playerPosOpt.HasValue)
                {
                    pathData.PlayerPosition = playerPosOpt.Value;
                }
                var nextWp = _pathPlanningManager?.CurrentState?.NextWaypoint;
                if (nextWp.HasValue)
                {
                    pathData.TargetPosition = nextWp.Value;
                }


                var tempTarget = _pathPlanningManager?.Tracker?.CurrentPathState?.TemporaryTarget;
                if (tempTarget.HasValue && !minimapBounds.IsEmpty)
                {
                    pathData.TemporaryTarget = new SdPointF(
                        tempTarget.Value.X,
                        tempTarget.Value.Y);
                }



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
                            pathData.RopeAlignTolerance = (float)AppConfig.Instance.Navigation.WaypointReachDistance;
                        }
                    }
                }
                return pathData;
            }
            catch (Exception ex)
            {
                Logger.Error($"[MinimapViewer] BuildPathVisualizationData 錯誤: {ex.Message}");
                return null;
            }
        }

        /// <summary>停止 LiveView 擷取並清空即時預覽參考。</summary>
        private void StopAndReleaseAllResources()
        {
            try
            {
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

            groupBox_Action.Enabled = selectedMode is EditMode.Waypoint or EditMode.Select or EditMode.Link;

            if (_mapEditor == null) return;
            _mapEditor.SetEditMode(selectedMode);
            pictureBoxMinimap.Invalidate();

            MsgLog.ShowStatus(textBox1, $"編輯模式切換至: {selectedMode}");
        }

        private void rdo_SelectMode_CheckedChanged(object sender, EventArgs e)
        {
            OnEditModeChanged(sender, e);
        }

        #region Merged UI Events (from partial classes)

        /// <summary>依 LiveView／自動導航狀態顯示或隱藏獨立小地圖視窗。</summary>
        private void UpdateMinimapViewerVisibility()
        {
            try
            {
                if (_minimapViewer == null) return;
                bool pathLoaded = loadedPathData != null && (loadedPathData.Nodes?.Count ?? 0) > 0;
                bool autoStartChecked = ckB_Start.Checked;
                bool liveViewReady = liveViewManager?.IsRunning == true && _isLiveViewTabActive;

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

            cbo_ActionType.SelectedIndex = 0;
        }

        private void cbo_ActionType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbo_ActionType.SelectedItem is not ComboBoxItem item || _mapEditor == null) return;

            _mapEditor.SetCurrentActionType(item.Value);
            pictureBoxMinimap.Invalidate();
        }

        private void UpdateActionComboBoxSelection(int action)
        {
            if (action == -1) return;
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

            if (_mapEditor != null && !minimapBounds.IsEmpty && pictureBoxMinimap.Image != null)
            {
                var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
                var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);

                _mapEditor.UpdateMousePosition(screenPoint);
                _mapEditor.UpdateHoveredNode(screenPoint);
                pictureBoxMinimap.Invalidate();

                lbl_MouseCoords.Text = $"座標: ({imagePoint.X:F1}, {imagePoint.Y:F1})";
            }
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            if (_mapEditor == null || minimapBounds.IsEmpty || pictureBoxMinimap.Image == null)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

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
                scaleY = pbHeight / imgHeight;
                scaleX = scaleY;
                offsetX = (pbWidth - imgWidth * scaleX) / 2;
            }
            else
            {
                scaleX = pbWidth / imgWidth;
                scaleY = scaleX;
                offsetY = (pbHeight - imgHeight * scaleY) / 2;
            }

            PointF ConvertScreenToDisplay(PointF screenPoint)
            {
                float relX = screenPoint.X - minimapBounds.X;
                float relY = screenPoint.Y - minimapBounds.Y;
                return new PointF(
                    relX * scaleX + offsetX,
                    relY * scaleY + offsetY
                );
            }

            DrawPathEditorGrid(e.Graphics, imgWidth, imgHeight, scaleX, scaleY, offsetX, offsetY);

            _mapEditor.Render(e.Graphics, ConvertScreenToDisplay);

            DrawPathEditorRuler(e.Graphics, imgWidth, imgHeight, scaleX, scaleY, offsetX, offsetY);

        }



        /// <summary>路徑編輯小地圖底圖上的對齊網格。</summary>
        private void DrawPathEditorGrid(Graphics g, float imgWidth, float imgHeight,
            float scaleX, float scaleY, float offsetX, float offsetY)
        {
            const int MajorTickInterval = 10;
            const int RulerSize = 18;

            using var gridPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1);

            for (int x = 0; x <= (int)imgWidth; x++)
            {
                if (x % MajorTickInterval == 0 && x != 0)
                {
                    float screenX = offsetX + x * scaleX;
                    g.DrawLine(gridPen, screenX, offsetY + RulerSize, screenX, offsetY + imgHeight * scaleY);
                }
            }

            for (int y = 0; y <= (int)imgHeight; y++)
            {
                if (y % MajorTickInterval == 0 && y != 0)
                {
                    float screenY = offsetY + y * scaleY;
                    g.DrawLine(gridPen, offsetX + RulerSize, screenY, offsetX + imgWidth * scaleX, screenY);
                }
            }
        }

        /// <summary>路徑編輯小地圖上方與左側的座標刻度尺。</summary>
        private void DrawPathEditorRuler(Graphics g, float imgWidth, float imgHeight,
            float scaleX, float scaleY, float offsetX, float offsetY)
        {
            const int RulerSize = 18;
            const int MajorTickInterval = 10;
            const int MinorTickInterval = 5;

            var bgColor = Color.FromArgb(30, 30, 30);
            var tickColor = Color.FromArgb(100, 100, 100);
            var textColor = Color.FromArgb(200, 200, 200);
            var majorTickLength = RulerSize - 4;
            var minorTickLength = RulerSize / 2;

            using var bgBrush = new SolidBrush(bgColor);
            using var tickPen = new Pen(tickColor, 1);
            using var textBrush = new SolidBrush(textColor);
            using var font = new Font("Consolas", 7f, FontStyle.Regular);

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

                if (_mapEditor != null)
                {
                    _mapEditor.UpdateMousePosition(new PointF(-1000, -1000));
                    _mapEditor.UpdateHoveredNode(new PointF(-1000, -1000));
                }

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
                var liveViewImage = pictureBoxLiveView.Image;
                var minimapImage = pictureBoxMinimap.Image;
                pictureBoxLiveView.Image = null;
                pictureBoxMinimap.Image = null;
                Application.DoEvents();

                liveViewImage?.Dispose();
                minimapImage?.Dispose();

                _currentDisplayFrame?.Dispose();
                _currentDisplayFrame = null;

                _monsterTemplates?.Dispose();


                _currentMinimapBoxes.Clear();
                if (_mapFileManager != null)
                {
                    _mapFileManager.MapSaved -= OnMapSaved;
                    _mapFileManager.MapLoaded -= OnMapFileLoaded;
                    _mapFileManager.StatusMessage -= OnMapFileManagerStatusMessage;
                    _mapFileManager.ErrorMessage -= OnMapFileManagerErrorMessage;
                    _mapFileManager.FileListChanged -= OnMapFileListChanged;
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

            Logger.Shutdown();

            base.OnFormClosed(e);
        }

        #endregion

        private async void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            if (_monsterTemplates == null) return;

            try
            {
                if (cbo_MonsterTemplates.SelectedItem == null) return;
                string selectedMonster = cbo_MonsterTemplates.SelectedItem.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(selectedMonster)) return;

                if (selectedMonster == "null")
                {
                    _monsterTemplates.ReleaseTemplates();
                    MsgLog.ShowStatus(textBox1, "已清除怪物模板選擇");
                    return;
                }

                MsgLog.ShowStatus(textBox1, $"載入怪物模板: {selectedMonster}");
                await _monsterTemplates.LoadSelectionAsync(selectedMonster, PathManager.MonstersDirectory);
                MsgLog.ShowStatus(textBox1, $"已載入 {_monsterTemplates.Templates.Count} 個模板");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"載入模板錯誤: {ex.Message}");
                _monsterTemplates.ReleaseTemplates();
            }
        }

        private async void btn_DownloadMonster_Click(object sender, EventArgs e)
        {
            try
            {
                string monsterName = Microsoft.VisualBasic.Interaction.InputBox(
                    "請輸入怪物名稱:", "下載怪物模板", "");

                if (string.IsNullOrWhiteSpace(monsterName)) return;

                if (_monsterDownloader == null)
                {
                    MsgLog.ShowError(textBox1, "下載器尚未初始化");
                    return;
                }

                btn_DownloadMonster.Enabled = false;
                btn_DownloadMonster.Text = "下載中...";

                var result = await _monsterDownloader.DownloadMonsterAsync(monsterName);

                if (result is { Success: true } ok)
                {
                    MonsterTemplateStore.PopulateMonsterCombo(cbo_MonsterTemplates, PathManager.MonstersDirectory);
                    MsgLog.ShowStatus(textBox1, $" 成功下載 {ok.DownloadedCount} 個模板");
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
                if (rdoButton.Checked)
                {
                    if (liveViewManager == null || !liveViewManager.IsRunning)
                    {
                        var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
                        if (captureItem == null)
                        {
                            MsgLog.ShowError(textBox1, "找不到遊戲視窗，請先開啟遊戲。");
                            rdoButton.Checked = false;
                            return;
                        }

                        MsgLog.ShowStatus(textBox1, "正在啟動背景擷取以處理路徑規劃...");

                        if (liveViewManager != null)
                        {
                            liveViewManager.StartLiveView(captureItem);
                            bool firstFrame = await liveViewManager.WaitForFirstFrameAsync(TimeSpan.FromSeconds(2));
                            if (!firstFrame)
                                Logger.Debug("[路徑規劃] 首幀未於 2 秒內就緒，仍繼續啟動追蹤。");
                        }
                    }

                    if (_pathPlanningManager == null)
                    {
                        MsgLog.ShowError(textBox1, "路徑規劃管理器尚未初始化");
                        rdoButton.Checked = false;
                        return;
                    }

                    await _pathPlanningManager.StartAsync(Config.General.GameWindowTitle);
                    MsgLog.ShowStatus(textBox1, "路徑規劃已啟動");
                }
                else
                {
                    if (_pathPlanningManager != null)
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
                    rdoButton.Checked = false;
                }
            }
        }

        #region 路徑規劃專用方法

        /// <summary>小地圖追蹤回呼：更新狀態列並驅動 FSM 啟動下一邊。</summary>
        private void OnPathTrackingUpdated(MinimapTrackingResult result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<MinimapTrackingResult>(OnPathTrackingUpdated), result);
                return;
            }

            var playerPosOpt = result.PlayerPosition;
            if (playerPosOpt.HasValue && playerPosOpt.Value != SdPointF.Empty)
            {
                var playerPos = playerPosOpt.Value;



                if (_pathPlanningManager?.CurrentState != null)
                {
                    var pathState = _pathPlanningManager.CurrentState;
                    var progress = $"{pathState.CurrentWaypointIndex + 1}/{pathState.PlannedPath.Count}";
                    var distance = pathState.DistanceToNextWaypoint;

                    var nextWaypointOpt = pathState.TemporaryTarget ?? pathState.NextWaypoint;

                    if (nextWaypointOpt.HasValue)
                    {
                        var nextWaypoint = nextWaypointOpt.Value;

                        var now = DateTime.UtcNow;
                        var elapsed = (now - _lastStatusUpdate).TotalMilliseconds;
                        if (elapsed >= StatusUpdateIntervalMs)
                        {
                            MsgLog.ShowStatus(textBox1, $"進度: {progress} 距離: {distance:F1} 目標: ({nextWaypoint.X},{nextWaypoint.Y})");
                            _lastStatusUpdate = now;
                        }

                        if (Config.Navigation.EnableAutoMovement && _movementController != null && _pathPlanningManager.IsRunning)
                        {
                            // 架構：追蹤回呼經 BeginInvoke 到 UI，可能早於 OnFrameAvailable 末尾同步 _visionDataReady，
                            // 造成誤判；Pipeline 在觸發事件前已設 VisionDataReady，以此為單一真相來源。
                            if (_gamePipeline == null || !_gamePipeline.VisionDataReady)
                                return;
                            _gamePipeline.VisionDataReady = false;
                            _visionDataReady = false;

                            NavigationEdge? currentEdge = _pathPlanningManager?.Tracker?.CurrentNavigationEdge;

                            bool hasAction = currentEdge != null &&
                                           currentEdge.ActionType != NavigationActionType.Walk;

                            if (currentEdge != null)
                            {
                                Logger.Info($"[導航狀態] 玩家=({playerPos.X:F1},{playerPos.Y:F1}) 目標=({nextWaypoint.X},{nextWaypoint.Y}) Edge={currentEdge.FromNodeId}->{currentEdge.ToNodeId} Action={currentEdge.ActionType}");
                            }

                            bool isAtTarget = _pathPlanningManager?.Tracker?.IsPlayerAtTarget() ?? false;

                            bool walkEdge = currentEdge != null && currentEdge.ActionType == NavigationActionType.Walk;

                            if (isAtTarget && !hasAction)
                            {
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
                            Logger.Debug($"[調試] 自動移動未啟用: EnableAutoMovement={Config.Navigation.EnableAutoMovement}");
                        }
                    }
                    else
                    {
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

        private async void ckB_Start_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is not CheckBox chkBox) return;

            try
            {
                if (chkBox.Checked)
                {
                    if (liveViewManager == null || !liveViewManager.IsRunning)
                    {
                        var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
                        if (captureItem == null)
                        {
                            MsgLog.ShowError(textBox1, "找不到遊戲視窗，請先開啟遊戲。");
                            chkBox.Checked = false;
                            return;
                        }

                        MsgLog.ShowStatus(textBox1, "正在啟動背景擷取...");

                        try
                        {
                            var minimapResult = await LoadMinimapWithMat(MinimapUsage.LiveViewOverlay);
                            if (minimapResult?.MinimapScreenRect.HasValue == true)
                            {
                                minimapBounds = minimapResult.MinimapScreenRect.Value;
                                _gamePipeline?.SetMinimapBoxes(new List<Rectangle> { minimapResult.MinimapScreenRect.Value });
                                MsgLog.ShowStatus(textBox1, "小地圖位置已定位");

                                _minimapViewer?.Show();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"[小地圖] 載入小地圖錯誤: {ex.Message}");
                        }

                        if (liveViewManager != null)
                        {
                            liveViewManager.StartLiveView(captureItem);
                            bool firstFrame = await liveViewManager.WaitForFirstFrameAsync(TimeSpan.FromSeconds(2));
                            if (!firstFrame)
                                Logger.Debug("[自動打怪] 首幀未於 2 秒內就緒，仍繼續後續流程。");
                        }
                    }

                    bool hasMonsterTemplate = !string.IsNullOrEmpty(_monsterTemplates?.SelectedMonsterName) &&
                                              (_monsterTemplates?.Templates.Any() == true);
                    bool hasDetectionMode = !string.IsNullOrEmpty(Config.Vision.DetectionMode);

                    if (hasMonsterTemplate && hasDetectionMode)
                    {
                        MsgLog.ShowStatus(textBox1, $"怪物辨識已啟動（模板：{_monsterTemplates?.SelectedMonsterName}，模式：{Config.Vision.DetectionMode}）");
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

                    int platformNodeCount = 0;
                    if (loadedPathData?.Nodes != null)
                    {
                        foreach (var n in loadedPathData.Nodes)
                        {
                            if (n.Type == "Platform")
                                platformNodeCount++;
                        }
                    }

                    if (platformNodeCount > 0 && _pathPlanningManager != null)
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
                    if (_pathPlanningManager != null && _pathPlanningManager.IsRunning)
                    {
                        await _pathPlanningManager.StopAsync();
                        MsgLog.ShowStatus(textBox1, "路徑規劃已停止");
                    }
                }

                UpdateMinimapViewerVisibility();
            }
            catch (Exception ex)
            {
                var action = chkBox.Checked ? "啟動" : "停止";
                MsgLog.ShowError(textBox1, $"自動打怪{action}失敗: {ex.Message}");
                if (chkBox.Checked)
                {
                    chkBox.Checked = false;
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