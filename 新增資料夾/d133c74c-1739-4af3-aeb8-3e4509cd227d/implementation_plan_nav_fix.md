# 緊急修復：還原 PathPlanningTracker 導航邊緣檢索邏輯

## 故障原因 (Root Cause)
在先前追求「嚴格架構」的重構過程中，我不慎將 `CurrentNavigationEdge` 屬性中尋找「上一個節點」與「當前邊緣」的核心代碼一併刪除。這導致 FSM (狀態機) 永遠拿不到移動指令，進而引發角色原地踏步。

## 變更項目 (Proposed Changes)

### 核心導航層 (Core Navigation)

#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C#/ArtaleAI/Core/PathPlanningTracker.cs)
- **邏輯還原**：重新補回 `prevNode` 的尋找邏輯，以及向 `_navGraph` 請求邊緣 (`GetEdge`) 的代碼。
- **維持嚴格性**：仍保留「找不到邊緣即回傳 `null`」的設計，徹底廢棄舊版的 `rescue_from` 假邊緣補丁。

## 驗證計畫 (Verification Plan)

### Automated Tests
- 執行 `dotnet build` 確保環境與語法無誤。

### Manual Verification
- **日誌追蹤**：確認日誌中出現 `[導航狀態]` 標籤。
- **功能測試**：請使用者再次啟動路徑規劃，確認角色能恢復走位。
