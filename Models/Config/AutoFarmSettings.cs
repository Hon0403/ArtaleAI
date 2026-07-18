namespace ArtaleAI.Models.Config
{
    /// <summary>自動打怪行為參數（防偵測休息、自動喝水、補助技能）。</summary>
    public class AutoFarmSettings
    {
        /// <summary>每隔多少分鐘休息一次；0 = 關閉。</summary>
        public int RestIntervalMinutes { get; set; } = 0;

        /// <summary>每次休息持續秒數。</summary>
        public int RestDurationSeconds { get; set; } = 60;

        /// <summary>間隔與持續時間的 ±百分比抖動；0 = 固定時程。</summary>
        public int RestJitterPercent { get; set; } = 20;

        public bool HealHpEnabled { get; set; }

        /// <summary>HP 低於此百分比時按補藥鍵（1–99）。</summary>
        public int HealHpThresholdPercent { get; set; } = 40;

        /// <summary>遊戲內 HP 藥水快捷鍵名稱，例如 Insert。</summary>
        public string HealHpHotkey { get; set; } = "Insert";

        public bool HealMpEnabled { get; set; }

        /// <summary>MP 低於此百分比時按補魔鍵（1–99）。</summary>
        public int HealMpThresholdPercent { get; set; } = 30;

        /// <summary>遊戲內 MP 藥水快捷鍵名稱，例如 Delete。</summary>
        public string HealMpHotkey { get; set; } = "Delete";

        /// <summary>同類型藥水最短間隔，避免連按。</summary>
        public int HealCooldownMs { get; set; } = 800;

        /// <summary>補助技能固定槽位數（UI／YAML／Coordinator 共用）。</summary>
        public const int MaxBuffSkillSlots = 5;

        /// <summary>補助技能（固定最多 <see cref="MaxBuffSkillSlots"/> 筆；未啟用者不按）。</summary>
        public List<BuffSkillEntry> BuffSkills { get; set; } = CreateDefaultBuffSkills();

        /// <summary>同一幀內多個技能到期時，兩次按鍵最短間隔。</summary>
        public int BuffCastGapMs { get; set; } = 450;

        /// <summary>攻擊輪轉技能固定槽位數。</summary>
        public const int MaxAttackSkillSlots = 3;

        /// <summary>無輪轉技可放時的主攻鍵（預設 Ctrl）。</summary>
        public string AttackPrimaryHotkey { get; set; } = "Ctrl";

        /// <summary>攻擊輪轉技（冷卻就緒時優先於主攻）。</summary>
        public List<AttackSkillEntry> AttackSkills { get; set; } = CreateDefaultAttackSkills();

        /// <summary>
        /// 導航進行中暫緩攻擊（優先走位）。
        /// 避免站在不同層高（如安全區）被螢幕攻擊框咬到下層怪而卡住。
        /// </summary>
        public bool PreferNavigationOverAttack { get; set; } = true;

        /// <summary>
        /// 攻擊目標與攻擊框中心的最大垂直差（螢幕像素）。
        /// 超過視為不同層高、打不到，不觸發攻擊。
        /// </summary>
        public int AttackMaxVerticalDeltaPx { get; set; } = 80;

        /// <summary>小地圖偵到其他玩家時暫停並執行換頻序列（Esc＋點選單模板）。</summary>
        public bool ChangeChannelOnOtherPlayers { get; set; }

        /// <summary>Esc 後點擊的「頻道」按鈕模板（相對 ContentRoot）。</summary>
        public string ChangeChannelMenuTemplate { get; set; } =
            "templates/MainScreen/channel_menu_button.png";

        /// <summary>頻道列表／面板錨點模板（相對 ContentRoot）；點擊改走格網，非面板中心。</summary>
        public string ChangeChannelPickTemplate { get; set; } =
            "templates/MainScreen/channel_pick_panel.png";

        /// <summary>選完頻道後的「確定」鈕模板（相對 ContentRoot）。</summary>
        public string ChangeChannelConfirmTemplate { get; set; } =
            "templates/MainScreen/channel_confirm_button.png";

        /// <summary>換頻後登入畫面「登入」鈕。</summary>
        public string ChangeChannelLoginTemplate { get; set; } =
            "templates/MainScreen/channel_login_button.png";

        /// <summary>登入後「選擇角色」鈕。</summary>
        public string ChangeChannelSelectCharacterTemplate { get; set; } =
            "templates/MainScreen/channel_select_character_button.png";

        /// <summary>換頻 UI 模板匹配閾值（0–1）。</summary>
        public double ChangeChannelMatchThreshold { get; set; } = 0.68;

        /// <summary>頻道面板內格網幾何（相對錨點模板）。</summary>
        public ChannelPickGridSettings ChangeChannelGrid { get; set; } = new();

        /// <summary>
        /// 選格策略：<c>random</c>（預設）或 <c>preferLowOccupancy</c>（偏好較空人數條）。
        /// </summary>
        public string ChangeChannelPickStrategy { get; set; } = "random";

        /// <summary>觸發後至少暫停秒數（含換頻讀取與回登入／選角）。</summary>
        public int ChangeChannelPauseSeconds { get; set; } = 90;

        /// <summary>兩次觸發之間的最短間隔（秒）。</summary>
        public int ChangeChannelCooldownSeconds { get; set; } = 120;

        /// <summary>換頻／等待期間是否自動關閉突發視窗。</summary>
        public bool InterruptDismissEnabled { get; set; } = true;

        /// <summary>
        /// 自動打怪開啟時即處理突發視窗（不必等遇人換頻觸發）。
        /// </summary>
        public bool InterruptDismissDuringAutoFarm { get; set; } = true;

        /// <summary>打怪清窗：小地圖連續消失超過此毫秒才嘗試。</summary>
        public int FarmInterruptMinimapLostMs { get; set; } = 1200;

        /// <summary>打怪清窗嘗試節流（毫秒）。</summary>
        public int FarmInterruptCooldownMs { get; set; } = 3000;

        /// <summary>
        /// 畫面變暗（選單／遮罩）時優先點擊的關閉鈕模板。
        /// </summary>
        public string InterruptDarkOverlayCloseTemplate { get; set; } =
            "templates/MainScreen/interrupt_close_dark_x.png";

        /// <summary>
        /// 多數介面可用 Esc 關閉：換頻安全步驟可優先 Esc，再點暗色 X。
        /// </summary>
        public bool InterruptPreferEscape { get; set; } = true;

        /// <summary>Esc 關閉節流（毫秒），避免輪詢期狂按。</summary>
        public int InterruptEscapeCooldownMs { get; set; } = 2800;

        /// <summary>突發視窗關閉鈕匹配閾值。</summary>
        public double InterruptMatchThreshold { get; set; } = 0.72;

        /// <summary>
        /// 額外白名單關閉模板（可留空）。Esc／暗色 X 之外的少見按鈕才放這裡。
        /// </summary>
        public List<string> InterruptDismissTemplates { get; set; } = [];

        /// <summary>
        /// 隊伍血條缺失時的重建參數（無使用者開關：自動打怪開啟即持續監測）。
        /// 沒有隊伍血條就沒有攻擊框，重建是硬前置條件。
        /// </summary>
        public string PartyWindowHotkey { get; set; } = "P";

        /// <summary>隊伍視窗「新建」鈕模板（相對 ContentRoot）。</summary>
        public string PartyCreateTemplate { get; set; } =
            "templates/MainScreen/party_create_button.png";

        /// <summary>血條連續消失超過此毫秒才視為未組隊（避免瞬間遮擋誤判）。</summary>
        public int PartyHpBarLostMs { get; set; } = 4000;

        /// <summary>兩次重建狀態機的最短間隔（毫秒）。</summary>
        public int PartyRecoveryCooldownMs { get; set; } = 10000;

        /// <summary>「新建」鈕模板匹配閾值。</summary>
        public double PartyCreateMatchThreshold { get; set; } = 0.72;

        private static List<BuffSkillEntry> CreateDefaultBuffSkills() =>
        [
            new() { Enabled = false, Hotkey = "F1", IntervalSeconds = 180 },
            new() { Enabled = false, Hotkey = "F2", IntervalSeconds = 180 },
            new() { Enabled = false, Hotkey = "F3", IntervalSeconds = 300 },
            new() { Enabled = false, Hotkey = "F4", IntervalSeconds = 180 },
            new() { Enabled = false, Hotkey = "F5", IntervalSeconds = 180 },
        ];

        private static List<AttackSkillEntry> CreateDefaultAttackSkills() =>
        [
            new() { Enabled = false, Hotkey = "A", CooldownSeconds = 30 },
            new() { Enabled = false, Hotkey = "S", CooldownSeconds = 20 },
            new() { Enabled = false, Hotkey = "D", CooldownSeconds = 15 },
        ];

        /// <summary>YAML 缺欄或較短列表時補齊到固定槽位數，避免 UI／Coordinator 越界。</summary>
        public void EnsureBuffSkillSlots()
        {
            BuffSkills ??= [];
            List<BuffSkillEntry> defaults = CreateDefaultBuffSkills();
            while (BuffSkills.Count < MaxBuffSkillSlots)
                BuffSkills.Add(CloneBuff(defaults[BuffSkills.Count]));

            if (BuffSkills.Count > MaxBuffSkillSlots)
                BuffSkills.RemoveRange(MaxBuffSkillSlots, BuffSkills.Count - MaxBuffSkillSlots);

            for (int i = 0; i < MaxBuffSkillSlots; i++)
            {
                BuffSkillEntry slot = BuffSkills[i] ?? CloneBuff(defaults[i]);
                BuffSkills[i] = slot;
                if (string.IsNullOrWhiteSpace(slot.Hotkey))
                    slot.Hotkey = defaults[i].Hotkey;
                slot.IntervalSeconds = Math.Clamp(slot.IntervalSeconds, 5, 3600);
            }
        }

        public void EnsureAttackSkillSlots()
        {
            AttackSkills ??= [];
            List<AttackSkillEntry> defaults = CreateDefaultAttackSkills();
            while (AttackSkills.Count < MaxAttackSkillSlots)
                AttackSkills.Add(CloneAttack(defaults[AttackSkills.Count]));

            if (AttackSkills.Count > MaxAttackSkillSlots)
                AttackSkills.RemoveRange(MaxAttackSkillSlots, AttackSkills.Count - MaxAttackSkillSlots);

            if (string.IsNullOrWhiteSpace(AttackPrimaryHotkey))
                AttackPrimaryHotkey = "Ctrl";

            for (int i = 0; i < MaxAttackSkillSlots; i++)
            {
                AttackSkillEntry slot = AttackSkills[i] ?? CloneAttack(defaults[i]);
                AttackSkills[i] = slot;
                if (string.IsNullOrWhiteSpace(slot.Hotkey))
                    slot.Hotkey = defaults[i].Hotkey;
                slot.CooldownSeconds = Math.Clamp(slot.CooldownSeconds, 5, 600);
            }
        }

        private static BuffSkillEntry CloneBuff(BuffSkillEntry source) =>
            new()
            {
                Enabled = source.Enabled,
                Hotkey = source.Hotkey,
                IntervalSeconds = source.IntervalSeconds
            };

        private static AttackSkillEntry CloneAttack(AttackSkillEntry source) =>
            new()
            {
                Enabled = source.Enabled,
                Hotkey = source.Hotkey,
                CooldownSeconds = source.CooldownSeconds
            };
    }
}
