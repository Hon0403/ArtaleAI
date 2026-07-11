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
        private MapEditorPropertyPanel? _mapPropertyPanel;
        private string? _lastMapFileSelection;
        private bool _suppressMapFileSelectionChange;
        private string _mapEditorTitleBase = "地圖編輯器";
        private Rectangle minimapBounds = Rectangle.Empty;
        private GameVisionCore? gameVision;
        private AppConfig Config => AppConfig.Instance;


        private MonsterTemplateStore? _monsterTemplates;
        /// <summary>無模板時供 <see cref="GamePipeline"/> 重複指派，避免每幀配置新 <see cref="List{Mat}"/>。</summary>
        private static readonly List<Mat> s_emptyMonsterTemplates = new();
        private LiveViewManager? liveViewManager;

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



        private string _lastReportedAction = "";
        private bool _skipNextMapClick;
        private bool _isMinimapPanning;
        private SdPoint _minimapPanStartClient;
        private PointF _minimapPanStartOffset;

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
            Logger.Initialize(PathManager.LogsDirectory, enableConsole: true);
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
                AppConfig.Initialize(PathManager.ConfigFilePath);
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

                pictureBoxMinimap.MouseWheel += pictureBoxMinimap_MouseWheel;

                SyncMapFileDropdowns(false);
                cbo_MapFiles.DropDown += (s, e) => SyncMapFileDropdowns(true);
                cbo_LoadPathFile.DropDown += (s, e) => SyncMapFileDropdowns(true);

                InitializeMonsterTemplateSystem();
                InitializeDetectionModeDropdown();

                InitializeActionComboBox();
                InitializeAdvancedModeCheckBox();
                InitializeMapEditorPropertyPanel();

                var tracker = new PathPlanningTracker(gameVision);
                _pathPlanningManager = new PathPlanningManager(tracker, Config);
                _movementController = new CharacterMovementController();
                _movementController.SetGameWindowTitle(Config.General.GameWindowTitle);

                IPlayerPositionProvider positionProvider = new LambdaPositionProvider(
                    () => _pathPlanningManager?.CurrentState?.CurrentPlayerPosition);
                _navigationExecutor = new NavigationExecutor(
                    _movementController, positionProvider, _movementController);
                _navigationExecutor.SetPathTracker(tracker);
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
                _mapFileManager.FileListChanged += () => SyncMapFileDropdowns(true);

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

                MsgLog.ShowStatus(textBox1, " 所有服務初始化完成");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"初始化失敗: {ex.Message}");
                Logger.Error($"[系統] InitializeServices 失敗: {ex.Message}", ex);
            }
        }

        /// <summary>統一刷新所有地圖相關下拉選單（載入路徑與地圖檔案）。</summary>
        private void SyncMapFileDropdowns(bool suppressLog = false)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SyncMapFileDropdowns(suppressLog)));
                return;
            }

            try
            {
                var mapFiles = _mapFileManager?.GetAvailableMapFiles() ?? Array.Empty<string>();
                
                void UpdateCombo(ComboBox combo)
                {
                    var currentSelection = combo.Text;
                    combo.Items.Clear();
                    combo.Items.Add("null");
                    foreach (var file in mapFiles)
                        combo.Items.Add(file);

                    if (!string.IsNullOrEmpty(currentSelection) && combo.Items.Contains(currentSelection))
                        combo.Text = currentSelection;
                }

                UpdateCombo(cbo_LoadPathFile);
                UpdateCombo(cbo_MapFiles);

                if (!suppressLog)
                    MsgLog.ShowStatus(textBox1, $"[地圖管理] 已同步 {mapFiles.Length} 個路徑檔案至下拉選單");
            }
            catch (Exception ex)
            {
                if (!suppressLog)
                    MsgLog.ShowError(textBox1, $"同步地圖列表失敗: {ex.Message}");
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

            rdo_PathMarker.CheckedChanged += OnEditModeChanged;
            rdo_RopeMarker.CheckedChanged += OnEditModeChanged;
            rdo_JumpLinkMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;

            pictureBoxMinimap.BackColor = Color.FromArgb(45, 45, 48);

            cbo_LoadPathFile.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
            cbo_DetectMode.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
            cbo_MonsterTemplates.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();

            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_mapEditor == null) return;

            if (_isPathEditingTabActive && e.Control)
            {
                if (e.KeyCode == Keys.Z)
                {
                    _mapEditor.Undo();
                    pictureBoxMinimap.Invalidate();
                    RefreshMapEditorPropertyPanel();
                    RefreshMapEditorStatusBar();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (e.KeyCode == Keys.Y)
                {
                    _mapEditor.Redo();
                    pictureBoxMinimap.Invalidate();
                    RefreshMapEditorPropertyPanel();
                    RefreshMapEditorStatusBar();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (_mapEditor.GetCurrentEditMode() == EditMode.Platform)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    _mapEditor.FinishCurrentPolyline();
                    pictureBoxMinimap.Invalidate();
                    RefreshMapEditorPropertyPanel();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _mapEditor.CancelCurrentDrawing();
                    pictureBoxMinimap.Invalidate();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            }
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

            MsgLog.ShowStatus(textBox1, "配置檔案載入完成");
        }

        public void OnMapSaved(string fileName, bool isNewFile)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnMapSaved(fileName, isNewFile));
                return;
            }

            _mapEditor?.ClearDirty();
            _lastMapFileSelection = fileName;
            RefreshMapEditorStatusBar();

            if (isNewFile)
            {
                SyncMapFileDropdowns(true);
            }

            RefreshMinimap();
            RefreshMapEditorPropertyPanel();
            UpdateMapEditorWindowTitle();
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
                    UpdateMapEditorWindowTitle();
                    RefreshMapEditorPropertyPanel();
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

                    SyncPathEditorMinimapBounds();

                    if (result.MinimapScreenRect.HasValue)
                    {
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
            RefreshMapEditorPropertyPanel();
            UpdateMapEditorWindowTitle();
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
                    bool isClimbEdge = currentEdge.ActionType is NavigationActionType.ClimbUp
                        or NavigationActionType.ClimbDown;
                    if (isClimbEdge)
                    {
                        if (NavigationRopeHelper.TryExtractRopeX(currentEdge, out float ropeX))
                        {
                            pathData.RopeAlignCenterX = ropeX;
                            pathData.RopeAlignTolerance = 1.0f;
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
                MsgLog.ShowStatus(textBox1, "所有資源已釋放");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"釋放資源錯誤: {ex.Message}");
            }
        }

        #endregion

        #region 地圖編輯事件

        private void InitializeAdvancedModeCheckBox()
        {
            rdo_TwoPointLink.Enabled = false;
        }

        private void chk_AdvancedMode_CheckedChanged(object? sender, EventArgs e)
        {
            if (!chk_AdvancedMode.Checked && _mapEditor?.GetCurrentEditMode() == EditMode.ManualEdge)
                rdo_SelectMode.Checked = true;

            UpdateEditModeAndActionUi();
        }

        private void InitializeMapEditorPropertyPanel()
        {
            _mapPropertyPanel = new MapEditorPropertyPanel
            {
                Dock = DockStyle.Fill
            };
            groupBox_PropertyPanel.Controls.Add(_mapPropertyPanel);

            panelToolsScroll.Resize += (_, _) => SyncSidebarToolsLayout();
            panel4.Resize += (_, _) => SyncSidebarToolsLayout();
            splitSidebar.SplitterMoved += (_, _) => SyncSidebarToolsLayout();
            SyncSidebarToolsLayout();

            if (_mapEditor == null) return;

            _mapEditor.SelectionChanged += OnMapEditorSelectionChanged;
            _mapEditor.DirtyStateChanged += OnMapEditorDirtyStateChanged;
            _mapEditor.MapMutated += OnMapEditorMapMutated;
            _mapEditor.ValidationChanged += OnMapEditorValidationChanged;
            _mapEditor.HistoryChanged += OnMapEditorHistoryChanged;
            _mapEditor.Layers.Changed += OnMapEditorLayersChanged;
            _mapEditor.ConfirmDestructiveAction = message =>
                MessageBox.Show(message, "確認刪除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
                DialogResult.Yes;

            _mapPropertyPanel.Bind(_mapEditor);
            _mapPropertyPanel.ValidationIssueActivated += OnValidationIssueActivated;
            RefreshMapEditorStatusBar();
        }

        private void SyncSidebarToolsLayout()
        {
            int width = Math.Max(280, panelToolsScroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            flowToolsStack.Width = width;
            foreach (Control child in flowToolsStack.Controls)
                child.Width = width;

            flowToolsStack.PerformLayout();
            panelToolsScroll.AutoScrollMinSize = new SdSize(0, flowToolsStack.PreferredSize.Height + 6);
        }

        private void OnLayerCheckboxChanged(object? sender, EventArgs e)
        {
            if (_mapEditor == null) return;

            _mapEditor.Layers.ShowPlatforms = chk_LayerPlatforms.Checked;
            _mapEditor.Layers.ShowRopes = chk_LayerRopes.Checked;
            _mapEditor.Layers.ShowJumpLinks = chk_LayerJumpLinks.Checked;
            _mapEditor.Layers.ShowManualAnchors = chk_LayerManualAnchors.Checked;
            _mapEditor.Layers.ShowNodes = chk_LayerNodes.Checked;
            _mapEditor.Layers.ShowEdges = chk_LayerEdges.Checked;
            _mapEditor.Layers.ShowValidationOverlays = chk_LayerValidation.Checked;
            _mapEditor.Layers.NotifyChanged();
        }

        private void OnMapEditorLayersChanged()
        {
            if (InvokeRequired)
            {
                BeginInvoke(OnMapEditorLayersChanged);
                return;
            }

            pictureBoxMinimap.Invalidate();
        }

        private void OnMapEditorHistoryChanged()
        {
            RefreshMapEditorStatusBar();
        }

        private void RefreshMapEditorStatusBar()
        {
            if (InvokeRequired)
            {
                BeginInvoke(RefreshMapEditorStatusBar);
                return;
            }

            if (_mapEditor == null)
            {
                lbl_MapStatus.Text = "—";
                return;
            }

            string undo = _mapEditor.CanUndo ? "可復原" : "—";
            lbl_MapStatus.Text =
                $"{_mapEditor.GetCurrentEditMode()} | {_mapEditor.FormatStatusSummary()} | Undo:{undo}";
        }

        private bool ConfirmDiscardUnsavedChanges(string actionDescription)
        {
            if (_mapEditor?.IsDirty != true)
                return true;

            var result = MessageBox.Show(
                $"目前有未儲存的地圖變更，確定要{actionDescription}嗎？",
                "未儲存變更",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            return result == DialogResult.Yes;
        }

        private void OnMapEditorMapMutated()
        {
            if (InvokeRequired)
            {
                BeginInvoke(OnMapEditorMapMutated);
                return;
            }

            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void OnMapEditorValidationChanged()
        {
            RefreshMapEditorPropertyPanel();
            RefreshMapEditorStatusBar();
        }

        private void OnValidationIssueActivated(MapEditorValidationIssue issue)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnValidationIssueActivated(issue));
                return;
            }

            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void OnMapEditorSelectionChanged()
        {
            RefreshMapEditorPropertyPanel();
            RefreshMapEditorStatusBar();
        }

        private void OnMapEditorDirtyStateChanged()
        {
            RefreshMapEditorPropertyPanel();
            UpdateMapEditorWindowTitle();
            RefreshMapEditorStatusBar();
        }

        private void RefreshMapEditorPropertyPanel()
        {
            if (InvokeRequired)
            {
                BeginInvoke(RefreshMapEditorPropertyPanel);
                return;
            }

            _mapPropertyPanel?.RefreshFromEditor(_mapEditor);
        }

        private void UpdateMapEditorWindowTitle()
        {
            if (tabControl1.SelectedTab != tabPage2) return;

            string fileName = _mapFileManager?.CurrentMapFileName ?? "未命名";
            string dirtySuffix = _mapEditor?.IsDirty == true ? " *" : string.Empty;
            UpdateWindowTitle($"{_mapEditorTitleBase} - {fileName}{dirtySuffix}");
        }

        private void UpdateEditModeAndActionUi()
        {
            EditMode selectedMode = EditMode.None;
            if (rdo_PathMarker.Checked) selectedMode = EditMode.Platform;
            else if (rdo_RopeMarker.Checked) selectedMode = EditMode.Rope;
            else if (rdo_JumpLinkMarker.Checked) selectedMode = EditMode.JumpLink;
            else if (rdo_DeleteMarker.Checked) selectedMode = EditMode.Delete;
            else if (rdo_SelectMode.Checked) selectedMode = EditMode.Select;
            else if (rdo_TwoPointLink.Checked) selectedMode = EditMode.ManualEdge;

            bool advancedActive = chk_AdvancedMode.Checked;

            rdo_TwoPointLink.Enabled = advancedActive;
            groupBox_Action.Enabled = (selectedMode == EditMode.ManualEdge) && advancedActive;

            if (_mapEditor != null)
            {
                _mapEditor.SetEditMode(selectedMode);
            }
            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void OnEditModeChanged(object? sender, EventArgs e)
        {
            if (sender is RadioButton rb && !rb.Checked)
                return;

            UpdateEditModeAndActionUi();
            RefreshMapEditorStatusBar();

            EditMode selectedMode = _mapEditor?.GetCurrentEditMode() ?? EditMode.None;
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
                bool pathLoaded = loadedPathData != null && 
                    ((loadedPathData.PolylinePlatforms?.Count ?? 0) > 0 ||
                     (loadedPathData.Ropes?.Count ?? 0) > 0 ||
                     (loadedPathData.JumpLinks?.Count ?? 0) > 0);
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
            if (_suppressMapFileSelectionChange || cbo_MapFiles.SelectedItem == null) return;

            string selectedFile = cbo_MapFiles.SelectedItem.ToString() ?? "";
            if (selectedFile == "null" || string.IsNullOrEmpty(selectedFile)) return;

            if (!ConfirmDiscardUnsavedChanges("載入另一張地圖"))
            {
                _suppressMapFileSelectionChange = true;
                try
                {
                    if (!string.IsNullOrEmpty(_lastMapFileSelection) &&
                        cbo_MapFiles.Items.Contains(_lastMapFileSelection))
                        cbo_MapFiles.SelectedItem = _lastMapFileSelection;
                }
                finally
                {
                    _suppressMapFileSelectionChange = false;
                }
                return;
            }

            _lastMapFileSelection = selectedFile;
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
            cbo_ActionType.Items.Add(new ComboBoxItem("Walk (走路)", (int)NavigationActionType.Walk));
            cbo_ActionType.Items.Add(new ComboBoxItem("SideJump (側跳)", (int)NavigationActionType.SideJump));
            cbo_ActionType.Items.Add(new ComboBoxItem("Jump (原地跳)", (int)NavigationActionType.Jump));
            cbo_ActionType.Items.Add(new ComboBoxItem("JumpDown (下跳)", (int)NavigationActionType.JumpDown));
            cbo_ActionType.Items.Add(new ComboBoxItem("Teleport (傳送)", (int)NavigationActionType.Teleport));

            cbo_ActionType.SelectedIndex = 0;
        }

        private void cbo_ActionType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbo_ActionType.SelectedItem is not ComboBoxItem item || _mapEditor == null) return;

            _mapEditor.SetCurrentActionType(item.Value);
            pictureBoxMinimap.Invalidate();
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

        private readonly struct MinimapViewportLayout
        {
            public float Scale { get; init; }
            public float OffsetX { get; init; }
            public float OffsetY { get; init; }
            public float ImageWidth { get; init; }
            public float ImageHeight { get; init; }

            public PointF ImageToPictureBox(PointF imagePoint) =>
                new(imagePoint.X * Scale + OffsetX, imagePoint.Y * Scale + OffsetY);

            public PointF PictureBoxToImage(PointF pictureBoxPoint) =>
                new(
                    (pictureBoxPoint.X - OffsetX) / Scale,
                    (pictureBoxPoint.Y - OffsetY) / Scale);
        }

        private bool TryGetMinimapViewportLayout(out MinimapViewportLayout layout)
        {
            layout = default;
            if (_mapEditor == null || pictureBoxMinimap.Image == null)
                return false;

            float pbWidth = pictureBoxMinimap.ClientSize.Width;
            float pbHeight = pictureBoxMinimap.ClientSize.Height;
            float imageWidth = pictureBoxMinimap.Image.Width;
            float imageHeight = pictureBoxMinimap.Image.Height;
            if (pbWidth <= 0 || pbHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return false;

            float fitScale = Math.Min(pbWidth / imageWidth, pbHeight / imageHeight);
            float scale = fitScale * _mapEditor.ZoomScale;
            float displayWidth = imageWidth * scale;
            float displayHeight = imageHeight * scale;

            layout = new MinimapViewportLayout
            {
                Scale = scale,
                OffsetX = (pbWidth - displayWidth) / 2f + _mapEditor.PanOffsetX,
                OffsetY = (pbHeight - displayHeight) / 2f + _mapEditor.PanOffsetY,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight
            };
            return scale > 0f;
        }

        private void ClampMinimapPanOffset()
        {
            if (_mapEditor == null || pictureBoxMinimap.Image == null)
                return;

            float pbWidth = pictureBoxMinimap.ClientSize.Width;
            float pbHeight = pictureBoxMinimap.ClientSize.Height;
            float imageWidth = pictureBoxMinimap.Image.Width;
            float imageHeight = pictureBoxMinimap.Image.Height;
            float fitScale = Math.Min(pbWidth / imageWidth, pbHeight / imageHeight);
            float scale = fitScale * _mapEditor.ZoomScale;
            float displayWidth = imageWidth * scale;
            float displayHeight = imageHeight * scale;

            float minPanX = displayWidth > pbWidth ? (pbWidth - displayWidth) / 2f : 0f;
            float maxPanX = displayWidth > pbWidth ? (displayWidth - pbWidth) / 2f : 0f;
            float minPanY = displayHeight > pbHeight ? (pbHeight - displayHeight) / 2f : 0f;
            float maxPanY = displayHeight > pbHeight ? (displayHeight - pbHeight) / 2f : 0f;

            _mapEditor.PanOffsetX = Math.Clamp(_mapEditor.PanOffsetX, minPanX, maxPanX);
            _mapEditor.PanOffsetY = Math.Clamp(_mapEditor.PanOffsetY, minPanY, maxPanY);
        }

        private void SyncPathEditorMinimapBounds()
        {
            if (pictureBoxMinimap.Image == null)
                return;

            minimapBounds = new Rectangle(0, 0, pictureBoxMinimap.Image.Width, pictureBoxMinimap.Image.Height);
            _mapEditor?.SetMinimapBounds(minimapBounds);
        }

        private PointF TranslatePictureBoxPointToImage(PointF pbPoint, PictureBox pb)
        {
            if (!TryGetMinimapViewportLayout(out var layout))
                return pbPoint;

            return layout.PictureBoxToImage(pbPoint);
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMinimapPanning && _mapEditor != null)
            {
                _mapEditor.PanOffsetX = _minimapPanStartOffset.X + (e.X - _minimapPanStartClient.X);
                _mapEditor.PanOffsetY = _minimapPanStartOffset.Y + (e.Y - _minimapPanStartClient.Y);
                ClampMinimapPanOffset();
                pictureBoxMinimap.Invalidate();
                lbl_MouseCoords.Text = "座標: (-, -) | Ctrl+拖曳平移";
                return;
            }

            if (_mapEditor != null && !minimapBounds.IsEmpty && pictureBoxMinimap.Image != null)
            {
                var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
                var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);

                if (_mapEditor.IsVertexDragging)
                {
                    _mapEditor.UpdateVertexDrag(screenPoint);
                    pictureBoxMinimap.Invalidate();
                    lbl_MouseCoords.Text =
                        $"座標: ({imagePoint.X:F1}, {imagePoint.Y:F1}) | 拖曳折點";
                    return;
                }

                bool preferNode = (ModifierKeys & Keys.Shift) != 0;
                _mapEditor.UpdateMousePosition(screenPoint);
                _mapEditor.UpdateHoveredNode(screenPoint, preferNode);
                pictureBoxMinimap.Invalidate();

                var hover = _mapEditor.GetHoverInfo();
                string segmentText = hover.HasSegmentContext ? $" | Seg {hover.SegmentIndex}" : string.Empty;
                string projText = hover.HasProjection
                    ? $" | Proj ({hover.ProjectionPoint.X:F1},{hover.ProjectionPoint.Y:F1})"
                    : string.Empty;
                string nodeText = hover.HasRuntimeNode ? $" | Node #{hover.RuntimeNodeIndex}" : string.Empty;
                string hintText = preferNode ? " | Shift:節點優先" : string.Empty;
                lbl_MouseCoords.Text =
                    $"座標: ({imagePoint.X:F1}, {imagePoint.Y:F1}){segmentText}{projText}{nodeText}{hintText}";
            }

            RefreshMapEditorStatusBar();
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            if (_mapEditor == null || minimapBounds.IsEmpty || pictureBoxMinimap.Image == null)
                return;

            if (!TryGetMinimapViewportLayout(out var layout))
                return;

            e.Graphics.Clear(pictureBoxMinimap.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            e.Graphics.DrawImage(
                pictureBoxMinimap.Image,
                layout.OffsetX,
                layout.OffsetY,
                layout.ImageWidth * layout.Scale,
                layout.ImageHeight * layout.Scale);

            PointF ConvertScreenToDisplay(PointF screenPoint)
            {
                float relX = screenPoint.X - minimapBounds.X;
                float relY = screenPoint.Y - minimapBounds.Y;
                return layout.ImageToPictureBox(new PointF(relX, relY));
            }

            DrawPathEditorGrid(
                e.Graphics,
                layout.ImageWidth,
                layout.ImageHeight,
                layout.Scale,
                layout.Scale,
                layout.OffsetX,
                layout.OffsetY);

            _mapEditor.Render(e.Graphics, ConvertScreenToDisplay);

            DrawPathEditorRuler(
                e.Graphics,
                layout.ImageWidth,
                layout.ImageHeight,
                layout.Scale,
                layout.Scale,
                layout.OffsetX,
                layout.OffsetY);
        }



        /// <summary>路徑編輯小地圖底圖上的對齊網格。</summary>
        private void DrawPathEditorGrid(Graphics g, float imgWidth, float imgHeight,
            float scaleX, float scaleY, float offsetX, float offsetY)
        {
            const int MajorTickInterval = 5;
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
            const int MajorTickInterval = 5;
            const int MinorTickInterval = 2;

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


        private void pictureBoxMinimap_MouseDown(object sender, MouseEventArgs e)
        {
            if (_mapEditor == null || minimapBounds.IsEmpty || e.Button != MouseButtons.Left)
                return;

            if ((ModifierKeys & Keys.Control) != 0)
            {
                _isMinimapPanning = true;
                _minimapPanStartClient = e.Location;
                _minimapPanStartOffset = new PointF(_mapEditor.PanOffsetX, _mapEditor.PanOffsetY);
                pictureBoxMinimap.Capture = true;
                pictureBoxMinimap.Cursor = Cursors.Hand;
                _skipNextMapClick = true;
                return;
            }

            var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
            var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);
            if (_mapEditor.TryBeginVertexDrag(screenPoint))
                _skipNextMapClick = true;
        }

        private void pictureBoxMinimap_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isMinimapPanning)
            {
                _isMinimapPanning = false;
                pictureBoxMinimap.Capture = false;
                pictureBoxMinimap.Cursor = Cursors.Default;
                _skipNextMapClick = true;
                pictureBoxMinimap.Invalidate();
                return;
            }

            if (_mapEditor == null || !_mapEditor.IsVertexDragging)
                return;

            _mapEditor.EndVertexDrag();
            _skipNextMapClick = true;
            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void pictureBoxMinimap_Click(object sender, MouseEventArgs e)
        {
            if (_skipNextMapClick)
            {
                _skipNextMapClick = false;
                return;
            }
            if (_mapEditor == null || minimapBounds.IsEmpty) return;
            var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
            var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);
            bool preferNode = (ModifierKeys & Keys.Shift) != 0;
            _mapEditor.HandleClick(screenPoint, e.Button, preferNode, preferNode);
            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void pictureBoxMinimap_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (ModifierKeys != Keys.Control || _mapEditor == null) return;

            float oldZoom = _mapEditor.ZoomScale;
            if (e.Delta > 0)
                _mapEditor.ZoomScale = Math.Min(10.0f, _mapEditor.ZoomScale + 0.1f);
            else
                _mapEditor.ZoomScale = Math.Max(0.5f, _mapEditor.ZoomScale - 0.1f);

            if (Math.Abs(oldZoom - _mapEditor.ZoomScale) > 0.001f)
            {
                ClampMinimapPanOffset();
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            try
            {
                if (_isMinimapPanning)
                {
                    _isMinimapPanning = false;
                    pictureBoxMinimap.Capture = false;
                    pictureBoxMinimap.Cursor = Cursors.Default;
                }

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
                if (!ConfirmDiscardUnsavedChanges("建立新地圖"))
                    return;

                var result = MessageBox.Show("確定要清空當前地圖並建立新檔案嗎？",
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
            if (_mapEditor?.IsDirty == true && e.CloseReason == CloseReason.UserClosing)
            {
                var result = MessageBox.Show(
                    "目前有未儲存的地圖變更，確定要離開嗎？",
                    "未儲存變更",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                    e.Cancel = true;
                else if (result == DialogResult.No)
                    e.Cancel = false;
            }

            if (e.Cancel)
                return;

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


                _monsterTemplates?.Dispose();


                if (_mapFileManager != null)
                {
                    _mapFileManager.MapSaved -= OnMapSaved;
                    _mapFileManager.MapLoaded -= OnMapFileLoaded;
                    _mapFileManager.StatusMessage -= OnMapFileManagerStatusMessage;
                    _mapFileManager.ErrorMessage -= OnMapFileManagerErrorMessage;

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

                            if (_gamePipeline.BlocksNavigationInput)
                                return;

                            var tracker = _pathPlanningManager?.Tracker;
                            if (tracker?.IsRescueCircuitBroken == true)
                            {
                                _fsm?.CancelNavigation("救援熔斷生效");
                                return;
                            }

                            NavigationEdge? currentEdge = tracker?.CurrentNavigationEdge;
                            NavigationEdge? edgeToExecute = currentEdge;
                            var executeTarget = nextWaypoint;

                            bool needsJumpApproach = currentEdge?.ActionType is
                                NavigationActionType.Jump or NavigationActionType.SideJump or NavigationActionType.JumpDown;

                            if (needsJumpApproach &&
                                tracker != null &&
                                currentEdge != null &&
                                _fsm?.CurrentState != NavigationState.Jumping &&
                                !tracker.IsSideJumpApproachInProgress)
                            {
                                if (!tracker.IsJumpTakeoffReachableByWalk(
                                        (System.Drawing.PointF)playerPos, currentEdge))
                                {
                                    tracker.ClearSideJumpApproachState();
                                    _fsm?.CancelNavigation("跨平台不可水平對位起跳點");
                                    if (!tracker.IsRescueCircuitBroken)
                                        tracker.TryRescuePath();
                                    return;
                                }

                                if (tracker.TryGetSideJumpApproachWalk(
                                    currentEdge,
                                    out var approachEdge,
                                    out var approachTarget))
                                {
                                    edgeToExecute = approachEdge;
                                    executeTarget = approachTarget;
                                    tracker.MarkSideJumpApproachStarted(currentEdge.FromNodeId);
                                    Logger.Info($"[導航] {currentEdge.ActionType} 起跳點未對齊，先 Walk 至 ({approachTarget.X:F1},{approachTarget.Y:F1})");
                                }
                            }
                            else if (!needsJumpApproach)
                            {
                                tracker?.ClearSideJumpApproachState();
                            }

                            Logger.Info($"[導航狀態] 玩家=({playerPos.X:F1},{playerPos.Y:F1}) 目標=({executeTarget.X:F1},{executeTarget.Y:F1}) Edge={edgeToExecute?.FromNodeId}->{edgeToExecute?.ToNodeId} Action={edgeToExecute?.ActionType}");

                            bool walkEdge = edgeToExecute != null &&
                                            edgeToExecute.ActionType == NavigationActionType.Walk;

                            if (edgeToExecute == null)
                            {
                                Logger.Error($"[導航狀態] 無合法導航邊可執行，停止自動導航。玩家=({playerPos.X:F1},{playerPos.Y:F1})");
                                _fsm?.CancelNavigation("無合法導航邊可執行");
                                return;
                            }

                            if (walkEdge &&
                                tracker != null &&
                                tracker.IsPlayerOnRope((System.Drawing.PointF)playerPos))
                            {
                                Logger.Debug($"[導航] 仍在繩上，暫不啟動 Walk player=({playerPos.X:F1},{playerPos.Y:F1})");
                            }
                            else if (_fsm != null &&
                                _fsm.TryStartNavigation(edgeToExecute, (SdPointF)playerPos, (SdPointF)executeTarget))
                            {
                                ReportAction($"{edgeToExecute.ActionType}");
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
            UpdateAutoAttackState();

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