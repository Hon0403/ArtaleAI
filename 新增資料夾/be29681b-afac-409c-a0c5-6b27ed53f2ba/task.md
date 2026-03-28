# 路徑規劃系統重構任務

## 步驟 1：建立圖結構
- [x] 建立 `Core/PathGraph.cs` - 圖結構和 A* 演算法
- [x] 建立 `EdgeVisualization` 類別 - 邊可視化

## 步驟 2：自動偵測連接
- [x] 實作 `BuildEdges()` - 偵測走路、跳躍、繩索連接

## 步驟 3：實作 A* 路徑搜尋
- [x] 實作 `FindPath()` - A* 演算法

## 步驟 4：可視化
- [x] 修改 `MinimapViewer.cs` - 繪製連接線和當前路徑

## 步驟 5：整合到現有系統
- [x] 修改 `PathPlanningTracker.cs` - 使用 PathGraph
- [x] 修改 `MainForm.cs` - 傳遞圖資料給 MinimapViewer
- [x] 修改 `SelectNextTarget` - 當目標在不同層時使用 A* 路線

## 步驟 6：測試
- [x] 編譯測試 - 成功
- [ ] 驗證可視化和 A* 路線（需用戶測試）

