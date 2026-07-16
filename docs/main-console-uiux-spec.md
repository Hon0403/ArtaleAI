# Main Console UI/UX 設計憲章

**狀態：v1 定稿** — 本文件為「主控台」分頁（`tabPage1`）的正式 UX 規格。Phase 0／1 已落地；後續變更以 Phase 驗收與「功能歸屬對照」為準，架構原則非必要不修改。

本文件定義主控台在新版自動打怪／監控流程下的 UI/UX 目標與驗收標準。

**對照文件：**

- [`map-editor-uiux-spec.md`](map-editor-uiux-spec.md) — 路徑編輯分頁設計憲章（互動模式與 Phase 驗收格式之參照）
- [`navigation-recovery.md`](navigation-recovery.md) — 執行期救援與 FSM 語意
- `README.md` § Flow A — 自動打怪主流程敘述

**核心前提：** 主控台是**操作儀表板（Dashboard）**，不是地圖編輯器。使用者在此完成「選設定 → 啟動 → 監控」，不應在此編輯幾何或拓撲。所有狀態回饋必須**誠實**——未實作的功能不得佔用版面或顯示假控件。

**硬性約束 — 日誌不等於儀表板：** `textBox1` + `MsgLog` 僅承擔**除錯與歷史紀錄**；可掃視的運行狀態（FSM、HP/MP、擷取、路徑進度）必須有**固定、結構化**的呈現區，不可全部淹沒在日誌流中。

---

## 設計目標

主控台的首要目標：**讓使用者在 10 秒內理解「能不能開、開了沒、卡在哪」**，而不必捲動日誌或切換分頁。

| 目標 | 說明 |
|------|------|
| 任務導向 IA | 依 Flow A 重排：前置 → 設定 → 執行 → 進階（收納） |
| 誠實性 | 隱藏或明確標示未完成功能；禁止 `checkBox1` 等佔位文字 |
| 結構化狀態 | 固定 StatusBar 顯示 FSM、HP/MP、擷取、路徑摘要 |
| 啟動閘門 | 勾選自動打怪前，inline 提示缺少的前置條件 |
| 與編輯器一致 | 沿用 Progressive disclosure、狀態列常駐、錯誤語意色 |
| 低侵入重構 | Phase 0／1 僅動 `MainForm.Designer.cs` + `MainForm.Console.cs`；不拆 God Form |

---

## 範圍界定

### 本規格納入（Tab 0「主控台」）

- 左側 `panel1` 控制區重排與標籤補全
- 右側 `panel2` 拆分為 StatusBar + LogPanel
- 啟動前置條件檢查與 inline 提示
- `groupBox6` 角色資訊綁定 `PlayerVitalsSnapshot`
- 定時休息（`groupBox8`）與 `AutoFarm` 設定接線
- 未完成區塊的隱藏／收納策略
- 三分頁功能歸屬邊界（見下文對照表）

### 明確 Out of Scope（v1）

| 項目 | 原因 | 後續 Phase |
|------|------|------------|
| 全站 WinForms 主題／暗色皮膚 | ROI 低、易破壞既有編輯器視覺 | Phase 3+ |
| `ConsolePresenter` / DI 完整抽離 | 切片已落地；完整 DI 可後續 | Phase 2 剩餘 |
| 輔助技能 UI 內容 | ~~業務規格未定~~ → 已定稿（週期熱鍵） | Phase 2 進行中 |
| 第四個「營運設定」分頁 | 條件未滿足（見功能歸屬對照） | Phase 2+ 可選 |
| RichTextBox 著色日誌 | 可選增強 | Phase 2 可選 |

### 不涉及的分頁

- **路徑編輯**（`tabPage2`）：見 [`map-editor-uiux-spec.md`](map-editor-uiux-spec.md)
- **即時顯示**（`tabPage3`）：疊加層由 `OverlayRenderer` 負責，主控台僅顯示摘要

---

## 現況盤點（控件清單）

