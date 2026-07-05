using ArtaleAI.Models.Config;
using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.UI.MapEditing;
using ArtaleAI.Utils;
using System.Drawing.Drawing2D;

namespace ArtaleAI.UI
{
    /// <summary>小地圖路徑幾何標記工具 (Geometry Marker)；以 Platforms 與 Ropes 為主要真實來源 (SSOT)。</summary>
    public class MapEditor
    {
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
        private int _manualEdgeStartIndex = -1;

        public float ZoomScale { get; set; } = 1.0f;

        public event Action<int>? OnNodeSelected;

        private static NavigationActionType ActionTypeFromResolvedUiCode(int uiCode)
        {
            // UI Code 現在與 NavigationActionType 列舉值完全對應
            return (NavigationActionType)uiCode;
        }

        private string AllocatePlatformId()
        {
            _currentMapData.Platforms ??= new List<PlatformSegmentData>();
            var taken = new HashSet<string>(
                _currentMapData.Platforms.Select(p => p.Id),
                StringComparer.Ordinal);
            for (int i = _currentMapData.Platforms.Count; ; i++)
            {
                string id = $"plat_{i}";
                if (taken.Add(id)) return id;
            }
        }

        private static float ComputeEdgeCost(int actionCode, float distance)
        {
            var type = (NavigationActionType)actionCode;
            return type switch
            {
                NavigationActionType.Walk => distance,
                NavigationActionType.Jump => 8.0f,
                NavigationActionType.SideJump => 8.0f,
                NavigationActionType.JumpDown => 2.0f,
                _ => distance
            };
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
        }

        private static float Distance(PointF a, NavNodeData b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
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
            _currentMapData.Platforms ??= new List<PlatformSegmentData>();
            _currentMapData.ManualEdges ??= new List<NavEdgeData>();

            _selectedNodeIndex = -1;
            _manualEdgeStartIndex = -1;
            _startPoint = null;
            _previewPoint = null;

            // 載入時立即跑一次拓撲生成以確保 UI 能正確顯示預覽
            MapGenerationService.BuildHTopology(_currentMapData);
        }

        public MapData GetCurrentMapData()
        {
            MapGenerationService.BuildHTopology(_currentMapData);
            return _currentMapData;
        }

