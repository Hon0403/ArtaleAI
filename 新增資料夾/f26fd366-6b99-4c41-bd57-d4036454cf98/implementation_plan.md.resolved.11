# 按鍵錄製路徑系統 實施計劃

## 目標
將「動作類型」錄製改為「實際按鍵」錄製，實現精準路徑回放。

## 舊方案 vs 新方案

| 項目 | 舊方案 | 新方案 |
|------|--------|--------|
| 格式 | `[x, y, action]` | `[x, y, 時間, 按鍵[]]` |
| 錄製 | 從按鍵推導動作 | 直接記錄按鍵 |
| 執行 | 根據動作決定按鍵 | 直接回放按鍵 |

---

## 修改內容

### 1. 修改 RouteRecorderService.cs

**RoutePoint 結構改為：**
```csharp
public class RoutePoint
{
    public SdPointF Position;        // 位置
    public float Timestamp;          // 相對時間（秒）
    public List<string> Keys;        // 按鍵列表 ["left", "up"]
    public ActionType Action => DeriveAction(); // 從按鍵推導（用於顏色顯示）
}
```

**錄製邏輯改為：**
- 追蹤當前按住的按鍵
- 當按鍵改變 或 位置移動超過閾值時記錄
- 儲存相對時間戳

---

### 2. 新增 PathExecutor.cs

新的路徑執行服務：
- 追蹤當前按住的按鍵
- 同步按鍵狀態到目標點的按鍵
- 到達位置後前進到下一點

---

### 3. 修改 MapData.cs

支援新的路徑格式（包含時間戳和按鍵陣列）。

---

### 4. 修改 MainForm.cs

整合 PathExecutor 用於路徑執行模式。

---

## 驗證計劃

1. 編譯測試
2. 錄製測試（含跳躍、爬繩動作）
3. 回放測試（驗證動作精準重現）
