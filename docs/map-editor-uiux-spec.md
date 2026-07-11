# Map Editor UI/UX 設計憲章

**狀態：v1 定稿** — 本文件為 Map Editor 重構的正式設計憲章；後續變更以 Phase 驗收為準，架構原則非必要不修改。

本文件定義地圖編輯器在新版 `MapData` 結構下的 UI/UX 目標與驗收標準。

**資料契約與拓撲語意**見：

- [`terrain-topology.md`](terrain-topology.md) — 三層分離（幾何 → 行為 → runtime 拓撲）
- [`navigation-recovery.md`](navigation-recovery.md) — 執行期救援與驗收契約
- [`attack-map-connectivity-gap.md`](attack-map-connectivity-gap.md) — 連通缺口實例（行為邊未標全）

**核心前提：** 持久化 SSOT 為 `PolylinePlatforms`、`Ropes`、`ManualEdgeAnchors`；`Nodes` / `Edges` 由 `MapGenerationService.BuildHTopology()` 在 runtime 推導（`[JsonIgnore]`）。編輯器不以節點／邊為主要編輯對象，而以「幾何標記 + 行為錨點 + 拓撲預覽 + 驗證回饋」為核心流程。

**硬性約束 — 拓撲預覽不等於可編輯對象：** runtime `Nodes` / `Edges` **僅供預覽與驗證**，不允許在畫布或屬性面板直接新增、拖曳、刪除或寫回 JSON。任何拓撲變更必須透過修改 `PolylinePlatforms`、`Ropes`、`ManualEdgeAnchors` 間接達成，以避免 runtime 成為第二份 SSOT。

---

## 設計目標

新版編輯器的首要目標：**不再擴大資料模型**，而是把 UI、驗證、連通性回饋做成閉環，讓使用者**不需打開 JSON** 就能看懂哪裡缺邊、哪裡斷圖。

具體目標：

| 目標 | 說明 |
|------|------|
| 單一 SSOT | 僅編輯 `PolylinePlatforms`、`Ropes`、`ManualEdgeAnchors`；避免 `Nodes` / `Edges` 與 JSON 雙重真相 |
| Progressive disclosure | 高頻幾何操作與低頻行為邊分層；`ManualEdge` 為進階模式 |
| Polyline-aware 互動 | segment hover、命中優先順序、刪除前高亮；解決舊版「只會畫水平平台」的誤刪／誤選 |
| 拓撲可視化 | 即時預覽 Walk / Climb / Manual 邊，確認自動推導是否符合預期 |
| 連通性回饋 | 主動提示斷圖、投影失敗、孤立子圖（對應 ATTACK 類「少一條下台邊」問題） |
| 單向 ManualEdge | Jump / JumpDown 等**不自動補反向邊**；反向需使用者明確再標 |

---

## 範圍界定

### 本重構納入

- 主／進階編輯模式與 polyline-aware 畫布互動
- 右側屬性面板（可驗收）
- Topology Preview、Validation、狀態／警告區（職責分離）
- Undo / Redo、Dirty state、刪除前確認（最低編輯器保障）
- Zoom、游標座標、Snap 指示（見下方驗收項）

### 明確 Out of Scope（v1 重構）

以下欄位在 `MapData` 已存在，但**本輪重構不實作 UI**；待幾何／行為／拓撲閉環穩定後，以「區域標記」進階模式納入下一階段：

| 欄位 | 角色 | 備註 |
|------|------|------|
| `SafeZones` | 策略區域 | 不改拓撲；見 [`terrain-topology.md`](terrain-topology.md) §二 |
| `RestrictedZones` | 策略區域 | 同上 |

避免「模型有、文件沒、UI 沒」的灰區：欄位保留於 `MapData`，編輯器 v1 不暴露編輯介面。

---

## ActionType 對照表

完整語意見 [`terrain-topology.md`](terrain-topology.md) §一、§二。編輯器實作時以此表為準：

| 值 | `NavigationActionType` | 建立方式 | 編輯器 UI | 備註 |
|----|------------------------|----------|-----------|------|
| 1 | `Walk` | `BuildHTopology` 自動 | 不可手標 | 同平台相鄰切點雙向邊 |
| 2 | `Jump` | `ManualEdgeAnchors` | ManualEdge 下拉 | **單向**；不自動補反向 |
| 3 | `SideJump` | `ManualEdgeAnchors` | ManualEdge 下拉 | **單向** |
| 4 | `JumpDown` | `ManualEdgeAnchors` | ManualEdge 下拉 | **單向** |
| 5 | `Teleport` | `ManualEdgeAnchors` | ManualEdge 下拉 | **單向** |
| 6 | `ClimbUp` | `Ropes` 推導 | 唯讀預覽 | 繩索投影後自動建立 |
| 7 | `ClimbDown` | `Ropes` 推導 | 唯讀預覽 | 同上 |

