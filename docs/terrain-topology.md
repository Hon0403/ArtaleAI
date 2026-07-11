# 地形分類與 MapData 對照

本文件統整「遊戲地形語意」與 `MapData` 持久化結構的對應關係。

系統將地形分為四類：**可走地形**、**跳躍型地形**、**垂直通道**、**特殊互動點**。  
其中 `PolylinePlatforms` 與 `Ropes` 儲存**幾何資料**；`ManualEdgeAnchors` 儲存**跨越行為**（行為錨點，不是地形幾何的一部分）。  
`Nodes` / `Edges` 由 `BuildHTopology()` 在 runtime 自動推導，**不直接持久化**——與「新格式唯一真相」一致：JSON 只保留幾何與行為宣告，拓撲由程式再生。

相關文件：[`navigation-recovery.md`](navigation-recovery.md)（執行期擾動與救援契約）。

---

## 一、地形四大類

```
┌──────────────────────────────────────────────────────────────┐
│  可走地形       沿表面連續移動（Walk）                         │
│  跳躍型地形     無法單靠走過去，需跨越斷層／階差（語意分類）      │
│  垂直通道       沿繩或梯攀爬（ClimbUp / ClimbDown）            │
│  特殊互動點     主動改變移動方式（JumpDown、Teleport 等）       │
└──────────────────────────────────────────────────────────────┘
      ↓ 幾何 SSOT                    ↓ 行為錨點（非幾何）
 PolylinePlatforms              ManualEdgeAnchors
      Ropes                     （+ ActionType）
```

**三層分離：** 幾何（能站在哪）→ 行為宣告（怎麼跨過去）→ runtime 拓撲（節點／有向邊）。

---

### 1. 可走地形

角色可直接沿表面移動，**不需跳躍**。

| 子類 | 說明 |
|------|------|
| **平行平台** | 水平地面 |
| **斜坡平台** | 折線或微斜面；只要能連續走過，皆視為可走 |

**與階梯的區分：** 若階梯在視覺上是**連續可走的折線**（角色能一路 Walk 過去），仍歸**可走地形**，標在 `PolylinePlatforms`。  
只有當階梯在玩法上必須**一階一階跨越**、無法連續 Walk 時，才歸**跳躍型地形**並以 `ManualEdgeAnchors` 標行為。

**執行層對應：** `NavigationActionType.Walk`  
**拓撲生成：** 同一 `PolylinePlatform` 上相鄰切分節點之間自動建立雙向 Walk 邊。

---

### 2. 跳躍型地形

**語意定義：** 角色不能單靠走路通過，必須**跨越**斷層、空隙或階差的地形類型。  
**注意：** 「跳躍型地形」描述的是地圖語意，**不等於**某個按鍵；實際怎麼過去由邊上的 `ActionType`（執行行為）決定。

| 子類（地形語意） | 說明 |
|------------------|------|
| **上下斷層** | 兩平台間有落差或空隙 |
| **階梯式斷層** | 需逐階跨越；非連續可走的折線階梯 |
| **跳躍落差地形** | 跳不準會掉進縫隙；實戰上屬斷層的一種表現 |

**常見執行行為（邊層級，非地形本身）：**

| 執行行為 | 用途 |
|----------|------|
| `Jump` | 一般跳躍跨越 |
| `SideJump` | 側向跳躍跨越 |
| `JumpDown` | 主動下跳（亦列於特殊互動點；見 §4） |

**標記方式：** `ManualEdgeAnchors`——在平台幾何上宣告「從 A 點到 B 點用哪種動作跨越」，由 `ResolveManualEdgeAnchors()` 轉成 runtime 有向邊。

---

### 3. 垂直通道（繩／梯）

不靠走、不靠跳，靠**攀爬**改變高度。  
實作上繩索與梯子**共用** `Ropes` 欄位；文件統稱**垂直通道（繩／梯）**，梯子無需另開獨立欄位。

| 子類 | 說明 |
|------|------|
| **繩索** | 垂直或近垂直繩段 |
| **梯子** | 與繩索同類；幾何與資料結構皆用 `Ropes` |

**執行層對應：** `ClimbUp`、`ClimbDown`  
**標記方式：** `Ropes`（`[ropeX, topY, bottomY]`）→ 拓撲生成時投影至平台並建立爬繩邊；邊 metadata 含 `ropeX:`。

---

### 4. 特殊互動點

會**主動改變**移動方式或位置的點，不是單純沿幾何表面延伸。

| 子類 | 說明 |
|------|------|
| **JumpDown** | 主動下跳至較低平台（地形上常接在斷層邊緣） |
| **Teleport** | 傳送 |
| **其他特殊跳點／傳送點** | 依遊戲機制擴充 |

**執行層對應：** `JumpDown`、`Teleport`  
**標記方式：** 同樣使用 `ManualEdgeAnchors`（行為錨點，非幾何）。

---

## 二、MapData 欄位對照

持久化 SSOT 為 `Models/Map/MapData.cs`。

