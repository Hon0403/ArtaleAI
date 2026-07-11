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
    }
}
