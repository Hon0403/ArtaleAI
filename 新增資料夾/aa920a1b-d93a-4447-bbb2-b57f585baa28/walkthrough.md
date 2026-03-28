# 導航系統與配置架構重組紀錄 (Structure Optimization Walkthrough)

我們已成功將專案從「混合制結構」遷移至「Models 為核心」的 Clean Architecture 結構。

## 關鍵變更內容

### 1. 配置模組遷移 (Config -> Models\Config)
- **搬遷檔案**：將原位於根目錄 `Config\` 的所有 6 個設定 DTOs 遷入 `Models\Config\`。
- **枚舉收網**：將遺落在 `Data\Enums.cs` 但屬於 `ArtaleAI.Config` 命名空間的枚舉（如 `EditMode`, `MonsterDetectionMode`）一併移入 `Models\Config\Enums.cs`。
- **命名空間對位**：將全域 `ArtaleAI.Config` 統一升級為 `ArtaleAI.Models.Config`。

### 2. 全域連動更新 (Global Rewire)
- **影響範圍**：16 個核心檔案（包含 `UI\`, `Services\`, `Core\`, `API\` 各層級）。
- **同步內容**：所有 `using ArtaleAI.Config;` 已對齊為 `using ArtaleAI.Models.Config;`。

### 3. 無情清理 (Ruthless Refactoring)
- **[DELETE]** 物理刪除了原根目錄 `Config\`。
- **[DELETE]** 物理刪除了 `Data\Enums.cs`。
- **驗證成果**：`dotnet build` 通過 (ExitCode 0)，確保專案無技術債殘留。

## 驗證結果
- **編譯狀態**：[SUCCESS]
- **語義一致性**：[PASSED]
- **物理路徑安全性**：[CLEAN]

> [!TIP]
> **架構建議**：
> 未來新增任何與「設定數據」相關的類型或枚舉，請直接於 `Models\Config` 下建立，以維護目前達成的 SSOT (Single Source of Truth) 與目錄一致性。
