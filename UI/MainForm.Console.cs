using ArtaleAI.Application.Console;
using ArtaleAI.Application.Pipeline;
using ArtaleAI.Domain.Navigation;
using ArtaleAI.Infrastructure.Capture;
using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Shared;
using System.Drawing;

namespace ArtaleAI
{
    public partial class MainForm
    {
        private readonly ConsolePresenter _consolePresenter = new();
        private System.Windows.Forms.Timer? _gameWindowPollTimer;
        private DateTime _lastVitalsUIUpdate = DateTime.MinValue;
        private TextBox? _healHotkeyListeningBox;
        private string _healHotkeyBackup = string.Empty;
        private static readonly Color HealHotkeyListenBackColor = Color.FromArgb(255, 255, 210);

        private void InitializeConsolePanel()
        {
            ConfigureStatusBarAppearance();
            BindConsoleEvents();
            InitializeRestSettings();
            InitializeHealSettings();
            RefreshConsoleUi();
        }

        private void InitializeRestSettings()
        {
            var farm = Config.AutoFarm;
            txt_RestIntervalMinutes.Text = Math.Max(0, farm.RestIntervalMinutes).ToString();
            txt_RestDurationSeconds.Text = Math.Clamp(farm.RestDurationSeconds, 5, 3600).ToString();
            txt_RestJitterPercent.Text = Math.Clamp(farm.RestJitterPercent, 0, 50).ToString();

            // 用白話提示補足標籤，避免非開發者誤解參數語意。
            var tip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };
            tip.SetToolTip(txt_RestIntervalMinutes, "打怪多久後休息一次。填 0 代表不休息。");
            tip.SetToolTip(txt_RestDurationSeconds, "每次休息要停多久（秒）。休息時不攻擊、不走路。");
            tip.SetToolTip(txt_RestJitterPercent, "讓休息時間略有變化，比較不像固定腳本。0＝完全固定，建議 20。");
            tip.SetToolTip(groupBox8, "定期暫停打怪，降低長時間連續操作被判定異常的風險。");
        }

        private void InitializeHealSettings()
        {
            var farm = Config.AutoFarm;
            chk_AutoHealHp.Checked = farm.HealHpEnabled;
            txt_HealHpThreshold.Text = Math.Clamp(farm.HealHpThresholdPercent, 1, 99).ToString();
            txt_HealHpHotkey.Text = string.IsNullOrWhiteSpace(farm.HealHpHotkey) ? "Insert" : farm.HealHpHotkey;
            chk_AutoHealMp.Checked = farm.HealMpEnabled;
            txt_HealMpThreshold.Text = Math.Clamp(farm.HealMpThresholdPercent, 1, 99).ToString();
            txt_HealMpHotkey.Text = string.IsNullOrWhiteSpace(farm.HealMpHotkey) ? "Delete" : farm.HealMpHotkey;

            chk_AutoHealHp.CheckedChanged += (_, _) => CommitHealSettings();
            chk_AutoHealMp.CheckedChanged += (_, _) => CommitHealSettings();
            txt_HealHpThreshold.KeyPress += txt_RestNumeric_KeyPress;
            txt_HealMpThreshold.KeyPress += txt_RestNumeric_KeyPress;
            txt_HealHpThreshold.Leave += (_, _) => CommitHealSettings();
            txt_HealMpThreshold.Leave += (_, _) => CommitHealSettings();

            ConfigureHealHotkeyCapture(txt_HealHpHotkey);
            ConfigureHealHotkeyCapture(txt_HealMpHotkey);
            KeyDown += MainForm_HealHotkeyForm_KeyDown;

            var tip = new ToolTip
            {
                AutoPopDelay = 9000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };
            tip.SetToolTip(chk_AutoHealHp, "勾選後，只要勾了自動打怪且有讀到血量，低於設定％會按快捷鍵。");
            tip.SetToolTip(chk_AutoHealMp, "勾選後，只要勾了自動打怪且有讀到魔量，低於設定％會按快捷鍵。");
            tip.SetToolTip(txt_HealHpHotkey, "點一下再按鍵盤任一鍵錄製（與楓谷熱鍵列相同）；Esc 取消錄製。");
            tip.SetToolTip(txt_HealMpHotkey, "點一下再按鍵盤任一鍵錄製（與楓谷熱鍵列相同）；Esc 取消錄製。");
            tip.SetToolTip(groupBox7, "快捷鍵可設成鍵盤上幾乎任一鍵；錄製時 Esc 僅用來取消，不會寫入。");
        }

