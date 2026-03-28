# Code Review 技術債清理 — 完成報告

## 修正摘要

### 問題一（最高優先）：消除重複 A* 實作 ✅

**Before**: `ShortestPathPlanner` 和 `NavigationGraph` 各自實作了一套 A*

| | `NavigationGraph.FindPath()` | `ShortestPathPlanner.PlanPath()` |
|---|---|---|
| 資料結構 | `SortedSet` + `inOpenSet` HashSet | `PriorityQueue` |
| 優先權更新 | ✅ 正確 remove → re-add | ❌ 重複入隊 |
| 結果 | **保留** | **已委派** |

**After**: `ShortestPathPlanner.PlanPath()` 現在只有一行：

```csharp
return graph.FindPath(startNodeId, goalNodeId);
```

刪除了 ~80 行重複且有 bug 的程式碼。

render_diffs(file:///d:/Full_end/C%23/ArtaleAI/Core/Domain/Navigation/ShortestPathPlanner.cs)

---

### 問題三：標註 SmartRopeNavigator 為過時 ✅

在 Edge-Driven 架構下，`InjectRopeEdges()` + A* 已完全取代 BFS 繩索搜尋。
加上 `[Obsolete]` 屬性，防止未來誤用。

render_diffs(file:///d:/Full_end/C%23/ArtaleAI/Services/SmartRopeNavigator.cs)

---

### 問題四：消除 Magic Number ✅

`PathNode.GetPriority()` 中的 `50` 硬編碼已提取為具名常數 `VerticalPenaltyThreshold`。

render_diffs(file:///d:/Full_end/C%23/ArtaleAI/Core/PathNode.cs)

---

### 問題二：雙軌系統（PathNode vs NavigationNode）⏳ 暫擱

此問題屬於長期架構遷移，目前兩套系統功能上並無衝突，列入後續計畫。

## 驗證結果

- **編譯測試**：`dotnet build` 通過，exit code 0。
