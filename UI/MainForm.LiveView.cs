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
using System.Diagnostics;
using System.Drawing;
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
    public partial class MainForm
    {
        private DateTime _lastConsoleMinimapOverlayUpdate = DateTime.MinValue;
        private volatile bool _consoleMinimapUiUpdatePending;
        private const int ConsoleMinimapOverlayIntervalMs = 33;

        #region UI 事件處理

        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _isLiveViewTabActive = ReferenceEquals(tabControl1.SelectedTab, tabPage3);
            _isPathEditingTabActive = ReferenceEquals(tabControl1.SelectedTab, tabPage2);

            bool isLiveViewRunning = liveViewManager?.IsRunning == true;
            bool keepCaptureForFarm =
                ckB_Start.Checked || (_pathPlanningManager?.IsRunning == true);

            // 掛機／導航進行中切分頁不可停擷取；僅在閒置離開即時顯示時釋放
            if (!_isLiveViewTabActive && !keepCaptureForFarm)
            {
                StopAndReleaseAllResources();
            }
            else if (!_isLiveViewTabActive && keepCaptureForFarm)
            {
                Logger.Debug("[系統] 掛機進行中切換分頁，保持 LiveView 擷取");
            }

            if (ReferenceEquals(tabControl1.SelectedTab, tabPage1))
            {
                UpdateWindowTitle("ArtaleAI");
            }
            else if (ReferenceEquals(tabControl1.SelectedTab, tabPageFarmSettings))
            {
                UpdateWindowTitle("ArtaleAI - 掛機設定");
            }
            else if (ReferenceEquals(tabControl1.SelectedTab, tabPage2))
            {
                await StartPathEditingModeAsync();
                UpdateMapEditorWindowTitle();
                RefreshMapEditorPropertyPanel();
            }
            else if (ReferenceEquals(tabControl1.SelectedTab, tabPage3))
            {
                UpdateWindowTitle("ArtaleAI - 即時顯示");

                if (!isLiveViewRunning)
                {
                    await StartLiveViewModeAsync();
                }
            }
            else
            {
                UpdateWindowTitle("ArtaleAI");
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
                // 先校正客戶區至設定尺寸（預設 1280×720），尺寸不對則不啟動——解析度漂移會大幅降低辨識率
                bool sizedOk = await EnsureGameClientSizeAsync(
                    forceImmediate: true, relocateMinimapIfResized: false);
                if (Config.General.ForceClientSizeWhileCapture && !sizedOk)
                {
                    int tw = Config.General.ForceClientWidth;
                    int th = Config.General.ForceClientHeight;
                    MsgLog.ShowError(
                        textBox1,
                        $"客戶區必須為 {tw}x{th} 才較易辨識。請改遊戲為視窗模式、取消最大化後再啟動。");
                    return;
                }

                var result = await LoadMinimapWithMat(MinimapUsage.LiveViewOverlay);
                if (result?.MinimapScreenRect.HasValue == true)
                {
                    minimapBounds = result.MinimapScreenRect.Value;
                    _gamePipeline?.SetMinimapBoxes(new List<Rectangle> { result.MinimapScreenRect.Value });
                    SetConsoleMinimapImage(result.MinimapImage is null ? null : (Bitmap)result.MinimapImage.Clone());
                    MsgLog.ShowStatus(textBox1, "小地圖位置已定位");

                    var captureItem = WindowFinder.TryCreateItemForWindow(Config.General.GameWindowTitle);
                    if (captureItem != null)
                    {
                        liveViewManager?.StartLiveView(captureItem);
                        SyncClientSizeGuardTimer();
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
                        _gamePipeline.AutoHealEnabled = ckB_Start.Checked;
                        _gamePipeline.AutoBuffEnabled = ckB_Start.Checked;
                        _gamePipeline.OtherPlayerAvoidanceEnabled =
                            ckB_Start.Checked && chk_ChangeChannelOnOtherPlayers.Checked;
                        _gamePipeline.SelectedMonsterName = _monsterTemplates?.SelectedMonsterNamesDisplay ?? string.Empty;

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

                    using var consoleMinimapClone = gameVision?.GetLastMinimapMatClone();
                    if (consoleMinimapClone != null && !consoleMinimapClone.Empty())
                    {
                        var overlayNow = DateTime.UtcNow;
                        if ((overlayNow - _lastConsoleMinimapOverlayUpdate).TotalMilliseconds >= ConsoleMinimapOverlayIntervalMs &&
                            !_consoleMinimapUiUpdatePending)
                        {
                            _lastConsoleMinimapOverlayUpdate = overlayNow;

                            // Console minimap：先把 A* 狀態疊到小地圖 Bitmap，再交給 UI 顯示
                            var consoleBmp = BitmapConverter.ToBitmap(consoleMinimapClone);
                            if (_pathPlanningManager?.IsRunning == true)
                            {
                                var pathData = BuildPathVisualizationData();
                                DrawAStarOverlayOnConsoleMinimap(consoleBmp, pathData);
                            }
                            else
                            {
                                DrawAStarOverlayOnConsoleMinimap(consoleBmp, pathData: null);
                            }

                            _consoleMinimapUiUpdatePending = true;
                            SetConsoleMinimapImage(consoleBmp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[系統] 處理畫面錯誤: {ex.Message}");
            }
        }

        private void SetConsoleMinimapImage(Bitmap? newImage)
        {
            if (IsDisposed || Disposing)
            {
                newImage?.Dispose();
                _consoleMinimapUiUpdatePending = false;
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetConsoleMinimapImage(newImage)));
                return;
            }

            try
            {
                var old = pictureBox_ConsoleMinimap.Image;
                pictureBox_ConsoleMinimap.Image = newImage;
                lbl_ConsoleMinimapPlaceholder.Visible = newImage == null;
                old?.Dispose();
            }
            finally
            {
                _consoleMinimapUiUpdatePending = false;
            }
        }

        private void DrawAStarOverlayOnConsoleMinimap(Bitmap bitmap, PathVisualizationData? pathData)
        {
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
                return;

            if (pathData == null)
                return;

            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            // 繩索（背景層）
            if (pathData.Ropes != null)
            {
                foreach (var rope in pathData.Ropes)
                {
                    var c = rope.IsPlayerOnRope ? Color.Cyan : Color.FromArgb(160, 220, 60, 60);
                    using var pen = new Pen(c, 1.5f);
                    g.DrawLine(pen, rope.X, rope.TopY, rope.X, rope.BottomY);
                }
            }

            // A* planned path：已走＝綠、未走＝黃；玩家→下一點＝藍虛線
            DrawPlannedPathPolyline(g, pathData);

            // 圖上其餘平台節點（淡紅），目前目標加亮
            if (pathData.WaypointPaths != null)
            {
                const float waypointRadius = 1.8f;
                foreach (var wp in pathData.WaypointPaths)
                {
                    var color = wp.IsBlacklisted
                        ? Color.Black
                        : wp.IsCurrentTarget
                            ? Color.Lime
                            : Color.FromArgb(140, 220, 40, 40);

                    using var brush = new SolidBrush(color);
                    g.FillEllipse(
                        brush,
                        wp.Position.X - waypointRadius,
                        wp.Position.Y - waypointRadius,
                        waypointRadius * 2,
                        waypointRadius * 2);
                }
            }

            // 玩家位置
            if (pathData.PlayerPosition.HasValue)
            {
                var style = Config.Appearance.MinimapPlayer;
                var frameColor = GameVisionCore.ParseColor(style.FrameColor);
                float size = Math.Max(4f, style.FrameThickness * 1.5f);
                DrawingHelper.DrawCrosshair(g, pathData.PlayerPosition.Value, size, frameColor, style.FrameThickness);
            }

            // 下一目標 / 臨時目標
            if (pathData.TargetPosition.HasValue)
            {
                using var pen = new Pen(Color.Gold, 2f);
                float r = 5f;
                var p = pathData.TargetPosition.Value;
                g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);
            }

            if (pathData.TemporaryTarget.HasValue)
            {
                using var pen = new Pen(Color.Cyan, 1.5f);
                float s = 6f;
                var p = pathData.TemporaryTarget.Value;
                g.DrawLine(pen, p.X - s, p.Y, p.X + s, p.Y);
                g.DrawLine(pen, p.X, p.Y - s, p.X, p.Y + s);
            }

            // 目前目標 Hitbox
            if (pathData.TargetHitbox.HasValue)
            {
                var hb = pathData.TargetHitbox.Value;
                bool inside = pathData.IsPlayerInsideTargetHitbox == true;

                using var pen = new Pen(inside ? Color.LimeGreen : Color.OrangeRed, 2f);
                using var brush = new SolidBrush(inside
                    ? Color.FromArgb(80, 0, 255, 0)
                    : Color.FromArgb(80, 255, 69, 0));

                g.FillRectangle(brush, hb.X, hb.Y, hb.Width, hb.Height);
                g.DrawRectangle(pen, hb.X, hb.Y, hb.Width, hb.Height);
            }

            // 診斷文字
            if (!string.IsNullOrWhiteSpace(pathData.CurrentAction))
            {
                using var font = new Font("Consolas", 9f, FontStyle.Bold);
                var text = $"ACT:{pathData.CurrentAction}";
                var textSize = g.MeasureString(text, font);

                using var bg = new SolidBrush(Color.FromArgb(120, 20, 20, 20));
                using var fg = new SolidBrush(Color.WhiteSmoke);

                var rect = new RectangleF(bitmap.Width - textSize.Width - 8, 6, textSize.Width + 4, textSize.Height + 2);
                g.FillRectangle(bg, rect);
                g.DrawString(text, font, fg, rect.X + 2, rect.Y + 1);
            }
        }

        /// <summary>
        /// 畫 planned path polyline。
        /// CurrentWaypointIndex 指向「下一個要到」；其前的邊視為已走。
        /// </summary>
        private static void DrawPlannedPathPolyline(Graphics g, PathVisualizationData pathData)
        {
            var path = pathData.PlannedPath;
            if (path == null || path.Count < 2)
                return;

            int current = Math.Clamp(pathData.CurrentWaypointIndex, 0, path.Count - 1);

            using var traveledPen = new Pen(Color.FromArgb(220, 40, 200, 80), 2.2f);
            using var remainingPen = new Pen(Color.FromArgb(220, 255, 200, 40), 2.2f);
            using var activePen = new Pen(Color.DeepSkyBlue, 2f) { DashStyle = DashStyle.Dash };

            for (int i = 0; i < path.Count - 1; i++)
            {
                // 邊 i→i+1：若終點已過 current 則已走；否則未走
                bool traveled = (i + 1) < current;
                g.DrawLine(traveled ? traveledPen : remainingPen, path[i], path[i + 1]);
            }

            // 強調「正在執行」的路段：玩家 → 下一目標（或臨時目標）
            if (pathData.PlayerPosition.HasValue)
            {
                var liveTarget = pathData.TemporaryTarget ?? pathData.TargetPosition;
                if (liveTarget.HasValue)
                    g.DrawLine(activePen, pathData.PlayerPosition.Value, liveTarget.Value);
            }

            // planned path 節點：已走／目前／未走分色
            for (int i = 0; i < path.Count; i++)
            {
                var p = path[i];
                Color c =
                    i < current ? Color.LimeGreen :
                    i == current ? Color.Gold :
                    Color.Orange;

                float r = i == current ? 3.2f : 2.4f;
                using var brush = new SolidBrush(c);
                g.FillEllipse(brush, p.X - r, p.Y - r, r * 2, r * 2);
            }
        }


        private volatile bool _autoAttackEnabled = false;

        /// <summary>依勾選與下拉選項更新 <see cref="_autoAttackEnabled"/>。</summary>
        private void UpdateAutoAttackState()
        {
            _autoAttackEnabled = ckB_Start.Checked &&
                                 cbo_LoadPathFile.SelectedIndex > 0 &&
                                 cbo_DetectMode.SelectedItem != null &&
                                 (_monsterTemplates?.HasSelection == true);

            UpdatePrerequisitesLabel();
        }

        /// <summary>同步 GamePipeline 輸出的小地圖框與玩家標記（LiveView 疊加在 <see cref="OnFrameAvailable"/> 繪製）。</summary>
        private void OnGamePipelineFrameProcessed(FrameProcessingResult result)
        {
            OnConsoleFrameProcessed(result);
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




        /// <summary>組裝主控台運行小地圖所需的路徑／繩索／Hitbox 疊加資料。</summary>
        private PathVisualizationData? BuildPathVisualizationData()
        {
            try
            {
                var pathData = new PathVisualizationData();
                var state = _pathPlanningManager?.CurrentState;

                if (state?.PlannedPath is { Count: > 0 })
                {
                    pathData.PlannedPath = new List<SdPointF>(state.PlannedPath);
                    pathData.CurrentWaypointIndex = state.CurrentWaypointIndex;
                }

                var graph = _pathPlanningManager?.Tracker?.NavGraph;
                if (graph != null && graph.NodeCount > 0)
                {
                    var allNodes = graph.GetAllNodes();

                    var platformNodes = allNodes.Where(n => n.Type == NavigationNodeType.Platform).ToList();
                    var ropeNodes = allNodes.Where(n => n.Type == NavigationNodeType.Rope).ToList();

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

                var playerPosOpt = state?.CurrentPlayerPosition;
                if (playerPosOpt.HasValue)
                {
                    pathData.PlayerPosition = playerPosOpt.Value;
                }
                var nextWp = state?.NextWaypoint;
                if (nextWp.HasValue)
                {
                    pathData.TargetPosition = nextWp.Value;
                }

                var tempTarget = state?.TemporaryTarget;
                if (tempTarget.HasValue)
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
                Logger.Error($"[運行小地圖] BuildPathVisualizationData 錯誤: {ex.Message}");
                return null;
            }
        }

        /// <summary>停止 LiveView 擷取並清空即時預覽參考。</summary>
        private void StopAndReleaseAllResources()
        {
            try
            {
                liveViewManager?.StopLiveView();
                SyncClientSizeGuardTimer();
                MsgLog.ShowStatus(textBox1, "所有資源已釋放");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"釋放資源錯誤: {ex.Message}");
            }
        }

        #endregion
    }
}