        private void ConfigureHealHotkeyCapture(TextBox box)
        {
            box.ReadOnly = true;
            box.Cursor = Cursors.Hand;
            box.ShortcutsEnabled = false;
            box.MaxLength = 32;
            box.Enter += HealHotkeyBox_Enter;
            box.Leave += HealHotkeyBox_Leave;
            box.PreviewKeyDown += HealHotkeyBox_PreviewKeyDown;
            box.KeyPress += (_, e) => e.Handled = true;
        }

        private void HealHotkeyBox_Enter(object? sender, EventArgs e)
        {
            if (sender is not TextBox box)
                return;

            if (_healHotkeyListeningBox != null && _healHotkeyListeningBox != box)
                CancelHealHotkeyCapture();

            _healHotkeyListeningBox = box;
            _healHotkeyBackup = VirtualKeyParser.TryParse(box.Text, out _)
                ? box.Text
                : (box == txt_HealHpHotkey ? "Insert" : "Delete");
            box.Text = "按鍵…";
            box.BackColor = HealHotkeyListenBackColor;
            // Shift／Ctrl／Insert 在 TextBox 上常收不到；聆聽期間由 Form 攔截。
            KeyPreview = true;
        }

        private void HealHotkeyBox_Leave(object? sender, EventArgs e)
        {
            if (sender is TextBox box && _healHotkeyListeningBox == box)
                CancelHealHotkeyCapture();
        }

        private void HealHotkeyBox_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (_healHotkeyListeningBox == null)
                return;

