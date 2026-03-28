# ArtaleAI Clean Architecture 報告（降噪現況版）

## 目的

此文件為「架構藍圖 + 現況進度」混合版：  
保留重構方向，刪除過時行號與舊結構描述，並對齊目前 P2 已完成成果。

---

## 一、現況概覽（As-Is）

### 已落地（P2 完成）

1. **GamePipeline 協調器化**
   - `MainForm` 每幀主流程已委派 `GamePipeline.ProcessFrame(...)`。
   - 以 `OnFrameProcessed` / `FrameProcessingResult` 回傳偵測快照。
   - Pipeline 內共享清單維持 lock 語意（血條、怪物、小地圖標記）。

2. **MapFileManager 去 UI 依賴**
   - 建構子為 `MapFileManager(MapEditor mapEditor)`。
   - 透過事件對外：`MapLoaded`、`MapSaved`、`StatusMessage`、`ErrorMessage`、`FileListChanged`。
   - `ComboBox` 操作已回歸 `MainForm`。

3. **導航穩定性收斂**
   - `NavigationStateMachine.NotifyTargetReached()` 已加入 SSOT gate。
   - `PathPlanningTracker.CurrentNavigationEdge` 優先採 `PlannedPathNodes`（nodeId）解析。
   - `OnPathTrackingUpdated` 已收斂為「路徑追蹤狀態處理」，不再作為小地圖資料來源。

4. **MainForm 小地圖資料來源收斂（第二步）**
   - 移除多處手動覆寫 `_currentMinimapBoxes` 的流程。
   - 改為在定位到 minimap rect 時，由 `GamePipeline.SetMinimapBoxes(...)` 更新。
   - 每幀不再由 `MainForm` 反向覆寫 pipeline 的 minimap boxes。

### 仍有技術債（但已可控）

- `MainForm` 仍偏大，尚未完成完整 UI/應用邏輯切割。
- `GameVisionCore` 仍為多職責集中類別，尚未拆 detector 模組。
- 多處既有 nullable warning 尚未在本輪處理（非本階段阻塞）。

---

## 二、職責邊界（現況準則）

### UI 層（MainForm / MapEditor / MinimapViewer）

- 負責事件、畫面、控件生命週期、狀態顯示。
- 不主動產生偵測結果，不維護第二套 minimap marker/box 計算邏輯。

### Application 層（GamePipeline / NavigationStateMachine / MapFileManager）

- `GamePipeline`：每幀協調與快照輸出。
- `NavigationStateMachine`：導航狀態轉移與執行流程封裝。
- `MapFileManager`：地圖載入儲存流程與事件發布（不碰 UI 控件）。

### Domain 層（NavigationGraph/Node/Edge）

- 保存路徑模型、邊行為語意、尋路策略與節點關聯。
- 上層必須以 Domain 模型為唯一語意來源，避免 UI 補丁覆寫。

---

## 三、重構藍圖（保留方向）

> 此段為方向藍圖，非本輪已完成清單。

1. **拆分 GameVisionCore**
   - 目標拆為：血條偵測、小地圖偵測、怪物偵測、模板管理。
2. **MainForm 進一步瘦身**
   - 把可抽離的決策/轉換邏輯下放到 Application 專用服務。
3. **可視化資料建構專責化**
   - 將路徑疊圖資料建構集中至 `PathVisualizationBuilder`（候選）。
4. **枚舉與動作語意統一**
   - 維持 Domain 的動作語意為單一真實來源，逐步清理重複映射。

---

## 三點五、下一輪根治計畫（Jump/路徑資料治理）

> 來源：本輪實測 log（`artale-20260327-234857`、`artale-20260327-235309`、`artale-20260327-235509`）  
> 現況：執行層已加防呆可止血，但資料層仍存在方向錯誤與雙向缺失風險。

### A. 跳躍資料語意根治（SSOT 在 MapData）

1. **矯正既有路徑檔錯邊**
   - 以 `Nodes/Edges` 為準，修正 `JumpLeft/JumpRight` 與幾何方向不一致邊。
   - `test.json` 已確認案例：`n4 -> n6`、`n7 -> n9` 應改為左向跳躍語意。

2. **雙向邊顯式化**
   - 嚴格遵守方案 B：需要可往返時，必須顯式建立 `A->B` 與 `B->A` 兩條邊。
   - 禁止依賴任何自動補反向邊行為。

