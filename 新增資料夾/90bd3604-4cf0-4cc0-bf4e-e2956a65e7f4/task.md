#- [x] **實作節點動作選擇 UI**
    - [x] 在 `MainForm.Designer.cs` 新增 `cbo_ActionType` (ComboBox) 和 `rdo_SelectMode` (RadioButton)。
    - [x] 在 `MainForm.cs` 初始化 `cbo_ActionType` 選項 (Walk, Jump, ClimbUp, etc.)。
    - [x] 綁定事件處理：選擇動作時更新 `MapEditor` 狀態。

- [x] **修改 `MapEditor.cs` 支援動作選取與修改**
    - [x] 新增 `EditMode.Select` 模式邏輯。
    - [x] 實作節點選取功能 (點擊判定)。
    - [x] 實作 `UpdateSelectedNodeAction(int actionType)` 方法。
    - [x] 修改 `DrawCompletedShapes`：
        - [x] 根據 ActionType 繪製不同顏色的節點 (Walk=白, Jump=藍, Climb=綠)。
        - [x] 繪製選取 (黃色) 和懸停 (高亮) 效果。
        - [x] (Optional) 顯示節點順序或動作標籤。

- [x] **修改 `MainForm.cs` 整合邏輯**
    - [x] 在 `OnEditModeChanged` 處理 `Select` 模式切換。
    - [x] 在 `pBox_Minimap_MouseClick` 處理選取事件，並同步更新 `cbo_ActionType` 顯示。
    - [x] 確保資料存檔時包含 ActionType 資訊 (JSON 格式已支援)。時同步更新選取節點的動作
- [x] 驗證 <!-- id: 4 -->
    - [x] 確認可以精確點選任意與修改中間節點
    - [x] 確認視覺上能區分不同動作的節點

- [x] **修正 Action Code 執行邏輯**
    - [x] 在 `PathPlanningTracker.cs` 中補完 `ActionCodeToKeys` (支援 9-12: LeftJump, RightJump, ClimbUp, ClimbDown)。