**ManualEdge 下拉應包含：** `Jump`、`SideJump`、`JumpDown`、`Teleport`（不含 `Walk`、`ClimbUp`、`ClimbDown`）。

**動作類型設定區（ActionType picker）** 僅在 `ManualEdge` 模式且進階模式啟用時顯示；其他模式隱藏或 disabled。

---

## 核心資訊架構

編輯器畫面分為四區，對應三種任務：**建立幾何**、**編輯屬性**、**檢查拓撲**。

```
┌─────────────────────────────────────────────────────────────┐
│  工具列（模式切換、Undo/Redo、儲存、圖層開關）                  │
├──────────────────────────────────┬──────────────────────────┤
│                                  │  右側屬性面板              │
│  主畫布                           │  （選取物件詳情 + 可編輯欄位）│
│  minimap + 幾何 + 拓撲預覽層       │                          │
│                                  │                          │
├──────────────────────────────────┴──────────────────────────┤
│  狀態列 + 警告區（驗證彙總、可點擊跳轉）                         │
└─────────────────────────────────────────────────────────────┘
```

| 區塊 | 職責 |
|------|------|
| 工具列 | 模式切換、Undo/Redo、Dirty 指示、圖層 toggle |
| 主畫布 | 幾何繪製、選取、hover、Topology Preview 視覺層 |
| 右側屬性面板 | 持久化欄位編輯 + runtime 唯讀解析結果 |
| 狀態／警告區 | Validation 彙總、連通性摘要、點擊跳轉至問題物件 |

---

## 三層回饋分工（Preview / Validation / 警告區）

三者連動但職責不同，不可混為一個「檢查模式」各做各的：

| 層級 | 名稱 | 職責 | 使用者問的問題 |
|------|------|------|----------------|
| 視覺層 | **Topology Preview** | 在畫布上**看見** runtime `Nodes`、`Edges` | 「程式推導出什麼？」 |
| 邏輯層 | **Validation** | **判斷**資料是否可成功生成拓撲、是否連通 | 「哪裡有問題？」 |
| 呈現層 | **狀態／警告區** | **彙總** Validation 結果、支援點擊跳轉 | 「我該先修哪一個？」 |

**資料流：**

```
MapData 變更 → BuildHTopology() → Validation 執行
                                        ↓
              Topology Preview ←── 結果 ──→ 警告區列表
                     ↑                          ↓
              圖層 toggle              點擊 → 選取物件 + 屬性面板
```

---

## 工具列模式

### 主模式（預設可見）

| 模式 | 用途 | 對應資料 |
|------|------|----------|
| `Select` | 選取、檢視、微調 | 全部物件 |
| `Platform` | 建立／編輯 `PolylinePlatforms` | 幾何 |
| `Rope` | 建立／編輯 `Ropes` | 幾何 |
| `Delete` | 刪除平台、繩索、行為錨點 | 全部物件 |

### 進階模式（Advanced 收納）

| 模式 | 用途 | 對應資料 |
|------|------|----------|
| `ManualEdge` | 建立 `ManualEdgeAnchors` | 行為錨點 |
| 圖層 toggle | 開關 Preview 各層 | runtime 視覺 |

Validation 不是獨立「模式」，而是每次 `BuildHTopology()` 後自動執行，結果進警告區。

### 模式切換原則

- `Platform`、`Rope` 放第一層；`ManualEdge` 放進階區塊
- `Select` 可搭配 hover 命中，簡單檢視不必反覆切工具
- 切換模式若使選取物件不可編輯，屬性面板保留**只讀**上下文

---

## 主畫布互動

### Platform 模式

- 逐點建立 `PolylinePlatform`，每次新增點更新預覽折線
- segment-level hover：顯示目前 segment index 與 projection point
- 完成後觸發 `BuildHTopology()`，同步 Walk 切點

### Rope 模式

- 上下兩端點建立，存為 `[ropeX, topY, bottomY]`
- 建立後重建拓撲，預覽 `ClimbUp` / `ClimbDown`

### ManualEdge 模式

- 起點平台錨點 → 終點平台錨點 → 選擇 `ActionType`
- 方向箭頭或連線預覽，強化「有向邊」語意
- **單向 Jump / JumpDown / SideJump / Teleport 不自動補反向**

### Select 模式

