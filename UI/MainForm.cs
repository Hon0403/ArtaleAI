using ArtaleAI.Infrastructure.External;
using ArtaleAI.Infrastructure.External.Config;
using ArtaleAI.Models.Config;
using ArtaleAI.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Map;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Application.Pipeline;
using ArtaleAI.Application.Navigation;
using ArtaleAI.Application.Movement;
using ArtaleAI.Infrastructure.Capture;
using ArtaleAI.Infrastructure.Persistence;
using ArtaleAI.Infrastructure.Input;
using ArtaleAI.Contracts;
using ArtaleAI.UI;
using ArtaleAI.UI.MapEditor;
using ArtaleAI.Models.Visualization;
using ArtaleAI.Shared;
using ArtaleAI.Domain.Navigation;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Concurrent;
using System.ComponentModel;
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
        private LiveViewManager? liveViewManager;

        private readonly object imageUpdateLock = new object();

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
            // 設計工具會實例化 Form：只跑 InitializeComponent，避免 Logger／Capture／設定檔把設計面打掛
            InitializeComponent();
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
                return;

            Logger.Initialize(PathManager.LogsDirectory, enableConsole: true);
            Logger.Info("[系統] ArtaleAI 正在啟動...");

            InitializeServices();
            BindEvents();
            InitializeConsolePanel();
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
                _pathPlanningManager = new PathPlanningManager(tracker);
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
                _gamePipeline.MonsterCatalog = _monsterTemplates.Catalog;
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

                UpdatePrerequisitesLabel();
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
                _suppressMonsterListEvents = true;
                try
                {
                    MonsterTemplateStore.PopulateMonsterList(
                        clb_MonsterTemplates, PathManager.MonstersDirectory);
                }
                finally
                {
                    _suppressMonsterListEvents = false;
                }

                clb_MonsterTemplates.ItemCheck += clb_MonsterTemplates_ItemCheck;
                int count = MonsterTemplateStore.EnumerateMonsterFolderNames(PathManager.MonstersDirectory).Count;
                MsgLog.ShowStatus(textBox1, $"可選怪物 {count} 種（最多同時勾選 {MonsterTemplateStore.SoftSelectLimit} 種）");
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
            rdo_SafeZoneMarker.CheckedChanged += OnEditModeChanged;
            rdo_DeleteMarker.CheckedChanged += OnEditModeChanged;

            pictureBoxMinimap.BackColor = Color.FromArgb(45, 45, 48);

            cbo_LoadPathFile.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
            cbo_DetectMode.SelectedIndexChanged += (s, e) => UpdateAutoAttackState();
            // 怪物勾選變更由 clb_MonsterTemplates_ItemCheck → Reload 內呼叫 UpdateAutoAttackState

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
    }
}
