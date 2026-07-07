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
        private const float SelectionRadius = 5.0f;
        /// <summary>手動邊為細線，刪除命中需比平台/繩索更寬，且優先於幾何底圖。</summary>
        private const float ManualEdgeDeleteHitSlop = 8.0f;

        private enum DeleteTargetKind { None, ManualEdge, Rope, Platform }

        private sealed record DeleteTarget(
            DeleteTargetKind Kind,
            ManualEdgeAnchor? ManualEdge,
            float[]? Rope,
            int RopeIndex,
            PolylinePlatformData? Platform,
            PolylineHitResult? PlatformHit);

        private int _hoveredNodeIndex = -1;
        private int _currentActionType = 0;
        private PlatformAnchor? _manualEdgeStartAnchor;

        private PolylinePlatformData? _hoveredPlatform = null;
        private float[]? _hoveredRope = null;
        private ManualEdgeAnchor? _hoveredManualEdgeAnchor = null;
        private List<PointF> _activeDrawingPoints = new();
        private int _hoveredSegmentIndex = -1;
        private PointF _hoveredProjectionPoint = PointF.Empty;

        public float ZoomScale { get; set; } = 1.0f;

        private record PlatformAnchor(
            PolylinePlatformData Platform,
            PointF ProjectedPoint,
            int SegmentIndex,
            float Distance);

        private PlatformAnchor? FindNearestPlatformProjection(PointF p, float threshold)
        {
            PlatformAnchor? best = null;
            foreach (var plat in _currentMapData.PolylinePlatforms ?? new List<PolylinePlatformData>())
            {
                var hit = GetDistanceToPolyline(p, plat);
                if (hit.Distance <= threshold && (best == null || hit.Distance < best.Distance))
                {
                    best = new PlatformAnchor(plat, hit.ProjectionPoint, hit.SegmentIndex, hit.Distance);
                }
            }

            return best;
        }

        private static PointF RoundAnchorPoint(PointF p) =>
            new(
                (float)Math.Round(p.X, 1, MidpointRounding.AwayFromZero),
                (float)Math.Round(p.Y, 1, MidpointRounding.AwayFromZero));

        private static bool AnchorsEqual(ManualEdgeAnchor a, ManualEdgeAnchor b) =>
            string.Equals(a.FromPlatformId, b.FromPlatformId, StringComparison.Ordinal) &&
            string.Equals(a.ToPlatformId, b.ToPlatformId, StringComparison.Ordinal) &&
            Math.Abs(a.FromX - b.FromX) < 0.05f &&
            Math.Abs(a.FromY - b.FromY) < 0.05f &&
            Math.Abs(a.ToX - b.ToX) < 0.05f &&
            Math.Abs(a.ToY - b.ToY) < 0.05f;

        private static bool EdgeMatchesAnchor(NavEdgeData edge, ManualEdgeAnchor anchor)
        {
            string fromId = MapGenerationService.BuildVirtualNodeId(anchor.FromPlatformId, anchor.FromX, anchor.FromY);
            string toId = MapGenerationService.BuildVirtualNodeId(anchor.ToPlatformId, anchor.ToX, anchor.ToY);
            return string.Equals(edge.FromNodeId, fromId, StringComparison.Ordinal) &&
                   string.Equals(edge.ToNodeId, toId, StringComparison.Ordinal);
        }

        private string AllocatePlatformId()
        {
            _currentMapData.PolylinePlatforms ??= new List<PolylinePlatformData>();
            var taken = new HashSet<string>(
                _currentMapData.PolylinePlatforms.Select(p => p.Id),
                StringComparer.Ordinal);
            for (int i = _currentMapData.PolylinePlatforms.Count; ; i++)
            {
                string id = $"plat_{i}";
                if (taken.Add(id)) return id;
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
            _currentMapData.PolylinePlatforms ??= new List<PolylinePlatformData>();
            _currentMapData.ManualEdgeAnchors ??= new List<ManualEdgeAnchor>();

            _manualEdgeStartAnchor = null;
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
                _manualEdgeStartAnchor = null;
                _startPoint = null;
                _previewPoint = null;
            }

            _currentEditMode = mode;

            if (mode == EditMode.ManualEdge)
            {
                _manualEdgeStartAnchor = null;
                _startPoint = null;
                _previewPoint = null;
            }

            _hoveredNodeIndex = -1;
            _hoveredPlatform = null;
            _hoveredRope = null;
            _hoveredManualEdgeAnchor = null;
        }

        public EditMode GetCurrentEditMode()
        {
            return _currentEditMode;
        }

        public void UpdateMousePosition(PointF screenPoint)
        {
            if (minimapBounds.IsEmpty)
            {
                _previewPoint = null;
                return;
            }

            bool isDrawing = (_currentEditMode == EditMode.Platform && _activeDrawingPoints.Count > 0) ||
                             (_currentEditMode != EditMode.Platform && _startPoint.HasValue);

            if (!isDrawing)
            {
                _previewPoint = null;
                return;
            }

            _previewPoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);
        }

        public void CancelCurrentDrawing()
        {
            _activeDrawingPoints.Clear();
            _startPoint = null;
            _previewPoint = null;
            _hoveredSegmentIndex = -1;
            _hoveredProjectionPoint = PointF.Empty;
            Logger.Info("[編輯器] 取消目前繪製狀態與重置 UI 狀態");
        }

        public void FinishCurrentPolyline()
        {
            var cleanedPoints = new List<PointF>();
            const float MinVertexDistance = 1.5f;

            foreach (var pt in _activeDrawingPoints)
            {
                if (cleanedPoints.Count == 0)
                {
                    cleanedPoints.Add(pt);
                }
                else
                {
                    var prev = cleanedPoints[cleanedPoints.Count - 1];
                    float dx = pt.X - prev.X;
                    float dy = pt.Y - prev.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist >= MinVertexDistance)
                    {
                        cleanedPoints.Add(pt);
                    }
                }
            }

            if (cleanedPoints.Count >= 2)
            {
                var newPlat = new PolylinePlatformData
                {
                    Id = AllocatePlatformId(),
                    Points = cleanedPoints.Select(p => new PlatformPointData 
                    { 
                        X = (float)Math.Round(p.X, 1), 
                        Y = (float)Math.Round(p.Y, 1) 
                    }).ToList()
                };

                _currentMapData.PolylinePlatforms ??= new List<PolylinePlatformData>();
                _currentMapData.PolylinePlatforms.Add(newPlat);
                Logger.Info($"[編輯器] 建立折線平台幾何: Id={newPlat.Id}, 頂點數={newPlat.Points.Count}");

                CancelCurrentDrawing();
                MapGenerationService.BuildHTopology(_currentMapData);
            }
            else
            {
                Logger.Warning("[編輯器] 折線平台頂點數不足 2，取消建立");
                CancelCurrentDrawing();
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
                // Select 模式僅供檢視，不寫入任何資料；Hover 由 UpdateHoveredNode 處理
            }
            else if (_currentEditMode == EditMode.Platform)
            {
                if (button == MouseButtons.Right)
                {
                    if (_activeDrawingPoints.Count > 0)
                    {
                        Logger.Info("[編輯器] 右鍵點擊：嘗試完成目前折線平台");
                        FinishCurrentPolyline();
                    }
                    else
                    {
                        CancelCurrentDrawing();
                        Logger.Info("[編輯器] 右鍵點擊：取消平台模式所有繪製狀態");
                    }
                }
                else
                {
                    if (_activeDrawingPoints.Count == 0)
                    {
                        // 插點檢測 (Phase 3 核心)
                        PolylinePlatformData? hitPlat = null;
                        PolylineHitResult? hitResult = null;
                        float bestDist = float.MaxValue;
                        float threshold = SelectionRadius * 2.0f; // 10 像素
                        
                        if (_currentMapData.PolylinePlatforms != null)
                        {
                            foreach (var plat in _currentMapData.PolylinePlatforms)
                            {
                                var hit = GetDistanceToPolyline(relativePoint, plat);
                                if (hit.Distance < threshold && hit.Distance < bestDist)
                                {
                                    bestDist = hit.Distance;
                                    hitPlat = plat;
                                    hitResult = hit;
                                }
                            }
                        }

                        // 防呆規則：如果命中點距離此折線平台上的任何既有頂點 (Vertex) 過近，則忽略插點
                        bool isTooCloseToVertex = false;
                        const float MinVertexDistanceThreshold = 5.0f; // 5 像素

                        if (hitPlat != null && hitResult != null)
                        {
                            foreach (var v in hitPlat.Points)
                            {
                                float dx = hitResult.ProjectionPoint.X - v.X;
                                float dy = hitResult.ProjectionPoint.Y - v.Y;
                                float distToVertex = (float)Math.Sqrt(dx * dx + dy * dy);
                                if (distToVertex <= MinVertexDistanceThreshold)
                                {
                                    isTooCloseToVertex = true;
                                    break;
                                }
                            }

                            if (!isTooCloseToVertex)
                            {
                                // 插入新折點
                                var newPt = new PlatformPointData
                                {
                                    X = (float)Math.Round(hitResult.ProjectionPoint.X, 1),
                                    Y = (float)Math.Round(hitResult.ProjectionPoint.Y, 1)
                                };
                                hitPlat.Points.Insert(hitResult.SegmentIndex + 1, newPt);
                                Logger.Info($"[編輯器] 於折線平台 {hitPlat.Id} 的段 {hitResult.SegmentIndex} 插入折點: ({newPt.X:F1}, {newPt.Y:F1})");
                                
                                MapGenerationService.BuildHTopology(_currentMapData);
                                return; // 結束，不進入繪製起點
                            }
                            else
                            {
                                Logger.Warning("[編輯器] 點擊位置距離既有折點過近，忽略此次插點操作");
                            }
                        }

                        // 開始新平台的繪製
                        _activeDrawingPoints.Add(relativePoint);
                        Logger.Info($"[編輯器] 設定折線平台起點: ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                    }
                    else
                    {
                        var nextPt = _previewPoint ?? relativePoint;
                        _activeDrawingPoints.Add(nextPt);
                        Logger.Info($"[編輯器] 新增折線平台折點 {_activeDrawingPoints.Count - 1}: ({nextPt.X:F1}, {nextPt.Y:F1})");
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
                    _manualEdgeStartAnchor = null;
                    _startPoint = null;
                    _previewPoint = null;
                    Logger.Info("[編輯器] 取消建立例外邊");
                }
                else
                {
                    float threshold = SelectionRadius * 2.0f;
                    var hitAnchor = FindNearestPlatformProjection(relativePoint, threshold);
                    if (hitAnchor == null) return;

                    if (_manualEdgeStartAnchor == null)
                    {
                        _manualEdgeStartAnchor = hitAnchor;
                        var pt = RoundAnchorPoint(hitAnchor.ProjectedPoint);
                        _startPoint = pt;
                        Logger.Info($"[編輯器] 選擇例外邊起點: 平台={hitAnchor.Platform.Id} ({pt.X:F1}, {pt.Y:F1})");
                    }
                    else
                    {
                        var start = _manualEdgeStartAnchor;
                        var end = hitAnchor;
                        var fromPt = RoundAnchorPoint(start.ProjectedPoint);
                        var toPt = RoundAnchorPoint(end.ProjectedPoint);

                        if (string.Equals(start.Platform.Id, end.Platform.Id, StringComparison.Ordinal) &&
                            Math.Abs(fromPt.X - toPt.X) < 0.05f &&
                            Math.Abs(fromPt.Y - toPt.Y) < 0.05f)
                        {
                            Logger.Info("[編輯器] 取消例外邊（點選同一起點）");
                            _manualEdgeStartAnchor = null;
                            _startPoint = null;
                            _previewPoint = null;
                            return;
                        }

                        _currentMapData.ManualEdgeAnchors ??= new List<ManualEdgeAnchor>();
                        var newAnchor = new ManualEdgeAnchor
                        {
                            FromPlatformId = start.Platform.Id,
                            FromX = fromPt.X,
                            FromY = fromPt.Y,
                            FromSegmentIndex = start.SegmentIndex,
                            ToPlatformId = end.Platform.Id,
                            ToX = toPt.X,
                            ToY = toPt.Y,
                            ToSegmentIndex = end.SegmentIndex,
                            ActionType = (NavigationActionType)_currentActionType
                        };

                        bool duplicated = _currentMapData.ManualEdgeAnchors.Any(a => AnchorsEqual(a, newAnchor));
                        if (duplicated)
                        {
                            Logger.Warning("[編輯器] 手動例外邊已存在，不予重複建立");
                            _manualEdgeStartAnchor = null;
                            _startPoint = null;
                            _previewPoint = null;
                            return;
                        }

                        _currentMapData.ManualEdgeAnchors.Add(newAnchor);
                        Logger.Info($"[編輯器] 建立手動例外邊錨點: {start.Platform.Id}({fromPt.X:F1},{fromPt.Y:F1}) -> {end.Platform.Id}({toPt.X:F1},{toPt.Y:F1}) Action={newAnchor.ActionType}");

                        _manualEdgeStartAnchor = null;
                        _startPoint = null;
                        _previewPoint = null;

                        MapGenerationService.BuildHTopology(_currentMapData);
                    }
                }
            }
            else if (_currentEditMode == EditMode.Delete)
            {
                HandleDeleteAction(relativePoint);
            }
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

            // 1. 繪製實際的 Platforms 幾何線段 (萊姆綠粗線，Hover時以亮萊姆綠且加粗高亮，被選中的Segment特別高亮)
            if (_currentMapData.PolylinePlatforms?.Any() == true)
            {
                foreach (var plat in _currentMapData.PolylinePlatforms)
                {
                    if (plat.Points == null || plat.Points.Count < 2) continue;

                    bool isPlatHovered = (plat == _hoveredPlatform);
                    Color platColor = isPlatHovered ? Color.Chartreuse : Color.Lime;
                    float platWidth = isPlatHovered ? 6.0f : 4.0f;

                    for (int i = 0; i < plat.Points.Count - 1; i++)
                    {
                        var p1 = convert(new PointF(minimapBounds.X + plat.Points[i].X, minimapBounds.Y + plat.Points[i].Y));
                        var p2 = convert(new PointF(minimapBounds.X + plat.Points[i + 1].X, minimapBounds.Y + plat.Points[i + 1].Y));

                        bool isSegHovered = isPlatHovered && (i == _hoveredSegmentIndex);
                        float currentWidth = isSegHovered ? 8.0f : platWidth;
                        Color currentColor = isSegHovered ? Color.Gold : platColor;

                        using (var pen = new Pen(currentColor, currentWidth))
                        {
                            g.DrawLine(pen, p1, p2);
                        }

                        g.FillRectangle(Brushes.White, p1.X - 2, p1.Y - 2, 4, 4);
                        if (i == plat.Points.Count - 2)
                        {
                            g.FillRectangle(Brushes.White, p2.X - 2, p2.Y - 2, 4, 4);
                        }
                    }

                    if (isPlatHovered && _hoveredProjectionPoint != PointF.Empty)
                    {
                        var pProj = convert(new PointF(minimapBounds.X + _hoveredProjectionPoint.X, minimapBounds.Y + _hoveredProjectionPoint.Y));
                        using (var projBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
                        {
                            g.FillEllipse(projBrush, pProj.X - 4f, pProj.Y - 4f, 8f, 8f);
                        }
                        g.DrawEllipse(Pens.Black, pProj.X - 4f, pProj.Y - 4f, 8f, 8f);
                    }
                }
            }

            // 2. 繪製拓撲邊 (細線代表自動生成，醒目橘線代表手動例外，Hover手動邊時以亮紅粗線高亮)
            if (_currentMapData.Edges.Count > 0)
            {
                foreach (var edge in _currentMapData.Edges)
                {
                    int fromIdx = FindNodeIndexById(edge.FromNodeId);
                    int toIdx = FindNodeIndexById(edge.ToNodeId);
                    if (fromIdx < 0 || toIdx < 0) continue;

                    var p1Data = _currentMapData.Nodes[fromIdx];
                    var p2Data = _currentMapData.Nodes[toIdx];

                    bool isManual = _currentMapData.ManualEdgeAnchors != null &&
                        _currentMapData.ManualEdgeAnchors.Any(a => EdgeMatchesAnchor(edge, a));

                    bool isEdgeHovered = isManual && _hoveredManualEdgeAnchor != null &&
                        EdgeMatchesAnchor(edge, _hoveredManualEdgeAnchor);

                    Color lineColor = GetEdgeDrawColor(edge);
                    var p1 = convert(new PointF(minimapBounds.X + p1Data.X, minimapBounds.Y + p1Data.Y));
                    var p2 = convert(new PointF(minimapBounds.X + p2Data.X, minimapBounds.Y + p2Data.Y));

                    float penWidth = isEdgeHovered ? 4.0f : (isManual ? 2.5f : 1.5f);
                    Color drawColor = isEdgeHovered ? Color.Red : (isManual ? Color.OrangeRed : Color.FromArgb(120, lineColor));

                    using (var pen = new Pen(drawColor, penWidth))
                    {
                        g.DrawLine(pen, p1, p2);

                        float midX = (p1.X + p2.X) / 2;
                        float midY = (p1.Y + p2.Y) / 2;
                        DrawArrow(g, p1, p2, new PointF(midX, midY), isEdgeHovered ? Brushes.Red : (isManual ? Brushes.OrangeRed : Brushes.Cyan));
                    }
                }
            }

            // 3. 繪製自動生成的拓撲節點 (灰色小點代表自動生成，黃/白代表選取/Hover高亮，橘紅代表例外邊端點)
            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var nav = _currentMapData.Nodes[i];
                var pos = convert(new PointF(minimapBounds.X + nav.X, minimapBounds.Y + nav.Y));

                bool isManualEndpoint = _currentMapData.ManualEdgeAnchors != null &&
                    _currentMapData.ManualEdgeAnchors.Any(a =>
                        string.Equals(nav.Id, MapGenerationService.BuildVirtualNodeId(a.FromPlatformId, a.FromX, a.FromY), StringComparison.Ordinal) ||
                        string.Equals(nav.Id, MapGenerationService.BuildVirtualNodeId(a.ToPlatformId, a.ToX, a.ToY), StringComparison.Ordinal));

                Color nodeColor = Color.FromArgb(180, Color.DarkGray);
                float radius = PointRadius - 0.5f;

                if (i == _hoveredNodeIndex)
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
            }

            // 4. 繪製繩索幾何 (黃色粗線，Hover時以亮黃色且加粗高亮)
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

                    bool isRopeHovered = (rope == _hoveredRope);
                    Color ropeColor = isRopeHovered ? Color.LightYellow : Color.Yellow;
                    float ropeWidth = isRopeHovered ? 5.5f : 3.5f;

                    using var pen = new Pen(ropeColor, ropeWidth);
                    g.DrawLine(pen, pTop, pBottom);

                    g.FillRectangle(Brushes.Red, pTop.X - 3, pTop.Y - 3, 6, 6);
                    g.FillRectangle(Brushes.Green, pBottom.X - 3, pBottom.Y - 3, 6, 6);
                }
            }
        }

        /// <summary>
        /// 取得自動/手動拓撲邊的渲染顏色。
        /// 顏色語意對照：
        /// - Magenta (洋紅): Walk (基礎自動通行步行的細邊)
        /// - Cyan (青色): ClimbUp / ClimbDown (繩索垂直邊)
        /// - MediumPurple (粉紫): SideJump (平台兩端往外側跳的自動邊)
        /// - Yellow (黃色): JumpDown (平台邊緣往下跳的自動邊)
        /// - DeepSkyBlue (深天藍): Jump (向上或往前跳躍的自動邊)
        /// - OrangeRed (橘紅): Teleport (傳送點之間的自動邊)
        /// - OrangeRed (加粗 2.5f) / Red (Hover加粗 4.0f): ManualEdgeAnchors 解析後的例外邊
        /// </summary>
        private static Color GetEdgeDrawColor(NavEdgeData edge)
        {
            if (edge.ActionType == NavigationActionType.Walk)
            {
                return Color.Magenta;
            }

            return edge.ActionType switch
            {
                NavigationActionType.ClimbUp or NavigationActionType.ClimbDown => Color.Cyan,
                NavigationActionType.SideJump => Color.MediumPurple,
                NavigationActionType.JumpDown => Color.Yellow,
                NavigationActionType.Jump => Color.DeepSkyBlue,
                NavigationActionType.Teleport => Color.OrangeRed,
                _ => Color.White
            };
        }

        private void DrawPreviewShapes(Graphics g, Func<PointF, PointF> convert)
        {
            if (_currentEditMode == EditMode.Platform)
            {
                if (_activeDrawingPoints.Count > 0)
                {
                    // 1. 繪製已確定的折線段 (Cyan 實線)
                    using (var pen = new Pen(Color.Cyan, 2.5f))
                    {
                        for (int i = 0; i < _activeDrawingPoints.Count - 1; i++)
                        {
                            var p1 = convert(new PointF(minimapBounds.X + _activeDrawingPoints[i].X, minimapBounds.Y + _activeDrawingPoints[i].Y));
                            var p2 = convert(new PointF(minimapBounds.X + _activeDrawingPoints[i + 1].X, minimapBounds.Y + _activeDrawingPoints[i + 1].Y));
                            g.DrawLine(pen, p1, p2);
                        }
                    }

                    // 2. 繪製已確定折線頂點的紅色圓點
                    using (var brush = new SolidBrush(Color.Red))
                    {
                        foreach (var pt in _activeDrawingPoints)
                        {
                            var pScreen = convert(new PointF(minimapBounds.X + pt.X, minimapBounds.Y + pt.Y));
                            g.FillEllipse(brush, pScreen.X - 3, pScreen.Y - 3, 6, 6);
                        }
                    }

                    // 3. 繪製最後一個確定點到滑鼠預覽點的虛線段 (Cyan 虛線)
                    if (_previewPoint.HasValue)
                    {
                        var lastPt = _activeDrawingPoints[_activeDrawingPoints.Count - 1];
                        var pLast = convert(new PointF(minimapBounds.X + lastPt.X, minimapBounds.Y + lastPt.Y));
                        var pMouse = convert(new PointF(minimapBounds.X + _previewPoint.Value.X, minimapBounds.Y + _previewPoint.Value.Y));

                        using (var pen = new Pen(Color.Cyan, 2f) { DashStyle = DashStyle.Dash })
                        {
                            g.DrawLine(pen, pLast, pMouse);
                        }
                    }
                }
            }
            else
            {
                bool isLineMode = _currentEditMode == EditMode.Rope || _currentEditMode == EditMode.ManualEdge;

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
        }

        public class PolylineHitResult
        {
            public float Distance { get; set; } = float.MaxValue;
            public int SegmentIndex { get; set; } = -1;
            public PointF ProjectionPoint { get; set; } = PointF.Empty;
        }

        private static PolylineHitResult GetDistanceToPolyline(PointF p, PolylinePlatformData plat)
        {
            var result = new PolylineHitResult();
            if (plat.Points == null || plat.Points.Count < 2) return result;

            for (int i = 0; i < plat.Points.Count - 1; i++)
            {
                var A = new PointF(plat.Points[i].X, plat.Points[i].Y);
                var B = new PointF(plat.Points[i + 1].X, plat.Points[i + 1].Y);

                float abx = B.X - A.X;
                float aby = B.Y - A.Y;
                float ab2 = abx * abx + aby * aby;

                float cx, cy;
                PointF projPt;
                int currentSegIdx = i;

                if (ab2 < 0.001f)
                {
                    cx = A.X;
                    cy = A.Y;
                    projPt = A;
                }
                else
                {
                    float apx = p.X - A.X;
                    float apy = p.Y - A.Y;
                    float t = (apx * abx + apy * aby) / ab2;
                    t = Math.Max(0.0f, Math.Min(1.0f, t));
                    cx = A.X + t * abx;
                    cy = A.Y + t * aby;
                    projPt = new PointF(cx, cy);
                }

                float dx = p.X - cx;
                float dy = p.Y - cy;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < result.Distance)
                {
                    result.Distance = dist;
                    result.SegmentIndex = currentSegIdx;
                    result.ProjectionPoint = projPt;
                }
            }

            return result;
        }

        private bool TryGetManualEdgeDrawSegment(ManualEdgeAnchor anchor, out PointF from, out PointF to)
        {
            foreach (var edge in _currentMapData.Edges)
            {
                if (!EdgeMatchesAnchor(edge, anchor))
                    continue;

                int fromIdx = FindNodeIndexById(edge.FromNodeId);
                int toIdx = FindNodeIndexById(edge.ToNodeId);
                if (fromIdx < 0 || toIdx < 0)
                    break;

                from = new PointF(_currentMapData.Nodes[fromIdx].X, _currentMapData.Nodes[fromIdx].Y);
                to = new PointF(_currentMapData.Nodes[toIdx].X, _currentMapData.Nodes[toIdx].Y);
                return true;
            }

            from = new PointF(anchor.FromX, anchor.FromY);
            to = new PointF(anchor.ToX, anchor.ToY);
            return true;
        }

        private float GetDistanceToManualEdgeAnchor(PointF p, ManualEdgeAnchor anchor)
        {
            TryGetManualEdgeDrawSegment(anchor, out PointF from, out PointF to);
            return GetDistanceToSegment(p, from, to);
        }

        private DeleteTarget ResolveDeleteTarget(PointF clickPosition, float threshold)
        {
            ManualEdgeAnchor? bestManualEdge = null;
            float bestManualEdgeDist = float.MaxValue;
            float manualEdgeThreshold = Math.Max(threshold, ManualEdgeDeleteHitSlop);

            if (_currentMapData.ManualEdgeAnchors != null)
            {
                foreach (var anchor in _currentMapData.ManualEdgeAnchors)
                {
                    float dist = GetDistanceToManualEdgeAnchor(clickPosition, anchor);
                    if (dist < manualEdgeThreshold && dist < bestManualEdgeDist)
                    {
                        bestManualEdgeDist = dist;
                        bestManualEdge = anchor;
                    }
                }
            }

            if (bestManualEdge != null)
            {
                return new DeleteTarget(
                    DeleteTargetKind.ManualEdge,
                    bestManualEdge,
                    null,
                    -1,
                    null,
                    null);
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

            if (bestRope != null)
            {
                return new DeleteTarget(
                    DeleteTargetKind.Rope,
                    null,
                    bestRope,
                    bestRopeIdx,
                    null,
                    null);
            }

            PolylinePlatformData? bestPlatform = null;
            PolylineHitResult? bestPlatformHit = null;
            float bestPlatformDist = float.MaxValue;

            if (_currentMapData.PolylinePlatforms != null)
            {
                foreach (var plat in _currentMapData.PolylinePlatforms)
                {
                    var hit = GetDistanceToPolyline(clickPosition, plat);
                    if (hit.Distance < threshold && hit.Distance < bestPlatformDist)
                    {
                        bestPlatformDist = hit.Distance;
                        bestPlatform = plat;
                        bestPlatformHit = hit;
                    }
                }
            }

            if (bestPlatform != null)
            {
                return new DeleteTarget(
                    DeleteTargetKind.Platform,
                    null,
                    null,
                    -1,
                    bestPlatform,
                    bestPlatformHit);
            }

            return new DeleteTarget(DeleteTargetKind.None, null, null, -1, null, null);
        }

        private void PruneManualEdgeAnchorsForPlatform(string platformId)
        {
            if (_currentMapData.ManualEdgeAnchors == null || _currentMapData.ManualEdgeAnchors.Count == 0)
                return;

            int removed = _currentMapData.ManualEdgeAnchors.RemoveAll(a =>
                string.Equals(a.FromPlatformId, platformId, StringComparison.Ordinal) ||
                string.Equals(a.ToPlatformId, platformId, StringComparison.Ordinal));

            if (removed > 0)
            {
                Logger.Info($"[編輯器] 連帶移除平台 {platformId} 相關的手動例外邊錨點: {removed} 條");
            }
        }

        private void ApplyDeleteTarget(DeleteTarget target)
        {
            switch (target.Kind)
            {
                case DeleteTargetKind.ManualEdge when target.ManualEdge != null:
                    _currentMapData.ManualEdgeAnchors?.Remove(target.ManualEdge);
                    Logger.Info(
                        $"[編輯器] 刪除手動例外邊錨點: {target.ManualEdge.FromPlatformId} -> {target.ManualEdge.ToPlatformId} Action={target.ManualEdge.ActionType}");
                    MapGenerationService.BuildHTopology(_currentMapData);
                    break;

                case DeleteTargetKind.Rope when target.Rope != null && target.RopeIndex >= 0:
                    _currentMapData.Ropes?.RemoveAt(target.RopeIndex);
                    Logger.Info(
                        $"[編輯器] 刪除繩索幾何: X={target.Rope[0]:F1}, Y={target.Rope[1]:F1}~{target.Rope[2]:F1}");
                    MapGenerationService.BuildHTopology(_currentMapData);
                    break;

                case DeleteTargetKind.Platform when target.Platform != null:
                    string platformId = target.Platform.Id;
                    _currentMapData.PolylinePlatforms?.Remove(target.Platform);
                    PruneManualEdgeAnchorsForPlatform(platformId);
                    Logger.Info($"[編輯器] 刪除折線平台幾何: Id={platformId}");
                    MapGenerationService.BuildHTopology(_currentMapData);
                    break;
            }
        }

        private void HandleDeleteAction(PointF clickPosition)
        {
            float threshold = SelectionRadius * 2.0f;
            var target = ResolveDeleteTarget(clickPosition, threshold);

            if (target.Kind == DeleteTargetKind.None)
            {
                Logger.Info("[編輯器] 未命中任何可刪除的標記（手動邊、繩索、平台）");
                return;
            }

            ApplyDeleteTarget(target);
        }

        private static float GetDistanceToSegment(PointF p, PointF a, PointF b)
        {
            float abx = b.X - a.X;
            float aby = b.Y - a.Y;
            float ab2 = abx * abx + aby * aby;
            if (ab2 < 0.001f)
            {
                float dx = p.X - a.X;
                float dy = p.Y - a.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            float apx = p.X - a.X;
            float apy = p.Y - a.Y;
            float t = (apx * abx + apy * aby) / ab2;
            t = Math.Max(0.0f, Math.Min(1.0f, t));

            float cx = a.X + t * abx;
            float cy = a.Y + t * aby;
            float distX = p.X - cx;
            float distY = p.Y - cy;
            return (float)Math.Sqrt(distX * distX + distY * distY);
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

            float dxBot = ropeX - p.X;
            float dyBot = bottomY - p.Y;
            return (float)Math.Sqrt(dxBot * dxBot + dyBot * dyBot);
        }

        /// <summary>
        /// 更新目前滑鼠所在的 Hover 物件狀態。
        /// Delete 模式命中順序：手動邊 → 繩索 → 平台（與 HandleDeleteAction 共用 ResolveDeleteTarget）。
        /// </summary>
        public void UpdateHoveredNode(PointF screenPoint)
        {
            if (minimapBounds.IsEmpty) return;

            var relativePoint = new PointF(
               screenPoint.X - minimapBounds.X,
               screenPoint.Y - minimapBounds.Y);

            // 重設所有 Hover 狀態
            _hoveredNodeIndex = -1;
            _hoveredPlatform = null;
            _hoveredRope = null;
            _hoveredManualEdgeAnchor = null;
            _hoveredSegmentIndex = -1;
            _hoveredProjectionPoint = PointF.Empty;

            bool isPlatformModeNotDrawing = _currentEditMode == EditMode.Platform && _activeDrawingPoints.Count == 0;

            if (_currentEditMode == EditMode.Delete)
            {
                var deleteTarget = ResolveDeleteTarget(relativePoint, SelectionRadius * 2.0f);
                switch (deleteTarget.Kind)
                {
                    case DeleteTargetKind.ManualEdge:
                        _hoveredManualEdgeAnchor = deleteTarget.ManualEdge;
                        break;
                    case DeleteTargetKind.Rope:
                        _hoveredRope = deleteTarget.Rope;
                        break;
                    case DeleteTargetKind.Platform:
                        _hoveredPlatform = deleteTarget.Platform;
                        if (deleteTarget.PlatformHit != null)
                        {
                            _hoveredSegmentIndex = deleteTarget.PlatformHit.SegmentIndex;
                            _hoveredProjectionPoint = deleteTarget.PlatformHit.ProjectionPoint;
                        }
                        break;
                }

                return;
            }

            if (isPlatformModeNotDrawing)
            {
                float threshold = SelectionRadius * 2.0f;

                PolylinePlatformData? bestPlatform = null;
                PolylineHitResult? bestHitResult = null;
                float bestPlatformDist = float.MaxValue;
                if (_currentMapData.PolylinePlatforms != null)
                {
                    foreach (var plat in _currentMapData.PolylinePlatforms)
                    {
                        var hit = GetDistanceToPolyline(relativePoint, plat);
                        if (hit.Distance < threshold && hit.Distance < bestPlatformDist)
                        {
                            bestPlatformDist = hit.Distance;
                            bestPlatform = plat;
                            bestHitResult = hit;
                        }
                    }
                }

                if (bestPlatform != null)
                {
                    bool isTooCloseToVertex = false;
                    if (bestHitResult != null)
                    {
                        foreach (var v in bestPlatform.Points)
                        {
                            float dx = bestHitResult.ProjectionPoint.X - v.X;
                            float dy = bestHitResult.ProjectionPoint.Y - v.Y;
                            float distToVertex = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (distToVertex <= 5.0f)
                            {
                                isTooCloseToVertex = true;
                                break;
                            }
                        }
                    }

                    if (!isTooCloseToVertex)
                    {
                        _hoveredPlatform = bestPlatform;
                        if (bestHitResult != null)
                        {
                            _hoveredSegmentIndex = bestHitResult.SegmentIndex;
                            _hoveredProjectionPoint = bestHitResult.ProjectionPoint;
                        }
                    }
                }
            }
            else if (_currentEditMode == EditMode.ManualEdge)
            {
                float threshold = SelectionRadius * 2.0f;
                var hit = FindNearestPlatformProjection(relativePoint, threshold);
                if (hit != null)
                {
                    _hoveredPlatform = hit.Platform;
                    _hoveredSegmentIndex = hit.SegmentIndex;
                    _hoveredProjectionPoint = hit.ProjectedPoint;
                }
            }
            else
            {
                _hoveredNodeIndex = FindNearestRuntimeNodeIndex(relativePoint);
            }
        }

        private int FindNearestRuntimeNodeIndex(PointF relativePoint)
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
                NavigationActionType.ClimbUp => "ClimbUp",
                NavigationActionType.ClimbDown => "ClimbDown",
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
