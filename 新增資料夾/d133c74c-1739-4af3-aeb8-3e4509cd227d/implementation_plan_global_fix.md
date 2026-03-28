# 全域配置引用修復計畫 (第二輪)

本計畫旨在徹底解決 `AppConfig` 模組化重構後，剩餘檔案中未更新的屬性引用問題。

## 待遷移引用清單

| 舊屬性名 | 新路徑 |
| :--- | :--- |
| `BloodBarDetectIntervalMs` | `Vision.BloodBarDetectIntervalMs` |
| `MonsterDetectIntervalMs` | `Vision.MonsterDetectIntervalMs` |
| `DetectionMode` | `Vision.DetectionMode` |
| `DefaultThreshold` | `Vision.DefaultThreshold` |
| `MaxDetectionResults` | `Vision.MaxDetectionResults` |
| `CaptureFrameRate` | `Vision.CaptureFrameRate` |
| `GlobalArrivalTolerance` | `Navigation.ArrivalTolerance` |
| `LastSelectedWindowName` | `General.LastSelectedWindowName` |
| `MinimapViewer` | `Appearance.MinimapViewer` |
| `PartyRedBar` | `Appearance.PartyRedBar` |
| `DetectionBox` | `Appearance.DetectionBox` |
| `AttackRange` | `Appearance.AttackRange` |
| `Minimap` | `Appearance.Minimap` |
| `Monster` | `Appearance.Monster` |
| `MinimapPlayer` | `Appearance.MinimapPlayer` |

## 預計變更檔案

### [Component] Services

#### [MODIFY] [GamePipeline.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/GamePipeline.cs)
- 更新視覺偵測相關參數。

#### [MODIFY] [NavigationExecutor.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/NavigationExecutor.cs)
- 更新 `GlobalArrivalTolerance` 引用。

#### [MODIFY] [WindowFinder.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/WindowFinder.cs)
- 更新 `LastSelectedWindowName` 引用。

### [Component] UI

#### [MODIFY] [LiveViewManager.cs](file:///d:/Full_end/C%23/ArtaleAI/UI/LiveViewManager.cs)
- 更新 `CaptureFrameRate` 引用。

#### [MODIFY] [MinimapViewer.cs](file:///d:/Full_end/C%23/ArtaleAI/UI/MinimapViewer.cs)
- 更新 `MinimapViewer` 視覺樣式引用。

#### [MODIFY] [OverlayRenderer.cs](file:///d:/Full_end/C%23/ArtaleAI/UI/OverlayRenderer.cs)
- 更新所有 `Appearance` 相關的視覺樣式引用。

## 驗證計畫

### 自動化測試
- 再次執行 `dotnet build` 確保所有 `CS1061` 與 `CS0117` 錯誤均已消失。

### 手動驗證
- 觀察 `OverlayRenderer` 是否能正確讀取顏色與線寬設定。
- 確認 `GamePipeline` 的偵測頻率是否符合 `config.yaml` 的設定。
