# AppConfig 全域引用修復紀錄

本文件記錄了針對 AppConfig 模組化重構後，剩餘全域引用的遷移與修復。

## 已修復檔案與變更內容

### 1. `Services\GamePipeline.cs`
- 將所有視覺偵測參數（`BloodBarDetectIntervalMs`, `MonsterDetectIntervalMs` 等）遷移至 `config.Vision` 下。

### 2. `Services\NavigationExecutor.cs`
- 更新 `GlobalArrivalTolerance` 引用至 `AppConfig.Instance.Navigation.ArrivalTolerance`。

### 3. `Services\WindowFinder.cs`
- 將 `LastSelectedWindowName` 遷移至 `General` 模組。

### 4. `UI\LiveViewManager.cs`
- 將 `CaptureFrameRate` 遷移至 `Vision` 模組。

### 5. `UI\MinimapViewer.cs`
- 更新樣式引用路徑。
- **重要修復**：修正 `ZoomFactor` 從 `int` 改為 `double` 後引發的所有型別轉型錯誤與繪圖計算誤差。
- 加入了 `RenderPathData` 的 Null 檢查，強化程式穩定性。

### 6. `UI\OverlayRenderer.cs`
- 更新所有 `Appearance` 相關樣式（`PartyRedBar`, `DetectionBox`, `AttackRange`, `Minimap`, `Monster`, `MinimapPlayer`）的引用路徑。

## 驗證結果
- [x] 所有相關的 `CS1061` 與 `CS0117` 編譯錯誤均已消除。
- [x] `MinimapViewer` 全面的顯式轉型確保了在 `double` 縮放倍率下的繪圖正確性。
