# AppConfig 重構後的編譯錯誤修復

本文件記錄了針對 AppConfig 模組化重構後遺留問題的修復。

## 已修復問題

### 1. 補齊 `MinimapViewerStyle` 類別
在 `MinimapStyles.cs` 中新增了 `MinimapViewerStyle` 類別定義，包含：
- `Enabled`: 是否啟用放大鏡視覺。
- `ZoomFactor`: 放大倍率。
- `OffsetX/Y`: 視窗偏移。
- `Width/Height`: 視窗尺寸。
- `BaseSize`: 渲染基準大小。

### 2. 修正 `MainForm.cs` 型別轉換
在 `MainForm.cs` 第 1310 行加入了顯式轉型，將 `double` 型別的 `ArrivalTolerance` 轉換為 `float`，解決了 CS0266 編譯錯誤。

```csharp
// 修正後
float arrivalTolerance = (float)AppConfig.Instance.Navigation.ArrivalTolerance;
```

## 驗證結果
- [x] `AppearanceSettings.cs` 引用 `MinimapViewerStyle` 不再報錯。
- [x] `MainForm.cs` 數值賦值不再報錯。
