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
            // 可視尺寸（含 splitConsoleMonitor.SplitterDistance）只認 Designer，勿在此覆寫
            BindConsoleEvents();
            InitializeRestSettings();
            InitializeHealSettings();
            InitializeBuffSettings();
            InitializeAttackSettings();
            InitializeOtherPlayerAvoidanceSettings();
            RefreshConsoleUi();
        }

        private void InitializeOtherPlayerAvoidanceSettings()
        {
            var farm = Config.AutoFarm;
            chk_ChangeChannelOnOtherPlayers.Checked = farm.ChangeChannelOnOtherPlayers;
            chk_ChangeChannelOnOtherPlayers.CheckedChanged += (_, _) => CommitOtherPlayerAvoidanceSettings();

            var tip = new ToolTip
            {
                AutoPopDelay = 10000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };
            tip.SetToolTip(
                chk_ChangeChannelOnOtherPlayers,
                "勾選且開始自動打怪後：小地圖偵到其他玩家會暫停走路／攻擊，" +
                "並執行 Esc → 點「頻道」→ 在列表可見格隨機點一頻（可於 config 改 preferLowOccupancy）。");
        }

        private void CommitOtherPlayerAvoidanceSettings()
        {
            var farm = Config.AutoFarm;
            if (farm.ChangeChannelOnOtherPlayers == chk_ChangeChannelOnOtherPlayers.Checked)
                return;

            farm.ChangeChannelOnOtherPlayers = chk_ChangeChannelOnOtherPlayers.Checked;
            try
            {
                Config.Save();
                MsgLog.ShowStatus(
                    textBox1,
                    farm.ChangeChannelOnOtherPlayers
                        ? "已開啟遇人換頻／退避"
                        : "已關閉遇人換頻／退避");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"儲存遇人換頻設定失敗: {ex.Message}");
            }
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
            tip.SetToolTip(txt_RestIntervalMinutes, "打怪多久後休息一次。填 0 代表不休息。時間到會先走到最近的安全區或繩索再開始休息。");
            tip.SetToolTip(txt_RestDurationSeconds, "到達休息點後要停多久（秒）。休息倒數期間不攻擊、不走路。");
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

            ConfigureHotkeyCapture(txt_HealHpHotkey, "Insert");
            ConfigureHotkeyCapture(txt_HealMpHotkey, "Delete");
            KeyDown += MainForm_HotkeyForm_KeyDown;

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

        private void InitializeBuffSettings()
        {
            var farm = Config.AutoFarm;
            farm.EnsureBuffSkillSlots();

            BindBuffSlot(0, chk_Buff1, txt_Buff1Interval, txt_Buff1Hotkey);
            BindBuffSlot(1, chk_Buff2, txt_Buff2Interval, txt_Buff2Hotkey);
            BindBuffSlot(2, chk_Buff3, txt_Buff3Interval, txt_Buff3Hotkey);
            BindBuffSlot(3, chk_Buff4, txt_Buff4Interval, txt_Buff4Hotkey);
            BindBuffSlot(4, chk_Buff5, txt_Buff5Interval, txt_Buff5Hotkey);

            chk_Buff1.CheckedChanged += (_, _) => CommitBuffSettings();
            chk_Buff2.CheckedChanged += (_, _) => CommitBuffSettings();
            chk_Buff3.CheckedChanged += (_, _) => CommitBuffSettings();
            chk_Buff4.CheckedChanged += (_, _) => CommitBuffSettings();
            chk_Buff5.CheckedChanged += (_, _) => CommitBuffSettings();
            txt_Buff1Interval.KeyPress += txt_RestNumeric_KeyPress;
            txt_Buff2Interval.KeyPress += txt_RestNumeric_KeyPress;
            txt_Buff3Interval.KeyPress += txt_RestNumeric_KeyPress;
            txt_Buff4Interval.KeyPress += txt_RestNumeric_KeyPress;
            txt_Buff5Interval.KeyPress += txt_RestNumeric_KeyPress;
            txt_Buff1Interval.Leave += (_, _) => CommitBuffSettings();
            txt_Buff2Interval.Leave += (_, _) => CommitBuffSettings();
            txt_Buff3Interval.Leave += (_, _) => CommitBuffSettings();
            txt_Buff4Interval.Leave += (_, _) => CommitBuffSettings();
            txt_Buff5Interval.Leave += (_, _) => CommitBuffSettings();

            ConfigureHotkeyCapture(txt_Buff1Hotkey, "F1");
            ConfigureHotkeyCapture(txt_Buff2Hotkey, "F2");
            ConfigureHotkeyCapture(txt_Buff3Hotkey, "F3");
            ConfigureHotkeyCapture(txt_Buff4Hotkey, "F4");
            ConfigureHotkeyCapture(txt_Buff5Hotkey, "F5");

            var tip = new ToolTip
            {
                AutoPopDelay = 9000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };
            tip.SetToolTip(
                groupBox9,
                "勾選自動打怪後，依「秒數」週期重按各技能鍵。" +
                "間隔會自動加減約 10% 隨機，降低固定腳本節奏；不讀畫面，請遊戲內先綁好 Buff。");
        }

        private void InitializeAttackSettings()
        {
            var farm = Config.AutoFarm;
            farm.EnsureAttackSkillSlots();

            txt_AttackPrimaryHotkey.Text = string.IsNullOrWhiteSpace(farm.AttackPrimaryHotkey)
                ? "Ctrl"
                : farm.AttackPrimaryHotkey;
            BindAttackSlot(0, chk_Attack1, txt_Attack1Cooldown, txt_Attack1Hotkey);
            BindAttackSlot(1, chk_Attack2, txt_Attack2Cooldown, txt_Attack2Hotkey);
            BindAttackSlot(2, chk_Attack3, txt_Attack3Cooldown, txt_Attack3Hotkey);

            chk_Attack1.CheckedChanged += (_, _) => CommitAttackSettings();
            chk_Attack2.CheckedChanged += (_, _) => CommitAttackSettings();
            chk_Attack3.CheckedChanged += (_, _) => CommitAttackSettings();
            txt_Attack1Cooldown.KeyPress += txt_RestNumeric_KeyPress;
            txt_Attack2Cooldown.KeyPress += txt_RestNumeric_KeyPress;
            txt_Attack3Cooldown.KeyPress += txt_RestNumeric_KeyPress;
            txt_Attack1Cooldown.Leave += (_, _) => CommitAttackSettings();
            txt_Attack2Cooldown.Leave += (_, _) => CommitAttackSettings();
            txt_Attack3Cooldown.Leave += (_, _) => CommitAttackSettings();

            ConfigureHotkeyCapture(txt_AttackPrimaryHotkey, "Ctrl");
            ConfigureHotkeyCapture(txt_Attack1Hotkey, "A");
            ConfigureHotkeyCapture(txt_Attack2Hotkey, "S");
            ConfigureHotkeyCapture(txt_Attack3Hotkey, "D");

            var tip = new ToolTip
            {
                AutoPopDelay = 9000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };
            tip.SetToolTip(
                groupBox_Attack,
                "鎖定怪時：冷卻就緒的技優先，否則按主攻鍵。冷卻自動 ±10%。預設主攻為 Ctrl。");
        }

        private void BindAttackSlot(int index, CheckBox enabled, TextBox cooldown, TextBox hotkey)
        {
            AttackSkillEntry slot = Config.AutoFarm.AttackSkills[index];
            enabled.Checked = slot.Enabled;
            cooldown.Text = Math.Clamp(slot.CooldownSeconds, 5, 600).ToString();
            hotkey.Text = string.IsNullOrWhiteSpace(slot.Hotkey)
                ? (index == 0 ? "A" : index == 1 ? "S" : "D")
                : slot.Hotkey;
        }

        private void BindBuffSlot(int index, CheckBox enabled, TextBox interval, TextBox hotkey)
        {
            BuffSkillEntry slot = Config.AutoFarm.BuffSkills[index];
            enabled.Checked = slot.Enabled;
            interval.Text = Math.Clamp(slot.IntervalSeconds, 5, 3600).ToString();
            hotkey.Text = string.IsNullOrWhiteSpace(slot.Hotkey) ? $"F{index + 1}" : slot.Hotkey;
        }

        private void ConfigureHotkeyCapture(TextBox box, string fallbackHotkey)
        {
            // ReadOnly／Cursor／ShortcutsEnabled／MaxLength 已在 Designer
            box.Tag = fallbackHotkey;
            box.Enter += HotkeyBox_Enter;
            box.Leave += HotkeyBox_Leave;
            box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
            box.KeyPress += (_, e) => e.Handled = true;
        }

        private void HotkeyBox_Enter(object? sender, EventArgs e)
        {
            if (sender is not TextBox box)
                return;

            if (_healHotkeyListeningBox != null && _healHotkeyListeningBox != box)
                CancelHotkeyCapture();

            _healHotkeyListeningBox = box;
            string fallback = box.Tag as string ?? "F1";
            _healHotkeyBackup = VirtualKeyParser.TryParse(box.Text, out _)
                ? box.Text
                : fallback;
            box.Text = "按鍵…";
            box.BackColor = HealHotkeyListenBackColor;
            KeyPreview = true;
        }

        private void HotkeyBox_Leave(object? sender, EventArgs e)
        {
            if (sender is TextBox box && _healHotkeyListeningBox == box)
                CancelHotkeyCapture();
        }

        private void HotkeyBox_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (_healHotkeyListeningBox == null)
                return;

            e.IsInputKey = true;
        }

        private void MainForm_HotkeyForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_healHotkeyListeningBox == null)
                return;

            TryCaptureHotkey(e);
        }

        private void TryCaptureHotkey(KeyEventArgs e)
        {
            TextBox? box = _healHotkeyListeningBox;
            if (box == null)
                return;

            e.SuppressKeyPress = true;
            e.Handled = true;

            // Esc 保留為「取消錄製」；若要綁 Escape，請手填 Escape 後存設定。
            if (e.KeyCode == Keys.Escape)
            {
                CancelHotkeyCapture();
                return;
            }

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
            EndHotkeyCaptureVisual(box);
            PersistHotkeyBox(box);
        }

        private void CancelHotkeyCapture()
        {
            if (_healHotkeyListeningBox == null)
                return;

            TextBox box = _healHotkeyListeningBox;
            _healHotkeyListeningBox = null;
            KeyPreview = false;

            string fallback = box.Tag as string ?? "F1";
            box.Text = string.IsNullOrWhiteSpace(_healHotkeyBackup)
                ? fallback
                : _healHotkeyBackup;
            if (!VirtualKeyParser.TryParse(box.Text, out _))
                box.Text = fallback;

            EndHotkeyCaptureVisual(box);
        }

        private static void EndHotkeyCaptureVisual(TextBox box)
        {
            box.BackColor = SystemColors.Window;
        }

        private void PersistHotkeyBox(TextBox box)
        {
            if (box == txt_HealHpHotkey || box == txt_HealMpHotkey)
                CommitHealSettings();
            else if (box == txt_AttackPrimaryHotkey
                     || box == txt_Attack1Hotkey
                     || box == txt_Attack2Hotkey
                     || box == txt_Attack3Hotkey)
                CommitAttackSettings();
            else
                CommitBuffSettings();
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

        private void CommitBuffSettings()
        {
            if (_healHotkeyListeningBox != null)
                return;

            var farm = Config.AutoFarm;
            farm.EnsureBuffSkillSlots();

            bool slotChanged =
                ApplyBuffSlotFromUi(0, chk_Buff1, txt_Buff1Interval, txt_Buff1Hotkey, "F1")
                | ApplyBuffSlotFromUi(1, chk_Buff2, txt_Buff2Interval, txt_Buff2Hotkey, "F2")
                | ApplyBuffSlotFromUi(2, chk_Buff3, txt_Buff3Interval, txt_Buff3Hotkey, "F3")
                | ApplyBuffSlotFromUi(3, chk_Buff4, txt_Buff4Interval, txt_Buff4Hotkey, "F4")
                | ApplyBuffSlotFromUi(4, chk_Buff5, txt_Buff5Interval, txt_Buff5Hotkey, "F5");

            if (!slotChanged)
                return;

            _gamePipeline?.ResetBuffSchedule();

            try
            {
                Config.Save();
                int enabledCount = farm.BuffSkills.Count(s => s.Enabled);
                MsgLog.ShowStatus(
                    textBox1,
                    enabledCount == 0
                        ? "已儲存補助技能：全部關閉"
                        : $"已儲存補助技能：啟用 {enabledCount} 個");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"儲存補助技能設定失敗: {ex.Message}");
            }
        }

        private bool ApplyBuffSlotFromUi(
            int index,
            CheckBox enabled,
            TextBox intervalBox,
            TextBox hotkeyBox,
            string fallbackHotkey)
        {
            BuffSkillEntry slot = Config.AutoFarm.BuffSkills[index];
            string hotkey = NormalizeHotkeyOrFallback(hotkeyBox.Text, fallbackHotkey);
            hotkeyBox.Text = hotkey;
            int interval = ParseClampedInt(intervalBox.Text, 5, 3600, 180);
            intervalBox.Text = interval.ToString();

            bool changed = slot.Enabled != enabled.Checked
                || slot.IntervalSeconds != interval
                || !string.Equals(slot.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase);

            slot.Enabled = enabled.Checked;
            slot.Hotkey = hotkey;
            slot.IntervalSeconds = interval;
            return changed;
        }

        private void CommitAttackSettings()
        {
            if (_healHotkeyListeningBox != null)
                return;

            var farm = Config.AutoFarm;
            farm.EnsureAttackSkillSlots();

            string primary = NormalizeHotkeyOrFallback(txt_AttackPrimaryHotkey.Text, "Ctrl");
            txt_AttackPrimaryHotkey.Text = primary;

            bool slotChanged =
                ApplyAttackSlotFromUi(0, chk_Attack1, txt_Attack1Cooldown, txt_Attack1Hotkey, "A")
                | ApplyAttackSlotFromUi(1, chk_Attack2, txt_Attack2Cooldown, txt_Attack2Hotkey, "S")
                | ApplyAttackSlotFromUi(2, chk_Attack3, txt_Attack3Cooldown, txt_Attack3Hotkey, "D");

            bool primaryChanged = !string.Equals(
                farm.AttackPrimaryHotkey,
                primary,
                StringComparison.OrdinalIgnoreCase);
            farm.AttackPrimaryHotkey = primary;

            if (!slotChanged && !primaryChanged)
                return;

            _gamePipeline?.ResetAttackCooldowns();

            try
            {
                Config.Save();
                int enabledCount = farm.AttackSkills.Count(s => s.Enabled);
                MsgLog.ShowStatus(
                    textBox1,
                    enabledCount == 0
                        ? $"已儲存攻擊輪轉：僅主攻 {primary}"
                        : $"已儲存攻擊輪轉：主攻 {primary}＋{enabledCount} 技");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"儲存攻擊輪轉設定失敗: {ex.Message}");
            }
        }

        private bool ApplyAttackSlotFromUi(
            int index,
            CheckBox enabled,
            TextBox cooldownBox,
            TextBox hotkeyBox,
            string fallbackHotkey)
        {
            AttackSkillEntry slot = Config.AutoFarm.AttackSkills[index];
            string hotkey = NormalizeHotkeyOrFallback(hotkeyBox.Text, fallbackHotkey);
            hotkeyBox.Text = hotkey;
            int cooldown = ParseClampedInt(cooldownBox.Text, 5, 600, 30);
            cooldownBox.Text = cooldown.ToString();

            bool changed = slot.Enabled != enabled.Checked
                || slot.CooldownSeconds != cooldown
                || !string.Equals(slot.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase);

            slot.Enabled = enabled.Checked;
            slot.Hotkey = hotkey;
            slot.CooldownSeconds = cooldown;
            return changed;
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
                IsSeekingRestSpot = _gamePipeline?.IsSeekingRestSpot == true,
                IsHealRetreating = _gamePipeline?.IsHealRetreating == true,
                IsSeekingHealSafeZone = _gamePipeline?.IsSeekingHealSafeZone == true,
                IsAvoidingOtherPlayers = _gamePipeline?.IsAvoidingOtherPlayers == true,
                IsRecoveringParty = _gamePipeline?.IsRecoveringParty == true,
                FsmState = _fsm?.CurrentState ?? NavigationState.Idle,
                HpRatio = hasVitals ? vitals!.HpRatio : null,
                MpRatio = hasVitals ? vitals!.MpRatio : null,
                HasVitalsReading = hasVitals,
                HealStatusHint = _gamePipeline?.GetAutoHealStatusHint(),
                BuffStatusHint = _gamePipeline?.GetBuffStatusHint(),
                AttackStatusHint = _gamePipeline?.GetAttackStatusHint(),
                OtherPlayerAvoidanceHint = _gamePipeline?.GetOtherPlayerAvoidanceStatusHint(),
                PartyRecoveryHint = _gamePipeline?.GetPartyRecoveryStatusHint(),
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
