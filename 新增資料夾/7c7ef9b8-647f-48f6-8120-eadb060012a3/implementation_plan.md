# 平台邊界處理系統實作計劃

從 RestrictedZones 讀取平台邊界，在移動控制中進行三重防護邊界檢查，並使用事件機制解耦模組間通訊。

## User Review Required

> [!IMPORTANT]
> **設計決策**：使用事件機制（`event Action<string>`）取代雙向引用，降低模組耦合度

> [!WARNING]  
> **邊界來源**：優先使用 `RestrictedZones`，若為空則從 `WaypointPaths` 自動計算（往內縮 5px）

---

## Proposed Changes

### DataModels

新增平台邊界資料結構。

#### [NEW] PlatformBounds (在 DataModels.cs 中)

```csharp
/// <summary>
/// 平台邊界 - 定義角色可移動的安全範圍
/// </summary>
public class PlatformBounds
{
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinY { get; set; }
    public float MaxY { get; set; }
    
    public override string ToString() => 
        $"X=[{MinX:F1}, {MaxX:F1}], Y=[{MinY:F1}, {MaxY:F1}]";
}
```

---

### Configuration

新增邊界相關配置參數。

#### [MODIFY] [config.yaml](file:///d:/Full_end/C%23/ArtaleAI/Data/config.yaml)

在路徑規劃設定區塊後新增：

```yaml
# ============================================
# 平台邊界處理設定
# ============================================
platformBounds:
  # 緩衝區大小（像素，接近邊界時提前觸發減速）
  bufferZone: 5.0
  # 緊急區域（像素，超出此範圍強制停止）
  emergencyZone: 2.0
  # 邊界事件冷卻時間（毫秒，防止反覆觸發）
  cooldownMs: 500
```

---

### CharacterMovementController

加入邊界檢查和事件通知。

#### [MODIFY] [CharacterMovementController.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/CharacterMovementController.cs)

**新增欄位：**
- `_platformBounds: PlatformBounds?`
- `_lastBoundaryHitTime: DateTime`
- `_boundaryCooldownMs: int`

**新增事件：**
- `event Action<string>? OnBoundaryHit` - 當觸發邊界時通知（left/right）
- `event Action<SdPointF>? OnTargetOutOfBounds` - 目標超出邊界時通知

**新增方法：**
- `SetPlatformBounds(PlatformBounds bounds, int cooldownMs)`

**修改 MoveToTargetAsync：**
- 加入三重防護邊界檢查（緊急停止、提前減速、目標驗證）
- 觸發邊界事件並遵守冷卻時間

---

### PathPlanningTracker

處理邊界事件並重新選擇目標。

#### [MODIFY] [PathPlanningTracker.cs](file:///d:/Full_end/C%23/ArtaleAI/Core/PathPlanningTracker.cs)

**新增欄位：**
- `_platformBounds: PlatformBounds?`

**新增方法：**
- `SetPlatformBounds(PlatformBounds bounds)`
- `OnBoundaryHit(string direction)` - 處理邊界觸發，選擇安全方向的目標
- `OnTargetOutOfBounds(SdPointF target)` - 處理目標超出邊界

---

### PathPlanningManager

統籌邊界設定和事件轉發。

#### [MODIFY] [PathPlanningManager.cs](file:///d:/Full_end/C%23/ArtaleAI/Services/PathPlanningManager.cs)

**新增欄位：**
- `_movementController: CharacterMovementController?`

**新增方法：**
- `SetMovementController(CharacterMovementController controller)`
- 在 `LoadPlannedPath` 中解析 `RestrictedZones` 並計算 `PlatformBounds`

---

## Verification Plan

### Manual Verification

由於此功能需要遊戲環境配合，建議使用以下步驟進行手動測試：

1. **準備測試路徑檔案**：
   - 在地圖編輯器中設定 2 個 RestrictedZones 標記（平台左右邊界）
   - 儲存路徑檔案

2. **啟動路徑規劃**：
   - 載入測試路徑檔案
   - 啟動自動打怪或路徑規劃
   
3. **驗證邊界處理**：
   - 觀察控制台輸出，確認有 `[邊界處理]` 相關日誌
   - 當角色接近邊界時，應看到「接近邊界」的警告
   - 角色應自動改變方向，不會掉落

4. **驗證冷卻機制**：
   - 觀察邊界事件不會在 500ms 內重複觸發
