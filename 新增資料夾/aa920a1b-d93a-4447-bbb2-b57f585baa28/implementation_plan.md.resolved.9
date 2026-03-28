# 配置模組遷移計畫 (Migration to Models\Config)

## 目的
優化專案目錄結構，將根目錄的 `Config` 移至 `Models\Config`，使配置類別 (Settings DTOs) 與其數據模型本質達成語義對齊。

## 執行方案 (Migration Items)

### 1. [MOVE] 重建設定類別
- 在 `Models\Config` 底下重建：
  - `AppConfig.cs`
  - `GeneralSettings.cs`
  - `VisionSettings.cs`
  - `NavigationSettings.cs`
  - `EditorSettings.cs`
  - `AppearanceSettings.cs`
- **關鍵變更**：將命名空間從 `ArtaleAI.Config` 替換為 `ArtaleAI.Models.Config`。

### 2. [MODIFY] 全域命名空間更新
- 掃描並更新所有檔案中的 `using ArtaleAI.Config;`。
- **影響範圍**：
    - `UI/*.cs`
    - `Services/*.cs`
    - `Core/*.cs`
    - `API/*.cs`

### 3. [DELETE] 清理舊遺產
- 建置成功後，徹底刪除專案根目錄下的 `Config` 資料夾。

## 驗證計畫
- **建置驗證**：`dotnet build` 通過。
- **功能驗證**：確認 `config.yaml` 能在新的命名空間結構下被 correctly deserialized 並載入。