        public void SetEditMode(EditMode mode)
        {
            if ((_currentEditMode == EditMode.Platform ||
                 _currentEditMode == EditMode.Rope) &&
                _startPoint.HasValue)
            {
                Logger.Info($"[編輯器] 放棄未完成的幾何繪製: {_currentEditMode}");
                _startPoint = null;
                _previewPoint = null;
            }

            if (_currentEditMode == EditMode.ManualEdge && mode != EditMode.ManualEdge)
            {
                _manualEdgeStartIndex = -1;
                _startPoint = null;
                _previewPoint = null;
            }

            _currentEditMode = mode;

            if (mode == EditMode.ManualEdge)
            {
                _manualEdgeStartIndex = -1;
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

            if (_currentEditMode == EditMode.Platform)
            {
                // 鎖定 Y 軸進行水平對齊預覽
                _previewPoint = new PointF(
                    screenPoint.X - minimapBounds.X,
                    _startPoint.Value.Y);
            }
            else
            {
                _previewPoint = new PointF(
                    screenPoint.X - minimapBounds.X,
                    screenPoint.Y - minimapBounds.Y);
            }
        }

        public void HandleClick(PointF screenPoint, MouseButtons button = MouseButtons.Left)
        {
            if (minimapBounds.IsEmpty) return;

            var relativePoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);

            if (_currentEditMode == EditMode.Select)
            {
                if (button == MouseButtons.Left)
                {
                    int nearestIndex = FindNearestNodeIndex(relativePoint);
                    _selectedNodeIndex = nearestIndex;
                    if (_selectedNodeIndex != -1)
                    {
                        var node = _currentMapData.Nodes[_selectedNodeIndex];
                        Logger.Info($"[編輯器] 選取生成節點 {_selectedNodeIndex} (Id={node.Id})");
                        OnNodeSelected?.Invoke(node.EditorActionCode);
                    }
                    else
                    {
                        OnNodeSelected?.Invoke(-1);
                    }
                }
            }
            else if (_currentEditMode == EditMode.Platform)
            {
                if (button == MouseButtons.Right)
                {
                    _startPoint = null;
                    _previewPoint = null;
                    Logger.Info("[編輯器] 取消平台幾何繪製");
                }
                else
                {
                    if (!_startPoint.HasValue)
                    {
                        _startPoint = relativePoint;
                        Logger.Info($"[編輯器] 設定平台起點: ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                    }
                    else
                    {
                        var start = _startPoint.Value;
                        var end = relativePoint;

                        float length = Math.Abs(start.X - end.X);
                        if (length < 2.0f)
                        {
                            Logger.Warning($"[編輯器] 平台幾何長度過短 ({length:F1} < 2.0)，取消建立");
                            _startPoint = null;
                            _previewPoint = null;
                            return;
                        }

                        float x1 = Math.Min(start.X, end.X);
                        float x2 = Math.Max(start.X, end.X);
                        float y = start.Y;

                        var newPlat = new PlatformSegmentData
                        {
                            Id = AllocatePlatformId(),
                            X1 = (float)Math.Round(x1, 1),
                            X2 = (float)Math.Round(x2, 1),
                            Y = (float)Math.Round(y, 1)
                        };

                        _currentMapData.Platforms.Add(newPlat);
                        Logger.Info($"[編輯器] 建立平台幾何: Id={newPlat.Id}, X1={newPlat.X1:F1}, X2={newPlat.X2:F1}, Y={newPlat.Y:F1}");

                        _startPoint = null;
                        _previewPoint = null;

                        MapGenerationService.BuildHTopology(_currentMapData);
                    }
                }
            }
            else if (_currentEditMode == EditMode.Rope)
            {
                if (button == MouseButtons.Right)
                {
                    _startPoint = null;
                    _previewPoint = null;
                    Logger.Info("[編輯器] 取消繩索幾何繪製");
                }
                else
                {
                    if (!_startPoint.HasValue)
                    {
                        _startPoint = relativePoint;
                        Logger.Info($"[編輯器] 設定繩索起點: ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                    }
                    else
                    {
                        var start = _startPoint.Value;
                        var end = relativePoint;

                        float length = Math.Abs(start.Y - end.Y);
                        if (length < 2.0f)
                        {
                            Logger.Warning($"[編輯器] 繩索幾何長度過短 ({length:F1} < 2.0)，取消建立");
                            _startPoint = null;
                            _previewPoint = null;
                            return;
                        }

                        float topY = Math.Min(start.Y, end.Y);
                        float bottomY = Math.Max(start.Y, end.Y);
                        float x = start.X;

                        _currentMapData.Ropes.Add(new[] {
                            (float)Math.Round(x, 1),
                            (float)Math.Round(topY, 1),
                            (float)Math.Round(bottomY, 1)
                        });

                        Logger.Info($"[編輯器] 建立繩索幾何: X={x:F1}, Y={topY:F1}~{bottomY:F1}");

                        _startPoint = null;
                        _previewPoint = null;

                        MapGenerationService.BuildHTopology(_currentMapData);
                    }
                }
            }
            else if (_currentEditMode == EditMode.ManualEdge)
            {
                if (button == MouseButtons.Right)
                {
                    _manualEdgeStartIndex = -1;
                    _startPoint = null;
                    _previewPoint = null;
                    Logger.Info("[編輯器] 取消建立例外邊");
                }
                else
                {
                    int clickedIndex = FindNearestNodeIndex(relativePoint);
                    if (clickedIndex != -1)
                    {
                        if (_manualEdgeStartIndex == -1)
                        {
                            _manualEdgeStartIndex = clickedIndex;
                            var node = _currentMapData.Nodes[clickedIndex];
                            _startPoint = new PointF(node.X, node.Y);
                            Logger.Info($"[編輯器] 選擇例外邊起點: {clickedIndex} (Id={node.Id})");
                        }
                        else
                        {
                            if (clickedIndex != _manualEdgeStartIndex)
                            {
                                var fromNode = _currentMapData.Nodes[_manualEdgeStartIndex];
                                var toNode = _currentMapData.Nodes[clickedIndex];

                                if (string.Equals(fromNode.Id, toNode.Id, StringComparison.Ordinal))
                                {
                                    Logger.Warning("[編輯器] 例外邊不可連接相同節點，取消建立");
                                    _manualEdgeStartIndex = -1;
                                    _startPoint = null;
                                    _previewPoint = null;
                                    return;
                                }

                                _currentMapData.ManualEdges ??= new List<NavEdgeData>();
                                bool duplicated = _currentMapData.ManualEdges.Any(e =>
                                    string.Equals(e.FromNodeId, fromNode.Id, StringComparison.Ordinal) &&
                                    string.Equals(e.ToNodeId, toNode.Id, StringComparison.Ordinal));
                                if (duplicated)
                                {
                                    Logger.Warning("[編輯器] 手動例外邊已存在，不予重複建立");
                                    _manualEdgeStartIndex = -1;
                                    _startPoint = null;
                                    _previewPoint = null;
                                    return;
                                }

                                var actionType = ActionTypeFromResolvedUiCode(_currentActionType);
                                float dist = (float)Math.Sqrt(Math.Pow(fromNode.X - toNode.X, 2) + Math.Pow(fromNode.Y - toNode.Y, 2));
                                float cost = ComputeEdgeCost(_currentActionType, dist);

                                _currentMapData.ManualEdges.Add(new NavEdgeData
                                {
                                    FromNodeId = fromNode.Id,
                                    ToNodeId = toNode.Id,
                                    ActionType = actionType,
                                    Cost = cost
                                });

                                Logger.Info($"[編輯器] 建立手動例外邊: {fromNode.Id} -> {toNode.Id} (Action={actionType})");

                                _manualEdgeStartIndex = -1;
                                _startPoint = null;
                                _previewPoint = null;

                                MapGenerationService.BuildHTopology(_currentMapData);
                            }
                            else
                            {
                                Logger.Info("[編輯器] 取消例外邊（點選同一起點）");
                                _manualEdgeStartIndex = -1;
                                _startPoint = null;
                                _previewPoint = null;
                            }
                        }
                    }
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

            // 1. 繪製實際的 Platforms 幾何線段 (萊姆綠粗線)
            if (_currentMapData.Platforms?.Any() == true)
            {
                foreach (var plat in _currentMapData.Platforms)
                {
                    var p1 = convert(new PointF(minimapBounds.X + plat.X1, minimapBounds.Y + plat.Y));
                    var p2 = convert(new PointF(minimapBounds.X + plat.X2, minimapBounds.Y + plat.Y));

                    using (var pen = new Pen(Color.Lime, 4.0f))
                    {
                        g.DrawLine(pen, p1, p2);
                    }
                    g.FillRectangle(Brushes.White, p1.X - 2, p1.Y - 2, 4, 4);
                    g.FillRectangle(Brushes.White, p2.X - 2, p2.Y - 2, 4, 4);
                }
            }

            // 2. 繪製拓撲邊 (細線代表自動生成，醒目橘線代表手動例外)
            if (_currentMapData.Edges.Count > 0)
            {
                foreach (var edge in _currentMapData.Edges)
                {
                    int fromIdx = FindNodeIndexById(edge.FromNodeId);
                    int toIdx = FindNodeIndexById(edge.ToNodeId);
                    if (fromIdx < 0 || toIdx < 0) continue;

                    var p1Data = _currentMapData.Nodes[fromIdx];
                    var p2Data = _currentMapData.Nodes[toIdx];

                    bool isManual = _currentMapData.ManualEdges != null && _currentMapData.ManualEdges.Any(me =>
                        string.Equals(me.FromNodeId, edge.FromNodeId, StringComparison.Ordinal) &&
                        string.Equals(me.ToNodeId, edge.ToNodeId, StringComparison.Ordinal));

                    Color lineColor = GetEdgeDrawColor(edge);
                    var p1 = convert(new PointF(minimapBounds.X + p1Data.X, minimapBounds.Y + p1Data.Y));
                    var p2 = convert(new PointF(minimapBounds.X + p2Data.X, minimapBounds.Y + p2Data.Y));

                    float penWidth = isManual ? 2.5f : 1.5f;
                    Color drawColor = isManual ? Color.OrangeRed : Color.FromArgb(120, lineColor);

                    using (var pen = new Pen(drawColor, penWidth))
                    {
                        g.DrawLine(pen, p1, p2);

                        float midX = (p1.X + p2.X) / 2;
                        float midY = (p1.Y + p2.Y) / 2;
                        DrawArrow(g, p1, p2, new PointF(midX, midY), isManual ? Brushes.OrangeRed : Brushes.Cyan);
                    }
                }
            }

            // 3. 繪製自動生成的拓撲節點 (灰色小點代表自動生成，黃/白代表選取/Hover高亮，橘紅代表例外邊端點)
            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var nav = _currentMapData.Nodes[i];
                var pos = convert(new PointF(minimapBounds.X + nav.X, minimapBounds.Y + nav.Y));

                bool isManualEndpoint = _currentMapData.ManualEdges != null && _currentMapData.ManualEdges.Any(me =>
                    string.Equals(me.FromNodeId, nav.Id, StringComparison.Ordinal) ||
                    string.Equals(me.ToNodeId, nav.Id, StringComparison.Ordinal));

                Color nodeColor = Color.FromArgb(180, Color.DarkGray); // 預設中性灰
                float radius = PointRadius - 0.5f;

                if (i == _selectedNodeIndex)
                {
                    nodeColor = Color.Yellow;
                    g.FillEllipse(Brushes.Yellow, pos.X - radius - 2, pos.Y - radius - 2, (radius + 2) * 2, (radius + 2) * 2);
                    g.DrawEllipse(Pens.Black, pos.X - radius - 2, pos.Y - radius - 2, (radius + 2) * 2, (radius + 2) * 2);
                }
                else if (i == _hoveredNodeIndex)
                {
                    nodeColor = Color.White;
                    g.DrawEllipse(Pens.White, pos.X - radius - 1, pos.Y - radius - 1, (radius + 1) * 2, (radius + 1) * 2);
                }
                else if (isManualEndpoint)
                {
                    nodeColor = Color.OrangeRed;
                }

                using (var brush = new SolidBrush(nodeColor))
                {
                    g.FillEllipse(brush, pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
                }

                if (i == 0) DrawLabel(g, pos, "Start", Color.Lime);
                else if (i == _currentMapData.Nodes.Count - 1) DrawLabel(g, pos, "End", Color.Red);

                if (i == _selectedNodeIndex)
                {
                    string actionName = GetActionName(nav.EditorActionCode);
                    DrawLabel(g, new PointF(pos.X, pos.Y - 15), $"{i}: {actionName}", Color.Yellow);
                }
            }

            // 4. 繪製繩索幾何 (黃色粗線)
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

                    using var pen = new Pen(Color.Yellow, 3.5f);
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

            bool isRope = edge.InputSequence?.Any(s => s.StartsWith("ropeX:")) ?? false;
            if (isRope) return Color.Cyan;

            return edge.ActionType switch
            {
                NavigationActionType.SideJump => Color.MediumPurple,
                NavigationActionType.JumpDown => Color.Yellow,
                NavigationActionType.Jump => Color.DeepSkyBlue,
                NavigationActionType.Teleport => Color.OrangeRed,
                _ => Color.White
            };
        }

        private void DrawPreviewShapes(Graphics g, Func<PointF, PointF> convert)
        {
            bool isLineMode = _currentEditMode == EditMode.Platform ||
                               _currentEditMode == EditMode.Rope ||
                               _currentEditMode == EditMode.ManualEdge;

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
            float threshold = SelectionRadius * 2.0f; // 4.0 像素

            PlatformSegmentData? bestPlatform = null;
            float bestPlatformDist = float.MaxValue;

            if (_currentMapData.Platforms != null)
            {
                foreach (var plat in _currentMapData.Platforms)
                {
                    float dist = GetDistanceToPlatform(clickPosition, plat);
                    if (dist < threshold && dist < bestPlatformDist)
                    {
                        bestPlatformDist = dist;
                        bestPlatform = plat;
                    }
                }
            }

            float[]? bestRope = null;
            float bestRopeDist = float.MaxValue;
            int bestRopeIdx = -1;

            if (_currentMapData.Ropes != null)
            {
                for (int i = 0; i < _currentMapData.Ropes.Count; i++)
                {
                    var rope = _currentMapData.Ropes[i];
                    if (rope.Length < 3) continue;

                    float dist = GetDistanceToRope(clickPosition, rope);
                    if (dist < threshold && dist < bestRopeDist)
                    {
                        bestRopeDist = dist;
                        bestRope = rope;
                        bestRopeIdx = i;
                    }
                }
            }

            NavEdgeData? bestManualEdge = null;
            float bestManualEdgeDist = float.MaxValue;

            if (_currentMapData.ManualEdges != null)
            {
                foreach (var edge in _currentMapData.ManualEdges)
                {
                    int fromIdx = FindNodeIndexById(edge.FromNodeId);
                    int toIdx = FindNodeIndexById(edge.ToNodeId);
                    if (fromIdx < 0 || toIdx < 0) continue;

                    var fromNode = _currentMapData.Nodes[fromIdx];
                    var toNode = _currentMapData.Nodes[toIdx];

                    float dist = GetDistanceToEdge(clickPosition, fromNode, toNode);
                    if (dist < threshold && dist < bestManualEdgeDist)
                    {
                        bestManualEdgeDist = dist;
                        bestManualEdge = edge;
                    }
                }
            }

            // 決定最近的命中幾何物件與例外邊
            float minOfAll = Math.Min(bestPlatformDist, Math.Min(bestRopeDist, bestManualEdgeDist));

            if (minOfAll >= threshold)
            {
                Logger.Info("[編輯器] 未命中任何可刪除的幾何物件或手動邊");
                return;
            }

            if (bestPlatform != null && Math.Abs(bestPlatformDist - minOfAll) < 0.001f)
            {
                _currentMapData.Platforms.Remove(bestPlatform);
                Logger.Info($"[編輯器] 刪除平台幾何: Id={bestPlatform.Id}, Y={bestPlatform.Y:F1}");
                MapGenerationService.BuildHTopology(_currentMapData);
            }
            else if (bestRope != null && bestRopeIdx >= 0 && Math.Abs(bestRopeDist - minOfAll) < 0.001f)
            {
                _currentMapData.Ropes.RemoveAt(bestRopeIdx);
                Logger.Info($"[編輯器] 刪除繩索幾何: X={bestRope[0]:F1}, Y={bestRope[1]:F1}~{bestRope[2]:F1}");
                MapGenerationService.BuildHTopology(_currentMapData);
            }
            else if (bestManualEdge != null && Math.Abs(bestManualEdgeDist - minOfAll) < 0.001f)
            {
                _currentMapData.ManualEdges.Remove(bestManualEdge);
                Logger.Info($"[編輯器] 刪除手動例外邊: {bestManualEdge.FromNodeId} -> {bestManualEdge.ToNodeId}");
                MapGenerationService.BuildHTopology(_currentMapData);
            }
        }

        private static float GetDistanceToPlatform(PointF p, PlatformSegmentData plat)
        {
            if (p.X >= plat.X1 && p.X <= plat.X2)
            {
                return Math.Abs(p.Y - plat.Y);
            }
            if (p.X < plat.X1)
            {
                float dx = plat.X1 - p.X;
                float dy = plat.Y - p.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                float dx = plat.X2 - p.X;
                float dy = plat.Y - p.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
        }

        private static float GetDistanceToRope(PointF p, float[] rope)
        {
            float ropeX = rope[0];
            float topY = rope[1];
            float bottomY = rope[2];

            if (p.Y >= topY && p.Y <= bottomY)
            {
                return Math.Abs(p.X - ropeX);
            }
            if (p.Y < topY)
            {
                float dx = ropeX - p.X;
                float dy = topY - p.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
            else
            {
                float dx = ropeX - p.X;
                float dy = bottomY - p.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
        }

        private static float GetDistanceToEdge(PointF p, NavNodeData fromNode, NavNodeData toNode)
        {
            float ax = fromNode.X;
            float ay = fromNode.Y;
            float bx = toNode.X;
            float by = toNode.Y;

            float abx = bx - ax;
            float aby = by - ay;
            float ab2 = abx * abx + aby * aby;
            if (ab2 < 0.001f)
            {
                return (float)Math.Sqrt((p.X - ax) * (p.X - ax) + (p.Y - ay) * (p.Y - ay));
            }

            float apx = p.X - ax;
            float apy = p.Y - ay;
            float t = (apx * abx + apy * aby) / ab2;
            t = Math.Max(0.0f, Math.Min(1.0f, t));

            float cx = ax + t * abx;
            float cy = ay + t * aby;

            float dx = p.X - cx;
            float dy = p.Y - cy;
            return (float)Math.Sqrt(dx * dx + dy * dy);
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
            var type = (NavigationActionType)action;
            return type switch
            {
                NavigationActionType.Walk => Color.White,
                NavigationActionType.Jump => Color.DeepSkyBlue,
                NavigationActionType.SideJump => Color.Purple,
                NavigationActionType.JumpDown => Color.Yellow,
                NavigationActionType.Teleport => Color.OrangeRed,
                _ => Color.Gray
            };
        }

        private string GetActionName(int action)
        {
            var type = (NavigationActionType)action;
            return type switch
            {
                NavigationActionType.Walk => "Walk",
                NavigationActionType.Jump => "Jump",
                NavigationActionType.SideJump => "SideJump",
                NavigationActionType.JumpDown => "JumpDown",
                NavigationActionType.Teleport => "Teleport",
                _ => $"Act:{action}"
            };
        }

        private void DrawArrow(Graphics g, PointF p1, PointF p2, PointF mid, Brush brush)
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

            g.FillPolygon(brush, arrow);
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
