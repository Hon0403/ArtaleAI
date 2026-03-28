# 平台邊界處理系統實作

## 目標
實作從 RestrictedZones 讀取平台邊界，並在移動控制中進行邊界檢查和處理。

## 任務清單

### 1. 資料結構 (DataModels.cs)
- [x] 新增 `PlatformBounds` 類別（MinX, MaxX, MinY, MaxY）

### 2. 配置設定 (config.yaml)
- [x] 新增 `platformBounds` 配置區塊（bufferZone, cooldownMs）

### 3. 路徑規劃管理器 (PathPlanningManager.cs)
- [x] 新增 `SetPlatformBounds` 方法
- [x] 在 `LoadPlannedPath` 時解析 RestrictedZones 並計算邊界  
- [x] 新增邊界事件（`OnBoundaryHit`）的轉發

### 4. 移動控制器 (CharacterMovementController.cs)
- [x] 新增 `BoundaryHit` 事件
- [x] 新增 `SetPlatformBounds` 方法
- [x] 在 `MoveToTargetAsync` 中加入三重防護邊界檢查
- [x] 實作防抖動機制（cooldown）

### 5. 路徑追蹤器 (PathPlanningTracker.cs)
- [x] 新增 `OnBoundaryHit` 處理方法
- [x] 實作接近邊界時重新選擇目標的邏輯

### 6. 整合與測試
- [x] 編譯成功驗證