以下為 `MainForm.Designer.cs` → `panel1` **v1 現況**（相對舊草稿已變更處已標註）：

| 控件 | 顯示文字 | 接線狀態 | v1 處置 |
|------|----------|----------|---------|
| `groupBox_Prereq` / `lbl_GameWindowStatus` | 前置狀態 | ✓ | 保留 |
| `groupBox_Settings` | 設定 | ✓ | 路徑／偵測／怪物／下載同一組 |
| `lbl_LoadPathFile` + `cbo_LoadPathFile` | 路徑檔 | ✓ | 保留 |
| `lbl_DetectMode` + `cbo_DetectMode` | 偵測模式 | ✓ | 保留 |
| `clb_MonsterTemplates` + `btn_DownloadMonster` | 怪物／下載 | ✓ | 多選勾選清單 |
| `groupBox_Execute` / `ckB_Start` / `lbl_Prerequisites` | 執行／自動打怪 | ✓ | 保留；前置 inline |
| `groupBox6` / `prg_Hp` / `prg_Mp` | 角色資訊 | ✓ | Phase 1 已綁定 vitals |
| `groupBox7` / 自動喝水 | **補 HP／MP** | ✓ 閾值％＋快捷鍵；Pipeline 節流 | **留**主控台；需遊戲內先綁藥水鍵 |
| `groupBox8` | **定時休息** | ✓ `AutoFarm` | **留** |
| `groupBox9` | **補助技能** | ✓ 5 槽＋間隔＋快捷鍵（抖動程式內建 ±10%） | **留**主控台營運列 |
| `panel_StatusBar` / `lbl_Status_*` | 結構化狀態 | ✓ | Phase 1 已綁定 |
| `textBox1` | 日誌 | ✓ `MsgLog`；ReadOnly | LogPanel 子區 |

### 啟動閘門邏輯（既有程式碼）

`UpdateAutoAttackState()`（`MainForm.LiveView.cs`）定義自動攻擊啟用條件（怪物改為勾選清單，非舊版單一 ComboBox）：

```
_autoAttackEnabled =
    ckB_Start.Checked
    && cbo_LoadPathFile.SelectedIndex > 0
    && cbo_DetectMode.SelectedItem != null
    && （已勾選至少一種怪物）
```

`UpdatePrerequisitesLabel()` 將遊戲視窗、路徑、偵測、怪物等條件**視覺化**於執行區，而非僅寫入日誌。

---

## 核心資訊架構

主控台畫面分為**左操作、右監控**兩欄，對應兩種任務：**設定並啟動**、**監控運行狀態**。

```
┌─────────────────────────────────────────────────────────────────┐
│  Tab：主控台                                                     │
├──────────────────┬──────────────────────────────────────────────┤
│  左 panel1       │  右 panel2                                    │
│  (~260px)        │  (Fill)                                       │
│                  │  ┌──────────────────────────────────────────┐ │
│  ┌─────────────┐ │  │ StatusBar（固定高度 ~48px）               │ │
│  │ ① 前置狀態   │ │  │ 遊戲視窗 | 擷取 | FSM | HP/MP | 路徑進度  │ │
│  │ 遊戲視窗指示  │ │  └──────────────────────────────────────────┘ │
│  └─────────────┘ │  ┌──────────────────────────────────────────┐ │
│  ┌─────────────┐ │  │ LogPanel（textBox1，唯讀、可捲動）         │ │
│  │ ② 設定       │ │  │ 時間戳歷史訊息；錯誤行著色（Phase 1 可選）  │ │
│  │ 路徑檔       │ │  └──────────────────────────────────────────┘ │
│  │ 偵測模式     │ │                                               │
│  │ 怪物模板     │ │                                               │
│  └─────────────┘ │                                               │
│  ┌─────────────┐ │                                               │
│  │ ③ 執行       │ │                                               │
│  │ [自動打怪]   │ │                                               │
│  │ 前置條件提示  │ │                                               │
│  └─────────────┘ │                                               │
│  ┌─────────────┐ │                                               │
│  │ ④ 角色資訊   │ │                                               │
│  │ HP / MP 條  │ │                                               │
│  └─────────────┘ │                                               │
│  （進階區隱藏）   │                                               │
└──────────────────┴──────────────────────────────────────────────┘
```