### B. 地圖邊完整性檢查器（新增）

1. **存檔前靜態檢查**
   - 規則一：Jump 邊方向與幾何方向一致性。
   - 規則二：可往返區段缺反向邊提示。
   - 規則三：孤立節點 / 無入邊 / 無出邊。
   - 規則四：從起始候選點不可達節點群。

2. **輸出形式**
   - 先做文字錯誤清單（節點對、邊方向、建議修正）。
   - 第二階段再做可視化標記（在編輯器疊圖中高亮錯邊）。

### C. 編輯器互動防呆（避免再次產生錯邊）

1. `Link` 模式新增「反向補邊快捷操作」（僅提示，不自動寫入）。
2. 新增「方向語意提示」：連線當下即顯示目前幾何方向與 action 是否一致。
3. 保留執行層防呆（方向衝突自修、落點 X 驗收）作最後防線，不作資料層替代。

### D. 路徑檔單一來源治理（避免漂移）

1. 統一路徑檔 SSOT 目錄（避免 `MapData` 與 `bin/.../MapData` 雙份漂移）。
2. 載入與儲存流程強制走同一路徑來源，降低「編輯的是 A、執行的是 B」風險。

### E. 碰撞體（Hitbox）根治與一致化

1. **平台/斜坡命中判定收斂**
   - 針對斜坡邊緣與下坡頂端，定義可接受的命中區域與 Y 容忍策略。
   - 禁止在 UI、FSM、Executor 各自定義不同到點容忍，統一由單一設定來源管理。

2. **繩索抓取命中區收斂**
   - 統一繩索對位規則（`ropeX ± tolerance`）與爬行啟動前置條件。
   - 將「可抓繩」判定與「到達繩索目標」判定拆分，避免單一條件誤導流程。

3. **Hitbox 視覺化與診斷常態化**
   - 保留獨立視窗同時渲染：目標 Hitbox、玩家位置、繩索對位帶、當前 Action。
   - 將錯誤樣態（越界、卡繩、掉層）對應到可視化標記，縮短回歸分析時間。

---

## 四、P2 收斂清單（已完成）

1. **MapFileManager 事件一致化** ✅ 已完成
   - `MainForm` 已將 `StatusMessage` / `ErrorMessage` / `FileListChanged` 改為具名 handler。
   - `OnFormClosed` 已補齊 `MapLoaded`、`MapSaved`、`StatusMessage`、`ErrorMessage`、`FileListChanged` 的解除訂閱。
   - 事件生命週期已一致化，不再依賴匿名 lambda 訂閱造成的解除困難。
2. **本輪安全刪除（ProcessMinimapPlayer + OnNavigationFailed 事件鏈）** ✅ 已完成
   - `DetectionService.ProcessMinimapPlayer(...)` 已完成零呼叫驗證並安全移除。
   - 已移除 `OnNavigationFailed` 失效事件鏈（`INavigationStateMachine` / `NavigationStateMachine` / `PathPlanningTracker`）。
   - 驗證結果：`cmd /c dotnet build "ArtaleAI.sln" -c Debug` 維持 0 errors（warnings 另案治理）。
3. **DetectionModeConfig 陰影型別整併** ✅ 已完成刪除
   - 已完成全專案零引用驗證：`LegacyDetectionModeConfig` / Detection 層 `DetectionModeConfig` 僅剩宣告、無實際使用。
   - 已自 `Models/Detection/DetectionStyles.cs` 安全移除上述兩個型別。
   - 正式使用路徑已收斂為 `Models/Config/VisionSettings.cs` 的 `DetectionModeConfig`。
4. **MainForm 最後一輪清理** ✅ A/B 級已落地
   - **A 級已完成**：移除 MainForm 中僅殘留的 dead state 欄位與對應清理流程（不影響控制流）。
   - **B 級已完成**：移除 `OnPathStateChanged` / `OnWaypointReached` 純 UI 訊息事件鏈（含 `+=` / `-=` / handler）。
   - 驗證結果：`cmd /c dotnet build "ArtaleAI.sln" -c Debug` 維持 0 errors（warnings 另案治理）。

> 目前 P2 項目已全數收斂完成；後續工作請依「三、重構藍圖（保留方向）」與「五、高風險暫緩」規劃下一輪。

