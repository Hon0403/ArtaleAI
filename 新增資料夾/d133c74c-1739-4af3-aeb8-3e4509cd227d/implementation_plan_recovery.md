# 全域架構搶救與編譯恢復計畫 (Recovery Plan)

## 設計動機
在先前的 `MainForm.cs` 重構與 `AppConfig` 結構化過程中，由於清理過於激進，導致了大規模的引用斷路（298 個錯誤）。
本計畫旨在透過補齊橋接屬性、恢復遺失的 UI 輔助類別與核心服務實例，在不破壞新架構的前提下，恢復系統的編譯性與功能完整性。

## 待辦事項 (Recovery Checklist)
- [ ] **第一階段：補齊 AppConfig 橋接屬性 (Compatibility Layer)**
    - [ ] 在 `AppConfig.cs` 中補回 `ZoomFactor`, `GameWindowTitle`, `MinBarWidth` 等橋接屬性，使其指向嵌套的 `General`, `Vision`, `Navigation` 子項。
- [ ] **第二階段：恢復遺失的輔助類別與成員 (Missing Members)**
    - [ ] 重建 `MsgLog` 靜態類別（或將其整合至 `Logger` 並提供 UI 委派）。
    - [ ] 在 `MainForm.cs` 中補齊 `PathManager`, `OnPathStateChanged`, `OnWaypointReached`, `TranslatePictureBoxPointToImage` 等核心成員。
- [ ] **第三階段：修正命名空間與型別衝突 (Final Touches)**
    - [ ] 解決 `MonsterImageFetcher` 對 `PathManager` 的非法引用。
    - [ ] 修正 `NavigationExecutor` 的方法簽章不匹配問題 (`MoveToTargetAsync`, `ClimbRopeAsync`)。
- [ ] **第四階段：驗證**
    - [ ] 執行 `dotnet build` 直到 0 錯誤。

## 預期變動
### [MODIFY] [AppConfig.cs](file:///d:/Full_end/C#/ArtaleAI/Config/AppConfig.cs)
- 增加大量橋接屬性以向下相容舊有引用。

### [NEW] [MsgLog.cs](file:///d:/Full_end/C#/ArtaleAI/Utils/MsgLog.cs)
- 提供 `ShowStatus` 與 `ShowError` 的 UI 反饋介面。

### [MODIFY] [MainForm.cs](file:///d:/Full_end/C#/ArtaleAI/UI/MainForm.cs)
- 恢復被誤刪的核心服務控制實例。

## 驗證計畫
### 自動編譯
- `dotnet build /clp:ErrorsOnly`