| 區塊 | 職責 |
|------|------|
| ① 前置狀態 | 遊戲視窗是否找到、標題是否符合 `Config.General.GameWindowTitle` |
| ② 設定 | 路徑檔、偵測模式、怪物模板（Flow A 步驟 1–2） |
| ③ 執行 | 自動打怪勾選、啟動閘門 inline 提示 |
| ④ 角色資訊 | 即時 HP/MP（來自 `PlayerVitalsSnapshot`） |
| StatusBar | 可掃視的運行摘要，節流更新（沿用 `StatusUpdateIntervalMs = 500`） |
| LogPanel | 除錯與歷史；`MsgLog` 繼續寫入此區 |

---

## 資料源對照表

Phase 1 綁定時，各 UI 欄位應從以下來源讀取，**禁止**在 Form 內重複偵測邏輯。

| UI 欄位 | 資料來源 | 存取方式 | 更新頻率 |
|---------|----------|----------|----------|
| 遊戲視窗狀態 | `WindowFinder.TryCreateItemForWindow` | 啟動時 + 定時輪詢（5s）或 `ckB_Start` 觸發 | 事件驅動 |
| 擷取狀態 | `LiveViewManager.IsRunning` | `liveViewManager?.IsRunning` | 勾選變更時 |
| FSM 狀態 | `INavigationStateMachine.CurrentState` | `_fsm?.CurrentState` | `OnStateChanged` 或 500ms 節流 |
| 路徑進度 | `PathPlanningManager.CurrentState` | `_pathPlanningManager?.CurrentState` | `OnPathTrackingUpdated`（已有節流） |
| HP / MP 比例 | `PlayerVitalsSnapshot` | `GamePipeline.GetCurrentSnapshot().PlayerVitals` 或 `OnFrameProcessed` | ~與 vitals 偵測同頻 |
| 怪物辨識 | `GamePipeline.AutoAttackEnabled` + `MonsterCatalog` | `_gamePipeline` | 勾選／下拉變更 |
| 攻擊中 | `GamePipeline.IsAttacking` | `_gamePipeline.IsAttacking` | `OnFrameProcessed` |
| 導航讓出 | `GamePipeline.BlocksNavigationInput` | `_gamePipeline.BlocksNavigationInput` | `OnFrameProcessed` |
| 日誌訊息 | 各服務 `OnStatusMessage` / `MsgLog` | 既有事件鏈 | 即時 |

### FSM 顯示對照

| `NavigationState` | StatusBar 顯示文字 |
|-------------------|-------------------|
| `Idle` | 閒置 |
| `Moving_Horizontal` | 水平移動 |
| `Moving_Vertical` | 爬繩 |
| `Jumping` | 跳躍 |
| `Transitioning` | 過渡 |
| `Reached_Waypoint` | 抵達路點 |
| `Error` | 錯誤（語意色：警示） |

### HP/MP 顯示規則

- 資料來源：`PlayerVitalsSnapshot.HpRatio` / `MpRatio`（0.0–1.0）
- `HasFillReading == false` 時顯示 `—` 或「未校準」，不可顯示假數值
- 建議控件：`ProgressBar`（`Minimum=0, Maximum=100`）+ 百分比 `Label`
- 可選：沿用 `OverlayRenderer` 的血條配色 token（`AppearanceSettings.PlayerVitals`）

---

## 啟動閘門與 inline 提示

### 前置條件檢查清單

勾選「自動打怪」時，依序檢查並在 `lbl_Prerequisites`（新建）顯示摘要：