---

## 四點五、本輪實作摘要（Iteration Summary）

本輪已完成多項關鍵收斂：

1. **MainForm 小地圖資料源第二步收斂**
   - 移除多處 `_currentMinimapBoxes` 的手動覆寫路徑。
   - 改為在小地圖定位成功時，以 `GamePipeline.SetMinimapBoxes(...)` 單點更新。
   - 每幀不再由 `MainForm` 反向覆寫 pipeline 的 minimap boxes。

2. **MapFileManager 事件一致化修正**
   - 事件訂閱改為具名方法，避免匿名 lambda 造成無法精準解除。
   - 關閉流程補齊所有對應解除訂閱，降低記憶體與 UI 生命周期風險。

3. **編譯驗證**
   - `cmd /c dotnet build "ArtaleAI.sln" -c Debug`：0 errors（warnings 另案治理）。

4. **B1（PathPlanningManager 失效事件鏈）已完成**
   - 已移除 `PathPlanningManager.OnTrackingUpdated` 事件與其 relay 訂閱/解除鏈。
   - `OnPathStateChanged` / `OnWaypointReached` 事件仍由 Application 層保留，但 UI 層已移除非必要的純訊息監聽鏈。
   - 驗證結果：編譯仍維持 0 errors。

5. **B2（DetectionModeConfig 陰影型別）已完成刪除**
   - 已完成全專案零引用驗證（含型別名稱與命名空間限定檢索），確認 `LegacyDetectionModeConfig` 與 Detection 層 `DetectionModeConfig` 無實際引用。
   - 已自 `Models/Detection/DetectionStyles.cs` 移除兩個型別，結束相容層過渡。
   - 正式型別單一來源維持 `Models/Config/VisionSettings.cs` 的 `DetectionModeConfig`（SSOT）。
   - 驗證結果：`cmd /c dotnet build "ArtaleAI.sln" -c Debug` 維持 0 errors（warnings 另案治理）。

6. **本輪安全刪除（ProcessMinimapPlayer + OnNavigationFailed 事件鏈）已完成**
   - `DetectionService.ProcessMinimapPlayer(...)` 已確認全專案零呼叫後移除，避免 Detection 層殘留孤島方法。
   - `OnNavigationFailed` 事件鏈已自 `INavigationStateMachine`、`NavigationStateMachine`、`PathPlanningTracker` 移除，避免無訂閱 relay 持續存在。
   - 驗證結果：`cmd /c dotnet build "ArtaleAI.sln" -c Debug` 維持 0 errors（warnings 另案治理）。

7. **MainForm 最後一輪清理（A/B 級）已完成落地**
   - **A 級清理**：已移除 MainForm 內無實際消費的狀態欄位與對應釋放語句，收斂 UI 層責任邊界。
   - **B 級清理**：已移除 `OnPathStateChanged` / `OnWaypointReached` 純訊息事件鏈，避免 UI 層保留非必要監聽。
   - 驗證結果：`cmd /c dotnet build "ArtaleAI.sln" -c Debug` 維持 0 errors（warnings 另案治理）。

---

## 五、高風險暫緩（Do Not Rush）

1. 一次性大砍 `CharacterMovementController` 大段邏輯。
2. 直接移除所有 Walk 的垂直安全保險絲。
3. 把 UI 控件渲染流程硬抽到非 UI 執行緒層。

原則：先收斂資料源與責任，再做結構性大手術。

---

## 六、驗證清單

### Automated

1. `cmd /c dotnet build --no-restore`
2. 確認 0 errors（warnings 另案治理）

### Manual

1. 地圖編輯載入/儲存流程正常。
2. 即時顯示與獨立視窗同步正常（不依賴路徑規劃啟動）。
3. 路徑追蹤可持續推進，FSM 不出現卡死連鎖。
4. Jump 邊方向與幾何方向一致，不出現 `[跳躍] 邊方向與目標方向衝突` 警告。
5. 儲存前完整性檢查器可列出錯邊，修正後可清零。

---

## 七、Definition of Done（本版）

1. 文件描述與程式現況一致，不含過時行號/舊結構假設。
2. P2 已完成項目有明確標記，待辦與高風險項有明確邊界。
3. 後續實作可直接依「三點五、下一輪根治計畫」逐條執行。
