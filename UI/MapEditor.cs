using ArtaleAI.Models.Config;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.UI.MapEditing;
using ArtaleAI.Utils;
using System.Drawing.Drawing2D;

namespace ArtaleAI.UI
{
    /// <summary>小地圖上導航節點、邊、繩索之狀態與 GDI+ 繪製；持久化僅使用 <see cref="MapData.Nodes"/>／<see cref="MapData.Edges"/>。</summary>
    public class MapEditor
    {
        private const int SmartSideJumpActionCode = 13;

        private MapData _currentMapData = new MapData();
        private EditMode _currentEditMode = EditMode.None;

        private PointF? _startPoint = null;
        private PointF? _previewPoint = null;
        private Rectangle minimapBounds = Rectangle.Empty;

        public MapEditor(AppConfig settings)
        {
            _ = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        private const float PointRadius = 4.0f;
        private const float SelectionRadius = 2.0f;

        private int _selectedNodeIndex = -1;
        private int _hoveredNodeIndex = -1;
        private int _currentActionType = 0;
        private int _waypointAnchorIndex = -1;

        public event Action<int>? OnNodeSelected;

        private static NavigationActionType ActionTypeFromResolvedUiCode(int uiCode)
        {
            return uiCode switch
            {
                1 => NavigationActionType.Walk,
                2 => NavigationActionType.Walk,
                3 => NavigationActionType.ClimbUp,
                4 => NavigationActionType.ClimbDown,
                5 => NavigationActionType.JumpLeft,
                6 => NavigationActionType.JumpRight,
                7 => NavigationActionType.JumpDown,
                8 => NavigationActionType.Jump,
                9 => NavigationActionType.JumpLeft,
                10 => NavigationActionType.JumpRight,
                11 => NavigationActionType.ClimbUp,
                12 => NavigationActionType.ClimbDown,
                _ => NavigationActionType.Walk
            };
        }

        private static int ResolveActionCodeByGeometry(PointF fromPoint, PointF toPoint, int actionCode)
        {
            if (actionCode != SmartSideJumpActionCode)
            {
                return actionCode;
            }

            float dx = toPoint.X - fromPoint.X;
            return dx < 0 ? 9 : 10;
        }

        private static int JumpUiCodeOpposite(int jumpLeftOrRight) => jumpLeftOrRight == 9 ? 10 : 9;

        private static float ComputeEdgeCost(int resolvedUi, float distance)
        {
            return resolvedUi switch
            {
                0 => distance,
                11 => 5.0f,
                12 => 3.0f,
                9 or 10 => 8.0f,
                4 => 2.0f,
                _ => 6.0f
            };
        }

        private string AllocateNodeId()
        {
            var taken = new HashSet<string>(
                _currentMapData.Nodes.Select(n => n.Id),
                StringComparer.Ordinal);
            for (int i = _currentMapData.Nodes.Count; ; i++)
            {
                string id = $"n{i}";
                if (taken.Add(id)) return id;
            }
        }

        private void EnsureNodeIds()
        {
            foreach (var n in _currentMapData.Nodes)
            {
                if (string.IsNullOrWhiteSpace(n.Id))
                {
                    n.Id = AllocateNodeId();
                }
            }
        }

        private int FindNodeIndexById(string id)
        {
            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                if (string.Equals(_currentMapData.Nodes[i].Id, id, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        public void SetCurrentActionType(int actionType)
        {
            _currentActionType = actionType;
            Logger.Info($"[編輯器] SetCurrentActionType 被呼叫: actionType={actionType}");
            if (_selectedNodeIndex != -1)
            {
                UpdateSelectedNodeAction(actionType);
                ApplyActionToSelectedNodeConnections();
            }
        }

        private void UpdateSelectedNodeAction(int actionType)
        {
            if (_selectedNodeIndex < 0 || _selectedNodeIndex >= _currentMapData.Nodes.Count) return;

            var node = _currentMapData.Nodes[_selectedNodeIndex];
            if (node.Type == "Rope")
            {
                Logger.Warning($"[編輯器] 試圖對繩索節點設定動作，已攔截。Index: {_selectedNodeIndex}");
                return;
            }

            if (actionType == SmartSideJumpActionCode)
            {
                return;
            }

            node.EditorActionCode = actionType;
            Logger.Info($"[編輯器] 更新節點 {_selectedNodeIndex} EditorActionCode 為 {actionType}");
        }

        private void ApplyActionToSelectedNodeConnections()
        {
            if (_selectedNodeIndex < 0 || _selectedNodeIndex >= _currentMapData.Nodes.Count)
            {
                return;
            }

            var fromNav = _currentMapData.Nodes[_selectedNodeIndex];
            var fromPt = new PointF(fromNav.X, fromNav.Y);
            string fromId = fromNav.Id;

            if (_currentActionType == SmartSideJumpActionCode)
            {
                bool updated = false;
                bool reverseUpdated = false;

                if (TryResolveSmartSideJumpTarget(_selectedNodeIndex, out int toIdx))
                {
                    var toNav = _currentMapData.Nodes[toIdx];
                    int resolvedAction = ResolveActionCodeByGeometry(
                        fromPt,
                        new PointF(toNav.X, toNav.Y),
                        SmartSideJumpActionCode);

                    bool forwardUpdated = UpsertEdge(_selectedNodeIndex, toIdx, resolvedAction);
                    if (forwardUpdated)
                    {
                        updated = true;
                        reverseUpdated = EnsureReverseJumpConnection(_selectedNodeIndex, toIdx, resolvedAction);
                        Logger.Info($"[編輯器] 節點 {_selectedNodeIndex} 已套用 SmartSideJump 到邊界目標節點 {toIdx}。");
                    }
                }

                if (updated)
                {
                    Logger.Info($"[編輯器] 套用節點 {_selectedNodeIndex} 的出邊動作為 {GetActionName(_currentActionType)}");
                    if (reverseUpdated)
                    {
                        Logger.Info($"[編輯器] 已自動補齊節點 {_selectedNodeIndex} 相關反向跳邊。");
                    }
                }
                else
                {
                    Logger.Info($"[編輯器] 節點 {_selectedNodeIndex} 沒有可更新的邊界跳邊（未變更）。");
                }

                return;
            }

            bool regularUpdated = false;
            var edges = _currentMapData.Edges;
            int originalCount = edges.Count;

            for (int i = 0; i < originalCount; i++)
            {
                var edge = edges[i];
                if (!string.Equals(edge.FromNodeId, fromId, StringComparison.Ordinal)) continue;

                int toIdx = FindNodeIndexById(edge.ToNodeId);
                if (toIdx < 0) continue;

                var toNav = _currentMapData.Nodes[toIdx];
                int resolvedAction = ResolveActionCodeByGeometry(
                    fromPt,
                    new PointF(toNav.X, toNav.Y),
                    _currentActionType);

                var newType = ActionTypeFromResolvedUiCode(resolvedAction);
                float dist = Distance(fromPt, toNav);
                float cost = ComputeEdgeCost(resolvedAction, dist);

                if (edge.ActionType != newType || Math.Abs(edge.Cost - cost) > 0.001f)
                {
                    edge.ActionType = newType;
                    edge.Cost = cost;
                    regularUpdated = true;
                }
            }

            if (regularUpdated)
            {
                Logger.Info($"[編輯器] 套用節點 {_selectedNodeIndex} 的出邊動作為 {GetActionName(_currentActionType)}");
            }
            else
            {
                Logger.Info($"[編輯器] 節點 {_selectedNodeIndex} 沒有可更新的出邊（未變更）。");
            }
        }

        private static float Distance(PointF a, NavNodeData b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private bool TryResolveSmartSideJumpTarget(int fromIdx, out int bestToIdx)
        {
            bestToIdx = -1;
            if (fromIdx < 0 || fromIdx >= _currentMapData.Nodes.Count) return false;

            var fromNav = _currentMapData.Nodes[fromIdx];
            var fromPt = new PointF(fromNav.X, fromNav.Y);
            string fromId = fromNav.Id;

            float bestDistanceSq = float.MaxValue;

            foreach (var edge in _currentMapData.Edges)
            {
                if (!string.Equals(edge.FromNodeId, fromId, StringComparison.Ordinal)) continue;
                if (edge.ActionType != NavigationActionType.JumpLeft &&
                    edge.ActionType != NavigationActionType.JumpRight) continue;

                int candidateTo = FindNodeIndexById(edge.ToNodeId);
                if (candidateTo < 0) continue;

                var toNav = _currentMapData.Nodes[candidateTo];
                float dx = toNav.X - fromPt.X;
                float dy = toNav.Y - fromPt.Y;
                float distanceSq = dx * dx + dy * dy;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestToIdx = candidateTo;
                }
            }

            return bestToIdx >= 0;
        }

        private bool UpsertEdge(int fromIdx, int toIdx, int resolvedUiCode)
        {
            if (fromIdx < 0 || toIdx < 0 || fromIdx >= _currentMapData.Nodes.Count || toIdx >= _currentMapData.Nodes.Count)
                return false;

            string fromId = _currentMapData.Nodes[fromIdx].Id;
            string toId = _currentMapData.Nodes[toIdx].Id;
            var fromPt = new PointF(_currentMapData.Nodes[fromIdx].X, _currentMapData.Nodes[fromIdx].Y);
            var toNav = _currentMapData.Nodes[toIdx];

            var existing = _currentMapData.Edges.FirstOrDefault(e =>
                string.Equals(e.FromNodeId, fromId, StringComparison.Ordinal) &&
                string.Equals(e.ToNodeId, toId, StringComparison.Ordinal));

            var actionType = ActionTypeFromResolvedUiCode(resolvedUiCode);
            float dist = Distance(fromPt, toNav);
            float cost = ComputeEdgeCost(resolvedUiCode, dist);

            if (existing == null)
            {
                _currentMapData.Edges.Add(new NavEdgeData
                {
                    FromNodeId = fromId,
                    ToNodeId = toId,
                    ActionType = actionType,
                    Cost = cost
                });
                return true;
            }

            if (existing.ActionType == actionType && Math.Abs(existing.Cost - cost) < 0.001f) return false;
            existing.ActionType = actionType;
            existing.Cost = cost;
            return true;
        }

        private void ApplyDirectedConnection(
            int fromIdx,
            int toIdx,
            int uiActionCode,
            bool ensureReverseJumpIfSideJump,
            out int resolvedAction)
        {
            resolvedAction = 0;

            if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx) return;
            if (fromIdx >= _currentMapData.Nodes.Count || toIdx >= _currentMapData.Nodes.Count) return;

            var fromN = _currentMapData.Nodes[fromIdx];
            var toN = _currentMapData.Nodes[toIdx];

            resolvedAction = ResolveActionCodeByGeometry(
                new PointF(fromN.X, fromN.Y),
                new PointF(toN.X, toN.Y),
                uiActionCode);

            UpsertEdge(fromIdx, toIdx, resolvedAction);

            if (resolvedAction != 0)
            {
                fromN.EditorActionCode = resolvedAction;
            }

            if (ensureReverseJumpIfSideJump && (resolvedAction == 9 || resolvedAction == 10))
            {
                EnsureReverseJumpConnection(fromIdx, toIdx, resolvedAction);
            }

            PruneSelfLoopEdges(_currentMapData);
        }

        private static void PruneSelfLoopEdges(MapData data)
        {
            for (int i = data.Edges.Count - 1; i >= 0; i--)
            {
                var e = data.Edges[i];
                if (string.Equals(e.FromNodeId, e.ToNodeId, StringComparison.Ordinal))
                {
                    data.Edges.RemoveAt(i);
                    Logger.Info($"[編輯器] 自動清理自我連結: {e.FromNodeId}");
                }
            }
        }

        private bool EnsureReverseJumpConnection(int fromIdx, int toIdx, int resolvedForwardAction)
        {
            if (resolvedForwardAction != 9 && resolvedForwardAction != 10) return false;

            int expectedReverseUi = JumpUiCodeOpposite(resolvedForwardAction);
            var reverseType = ActionTypeFromResolvedUiCode(expectedReverseUi);

            string fromId = _currentMapData.Nodes[fromIdx].Id;
            string toId = _currentMapData.Nodes[toIdx].Id;

            var reverseEdge = _currentMapData.Edges.FirstOrDefault(e =>
                string.Equals(e.FromNodeId, toId, StringComparison.Ordinal) &&
                string.Equals(e.ToNodeId, fromId, StringComparison.Ordinal));

            var toN = _currentMapData.Nodes[toIdx];
            var fromN = _currentMapData.Nodes[fromIdx];
            float dist = Distance(new PointF(toN.X, toN.Y), fromN);
            float cost = ComputeEdgeCost(expectedReverseUi, dist);

            if (reverseEdge == null)
            {
                _currentMapData.Edges.Add(new NavEdgeData
                {
                    FromNodeId = toId,
                    ToNodeId = fromId,
                    ActionType = reverseType,
                    Cost = cost
                });
                toN.EditorActionCode = expectedReverseUi;
                return true;
            }

            if (reverseEdge.ActionType != reverseType || Math.Abs(reverseEdge.Cost - cost) > 0.001f)
            {
                reverseEdge.ActionType = reverseType;
                reverseEdge.Cost = cost;
                return true;
            }

            return false;
        }

        public void SetMinimapBounds(Rectangle bounds)
        {
            minimapBounds = bounds;
        }

        public void LoadMapData(MapData data)
        {
            _currentMapData = data ?? new MapData();
            _currentMapData.Nodes ??= new List<NavNodeData>();
            _currentMapData.Edges ??= new List<NavEdgeData>();
            _currentMapData.Ropes ??= new List<float[]>();

            EnsureNodeIds();

            _selectedNodeIndex = -1;
            _waypointAnchorIndex = -1;
            _startPoint = null;
            _previewPoint = null;
        }

        public MapData GetCurrentMapData()
        {
            MapGenerationService.BuildHTopology(_currentMapData);
            return _currentMapData;
        }

        public void SetEditMode(EditMode mode)
        {
            if ((_currentEditMode == EditMode.Waypoint ||
                 _currentEditMode == EditMode.Rope) &&
                _startPoint.HasValue)
            {
                Logger.Info($"[編輯器] 放棄未完成的繪製: {_currentEditMode}");
                _startPoint = null;
                _previewPoint = null;
            }

            if (_currentEditMode == EditMode.Link && mode != EditMode.Link)
            {
                _linkStartIndex = -1;
                _startPoint = null;
                _previewPoint = null;
            }

            _currentEditMode = mode;

            if (mode == EditMode.Link)
            {
                _linkStartIndex = -1;
                _startPoint = null;
                _previewPoint = null;
            }
        }

        public void UpdateMousePosition(PointF screenPoint)
        {
            if (!_startPoint.HasValue || minimapBounds.IsEmpty)
            {
                _previewPoint = null;
                return;
            }

            _previewPoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);
        }

        private int _linkStartIndex = -1;

        public void HandleClick(PointF screenPoint, MouseButtons button = MouseButtons.Left)
        {
            if (minimapBounds.IsEmpty) return;

            var relativePoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);

            if (_currentEditMode == EditMode.Select)
            {
                int nearestIndex = FindNearestNodeIndex(relativePoint);

                _selectedNodeIndex = nearestIndex;
                if (_selectedNodeIndex != -1)
                {
                    var node = _currentMapData.Nodes[_selectedNodeIndex];
                    int action = node.EditorActionCode;
                    ApplyActionToSelectedNodeConnections();
                    int actionForUi = _currentActionType == SmartSideJumpActionCode ? SmartSideJumpActionCode : action;
                    Logger.Info($"[編輯器] 選取節點 {_selectedNodeIndex} (Action={action})");
                    OnNodeSelected?.Invoke(actionForUi);
                }
                else
                {
                    OnNodeSelected?.Invoke(-1);
                }
            }
            else if (_currentEditMode == EditMode.Link)
            {
                int clickedIndex = FindNearestNodeIndex(relativePoint);

                if (clickedIndex != -1)
                {
                    if (_linkStartIndex == -1)
                    {
                        _linkStartIndex = clickedIndex;
                        var node = _currentMapData.Nodes[clickedIndex];
                        _startPoint = new PointF(node.X, node.Y);
                        Logger.Info($"[編輯器] 連結起點: {clickedIndex}");
                    }
                    else
                    {
                        if (clickedIndex != _linkStartIndex)
                        {
                            ApplyDirectedConnection(
                                _linkStartIndex,
                                clickedIndex,
                                _currentActionType,
                                ensureReverseJumpIfSideJump: true,
                                out int resolved);
                            Logger.Info(
                                $"[編輯器] 連結完成: {_linkStartIndex} -> {clickedIndex} (Action={GetActionName(resolved)})");
                        }
                        else
                        {
                            Logger.Info("[編輯器] 取消連結起點（點擊同一起點）");
                        }

                        _linkStartIndex = -1;
                        _startPoint = null;
                        _previewPoint = null;
                    }
                }
                else
                {
                    _linkStartIndex = -1;
                    _startPoint = null;
                    _previewPoint = null;
                }
            }
            else if (_currentEditMode == EditMode.Waypoint)
            {
                if (button == MouseButtons.Right)
                {
                    // 清除錨點：下一筆在空白處新增的節點不再自動連到「Nodes 清單上一筆」，
                    // 以達成「分段／截斷折線」；若要再接續連線請左鍵點既有節點設為錨點。
                    _waypointAnchorIndex = -1;
                    _startPoint = null;
                    Logger.Info("[編輯器] 路徑標記：右鍵已截斷鏈條，下一個新節點將獨立（不自動連邊），除非先左鍵選取錨點節點。");
                }
                else
                {
                    int clickedExisting = FindNearestNodeIndex(relativePoint);

                    if (clickedExisting != -1)
                    {
                        _waypointAnchorIndex = clickedExisting;
                        _selectedNodeIndex = clickedExisting;
                        var node = _currentMapData.Nodes[clickedExisting];
                        _startPoint = new PointF(node.X, node.Y);
                        OnNodeSelected?.Invoke(node.EditorActionCode);
                        Logger.Info($"[編輯器] 選中節點 {clickedExisting} 為錨點 (從此連出新節點)");
                    }
                    else
                    {
                        var newNode = new NavNodeData
                        {
                            Id = AllocateNodeId(),
                            X = (float)Math.Round(relativePoint.X, 1),
                            Y = (float)Math.Round(relativePoint.Y, 1),
                            Type = "Platform",
                            EditorActionCode = 0
                        };
                        _currentMapData.Nodes.Add(newNode);
                        int newIndex = _currentMapData.Nodes.Count - 1;

                        // 僅在已選錨點（含連續左鍵新增時自動帶入的上一節點）時自動拉邊；
                        // 右鍵截斷後錨點為 -1，不可再用「清單 index-1」推上一點，否則鏈條永遠不斷。
                        int prevIndex = _waypointAnchorIndex != -1 ? _waypointAnchorIndex : -1;

                        if (prevIndex != -1)
                        {
                            ApplyDirectedConnection(
                                prevIndex,
                                newIndex,
                                _currentActionType,
                                ensureReverseJumpIfSideJump: true,
                                out int actionToUse);
                            string actionName = GetActionName(actionToUse);
                            Logger.Info($"[手動選取] 新增節點 {newIndex} ({relativePoint.X:F1}, {relativePoint.Y:F1}) ← {actionName} ← 節點 {prevIndex}");
                        }
                        else
                        {
                            Logger.Info($"[編輯器] 新增起始節點 {newIndex} ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                        }

                        _waypointAnchorIndex = newIndex;
                        _startPoint = relativePoint;
                    }
                }
            }
            else if (_currentEditMode == EditMode.Rope)
            {
                _currentMapData.Ropes ??= new List<float[]>();

                if (!_startPoint.HasValue)
                {
                    _startPoint = relativePoint;
                    Logger.Info($"[編輯器] 繩索起點: ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                }
                else
                {
                    var start = _startPoint.Value;
                    var end = relativePoint;

                    float topY = Math.Min(start.Y, end.Y);
                    float bottomY = Math.Max(start.Y, end.Y);
                    float x = start.X;

                    _currentMapData.Ropes.Add(new[] {
                        (float)Math.Round(x, 1),
                        (float)Math.Round(topY, 1),
                        (float)Math.Round(bottomY, 1)
                    });

                    Logger.Info($"[編輯器] 建立繩索: X={x:F1}, Y={topY:F1}~{bottomY:F1}");

                    _startPoint = null;
                    _previewPoint = null;
                }
            }
            else if (_currentEditMode == EditMode.Delete)
            {
                HandleDeleteAction(relativePoint);
            }
        }

        private int FindNearestNodeIndex(PointF relativePoint)
        {
            if (_currentMapData.Nodes.Count == 0) return -1;

            int nearestIndex = -1;
            float minDistance = float.MaxValue;

            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var node = _currentMapData.Nodes[i];
                float dx = node.X - relativePoint.X;
                float dy = node.Y - relativePoint.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < SelectionRadius && dist < minDistance)
                {
                    minDistance = dist;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        public void Render(Graphics g, Func<PointF, PointF> convertToDisplay)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawCompletedShapes(g, convertToDisplay);
            DrawPreviewShapes(g, convertToDisplay);
        }

        private void DrawCompletedShapes(Graphics g, Func<PointF, PointF> convert)
        {
            if (minimapBounds.IsEmpty) return;

            if (_currentMapData.Edges.Count > 0)
            {
                foreach (var edge in _currentMapData.Edges)
                {
                    int fromIdx = FindNodeIndexById(edge.FromNodeId);
                    int toIdx = FindNodeIndexById(edge.ToNodeId);
                    if (fromIdx < 0 || toIdx < 0) continue;

                    var p1Data = _currentMapData.Nodes[fromIdx];
                    var p2Data = _currentMapData.Nodes[toIdx];

                    Color lineColor = GetEdgeDrawColor(edge);

                    var p1 = convert(new PointF(minimapBounds.X + p1Data.X, minimapBounds.Y + p1Data.Y));
                    var p2 = convert(new PointF(minimapBounds.X + p2Data.X, minimapBounds.Y + p2Data.Y));

                    using (var pen = new Pen(lineColor, 2.0f))
                    {
                        g.DrawLine(pen, p1, p2);

                        float midX = (p1.X + p2.X) / 2;
                        float midY = (p1.Y + p2.Y) / 2;
                        DrawArrow(g, p1, p2, new PointF(midX, midY));
                    }
                }
            }

            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var nav = _currentMapData.Nodes[i];
                var pos = convert(new PointF(minimapBounds.X + nav.X, minimapBounds.Y + nav.Y));
                int action = nav.EditorActionCode;

                Color nodeColor = GetNodeColor(action);
                float radius = PointRadius;

                if (i == _selectedNodeIndex)
                {
                    g.FillEllipse(Brushes.Yellow, pos.X - radius - 2, pos.Y - radius - 2, (radius + 2) * 2, (radius + 2) * 2);
                    g.DrawEllipse(Pens.Black, pos.X - radius - 2, pos.Y - radius - 2, (radius + 2) * 2, (radius + 2) * 2);
                }
                else if (i == _hoveredNodeIndex)
                {
                    g.DrawEllipse(Pens.White, pos.X - radius - 1, pos.Y - radius - 1, (radius + 1) * 2, (radius + 1) * 2);
                }

                using (var brush = new SolidBrush(nodeColor))
                {
                    g.FillEllipse(brush, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
                }

                if (i == 0) DrawLabel(g, pos, "Start", Color.Lime);
                else if (i == _currentMapData.Nodes.Count - 1) DrawLabel(g, pos, "End", Color.Red);

                if (i == _selectedNodeIndex)
                {
                    int displayAction = (_currentEditMode == EditMode.Select && _currentActionType == SmartSideJumpActionCode)
                        ? SmartSideJumpActionCode
                        : action;
                    string actionName = GetActionName(displayAction);
                    DrawLabel(g, new PointF(pos.X, pos.Y - 15), $"{i}: {actionName}", Color.Yellow);
                }
            }

            if (_currentMapData.Ropes?.Any() == true)
            {
                foreach (var rope in _currentMapData.Ropes)
                {
                    if (rope.Length < 3) continue;

                    float x = rope[0];
                    float topY = rope[1];
                    float bottomY = rope[2];

                    var pTop = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + topY));
                    var pBottom = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + bottomY));

                    using var pen = new Pen(Color.Yellow, 3f);
                    g.DrawLine(pen, pTop, pBottom);

                    g.FillRectangle(Brushes.Red, pTop.X - 3, pTop.Y - 3, 6, 6);
                    g.FillRectangle(Brushes.Green, pBottom.X - 3, pBottom.Y - 3, 6, 6);
                }
            }
        }

        private static Color GetEdgeDrawColor(NavEdgeData edge)
        {
            if (edge.ActionType == NavigationActionType.Walk)
            {
                return Color.Magenta;
            }

            return edge.ActionType switch
            {
                NavigationActionType.JumpLeft => Color.MediumPurple,
                NavigationActionType.JumpRight => Color.Purple,
                NavigationActionType.ClimbUp or NavigationActionType.ClimbDown => Color.Cyan,
                NavigationActionType.JumpDown => Color.Yellow,
                NavigationActionType.Jump => Color.DeepSkyBlue,
                _ => Color.White
            };
        }

        private void DrawPreviewShapes(Graphics g, Func<PointF, PointF> convert)
        {
            bool isLineMode = _currentEditMode == EditMode.Waypoint ||
                              _currentEditMode == EditMode.Rope ||
                              _currentEditMode == EditMode.Link;

            if (_startPoint.HasValue && _previewPoint.HasValue && isLineMode)
            {
                var startScreen = new PointF(
                    minimapBounds.X + _startPoint.Value.X,
                    minimapBounds.Y + _startPoint.Value.Y);
                var previewScreen = new PointF(
                    minimapBounds.X + _previewPoint.Value.X,
                    minimapBounds.Y + _previewPoint.Value.Y);

                using (var pen = new Pen(Color.Cyan, 2) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, convert(startScreen), convert(previewScreen));
                }
            }

            if (_startPoint.HasValue && isLineMode)
            {
                var startScreen = new PointF(
                    minimapBounds.X + _startPoint.Value.X,
                    minimapBounds.Y + _startPoint.Value.Y);
                var converted = convert(startScreen);
                using (var brush = new SolidBrush(Color.Red))
                {
                    g.FillEllipse(brush, converted.X - 2, converted.Y - 2, 4, 4);
                }
            }
        }