            e.IsInputKey = true;
        }

        private void MainForm_HealHotkeyForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_healHotkeyListeningBox == null)
                return;

            TryCaptureHealHotkey(e);
        }

        private void TryCaptureHealHotkey(KeyEventArgs e)
        {
            TextBox? box = _healHotkeyListeningBox;
            if (box == null)
                return;

            e.SuppressKeyPress = true;
            e.Handled = true;

            // Esc 保留為「取消錄製」；若要喝水綁 Escape，請手填 Escape 後存設定。
            if (e.KeyCode == Keys.Escape)
            {
                CancelHealHotkeyCapture();
                return;
            }

            // 滑鼠鍵不是鍵盤熱鍵；其餘 Virtual-Key（含 OEM／小鍵盤／修飾鍵）一律接受。
            if (IsMouseButton(e.KeyCode))
            {
                MsgLog.ShowStatus(textBox1, "請按鍵盤按鍵（不支援滑鼠鍵）");
                return;
            }

            ushort virtualKey = unchecked((ushort)(int)e.KeyCode);
            if (!VirtualKeyParser.TryFormat(virtualKey, out string displayName))
            {
                MsgLog.ShowStatus(textBox1, $"無法辨識按鍵碼：{(int)e.KeyCode}");
                return;
            }

            _healHotkeyListeningBox = null;
            KeyPreview = false;
            box.Text = displayName;
            EndHealHotkeyCaptureVisual(box);
            CommitHealSettings();
        }

        private void CancelHealHotkeyCapture()
        {
            if (_healHotkeyListeningBox == null)
                return;

            TextBox box = _healHotkeyListeningBox;
            _healHotkeyListeningBox = null;
            KeyPreview = false;

            box.Text = string.IsNullOrWhiteSpace(_healHotkeyBackup)
                ? (box == txt_HealHpHotkey ? "Insert" : "Delete")
                : _healHotkeyBackup;
            if (!VirtualKeyParser.TryParse(box.Text, out _))
                box.Text = box == txt_HealHpHotkey ? "Insert" : "Delete";

            EndHealHotkeyCaptureVisual(box);
        }

        private static void EndHealHotkeyCaptureVisual(TextBox box)
        {
            box.BackColor = SystemColors.Window;
        }

        private static bool IsMouseButton(Keys keyCode)
            => keyCode is Keys.LButton or Keys.RButton or Keys.MButton
                or Keys.XButton1 or Keys.XButton2;

        private void CommitHealSettings()
        {
            if (_healHotkeyListeningBox != null)
                return;

            string hpHotkey = NormalizeHotkeyOrFallback(txt_HealHpHotkey.Text, "Insert");
            string mpHotkey = NormalizeHotkeyOrFallback(txt_HealMpHotkey.Text, "Delete");
            txt_HealHpHotkey.Text = hpHotkey;
            txt_HealMpHotkey.Text = mpHotkey;

            int hpThreshold = ParseClampedInt(txt_HealHpThreshold.Text, 1, 99, 40);
            int mpThreshold = ParseClampedInt(txt_HealMpThreshold.Text, 1, 99, 30);
            txt_HealHpThreshold.Text = hpThreshold.ToString();
            txt_HealMpThreshold.Text = mpThreshold.ToString();

            var farm = Config.AutoFarm;
            bool changed = farm.HealHpEnabled != chk_AutoHealHp.Checked
                || farm.HealMpEnabled != chk_AutoHealMp.Checked
                || farm.HealHpThresholdPercent != hpThreshold
                || farm.HealMpThresholdPercent != mpThreshold
                || !string.Equals(farm.HealHpHotkey, hpHotkey, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(farm.HealMpHotkey, mpHotkey, StringComparison.OrdinalIgnoreCase);

            if (!changed)
                return;

            farm.HealHpEnabled = chk_AutoHealHp.Checked;
            farm.HealMpEnabled = chk_AutoHealMp.Checked;
            farm.HealHpThresholdPercent = hpThreshold;
            farm.HealMpThresholdPercent = mpThreshold;
            farm.HealHpHotkey = hpHotkey;
            farm.HealMpHotkey = mpHotkey;

            try
            {
                Config.Save();
                MsgLog.ShowStatus(
                    textBox1,
                    $"已儲存自動喝水：HP {(farm.HealHpEnabled ? $"<{hpThreshold}%→{hpHotkey}" : "關")}；" +
                    $"MP {(farm.HealMpEnabled ? $"<{mpThreshold}%→{mpHotkey}" : "關")}");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"儲存自動喝水設定失敗: {ex.Message}");
            }
        }

        private static string NormalizeHotkeyOrFallback(string text, string fallback)
        {
            string trimmed = text.Trim();
            if (VirtualKeyParser.TryParse(trimmed, out _))
                return trimmed;

            return fallback;
        }

        private void txt_RestNumeric_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
                return;

            if (!char.IsDigit(e.KeyChar))
                e.Handled = true;
        }

        private void txt_RestSettings_Leave(object? sender, EventArgs e)
        {
            CommitRestSettings();
        }

        private void CommitRestSettings()
        {
            int interval = ParseClampedInt(txt_RestIntervalMinutes.Text, 0, 999, 0);
            int duration = ParseClampedInt(txt_RestDurationSeconds.Text, 5, 3600, 60);
            int jitter = ParseClampedInt(txt_RestJitterPercent.Text, 0, 50, 20);

            txt_RestIntervalMinutes.Text = interval.ToString();
            txt_RestDurationSeconds.Text = duration.ToString();
            txt_RestJitterPercent.Text = jitter.ToString();

            var farm = Config.AutoFarm;
            bool changed = farm.RestIntervalMinutes != interval
                || farm.RestDurationSeconds != duration
                || farm.RestJitterPercent != jitter;

            if (!changed)
                return;

            farm.RestIntervalMinutes = interval;
            farm.RestDurationSeconds = duration;
            farm.RestJitterPercent = jitter;

            try
            {
                Config.Save();
                MsgLog.ShowStatus(
                    textBox1,
                    $"已儲存定時休息：每 {interval} 分鐘休息 {duration} 秒（隨機幅度 {jitter}）");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"儲存定時休息設定失敗: {ex.Message}");
            }
        }

        private static int ParseClampedInt(string text, int min, int max, int fallback)
        {
            if (!int.TryParse(text.Trim(), out int value))
                return fallback;

            return Math.Clamp(value, min, max);
        }

        private void ConfigureStatusBarAppearance()
        {
            foreach (var label in new[]
            {
                lbl_Status_Game,
                lbl_Status_Capture,
                lbl_Status_Fsm,
                lbl_Status_Vitals,
                lbl_Status_Path
            })
            {
                label.AutoSize = false;
                label.BackColor = Color.FromArgb(50, 50, 50);
                label.ForeColor = Color.Gainsboro;
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.Dock = DockStyle.Fill;
                label.Margin = new Padding(0);
                label.Padding = new Padding(4, 0, 0, 0);
            }
        }

        private void BindConsoleEvents()
        {
            cbo_DetectMode.SelectedIndexChanged += (_, _) => UpdatePrerequisitesLabel();
            // 怪物勾選：ItemCheck → Reload → UpdateAutoAttackState → UpdatePrerequisitesLabel

            _gameWindowPollTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _gameWindowPollTimer.Tick += (_, _) => RefreshConsoleUi();

            if (_fsm != null)
                _fsm.OnStateChanged += (_, _) => RefreshStatusBar();
        }

        private void SetGameWindowPollTimer(bool enabled)
        {
            if (_gameWindowPollTimer == null) return;

            if (enabled)
            {
                if (!_gameWindowPollTimer.Enabled)
                    _gameWindowPollTimer.Start();
            }
            else
            {
                _gameWindowPollTimer.Stop();
            }

            RefreshStatusBar();
        }

        /// <summary>刷新遊戲視窗狀態與自動打怪前置條件 inline 提示。</summary>
        private void UpdatePrerequisitesLabel() => RefreshConsoleUi();

        private void RefreshStatusBar() => RefreshConsoleUi();

        private void RefreshStatusBarPath(PathPlanningState? pathState = null)
            => RefreshConsoleUi(pathState);

        private void RefreshVitalsPanel(PlayerVitalsSnapshot? vitals)
            => RefreshConsoleUi(vitalsOverride: vitals);

        private void OnConsoleFrameProcessed(FrameProcessingResult result)
        {
            if (result == null) return;

            var now = DateTime.UtcNow;
            if ((now - _lastVitalsUIUpdate).TotalMilliseconds < StatusUpdateIntervalMs)
                return;

            _lastVitalsUIUpdate = now;
            RefreshConsoleUi(vitalsOverride: result.PlayerVitals);
        }

        private void RefreshConsoleUi(
            PathPlanningState? pathState = null,
            PlayerVitalsSnapshot? vitalsOverride = null)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => RefreshConsoleUi(pathState, vitalsOverride)));
                return;
            }

            ConsoleViewState state = _consolePresenter.Build(
                CollectConsoleStatusInput(pathState, vitalsOverride));
            BindConsoleViewState(state);
        }

        private ConsoleStatusInput CollectConsoleStatusInput(
            PathPlanningState? pathState,
            PlayerVitalsSnapshot? vitalsOverride)
        {
            string gameTitle = Config.General.GameWindowTitle;
            bool gameFound = !string.IsNullOrWhiteSpace(gameTitle)
                && WindowFinder.TryCreateItemForWindow(gameTitle) != null;

            var vitals = vitalsOverride
                ?? _gamePipeline?.GetCurrentSnapshot().PlayerVitals;
            bool hasVitals = vitals?.HasFillReading == true;

            pathState ??= _pathPlanningManager?.CurrentState;
            bool pathRunning = _pathPlanningManager?.IsRunning == true && pathState != null;

            return new ConsoleStatusInput
            {
                GameWindowTitle = gameTitle,
                GameFound = gameFound,
                CaptureRunning = liveViewManager?.IsRunning == true,
                IsResting = _gamePipeline?.IsResting == true,
                FsmState = _fsm?.CurrentState ?? NavigationState.Idle,
                HpRatio = hasVitals ? vitals!.HpRatio : null,
                MpRatio = hasVitals ? vitals!.MpRatio : null,
                HasVitalsReading = hasVitals,
                HealStatusHint = _gamePipeline?.GetAutoHealStatusHint(),
                PathRunning = pathRunning,
                WaypointIndex = pathState?.CurrentWaypointIndex ?? 0,
                WaypointTotal = pathState?.PlannedPath.Count ?? 0,
                DistanceToNextWaypoint = pathState?.DistanceToNextWaypoint ?? 0,
                AutoStartChecked = ckB_Start.Checked,
                PathFileSelected = cbo_LoadPathFile.SelectedIndex > 0,
                DetectModeSelected = cbo_DetectMode.SelectedItem != null,
                MonsterSelected = _monsterTemplates?.HasSelection == true,
                PlatformNodeCount = CountPlatformNodes()
            };
        }

        private void BindConsoleViewState(ConsoleViewState state)
        {
            lbl_GameWindowStatus.Text = state.GameWindowLabel;
            lbl_GameWindowStatus.ForeColor = ToUiColor(state.GameWindowTone, forPrereqPanel: true);

            lbl_Status_Game.Text = state.StatusGame;
            lbl_Status_Game.ForeColor = ToUiColor(state.StatusGameTone);

            lbl_Status_Capture.Text = state.StatusCapture;
            lbl_Status_Capture.ForeColor = ToUiColor(state.StatusCaptureTone);

            lbl_Status_Fsm.Text = state.StatusFsm;
            lbl_Status_Fsm.ForeColor = ToUiColor(state.StatusFsmTone);

            lbl_Status_Vitals.Text = state.StatusVitals;
            lbl_Status_Vitals.ForeColor = ToUiColor(state.StatusVitalsTone);

            lbl_Status_Path.Text = state.StatusPath;
            lbl_Status_Path.ForeColor = ToUiColor(state.StatusPathTone);

            lbl_Prerequisites.Text = state.PrerequisitesText;
            lbl_Prerequisites.ForeColor = state.PrerequisitesTone == ConsoleStatusTone.Neutral
                ? SystemColors.ControlText
                : ToUiColor(state.PrerequisitesTone, forPrereqPanel: true);

            if (state.HasVitalsReading)
            {
                prg_Hp.Value = state.HpPercent;
                prg_Mp.Value = state.MpPercent;
                lbl_HpPercent.Text = $"{state.HpPercent}%";
                lbl_MpPercent.Text = $"{state.MpPercent}%";
            }
            else
            {
                prg_Hp.Value = 0;
                prg_Mp.Value = 0;
                lbl_HpPercent.Text = "—";
                lbl_MpPercent.Text = "—";
            }
        }

        private static Color ToUiColor(ConsoleStatusTone tone, bool forPrereqPanel = false)
            => tone switch
            {
                ConsoleStatusTone.Positive => Color.DarkGreen,
                ConsoleStatusTone.Warning => Color.DarkOrange,
                ConsoleStatusTone.Danger => Color.Firebrick,
                ConsoleStatusTone.Accent => Color.DarkOrange,
                _ => forPrereqPanel ? SystemColors.ControlText : Color.Gainsboro
            };

        private int CountPlatformNodes()
        {
            if (loadedPathData?.Nodes == null)
                return 0;

            int count = 0;
            foreach (var node in loadedPathData.Nodes)
            {
                if (node.Type == "Platform")
                    count++;
            }

            return count;
        }
    }
}