| # | 條件 | 檢查方式 | 失敗訊息 |
|---|------|----------|----------|
| P1 | 遊戲視窗存在 | `WindowFinder.TryCreateItemForWindow(title) != null` | 找不到遊戲視窗：{title} |
| P2 | 路徑檔已選 | `cbo_LoadPathFile.SelectedIndex > 0` | 請選擇路徑檔 |
| P3 | 偵測模式已選 | `cbo_DetectMode.SelectedItem != null` | 請選擇偵測模式 |
| P4 | 怪物模板已選 | `cbo_MonsterTemplates.SelectedIndex > 0` | 請選擇怪物模板 |
| P5 | 路徑含平台節點 | `loadedPathData` 平台節點數 > 0 | 路徑檔無平台節點，導航不會啟動 |

P1 失敗時：**取消勾選** `ckB_Start`（既有行為保留）。

P2–P5 為警告時：允許勾選（背景擷取可啟動），但 StatusBar 與 `lbl_Prerequisites` 顯示黃色警告；`UpdateAutoAttackState` 仍會使 `_autoAttackEnabled = false`。

### 提示更新時機

- `cbo_LoadPathFile` / `cbo_DetectMode` / `cbo_MonsterTemplates` 的 `SelectedIndexChanged`
- `ckB_Start.CheckedChanged`（前後）
- 定時輪詢遊戲視窗（僅在已勾選自動打怪時，間隔 5s）

---

## 右側監控區（StatusBar + LogPanel）

### StatusBar 佈局

新建 `panel_StatusBar`（`Dock = Top`，高度 48px），內含：

| 子控件 | 名稱建議 | 內容範例 |
|--------|----------|----------|
| 遊戲 | `lbl_Status_Game` | 遊戲：已連線 / 未找到 |
| 擷取 | `lbl_Status_Capture` | 擷取：運行中 / 停止 |
| FSM | `lbl_Status_Fsm` | 導航：水平移動 |
| 角色 | `lbl_Status_Vitals` | HP 85% · MP 60% |
| 路徑 | `lbl_Status_Path` | 路點 3/12 · 距離 4.2 |

語意色（與地圖編輯器對齊）：

- 正常：`Color.Gainsboro`（背景 `Color.FromArgb(50, 50, 50)`，比照 `lbl_MapStatus`）
- 警告：`Color.DarkOrange`
- 錯誤：`Color.Firebrick`

### LogPanel

- `textBox1` 改 `Dock = Fill`，父容器 `panel_Log`（`Dock = Fill`）
- `ReadOnly = true`（若尚未設定）
- `MsgLog` API **不變**；Phase 1 可選升級為 `RichTextBox` 並讓 `ShowError` 真正著色

### 訊息分流原則

| 訊息類型 | 去向 | 範例 |
|----------|------|------|
| 可掃視狀態 | StatusBar | FSM、HP、路徑進度 |
| 一次性事件 | LogPanel | 地圖載入、模板下載完成 |
| 錯誤 | StatusBar（摘要）+ LogPanel（詳情） | 找不到遊戲視窗 |
| 高頻除錯 | 僅 LogPanel，且節流 | 既有 `ReportAction` 去重 |

---

## 左側操作區重排

### Dock 順序（v1 現況）

左側以 `Dock = Top` 自上而下堆疊（實作可用絕對座標微調內部控件，主區塊不得依賴混用 Bottom 造成視覺錯序）：

```
panel1
├── groupBox_Prereq      (Dock Top)   ← 遊戲視窗狀態
├── groupBox_Settings    (Dock Top)   ← 路徑檔、偵測模式、怪物、下載
├── groupBox_Execute     (Dock Top)   ← ckB_Start + lbl_Prerequisites
├── groupBox8            (Dock Top)   ← 定時休息
├── groupBox7            (Dock Top)   ← 自動喝水（閾值％＋快捷鍵）
├── groupBox6            (Dock Top)   ← 角色資訊 + ProgressBar
└── groupBox9            (Visible=false)
```