        private void HandleDeleteAction(PointF clickPosition)
        {
            float deletionRadius = SelectionRadius;

            int bestNodeIndex = -1;
            float bestNodeDistance = float.MaxValue;
            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var n = _currentMapData.Nodes[i];
                float dx = n.X - clickPosition.X;
                float dy = n.Y - clickPosition.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist <= deletionRadius && dist < bestNodeDistance)
                {
                    bestNodeDistance = dist;
                    bestNodeIndex = i;
                }
            }

            IList<float[]>? bestRopeList = null;
            int bestRopeIndex = -1;
            float bestRopeDistance = float.MaxValue;
            if (_currentMapData.Ropes?.Any() == true)
            {
                for (int i = 0; i < _currentMapData.Ropes.Count; i++)
                {
                    var coord = _currentMapData.Ropes[i];
                    if (coord.Length < 2) continue;

                    float dx = coord[0] - clickPosition.X;
                    float dy = coord[1] - clickPosition.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                    if (dist <= deletionRadius && dist < bestRopeDistance)
                    {
                        bestRopeDistance = dist;
                        bestRopeList = _currentMapData.Ropes;
                        bestRopeIndex = i;
                    }
                }
            }

            if (bestNodeIndex >= 0 && bestNodeDistance <= bestRopeDistance)
            {
                RemoveNodeAtIndex(bestNodeIndex);
                return;
            }