| 欄位 | 層級 | 角色 |
|------|------|------|
| `PolylinePlatforms` | 幾何 | 可走平台／斜坡折線 SSOT |
| `Ropes` | 幾何 | 垂直通道（繩／梯）段幾何 |
| `ManualEdgeAnchors` | 行為 | 跨平台動作宣告；**不屬於**平台或繩索幾何本身 |
| `SafeZones`、`RestrictedZones` | 策略 | 安全區／禁制區；不改拓撲 |
| `Nodes` | runtime | `BuildHTopology()` 推導的 `n_v_*` 虛擬節點（`[JsonIgnore]`） |
| `Edges` | runtime | Walk（自動）+ 錨點解析邊 + Climb（繩／梯）（`[JsonIgnore]`） |

### ManualEdgeAnchor 結構摘要

```json
{
  "FromPlatformId": "plat_0",
  "FromX": 189.9,
  "FromY": 162.5,
  "ToPlatformId": "plat_2",
  "ToX": 189.9,
  "ToY": 157.1,
  "ActionType": 2
}
```

- 語意：在**某平台幾何上的某座標**宣告一條**有向跨越行為**。
- **不綁定** runtime `nodeId`；由 `ResolveManualEdgeAnchors()` 解析後寫入 `Edges`。
- **單向邊**：只建立錨點宣告的方向；反向通行需另標一條錨點。

### NavigationActionType 對照

| 值 | 枚舉 | 地形語意類 | 備註 |
|----|------|------------|------|
| 1 | `Walk` | 可走地形 | 自動邊 |
| 2 | `Jump` | 跳躍型 | 執行行為 |
| 3 | `SideJump` | 跳躍型 | 執行行為 |
| 4 | `JumpDown` | 跳躍型／特殊互動 | 執行行為 |
| 5 | `Teleport` | 特殊互動 | 執行行為 |
| 6 | `ClimbUp` | 垂直通道 | 繩／梯 |
| 7 | `ClimbDown` | 垂直通道 | 繩／梯 |

---

## 三、拓撲生成流程（runtime）

`MapGenerationService.BuildHTopology(mapData)` 具冪等性，重複呼叫結果一致：

| 階段 | 內容 |
|------|------|
| **清理** | 移除所有 `n_v_` 開頭的虛擬節點與相關邊 |
| **A／B** | `PolylinePlatforms` → 弧長切點 → 節點 → 相鄰 **Walk** 雙向邊 |
| **C** | `Ropes` → 投影切點 + **ClimbUp／ClimbDown** 邊 |
| **D** | `ManualEdgeAnchors` → **Jump／JumpDown／Teleport** 等有向邊 |

切點來源：折線頂點、繩索投影、手動邊錨點。  
合併時以弧長與座標雙重容差去重，避免過密 waypoint（見載入時 `LogMapDataIssues()` 警告）。

---

## 四、交叉路口

**交叉路口不是第五種地形**，而是拓撲上「一個節點連出多條邊」的分支點。

- 分類上無需另開類別。
- 生成時視為**多分支節點**（例如同一切點同時接 Walk 與 Jump）。
- 路徑規劃（A*）在節點處依邊的 `ActionType` 選擇下一段行為。

---

## 五、連通性與標圖檢查清單

標圖時除幾何正確外，應確認行為邊是否構成預期的**可達路徑子圖**：

| 檢查項 | 說明 |
|--------|------|
| 單向 Jump 是否足夠 | 僅標上台、未標下台 → 上層無法 A* 規劃至下層平台 |
| 同平台 Walk 是否連續 | 折線頂點與錨點切分後，相鄰節點應有 Walk 邊 |
| 繩／梯是否投影成功 | 繩 X 需落在平台折線附近，否則無 Climb 邊 |
| 策略區 | `SafeZones` / `RestrictedZones` 不改變 `Edges`，不影響 A* |

**實例：** 怪物在底層 `plat_0` 巡邏、玩家在 `plat_1` 攻擊，且需 bot 沿底層規劃時，必須存在從上層到底層的 `ManualEdgeAnchors`（例如 `JumpDown`）。否則兩層在圖論上屬**不相連的兩個可達子圖**。  
→ 完整分析見 [`attack-map-connectivity-gap.md`](attack-map-connectivity-gap.md)（ATTACK 地圖，待探討）。

---

## 六、與導航執行的邊界

| 層級 | 職責 |
|------|------|
| **MapData（本文件）** | 定義「地圖上能怎麼走」的幾何 + 行為 SSOT |
| **PathPlanningTracker** | A* 選路、`SelectRandomPhysicalTarget` 隨機終點 |
| **NavigationExecutor** | 將邊的 `ActionType` 轉為按鍵與移動 |
| **navigation-recovery.md** | 執行失敗、停滯、熔斷等恢復契約 |

地形資料只解決「圖上有沒有路」；**會不會選那條路**屬於巡邏策略與整合層（例如攻擊模式候選過濾），不在 MapData 內。
