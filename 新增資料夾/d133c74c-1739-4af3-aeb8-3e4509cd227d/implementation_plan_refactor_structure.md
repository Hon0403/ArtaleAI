# 專案目錄結構與領域歸類重構計畫

## 修正動機 (Reasoning)
目前的目錄結構隱藏了若干「Vibe Coding」的特徵：
- **雜亂的枚舉 (Messy Enums)**：`Data\Enums.cs` 混合了多個不相關的領域狀態，造成命名空間污染。
- **邏輯洩漏 (Logic Leakage)**：`Models` 目錄下包含 JSON 序列化邏輯與座標轉換工具。
- **領域分裂 (Domain Fragmentation)**：路徑規劃模型 (`Models\PathPlanning`) 與導航核心 (`Core\Domain`) 被強制分離，導致引用關係過於複雜。

## 變更項目 (Proposed Changes)

### 1. 枚舉領域化 (Enum Re-homing)
- **[DELETE]** `Data\Enums.cs`
- **[NEW]** `Core\Vision\MonsterDetectionEnums.cs`: 包含 `MonsterDetectionMode` 與 `OcclusionHandling`。
- **[NEW]** `UI\MapEditor\MapEditorEnums.cs`: 包含 `EditMode` 與 `MinimapUsage`。
- **更新**：全局搜尋並替換所有命名空間引用 (`ArtaleAI.Config` -> `ArtaleAI.Core.Vision` / `ArtaleAI.UI.MapEditor`)。

### 2. 工具邏輯移轉 (Helper Relocation)
- **[MOVE]** `Models\JsonHelpers.cs` -> `Utils\JsonSerializationHelper.cs` (同時將 `FloatArrayConverter` 併入)。
- **更新**：更新 `Models` 空間，移除工具類別，僅保留資料屬性。

### 3. 導航領域整合 (Navigation Domain Integration)
- **[MOVE]** `Models\PathPlanning\PlatformSegment.cs` -> `Core\Domain\Navigation\PlatformSegment.cs`
- **[MOVE]** `Models\PathPlanning\RopeData.cs` -> `Core\Domain\Navigation\RopeData.cs`
- **命名空間升格**：將其命名空間從 `ArtaleAI.Models.PathPlanning` 改為 `ArtaleAI.Core.Domain.Navigation`。
- **效益**：使導航引擎能夠直接管理其幾何對象，減少跨層 DTO 需求。

## 驗證計畫 (Verification Plan)

### Automated Tests
- **全域編譯**：執行 `dotnet build` 確保數百個命名空間引用修改全部正確。
- **靜態檢查**：確認 `Models` 資料夾不再包含任何具體邏輯方法。

### Manual Verification
- **啟動應用程式**：確認地圖載入、小地圖視窗與路徑編輯器功能皆正常運作。