- 點選高亮 + 右側屬性面板載入
- 平台：整條 polyline 或 segment + projection point
- ManualEdge：起點、終點、`ActionType`、解析狀態

### 視圖操作與座標 Snap（驗收）

- **Zoom**：支援縮放（沿用 `ZoomScale` 或等效機制）
- **游標座標**：狀態列顯示畫布座標，利於對齊與除錯

polyline-aware 體驗的 Snap 驗收項（Phase 1 納入）：

- [ ] **平台點選 Snap indicator**：`Platform` 模式新增頂點時，顯示即將落點的視覺指示（十字、圓點或預覽點）
- [ ] **Rope 投影點**：建立或 hover `Rope` 時，顯示上下平台投影點與 `ropeX` 對齊線
- [ ] **ManualEdge 錨點投影點**：選取起點／終點時，顯示平台上的投影點與吸附位置
- [ ] **Segment index**：點擊或 hover 平台 segment 時，狀態列或畫布標註顯示當前 `segment index`

---

## 右側屬性面板（驗收清單）

屬性面板是**目前最大落差**，也是 v1 最高優先實作項。目標：使用者不看 JSON 即可理解物件狀態與拓撲影響。

### 無選取狀態

- [ ] 顯示目前模式與操作提示
- [ ] 顯示地圖摘要：平台數、繩索數、ManualEdge 數、節點／邊數（runtime）
- [ ] 顯示最近一次 Validation 摘要（警告數、連通子圖數）
- [ ] Dirty state 指示（未儲存變更）

### 選取 Platform 時

- [ ] `Id`（可編輯，變更時檢查唯一性）
- [ ] `Points[]` 列表（X、Y；可刪點、插點）
- [ ] 目前 segment index（若有 hover／選段）
- [ ] 幾何摘要：點數、總長度、過短 segment 警告
- [ ] 唯讀：此平台衍生的 runtime 節點數、Walk 邊數
- [ ] 相依警告：引用此 `Id` 的 `ManualEdgeAnchors` 列表

### 選取 Rope 時

- [ ] `ropeX`、`topY`、`bottomY`（可編輯）
- [ ] 唯讀：上下平台投影是否成功
- [ ] 唯讀：對應 `ClimbUp` / `ClimbDown` 邊是否建立

### 選取 ManualEdgeAnchor 時

- [ ] `FromPlatformId` / `ToPlatformId`、`FromX/Y`、`ToX/Y`、`ActionType`（可編輯）
- [ ] 唯讀：解析到的 runtime `FromNodeId` / `ToNodeId`
- [ ] 唯讀：解析成功／失敗原因
- [ ] 唯讀：反向邊是否存在（提示「僅單向」或「已有反向」）

### 面板互動原則

- 可編輯欄位僅限持久化契約；runtime `nodeId` 唯讀顯示
- 欄位變更影響拓撲時：debounce 重建或提供 `Rebuild Topology` 按鈕
- 警告項可從面板連結至畫布選取

---

## Topology Preview（視覺層）

### 預覽內容

- runtime `Nodes`
- Walk / ClimbUp / ClimbDown / Manual 解析邊（Jump、SideJump、JumpDown、Teleport）

### 圖層 toggle

- `Show Platforms` / `Show Ropes` / `Show Manual Anchors`
- `Show Nodes` / `Show Edges`
- `Show Validation Overlays`（孤立子圖、斷開邊界等疊加層）

### 視覺規則

| 邊類型 | 樣式建議 |
|--------|----------|
| Walk | 中性色、低彩度 |
| Climb | 固定垂直通道色（如 Cyan） |
| Jump / SideJump / JumpDown / Teleport | 不同線型或箭頭 |
| 錯誤／未解析 | 警示色 + 標註 |

Preview **不是編輯主體**；使用者先標幾何與行為，再透過預覽確認推導結果。

**不可編輯約束（重申）：** `Show Nodes` / `Show Edges` 圖層上的物件不可被選取後修改、拖曳或刪除；點擊 runtime 節點或邊時，僅顯示唯讀資訊（或引導至對應的持久化來源），不得提供寫入 `MapData.Nodes` / `MapData.Edges` 的路徑。

---

## Validation（邏輯層）

每次 `BuildHTopology()` 後執行（或 debounce 觸發）。至少涵蓋：

| 代碼 | 檢查項 | 嚴重度 |
|------|--------|--------|
| V-Rope | Rope 未能投影到上下平台 | Error |
| V-Anchor-Id | `ManualEdgeAnchor` 引用不存在的 `PlatformId` | Error |
| V-Anchor-Resolve | 錨點解析失敗，無對應切點或 node | Error |
| V-Segment | polyline segment 過短或點過密 | Warning |
| V-Connectivity | 存在多個不相連可達子圖 | Warning |
| V-Orphan | 孤立平台（無任何邊連接） | Warning |
| V-OneWay | 偵測到僅單向可達的路徑斷點（如僅上台無下台） | Info |

