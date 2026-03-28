# 重複定義與命名空間整合計畫 (Consolidation Plan)

此計畫旨在透過刪除重複定義及統一命名空間，徹底解決專案中的 CS0101 (重複定義) 與 CS0104 (引用歧義) 錯誤。

## 待解決問題
- **重複的 AppConfig**: `Data/AppConfig.cs` 與 `Config/AppConfig.cs` 衝突。
- **重複的 Logger**: `Services/Logger.cs` 與 `Utils/Logger.cs` 衝突。
- **重複的 Models**: `Data/DataModels.cs` 內的類別與 `Models/` 目錄下的實作衝突。
- **API 配置缺失**: `MonsterImageFetcher.cs` 找不到 `ApiConfig`。

## 預計變更 (Proposed Changes)

### 1. 基礎設施與清理 (Infrastructure & Cleanup)

#### [DELETE] [AppConfig.cs](file:///d:/Full_end/C#/ArtaleAI/Data/AppConfig.cs)
刪除舊版扁平化的 AppConfig，統一使用 `Config/` 下的模組化版本。

#### [DELETE] [DataModels.cs](file:///d:/Full_end/C#/ArtaleAI/Data/DataModels.cs)
刪除此檔案，該檔案定義了大量位於 `ArtaleAI.Config` 命名空間下的模型，與 `ArtaleAI.Models` 衝突。

#### [DELETE] [Logger.cs](file:///d:/Full_end/C#/ArtaleAI/Services/Logger.cs)
刪除自定義異步 Logger，統一使用 `Utils/Logger.cs` (基於 Serilog)。

#### [NEW] [ApiConfig.cs](file:///d:/Full_end/C#/ArtaleAI/API/Config/ApiConfig.cs)
重新建立 API 專用配置類別。

#### [MODIFY] [Logger.cs](file:///d:/Full_end/C#/ArtaleAI/Utils/Logger.cs)
確保具備以下靜態方法以支援現有呼叫：
```csharp
public static void Info(string msg);
public static void Warning(string msg);
public static void Error(string msg, Exception ex = null);
public static void Debug(string msg);
```

### 2. 邏輯層修正 (Core Logic Adjustments)

#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C#/ArtaleAI/Core/PathPlanningTracker.cs)
- 變更 `using ArtaleAI.Config;` 為 `using ArtaleAI.Models.PathPlanning;`
- 確保使用的 `PathPlanningState` 來自 `Models`。

#### [MODIFY] [GameVisionCore.cs](file:///d:/Full_end/C#/ArtaleAI/Core/GameVisionCore.cs)
- 統一偵測結果為 `ArtaleAI.Models.Detection.DetectionResult`。

#### [MODIFY] [CharacterMovementController.cs](file:///d:/Full_end/C#/ArtaleAI/Services/CharacterMovementController.cs)
- 修正 Logger 引用路徑。

## 驗證計畫 (Verification Plan)

### 自動化測試
1. **編譯驗證**: 執行 `dotnet build` 或利用 IDE 檢查器，確認下列錯誤消失：
   - CS0101: `AppConfig` 重複定義。
   - CS0104: `DetectionResult`, `Logger` 歧義。
   - CS0103: `MsgLog`, `ApiConfig` 缺失。

### 手動驗證
1. **日誌輸出檢查**: 啟動程式後檢查 `Logs/` 目錄是否正常生成最新的 `artale-*.log`。
2. **組態載入檢查**: 修改 `config.yaml` 後啟動，確認 UI 反映了正確的設定（證明 `Config/AppConfig.cs` 運作正常）。
3. **路徑點視覺化**: 在 `MainForm` 中加載地圖，確認路徑點能正確顯示在畫面上（證明 `Models/Map/MapData.cs` 正確運作）。