            if (bestRopeList != null && bestRopeIndex >= 0)
            {
                var deletedCoord = bestRopeList[bestRopeIndex];
                bestRopeList.RemoveAt(bestRopeIndex);
                Logger.Info($"刪除 Rope (Index {bestRopeIndex}): ({deletedCoord[0]:F1}, {deletedCoord[1]:F1})");
            }
        }

        private void RemoveNodeAtIndex(int bestIndex)
        {
            string removedId = _currentMapData.Nodes[bestIndex].Id;
            _currentMapData.Edges.RemoveAll(e =>
                string.Equals(e.FromNodeId, removedId, StringComparison.Ordinal) ||
                string.Equals(e.ToNodeId, removedId, StringComparison.Ordinal));

            if (_selectedNodeIndex == bestIndex) _selectedNodeIndex = -1;
            else if (_selectedNodeIndex > bestIndex) _selectedNodeIndex--;

            if (_waypointAnchorIndex == bestIndex) _waypointAnchorIndex = -1;
            else if (_waypointAnchorIndex > bestIndex) _waypointAnchorIndex--;

            if (_linkStartIndex == bestIndex) _linkStartIndex = -1;
            else if (_linkStartIndex > bestIndex) _linkStartIndex--;

            if (_hoveredNodeIndex == bestIndex) _hoveredNodeIndex = -1;
            else if (_hoveredNodeIndex > bestIndex) _hoveredNodeIndex--;

            var deleted = _currentMapData.Nodes[bestIndex];
            _currentMapData.Nodes.RemoveAt(bestIndex);
            Logger.Info($"刪除 Node (Index {bestIndex}): ({deleted.X:F1}, {deleted.Y:F1})");
        }

        public void UpdateHoveredNode(PointF screenPoint)
        {
            if (minimapBounds.IsEmpty) return;

            var relativePoint = new PointF(
               screenPoint.X - minimapBounds.X,
               screenPoint.Y - minimapBounds.Y);

            _hoveredNodeIndex = FindNearestNodeIndex(relativePoint);
        }

        private Color GetNodeColor(int action)
        {
            return action switch
            {
                9 => Color.MediumPurple,
                10 => Color.Purple,
                13 => Color.MediumPurple,
                11 => Color.Cyan,
                12 => Color.Cyan,
                4 => Color.Yellow,
                8 => Color.DeepSkyBlue,
                _ => Color.White
            };
        }

        private string GetActionName(int action)
        {
            return action switch
            {
                9 => "JumpLeft",
                10 => "JumpRight",
                13 => "SmartSideJump",
                11 => "ClimbUp",
                12 => "ClimbDn",
                4 => "DownJump",
                8 => "Jump",
                0 => "Walk",
                _ => $"Act:{action}"
            };
        }

        private void DrawArrow(Graphics g, PointF p1, PointF p2, PointF mid)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1) return;

            dx /= length;
            dy /= length;

            float arrowSize = 3;
            PointF[] arrow = new PointF[]
            {
                new PointF(mid.X + dx * arrowSize, mid.Y + dy * arrowSize),
                new PointF(mid.X - dx * arrowSize - dy * arrowSize, mid.Y - dy * arrowSize + dx * arrowSize),
                new PointF(mid.X - dx * arrowSize + dy * arrowSize, mid.Y - dy * arrowSize - dx * arrowSize)
            };

            g.FillPolygon(Brushes.Cyan, arrow);
        }

        private void DrawLabel(Graphics g, PointF pos, string text, Color color)
        {
            using (var font = new Font("Arial", 8))
            using (var brush = new SolidBrush(color))
            {
                var size = g.MeasureString(text, font);
                g.FillRectangle(Brushes.Black, pos.X, pos.Y - size.Height, size.Width, size.Height);
                g.DrawString(text, font, brush, pos.X, pos.Y - size.Height);
            }
        }
    }
}