連通性檢查應能具體指出：**哪些平台群組互不可達**，而非只顯示「A* 可能失敗」。參考 [`attack-map-connectivity-gap.md`](attack-map-connectivity-gap.md)。

---

## 狀態／警告區（呈現層）

- 集中顯示 Validation 結果，按嚴重度排序
- 每則警告可**點擊跳轉**：畫布選取對應物件 + 屬性面板載入
- 狀態列常駐：游標座標、目前模式、Dirty 指示、子圖連通摘要

---

## 刪除流程

### 刪除規則

| 物件 | 行為 |
|------|------|
| Platform | 刪除整條；移除或警告依賴其 `Id` 的 `ManualEdgeAnchors` |
| Rope | 刪除後重建拓撲 |
| ManualEdgeAnchor | 僅刪除該有向錨點 |

### 刪除互動

- hover 高亮將刪除的物件
- polyline segment 級命中，避免誤刪
- **刪除前確認**（平台、含相依 ManualEdge 時必須確認）
- 刪除後若連通性惡化，警告區立即更新

---

## 選取流程

### 命中優先順序（重疊時）

1. 錨點或控制點
2. `ManualEdgeAnchor`
3. `Rope`
4. `PolylinePlatform` segment
5. runtime 節點或邊（預覽層，通常不直接編輯）

### 選取後行為

- 畫布高亮 → 屬性面板載入 → 狀態列摘要 → Validation 關聯項標記

---

## 編輯器基礎保障（Undo / Dirty / 確認）

地圖編輯為高風險操作，v1 必須具備：

| 功能 | 最低要求 |
|------|----------|
| **Undo / Redo** | 支援平台／繩索／ManualEdge 的建立、修改、刪除；至少 20 步 |
| **Dirty state** | 標題或狀態列顯示未儲存；關閉／載入新檔前提示 |
| **刪除確認** | 刪除 Platform 或含相依錨點時彈出確認 |
| **儲存** | 明確儲存動作；僅持久化 SSOT 欄位 |

---

## 版本優先序

依**使用者可感知價值**與**閉環完整度**排序：

### Phase 1 — 屬性面板與選取閉環（最高優先）

1. 右側屬性面板（含驗收清單各狀態）
2. Select 流程與命中優先順序
3. 選取 ↔ 畫布高亮 ↔ 面板欄位雙向同步
4. 視圖操作與座標 Snap 驗收項（平台落點、投影點、segment index）

**完成標準：** 選取任一 Platform / Rope / ManualEdge，不看 JSON 即可讀懂其幾何、解析狀態與相依警告；Snap 指示讓 polyline 操作可預期。

### Phase 2 — Topology Preview + Validation

1. 圖層 toggle 與邊類型視覺區分
2. Validation 邏輯層（含連通性子圖分析）
3. 狀態／警告區 + 點擊跳轉

**完成標準：** 開啟 ATTACK 類地圖時，編輯器能明確提示「上台邊存在但缺少下台邊」或等效連通缺口，並可跳轉至相關 ManualEdge。

### Phase 3 — 編輯器耐久性

1. Undo / Redo
2. Dirty state 與儲存提示
3. Zoom、游標座標
4. 刪除前確認

**完成標準：** 可持續編輯多張地圖而不因誤操作損失工作。

### 已具備基礎（維護即可）

- 主模式四件套 + ManualEdge 進階收納
- Polyline-aware 建立／刪除命中
- 即時 `BuildHTopology()` 呼叫
- 單向 ManualEdge 語意

---

## 後續擴充（Phase 4+）

- `SafeZones` / `RestrictedZones` 區域標記 UI
- 多選與批次編輯
- segment 拆段／合併
- 自動建議 ManualEdge（連通缺口修復提示）
- 連通性分析專用視圖
- 快捷鍵與右鍵 context menu
- topology rebuild 局部重建

---

## 結論

資料模型已足以支撐本憲章；**下一步不在擴大 `MapData`，而在閉環**：

> 屬性面板 + 驗證回饋 + 連通性可視化 → 讓使用者直接看出「少了一條下台邊」這類問題。

實作時以 Phase 1 → 2 → 3 為序；每 Phase 以本文件驗收清單為合併條件，再進入下一 Phase。架構層面本憲章已定稿，剩餘工作為按 Phase 實作與驗收。
