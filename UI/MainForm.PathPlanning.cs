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
    public partial class MainForm
    {
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
                    var nextWaypointOpt = pathState.TemporaryTarget ?? pathState.NextWaypoint;

                    if (nextWaypointOpt.HasValue)
                    {
                        var nextWaypoint = nextWaypointOpt.Value;

                        var now = DateTime.UtcNow;
                        var elapsed = (now - _lastStatusUpdate).TotalMilliseconds;
                        if (elapsed >= StatusUpdateIntervalMs)
                        {
                            RefreshStatusBarPath(pathState);
                            _lastStatusUpdate = now;
                        }

                        if (Config.Navigation.EnableAutoMovement && _movementController != null && _pathPlanningManager.IsRunning)
                        {
                            // 架構：追蹤回呼經 BeginInvoke 到 UI，可能早於 OnFrameAvailable 末尾同步 _visionDataReady，
                            // 造成誤判；Pipeline 在觸發事件前已設 VisionDataReady，以此為單一真相來源。
                            if (_gamePipeline == null || !_gamePipeline.VisionDataReady)
                                return;
                            _gamePipeline.VisionDataReady = false;

                            // 攻擊租約只擋方向鍵（MovementController 讓路），不應擋 FSM tick。
                            var tracker = _pathPlanningManager?.Tracker;
                            if (tracker?.IsRescueCircuitBroken == true)
                            {
                                _fsm?.CancelNavigation("救援熔斷生效");
                                return;
                            }

                            NavigationEdge? currentEdge = tracker?.CurrentNavigationEdge;
                            NavigationEdge? edgeToExecute = currentEdge;
                            var executeTarget = nextWaypoint;

                            // 掛繩時必須先離繩；不可先跑 JumpDown 起跳檢查（會誤救援熔斷）。
                            if (tracker != null &&
                                tracker.IsPlayerOnRope((System.Drawing.PointF)playerPos))
                            {
                                var climbGoal = pathState.PlannedPath.Count > 0
                                    ? pathState.PlannedPath[^1]
                                    : executeTarget;

                                if (tracker.TryResolveRopeClimbTowardGoal(
                                        (System.Drawing.PointF)playerPos,
                                        (System.Drawing.PointF)climbGoal,
                                        out var ropeClimb,
                                        out var ropeLanding) &&
                                    ropeClimb != null)
                                {
                                    _gamePipeline.PreemptCombatForNavigation();
                                    tracker.MarkRopeDismountClimbStarted();
                                    Logger.Info(
                                        $"[導航] 掛繩改爬 {ropeClimb.ActionType} " +
                                        $"player=({playerPos.X:F1},{playerPos.Y:F1}) " +
                                        $"goalY={climbGoal.Y:F1} landing=({ropeLanding.X:F1},{ropeLanding.Y:F1})");

                                    if (_fsm != null &&
                                        _fsm.TryStartNavigation(
                                            ropeClimb, (SdPointF)playerPos, (SdPointF)ropeLanding))
                                    {
                                        ReportAction($"{ropeClimb.ActionType}");
                                    }

                                    return;
                                }

                                Logger.Warning(
                                    $"[導航] 掛繩但找不到 Climb 邊，暫不執行 " +
                                    $"player=({playerPos.X:F1},{playerPos.Y:F1})");
                                return;
                            }

                            bool needsJumpApproach = currentEdge?.ActionType is
                                NavigationActionType.Jump or NavigationActionType.SideJump or NavigationActionType.JumpDown;

                            // 換層邊／起跳對位前先搶回攻擊租約，避免 ↓／Alt 被長按擋下。
                            bool needsVerticalPriority = needsJumpApproach
                                || currentEdge?.ActionType is
                                    NavigationActionType.ClimbUp or NavigationActionType.ClimbDown;
                            if (needsVerticalPriority)
                                _gamePipeline.PreemptCombatForNavigation();

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

                            if (edgeToExecute == null)
                            {
                                Logger.Error($"[導航狀態] 無合法導航邊可執行，停止自動導航。玩家=({playerPos.X:F1},{playerPos.Y:F1})");
                                _fsm?.CancelNavigation("無合法導航邊可執行");
                                return;
                            }

                            if (_fsm != null &&
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
            SetGameWindowPollTimer(chkBox.Checked);

            try
            {
                if (chkBox.Checked)
                {
                    // 客戶區固定為設定尺寸（預設 1280×720）後再擷取，辨識才穩
                    bool sizedOk = await EnsureGameClientSizeAsync(
                        forceImmediate: true,
                        relocateMinimapIfResized: liveViewManager?.IsRunning == true);
                    if (Config.General.ForceClientSizeWhileCapture && !sizedOk)
                    {
                        int tw = Config.General.ForceClientWidth;
                        int th = Config.General.ForceClientHeight;
                        MsgLog.ShowError(
                            textBox1,
                            $"客戶區必須為 {tw}x{th} 才較易辨識。請改遊戲為視窗模式、取消最大化後再勾選。");
                        chkBox.Checked = false;
                        return;
                    }

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
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"[小地圖] 載入小地圖錯誤: {ex.Message}");
                        }

                        if (liveViewManager != null)
                        {
                            liveViewManager.StartLiveView(captureItem);
                            SyncClientSizeGuardTimer();
                            bool firstFrame = await liveViewManager.WaitForFirstFrameAsync(TimeSpan.FromSeconds(2));
                            if (!firstFrame)
                                Logger.Debug("[自動打怪] 首幀未於 2 秒內就緒，仍繼續後續流程。");
                        }
                    }
                    else
                    {
                        SyncClientSizeGuardTimer();
                    }

                    bool hasMonsterTemplate = _monsterTemplates?.Catalog.IsEmpty == false;
                    bool hasDetectionMode = !string.IsNullOrEmpty(Config.Vision.DetectionMode);

                    if (hasMonsterTemplate && hasDetectionMode)
                    {
                        MsgLog.ShowStatus(textBox1, $"怪物辨識已啟動（目標：{_monsterTemplates?.SelectedMonsterNamesDisplay}，模式：{Config.Vision.DetectionMode}）");
                    }
                    else
                    {
                        if (!hasMonsterTemplate)
                        {
                            MsgLog.ShowStatus(textBox1, "警告：尚未勾選要打的怪，怪物辨識不會啟動");
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
                    // 熄火順序有意義：
                    // 1) 先熄 Pipeline 決策旗標，阻止本幀後再發動攻擊／導航／休息。
                    // 2) 中斷在途導航飛行（取消 CTS），讓 MoveToTarget 迴圈跳出、停止每幀搶焦點。
                    // 3) 再鬆開任何按住的方向鍵，確保角色即刻定住。
                    _gamePipeline?.StopAutoFarmImmediately();
                    _fsm?.CancelNavigation("使用者關閉自動打怪");
                    _movementController?.StopMovement();

                    if (_pathPlanningManager != null && _pathPlanningManager.IsRunning)
                    {
                        await _pathPlanningManager.StopAsync();
                    }

                    MsgLog.ShowStatus(textBox1, "自動打怪已停止");
                    SyncClientSizeGuardTimer();
                }

                UpdatePrerequisitesLabel();
            }
            catch (Exception ex)
            {
                var action = chkBox.Checked ? "啟動" : "停止";
                MsgLog.ShowError(textBox1, $"自動打怪{action}失敗: {ex.Message}");
                if (chkBox.Checked)
                {
                    chkBox.Checked = false;
                }

                SetGameWindowPollTimer(false);
                UpdatePrerequisitesLabel();
            }
        }
    }
}