### 控件重新命名計畫（剩餘可選）

| 現名 | 新名 | 說明 |
|------|------|------|
| `groupBox6` | `groupBox_Vitals` | 角色資訊（可選） |
| `groupBox8` | `groupBox_Rest` | 定時休息（可選） |
| `groupBox7` | `groupBox_Heal` | 自動喝水（可選；內部控件已更名） |
| `chk_AutoHealHp` / `txt_HealHpThreshold` / `txt_HealHpHotkey` | （已存在） | Phase 2 已完成 |
| `chk_AutoHealMp` / `txt_HealMpThreshold` / `txt_HealMpHotkey` | （已存在） | Phase 2 已完成 |

---

## Phase 驗收清單

### Phase 0 — 誠實性與 IA 修復 — **已通過**

**目標：** 消除假功能與標籤缺失；不新增後端邏輯。

- [x] **隱藏空殼**：`groupBox9` 設 `Visible = false`（`groupBox7` 後改接線喝水；不再當空殼）
- [x] **`groupBox8` 例外**：不定為攻擊空殼隱藏，改為「定時休息」並接線（見現況盤點偏離說明）
- [x] **補標籤**：`lbl_LoadPathFile`（路徑檔）、`lbl_DetectMode`（偵測模式）
- [x] **設定分組**：路徑檔、偵測模式、怪物、下載歸於 `groupBox_Settings`
- [x] **執行分組**：`ckB_Start` 於 `groupBox_Execute`，含 `lbl_Prerequisites`
- [x] **前置提示控件**：`lbl_Prerequisites`，預設「尚未啟動」
- [x] **啟動檢查 UI 化**：`UpdatePrerequisitesLabel()`（`MainForm.Console.cs`）
- [x] **日誌唯讀**：`textBox1.ReadOnly = true`
- [x] **Dock 整理**：左側主區塊以 Top 堆疊

**完成標準：** 新使用者不需猜測下拉選單用途；看不到 `checkBox1` 佔位文字；勾選自動打怪前能一眼看出缺什麼。

**實作檔案：** `UI/MainForm.Designer.cs`、`UI/MainForm.Console.cs`、`UI/MainForm.cs`

---

### Phase 1 — 結構化狀態 — **已通過**

**目標：** 右側 StatusBar 可掃視；角色資訊有真實數值。

- [x] **StatusBar 控件組**：`panel_StatusBar` + 5 個 `lbl_Status_*`
- [x] **FSM 綁定**：訂閱 `_fsm.OnStateChanged` → `RefreshStatusBar`
- [x] **路徑進度綁定**：`RefreshStatusBarPath`／StatusBar 摘要
- [x] **擷取狀態**：`lbl_Status_Capture` 反映 `liveViewManager.IsRunning`（含小休狀態）
- [x] **HP/MP ProgressBar**：`prg_Hp`、`prg_Mp` + 百分比 Label
- [x] **Vitals 綁定**：`OnConsoleFrameProcessed` 節流更新
- [x] **遊戲視窗輪詢**：Timer 5s 更新前置與 StatusBar
- [x] **訊息分流**：高頻狀態走 StatusBar；日誌保留事件型訊息（RichTextBox 著色仍為可選）

**完成標準：** 不捲動日誌即可回答「現在在做什麼、角色血量多少、第幾個路點」。

**實作檔案：** `UI/MainForm.Designer.cs`、`UI/MainForm.Console.cs`、`UI/MainForm.LiveView.cs`

---

### Phase 2 — 進階功能與架構（進行中）

**目標：** 實作收納的進階區；降低 God Form 耦合。邊界見「功能歸屬對照」。

