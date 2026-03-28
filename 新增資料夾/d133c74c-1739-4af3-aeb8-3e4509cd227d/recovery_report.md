# 緊急檔案恢復報告

## 存活檔案（23 個 .cs）

| 資料夾 | 檔案 |
|:---|:---|
| `API/` | `MonsterImageFetcher.cs` |
| `Core/` | `GameVisionCore.cs`, `PathPlanningTracker.cs` |
| `Core/Domain/Navigation/` | `ShortestPathPlanner.cs` |
| `Core/Vision/` | `IVisionDetectors.cs` |
| `Models/Config/` | `AppConfig.cs`, `AppearanceSettings.cs`, `EditorSettings.cs`, `GeneralSettings.cs`, `NavigationSettings.cs`, `VisionSettings.cs` |
| `Services/` | `CharacterMovementController.cs`, `GamePipeline.cs`, `MapFileManager.cs`, `NavigationExecutor.cs`, `PathPlanningManager.cs`, `WindowFinder.cs` |
| `UI/` | `FloatingMagnifier.cs`, `LiveViewManager.cs`, `MainForm.cs`, `MapEditor.cs`, `MinimapViewer.cs`, `OverlayRenderer.cs` |

## 已遺失的關鍵檔案

> [!CAUTION]
> 以下檔案在磁碟上已完全找不到，沒有 `.git` 存放庫可供恢復。

- `ArtaleAI.csproj`、`Program.cs`
- `Data/config.yaml`、`Data/Enums.cs`
- `Utils/Logger.cs`、`Utils/DrawingHelper.cs`、`Utils/JsonSerializationHelper.cs`
- `Core/SharedGameState.cs`、`Core/MonsterThresholdEstimator.cs`
- `Core/Vision/TemplateManager.cs`、`Core/Vision/MonsterDetectionEnums.cs`
- `Services/NavigationStateMachine.cs`、`Services/GraphicsCapturer.cs`
- `Models/Detection/*`、`Models/Minimap/MinimapStyles.cs`、`Models/Visualization/*`、`Models/JsonHelpers.cs`

## 恢復建議

1. **Windows 資源回收筒** — 最快，有機會一次全部還原
2. **Visual Studio 開啟的標籤頁** — 如果 VS 還開著遺失的檔案，按 Ctrl+S 另存
3. **Windows「以前的版本」** — 右鍵資料夾 → 屬性 → 以前的版本
4. **磁碟復原工具（Recuva 等）** — 最後手段
