# 修復 GameVisionCore 顏色解析邏輯錯誤與效能優化

## 修正動機 (Reasoning)
在高壓的即時畫面處理迴圈中，先前的重構誤將 `GameVisionCore.ParseColor` 中的綠色 (G) 範圍判定寫錯 (`g <= 255` 誤判為非法)，導致所有正常的顏色設定值都會觸發例外，對日誌系統造成衝擊並波及 UI 渲染效能。

## 變更項目 (Proposed Changes)

### 核心與視覺處理 (Core & Vision)

#### [MODIFY] [GameVisionCore.cs](file:///d:/Full_end/C#/ArtaleAI/Core/GameVisionCore.cs)
- **邏輯修正**：將引發誤報的 `g <= 255` 修正為 `g > 255`。
- **架構優化**：引入基於 `ConcurrentDictionary` 的簡單顏色快取。
  - **動機**：渲染疊加層時每一幀都會重複解析相同的顏色字串。快取後可大幅減少字串切割、數值轉換與例外判定的開銷。
  - **原則**：仍然維持「不合法格式即拋出例外」的嚴格原則，但合法的解析結果將被重用。

## 驗證計畫 (Verification Plan)

### Automated Tests
- 執行 `dotnet build` 確保環境與語法無誤。

### Manual Verification
- **日誌觀察**：確認日誌檔案中不再出現大量的 `RGB 值必須在 0-255 之間` 錯誤。
- **視覺檢查**：啟動 LiveView，確認血條、怪物框、地圖標記的顏色是否如預期顯示（不再是白色預設值）。