- [x] **自動喝水 UI＋後端**：`groupBox7` 可見；`HealHp/Mp` 閾值％＋快捷鍵；`AutoHealCoordinator` 於自動打怪時節流按鍵
- [ ] **進階收納**：低優先；喝水已直接常駐主控台（參數少）
- [x] **輔助技能**：`groupBox9` 可見；5 槽啟用／秒數／快捷鍵；間隔抖動 ±10% 程式內建
- [x] **攻擊輪轉**：`groupBox_Attack`；主攻鍵＋最多 3 冷卻技；鎖定怪時優先按就緒技
- [ ] **`groupBox8` rename（可選）**：`groupBox_Rest`，與攻擊設定脫鉤命名
- [x] **ConsolePresenter（切片）**：`Application/Console/*` 聚合 `ConsoleViewState`；`MainForm.Console` 僅採集 Input＋綁定控件（尚未 DI）
- [x] **設定持久化（喝水／補助）**：寫入 `AutoFarm`（`heal*`／`buffSkills`）；休息參數同區塊

**完成標準：** 進階功能與主流程解耦；主控台 partial 可獨立測試綁定邏輯。

---

## 功能歸屬對照（Phase 2 邊界規格）

**判定原則：** 主控台只放「啟動決策＋可掃視狀態」；路徑編輯只放「空間／拓撲編輯」；即時顯示只放「視覺證據」。變更頻率低、誤觸成本高、或需大畫布者，不得擠進主控台主視線。

```
任務問句                              應落地分頁
─────────────────────                 ──────────
能不能開？缺什麼？現在卡哪？            主控台
這條路怎麼畫／哪裡斷圖？               路徑編輯
框有沒有對、打的是不是這隻？           即時顯示
喝水閾值／技能熱鍵（未就緒）           隱藏，或未來「營運設定」
```

### 總表：應留／應搬／應藏

| 功能（現況控件） | 處置 | 歸屬 | 理由 |
|------------------|------|------|------|
| 遊戲視窗狀態（`groupBox_Prereq`） | **留** | 主控台 | 啟動閘門 P0 |
| 路徑檔選擇（`cbo_LoadPathFile`） | **留** | 主控台 | 執行前必選；檔案內容改到路徑編輯 |
| 偵測模式（`cbo_DetectMode`） | **留** | 主控台 | 影響能否開與打什麼 |
| 怪物勾選／下載（`clb_*`／`btn_DownloadMonster`） | **留** | 主控台 | Flow A 設定核心 |
| 自動打怪＋前置提示（`ckB_Start`／`lbl_Prerequisites`） | **留** | 主控台 | 唯一執行開關 |
| StatusBar（遊戲／擷取／FSM／HPMP／路點） | **留** | 主控台 | 儀表板 SSOT；不可用日誌取代 |
| 角色資訊條（`groupBox6`／`prg_Hp`／`prg_Mp`） | **留** | 主控台 | 固定 vitals；與 StatusBar 互補 |
| 定時休息（`groupBox8`） | **留** | 主控台 | 與長開機運營強相關、參數少 |
| 自動喝水（`groupBox7`） | **留** | 主控台 | 閾值％＋快捷鍵；遊戲內先綁藥水 |
| 日誌（`textBox1`） | **留** | 主控台（次要） | 歷史事件；不得承擔高頻狀態 |
| 輔助技能空殼（`groupBox9`） | **留** | 主控台營運列 | 5 槽週期 Buff；與喝水同列下方 |
| 路徑新建／保存／檔案列表（`groupBox1`） | **留** | 路徑編輯 | 地圖 SSOT 管理 |
| 標記模式／圖層／屬性／ManualEdge | **留** | 路徑編輯 | 幾何與行為邊編輯 |
| 驗證層／連通回饋 | **留** | 路徑編輯 | 對應 map-editor 憲章 |
| 小地圖畫布（`pictureBoxMinimap`） | **留** | 路徑編輯 | 大畫布工作區 |
| 即時畫面（`pictureBoxLiveView`） | **留** | 即時顯示 | 視覺驗收；主控台只摘要 |
| Overlay（怪物框、路徑、ROI） | **留** | 即時顯示 | 證據在像素，不在儀表板文字 |
| `MinimapViewer` 浮窗 | **留** | 跨分頁（現有） | 運行輔助；非主控台排版一員 |

### 跨分頁契約（不可破壞）

| 資料／事件 | 生產者 | 消費者 | 規則 |
|------------|--------|--------|------|
| 地圖 JSON | 路徑編輯儲存 | 主控台 `cbo_LoadPathFile` | 儲存後必須 `SyncMapFileDropdowns` |
| 自動打怪運行中 | 主控台 | 三頁共用 pipeline | 切 Tab **不中斷**擷取與 FSM |
| 路徑進度／FSM | Pipeline | 主控台 StatusBar | 高頻只寫 StatusBar，不洗日誌 |
| 疊加可視化 | Pipeline／Overlay | 即時顯示 | 主控台不複製畫布控件 |

### 何時才加第四分頁

**預設不加。** 僅當下列條件同時成立才考慮「營運設定」（或同名）新 Tab：

1. 自動喝水＋輔助技能＋（可選）攻擊細節均已後端接線且通過驗收  
2. 主控台在顯示這些後需垂直捲動才能看到 StatusBar／執行區  
3. 使用者每場都需調這些參數（高頻），而非偶發設定  

在那之前：Phase 2 用主控台 **進階折疊**（`chk_ConsoleAdvanced`）即可，成本低於新分頁與 God Form 再膨脹。

### 明確禁止

- 在主控台放路徑幾何編輯控件（與路徑編輯 SSOT 衝突）  
- 在主控台複製即時顯示畫布（雙重真相＋效能）  
- 為「以後可能要用」預先露出空 GroupBox  
- 因版面空而把診斷／除錯工具塞滿主控台主視線  

---

## 與其他分頁的互動

| 場景 | 行為 |
|------|------|
| 自動打怪已啟動，切換至路徑編輯 | 背景擷取與 pipeline **不中斷**（既有設計） |
| 自動打怪已啟動，切換至即時顯示 | 同上；StatusBar 在主控台仍可見 |
| 地圖編輯器儲存路徑 | `SyncMapFileDropdowns` 更新 `cbo_LoadPathFile`；觸發 `UpdatePrerequisitesLabel` |
| `MinimapViewer` 浮窗 | 非主控台控件；啟動自動打怪時自動 `Show()`（既有） |

---

## 架構備註（Phase 2 目標）

現況 `MainForm` 直接持有 15+ 服務，主控台狀態散落在 `textBox1`、`ReportAction`、`OnPathTrackingUpdated`。

建議的閉環方向（**Presenter 切片已落地；DI 仍為後續**）：

```
GamePipeline / PathPlanningManager / LiveViewManager / WindowFinder（由 Form 採集）
                    ↓
            ConsoleStatusInput
                    ↓
            ConsolePresenter.Build
                    ↓
         ConsoleViewState（immutable）
                    ↓
         MainForm.Console.BindConsoleViewState
```

`ConsoleViewState` 單次刷新已包含：遊戲視窗、擷取、FSM、HP/MP、路徑進度、前置條件文案與色階。

---

## 版本優先序

```
Phase 0（誠實性 + IA）           ✅ 已通過
        ↓
Phase 1（StatusBar + Vitals）    ✅ 已通過
        ↓
Phase 2（進階功能 + Presenter）  → 待業務規格與後端就緒；遵守功能歸屬對照
        ↓
Phase 3+（主題／暗色）           → 低優先
```

**不建議**在 Phase 2 功能接線完成前進行 WinForms 主題美化——會放大版面重排成本，且無法解決空殼／耦合問題。

---

## 結論

主控台的問題本質是**資訊架構與誠實性**，不是視覺皮膚。Phase 0／1 已讓主控台成為可用的操作儀表板；**Phase 2 以「功能歸屬對照」為邊界**——優先接線真實功能與可選 Presenter，不新增分頁、不預先露出空殼。
