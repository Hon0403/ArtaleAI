using ArtaleAI.Models.Config;
using ArtaleAI.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.Shared;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ArtaleAI.UI.MapEditor
{
    /// <summary>小地圖路徑幾何標記工具 (Geometry Marker)；以 Platforms 與 Ropes 為主要真實來源 (SSOT)。</summary>
    public partial class MapEditor
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
        /// <summary>
        /// 編輯器唯一命中／吸附半徑（地圖像素）。
        /// hover、選取、刪除、折線插點、跳點、安全區、runtime 節點皆共用。
        /// </summary>
        private const float HitRadius = 1.5f;

        private const float MarkerLineWidthNormal = 3.5f;
        private const float MarkerLineWidthHovered = 5.5f;
        private const float MarkerLineWidthSelected = 6.5f;
        /// <summary>hover／select 發光底層相對主線加寬的量（螢幕像素）。</summary>
        private const float MarkerGlowExtraWidth = 6.0f;
        private const float MarkerCapHalfSize = 3.0f;
        private const float MarkerSelectionRingRadius = 6.0f;

        private enum MarkerVisualState { Normal, Hovered, Selected }

        /// <summary>單段線標記（繩索／跳點／安全區）的配色與樣式；由型別決定，與實例狀態無關。</summary>
        private readonly record struct LineMarkerStyle(
            Color NormalColor,
            Color HoverColor,
            Color SelectColor,
            Color StartCapColor,
            Color EndCapColor,
            bool Dashed);

        /// <summary>單次繪製一段線標記所需的幾何與狀態。</summary>
        private readonly record struct LineMarker(
            PointF A,
            PointF B,
            LineMarkerStyle Style,
            MarkerVisualState State);

        private static readonly LineMarkerStyle RopeStyle = new(
            Color.Yellow, Color.LightYellow, Color.Orange, Color.Red, Color.Green, Dashed: false);
        private static readonly LineMarkerStyle JumpLinkStyle = new(
            Color.Cyan, Color.LightSkyBlue, Color.DeepSkyBlue, Color.OrangeRed, Color.LimeGreen, Dashed: true);
        private static readonly LineMarkerStyle SafeZoneStyle = new(
            Color.LimeGreen, Color.PaleGreen, Color.DeepSkyBlue, Color.LimeGreen, Color.LimeGreen, Dashed: true);

        private enum DeleteTargetKind { None, ManualEdge, JumpLink, Rope, Platform }

        private sealed record DeleteTarget(
            DeleteTargetKind Kind,
            ManualEdgeAnchor? ManualEdge,
            float[]? LineGeometry,
            int LineIndex,
            PolylinePlatformData? Platform,
            PolylineHitResult? PlatformHit);

        private int _hoveredNodeIndex = -1;
        private int _currentActionType = 0;
        private PlatformAnchor? _manualEdgeStartAnchor;

        private PolylinePlatformData? _hoveredPlatform = null;
        private float[]? _hoveredRope = null;
        private float[]? _hoveredJumpLink = null;
        private ManualEdgeAnchor? _hoveredManualEdgeAnchor = null;
        private List<PointF> _activeDrawingPoints = new();
        private int _hoveredSegmentIndex = -1;
        private PointF _hoveredProjectionPoint = PointF.Empty;

        private MapEditorSelection _selection = MapEditorSelection.Empty;
        private bool _isDirty;
        private PointF _lastMousePosition = new(-1000, -1000);
        private PointF _lastNodeCyclePoint = PointF.Empty;
        private List<int> _nodeCycleCandidates = new();
        private int _nodeCycleIndex = -1;

        public event Action? SelectionChanged;
        public event Action? DirtyStateChanged;

        public bool IsDirty => _isDirty;
        public MapEditorSelection Selection => _selection;

        /// <summary>路徑編輯：每個小地圖邏輯像素對應的螢幕像素數（整數 1–12）。</summary>
        public float ZoomScale { get; set; } = 1.0f;
        public float PanOffsetX { get; set; }
        public float PanOffsetY { get; set; }

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

        public MapEditorHoverInfo GetHoverInfo()
        {
            if (_hoveredSegmentIndex < 0 && _hoveredProjectionPoint == PointF.Empty && _hoveredNodeIndex < 0)
                return MapEditorHoverInfo.Empty;

            return new MapEditorHoverInfo
            {
                SegmentIndex = _hoveredSegmentIndex,
                ProjectionPoint = _hoveredProjectionPoint,
                RuntimeNodeIndex = _hoveredNodeIndex
            };
        }

        public void MarkDirty()
        {
            if (_isDirty) return;
            _isDirty = true;
            DirtyStateChanged?.Invoke();
        }

        public void ClearDirty()
        {
            if (!_isDirty) return;
            _isDirty = false;
            DirtyStateChanged?.Invoke();
        }

        private void SetSelection(MapEditorSelection selection)
        {
            if (SelectionsEqual(_selection, selection)) return;
            _selection = selection;
            SelectionChanged?.Invoke();
        }

        private void ClearSelection()
        {
            SetSelection(MapEditorSelection.Empty);
        }

        private static bool SelectionsEqual(MapEditorSelection a, MapEditorSelection b)
        {
            if (a.Kind != b.Kind) return false;
            return a.Kind switch
            {
                MapEditorSelectionKind.None => true,
                MapEditorSelectionKind.Platform => ReferenceEquals(a.Platform, b.Platform)
                    && a.SegmentIndex == b.SegmentIndex,
                MapEditorSelectionKind.Rope => a.RopeIndex == b.RopeIndex,
                MapEditorSelectionKind.JumpLink => a.JumpLinkIndex == b.JumpLinkIndex,
                MapEditorSelectionKind.ManualEdge => ReferenceEquals(a.ManualEdge, b.ManualEdge),
                MapEditorSelectionKind.RuntimeNode => a.RuntimeNodeIndex == b.RuntimeNodeIndex,
                _ => false
            };
        }

        private void ApplySelectionFromPick(DeleteTarget pick)
        {
            switch (pick.Kind)
            {
                case DeleteTargetKind.ManualEdge when pick.ManualEdge != null:
                    SetSelection(MapEditorSelection.ForManualEdge(pick.ManualEdge));
                    break;
                case DeleteTargetKind.JumpLink when pick.LineIndex >= 0:
                    SetSelection(MapEditorSelection.ForJumpLink(pick.LineIndex));
                    break;
                case DeleteTargetKind.Rope when pick.LineIndex >= 0:
                    SetSelection(MapEditorSelection.ForRope(pick.LineIndex));
                    break;
                case DeleteTargetKind.Platform when pick.Platform != null:
                    SetSelection(MapEditorSelection.ForPlatform(
                        pick.Platform,
                        pick.PlatformHit?.SegmentIndex ?? -1,
                        pick.PlatformHit?.ProjectionPoint ?? PointF.Empty));
                    break;
                default:
                    ClearSelection();
                    break;
            }
        }

        private void InvalidateSelectionIfNeeded(DeleteTarget deleted)
        {
            if (_selection.IsEmpty) return;

            bool lost = deleted.Kind switch
            {
                DeleteTargetKind.ManualEdge =>
                    _selection.Kind == MapEditorSelectionKind.ManualEdge &&
                    ReferenceEquals(_selection.ManualEdge, deleted.ManualEdge),
                DeleteTargetKind.Rope =>
                    _selection.Kind == MapEditorSelectionKind.Rope &&
                    _selection.RopeIndex == deleted.LineIndex,
                DeleteTargetKind.JumpLink =>
                    _selection.Kind == MapEditorSelectionKind.JumpLink &&
                    _selection.JumpLinkIndex == deleted.LineIndex,
                DeleteTargetKind.Platform =>
                    _selection.Kind == MapEditorSelectionKind.Platform &&
                    ReferenceEquals(_selection.Platform, deleted.Platform),
                _ => false
            };

            if (lost) ClearSelection();
        }

        public string FormatInspectorText()
        {
            var lines = new List<string>
            {
                "── 地圖摘要 ──",
                $"平台: {_currentMapData.PolylinePlatforms?.Count ?? 0}",
                $"繩索: {_currentMapData.Ropes?.Count ?? 0}",
                $"跳點: {_currentMapData.JumpLinks?.Count ?? 0}",
                $"安全折點: {GetMapSummary().SafeZoneCount}",
                $"手動邊: {_currentMapData.ManualEdgeAnchors?.Count ?? 0}",
                $"Runtime 節點: {_currentMapData.Nodes.Count}",
                $"Runtime 邊: {_currentMapData.Edges.Count}",
                string.Empty
            };

            if (_selection.IsEmpty)
            {
                lines.Add("── 選取 ──");
                lines.Add("（無）點選平台、繩索或手動邊以檢視詳情。");
                lines.Add("密集節點：Shift+點擊循環選取；節點比幾何更近時可直接點選。");
                lines.Add("Runtime 節點／邊僅供預覽，不可直接編輯。");
                AppendHoverContext(lines);
                return string.Join(Environment.NewLine, lines);
            }

            lines.Add("── 選取 ──");
            switch (_selection.Kind)
            {
                case MapEditorSelectionKind.Platform:
                    AppendPlatformInspector(lines, _selection.Platform!, _selection.SegmentIndex);
                    break;
                case MapEditorSelectionKind.Rope:
                    AppendRopeInspector(lines, _selection.RopeIndex);
                    break;
                case MapEditorSelectionKind.JumpLink:
                    AppendJumpLinkInspector(lines, _selection.JumpLinkIndex);
                    break;
                case MapEditorSelectionKind.ManualEdge:
                    AppendManualEdgeInspector(lines, _selection.ManualEdge!);
                    break;
                case MapEditorSelectionKind.RuntimeNode:
                    AppendRuntimeNodeInspector(lines, _selection.RuntimeNodeIndex);
                    break;
            }

            AppendHoverContext(lines);
            return string.Join(Environment.NewLine, lines);
        }

        private void AppendHoverContext(List<string> lines)
        {
            var hover = GetHoverInfo();
            if (!hover.HasSegmentContext && !hover.HasProjection) return;

            lines.Add(string.Empty);
            lines.Add("── 游標上下文 ──");
            if (hover.HasSegmentContext)
                lines.Add($"Segment index: {hover.SegmentIndex}");
            if (hover.HasRuntimeNode)
            {
                var node = _currentMapData.Nodes[hover.RuntimeNodeIndex];
                lines.Add($"Hover 節點 [{hover.RuntimeNodeIndex}]: {node.Id}");
            }
            if (hover.HasProjection)
                lines.Add($"投影點: ({hover.ProjectionPoint.X:F1}, {hover.ProjectionPoint.Y:F1})");
        }

        private void AppendPlatformInspector(List<string> lines, PolylinePlatformData platform, int segmentIndex)
        {
            lines.Add($"類型: Platform");
            lines.Add($"Id: {platform.Id}");
            lines.Add($"點數: {platform.Points?.Count ?? 0}");
            if (segmentIndex >= 0)
                lines.Add($"選取 segment: {segmentIndex}");

            if (platform.Points != null)
            {
                for (int i = 0; i < platform.Points.Count; i++)
                {
                    var p = platform.Points[i];
                    string safeTag = p.IsSafeZone ? " [安全區]" : "";
                    lines.Add($"  [{i}] ({p.X:F1}, {p.Y:F1}){safeTag}");
                }
            }

            int dependentEdges = _currentMapData.ManualEdgeAnchors?.Count(a =>
                string.Equals(a.FromPlatformId, platform.Id, StringComparison.Ordinal) ||
                string.Equals(a.ToPlatformId, platform.Id, StringComparison.Ordinal)) ?? 0;
            if (dependentEdges > 0)
                lines.Add($"相依 ManualEdge: {dependentEdges} 條");

            int walkEdges = _currentMapData.Edges.Count(e => e.ActionType == NavigationActionType.Walk);
            lines.Add($"（全圖 Walk 邊: {walkEdges}，runtime 唯讀）");
        }

        private void AppendRopeInspector(List<string> lines, int ropeIndex)
        {
            lines.Add("類型: Rope");
            if (_currentMapData.Ropes == null || ropeIndex < 0 || ropeIndex >= _currentMapData.Ropes.Count)
            {
                lines.Add("（繩索已不存在）");
                return;
            }

            var rope = _currentMapData.Ropes[ropeIndex];
            if (rope.Length < 3)
            {
                lines.Add("資料格式無效");
                return;
            }

            lines.Add($"索引: {ropeIndex}");
            lines.Add($"ropeX: {rope[0]:F1}");
            lines.Add($"topY: {rope[1]:F1}");
            lines.Add($"bottomY: {rope[2]:F1}");

            int climbEdges = _currentMapData.Edges.Count(e =>
                e.ActionType is NavigationActionType.ClimbUp or NavigationActionType.ClimbDown);
            lines.Add($"Climb 邊（全圖）: {climbEdges}（runtime 唯讀）");
        }

        private void AppendJumpLinkInspector(List<string> lines, int jumpLinkIndex)
        {
            lines.Add("類型: JumpLink");
            if (_currentMapData.JumpLinks == null || jumpLinkIndex < 0 || jumpLinkIndex >= _currentMapData.JumpLinks.Count)
            {
                lines.Add("（跳點已不存在）");
                return;
            }

            var link = _currentMapData.JumpLinks[jumpLinkIndex];
            if (link.Length < 3)
            {
                lines.Add("資料格式無效");
                return;
            }

            lines.Add($"索引: {jumpLinkIndex}");
            lines.Add($"linkX: {link[0]:F1}");
            lines.Add($"topY: {link[1]:F1}");
            lines.Add($"bottomY: {link[2]:F1}");

            int jumpEdges = _currentMapData.Edges.Count(e =>
                e.ActionType is NavigationActionType.Jump or NavigationActionType.JumpDown);
            lines.Add($"Jump/JumpDown 邊（全圖）: {jumpEdges}（runtime 唯讀）");
            lines.Add("※ 拓撲自動建立下→上 Jump、上→下 JumpDown 雙向邊。");
        }

        private void AppendManualEdgeInspector(List<string> lines, ManualEdgeAnchor anchor)
        {
            lines.Add("類型: ManualEdgeAnchor");
            lines.Add($"From: {anchor.FromPlatformId} ({anchor.FromX:F1}, {anchor.FromY:F1})");
            lines.Add($"To:   {anchor.ToPlatformId} ({anchor.ToX:F1}, {anchor.ToY:F1})");
            lines.Add($"ActionType: {anchor.ActionType} ({GetActionName((int)anchor.ActionType)})");

            string fromNodeId = MapGenerationService.BuildVirtualNodeId(
                anchor.FromPlatformId, anchor.FromX, anchor.FromY);
            string toNodeId = MapGenerationService.BuildVirtualNodeId(
                anchor.ToPlatformId, anchor.ToX, anchor.ToY);

            bool resolved = _currentMapData.Edges.Any(e =>
                string.Equals(e.FromNodeId, fromNodeId, StringComparison.Ordinal) &&
                string.Equals(e.ToNodeId, toNodeId, StringComparison.Ordinal));

            lines.Add(resolved
                ? $"解析: 成功 → {fromNodeId} → {toNodeId}"
                : "解析: 失敗（無對應 runtime 邊）");

            bool hasReverse = _currentMapData.ManualEdgeAnchors?.Any(a =>
                !ReferenceEquals(a, anchor) &&
                string.Equals(a.FromPlatformId, anchor.ToPlatformId, StringComparison.Ordinal) &&
                string.Equals(a.ToPlatformId, anchor.FromPlatformId, StringComparison.Ordinal)) == true;

            lines.Add(hasReverse ? "反向邊: 已存在其他 ManualEdge" : "反向邊: 無（單向）");
        }

        private void AppendRuntimeNodeInspector(List<string> lines, int nodeIndex)
        {
            lines.Add("類型: Runtime Node（唯讀預覽）");
            lines.Add($"索引: {nodeIndex} / {_currentMapData.Nodes.Count - 1}");
            if (nodeIndex < 0 || nodeIndex >= _currentMapData.Nodes.Count)
            {
                lines.Add("（節點已不存在）");
                return;
            }

            var node = _currentMapData.Nodes[nodeIndex];
            lines.Add($"Id: {node.Id}");
            lines.Add($"座標: ({node.X:F1}, {node.Y:F1})");
            lines.Add($"PlatformId: {node.PlatformId ?? "—"}");
            if (_nodeCycleCandidates.Count > 1 && _nodeCycleIndex >= 0)
                lines.Add($"Shift 循環: {_nodeCycleIndex + 1}/{_nodeCycleCandidates.Count}（同區域再 Shift+點擊切換）");
            lines.Add("※ 不可直接編輯；請修改對應 Platform / Rope / ManualEdge。");
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
            ArtaleAI.Application.MapEditor.MapSafeZoneMigration.MigrateLegacySafeZones(_currentMapData);
            _currentMapData.Nodes ??= new List<NavNodeData>();
            _currentMapData.Edges ??= new List<NavEdgeData>();
            _currentMapData.Ropes ??= new List<float[]>();
            _currentMapData.JumpLinks ??= new List<float[]>();
            _currentMapData.PolylinePlatforms ??= new List<PolylinePlatformData>();
            _currentMapData.ManualEdgeAnchors ??= new List<ManualEdgeAnchor>();

            _manualEdgeStartAnchor = null;
            _startPoint = null;
            _previewPoint = null;

            // 載入時立即跑一次拓撲生成以確保 UI 能正確顯示預覽
            RebuildTopology();
            ClearSelection();
            ClearDirty();
            ClearHistory();
            SelectionChanged?.Invoke();
        }

        public MapData GetCurrentMapData()
        {
            RebuildTopology();
            return _currentMapData;
        }

        public void SetEditMode(EditMode mode)
        {
            if ((_currentEditMode == EditMode.Platform ||
                 _currentEditMode == EditMode.Rope ||
                 _currentEditMode == EditMode.JumpLink) &&
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
            _hoveredJumpLink = null;
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
            _lastMousePosition = _previewPoint.Value;
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
                CommitTopologyChange(raiseMutated: false);
            }
            else
            {
                Logger.Warning("[編輯器] 折線平台頂點數不足 2，取消建立");
                CancelCurrentDrawing();
            }
        }

        public void HandleClick(
            PointF screenPoint,
            MouseButtons button = MouseButtons.Left,
            bool preferRuntimeNode = false,
            bool cycleRuntimeNodes = false)
        {
            if (minimapBounds.IsEmpty) return;

            var relativePoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);

            if (_currentEditMode == EditMode.Select)
            {
                HandleSelectClick(relativePoint, preferRuntimeNode, cycleRuntimeNodes);
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
                        if (TryInsertVertexOnNearbyPolyline(relativePoint))
                            return;

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
                        if (!TryCommitVerticalChannelDrawing(
                                relativePoint,
                                _currentMapData.Ropes,
                                "繩索"))
                            return;

                        _startPoint = null;
                        _previewPoint = null;
                        CommitTopologyChange();
                    }
                }
            }
            else if (_currentEditMode == EditMode.JumpLink)
            {
                if (button == MouseButtons.Right)
                {
                    _startPoint = null;
                    _previewPoint = null;
                    Logger.Info("[編輯器] 取消跳點幾何繪製");
                }
                else
                {
                    if (!_startPoint.HasValue)
                    {
                        _startPoint = relativePoint;
                        Logger.Info($"[編輯器] 設定跳點起點: ({relativePoint.X:F1}, {relativePoint.Y:F1})");
                    }
                    else
                    {
                        _currentMapData.JumpLinks ??= new List<float[]>();
                        if (!TryCommitVerticalChannelDrawing(
                                relativePoint,
                                _currentMapData.JumpLinks,
                                "跳點"))
                            return;

                        _startPoint = null;
                        _previewPoint = null;
                        CommitTopologyChange();
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
                    var hitAnchor = FindNearestPlatformProjection(relativePoint, HitRadius);
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

                        CommitTopologyChange();
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

            DrawValidationOverlays(g, convert);

            if (Layers.ShowPlatforms)
                DrawPlatformsLayer(g, convert);

            if (Layers.ShowRopes)
                DrawRopesLayer(g, convert);
            if (Layers.ShowJumpLinks)
                DrawJumpLinksLayer(g, convert);
            if (Layers.ShowSafeZones)
                DrawSafeZonesLayer(g, convert);

            if (Layers.ShowManualAnchors)
                DrawManualEdgeAnchors(g, convert);

            if (Layers.ShowEdges)
                DrawEdgesLayer(g, convert);

            if (Layers.ShowNodes)
                DrawNodesLayer(g, convert);
        }

        private void DrawPlatformsLayer(Graphics g, Func<PointF, PointF> convert)
        {
            if (_currentMapData.PolylinePlatforms?.Any() != true)
                return;

            foreach (var plat in _currentMapData.PolylinePlatforms)
            {
                if (plat.Points == null || plat.Points.Count < 2) continue;

                bool isSelected = _selection.Kind == MapEditorSelectionKind.Platform &&
                    ReferenceEquals(plat, _selection.Platform);
                bool isPlatHovered = plat == _hoveredPlatform;
                Color platColor = isSelected ? Color.DeepSkyBlue :
                    isPlatHovered ? Color.Chartreuse : Color.Lime;
                float platWidth = isSelected ? 7.0f :
                    isPlatHovered ? 6.0f : 4.0f;

                for (int i = 0; i < plat.Points.Count - 1; i++)
                {
                    var p1 = convert(new PointF(minimapBounds.X + plat.Points[i].X, minimapBounds.Y + plat.Points[i].Y));
                    var p2 = convert(new PointF(minimapBounds.X + plat.Points[i + 1].X, minimapBounds.Y + plat.Points[i + 1].Y));

                    bool isSegSelected = isSelected &&
                        _selection.SegmentIndex >= 0 &&
                        i == _selection.SegmentIndex;
                    bool isSegHovered = isPlatHovered && i == _hoveredSegmentIndex;
                    float currentWidth = isSegSelected ? 9.0f :
                        isSegHovered ? 8.0f : platWidth;
                    Color currentColor = isSegSelected ? Color.White :
                        isSegHovered ? Color.Gold : platColor;

                    using (var pen = new Pen(currentColor, currentWidth))
                    {
                        g.DrawLine(pen, p1, p2);
                    }

                    g.FillRectangle(Brushes.White, p1.X - 2, p1.Y - 2, 4, 4);
                    if (isSelected && _currentEditMode == EditMode.Select)
                    {
                        g.DrawEllipse(Pens.DeepSkyBlue, p1.X - 5f, p1.Y - 5f, 10f, 10f);
                    }
                    if (i == plat.Points.Count - 2)
                    {
                        g.FillRectangle(Brushes.White, p2.X - 2, p2.Y - 2, 4, 4);
                        if (isSelected && _currentEditMode == EditMode.Select)
                        {
                            g.DrawEllipse(Pens.DeepSkyBlue, p2.X - 5f, p2.Y - 5f, 10f, 10f);
                        }
                    }
                }

                if (plat == _hoveredPlatform && _hoveredProjectionPoint != PointF.Empty)
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

        private void DrawEdgesLayer(Graphics g, Func<PointF, PointF> convert)
        {
            if (_currentMapData.Edges.Count == 0)
                return;

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
                bool isEdgeSelected = isManual && _selection.Kind == MapEditorSelectionKind.ManualEdge &&
                    _selection.ManualEdge != null &&
                    EdgeMatchesAnchor(edge, _selection.ManualEdge);

                Color lineColor = GetEdgePreviewColor(edge, isManual);
                var p1 = convert(new PointF(minimapBounds.X + p1Data.X, minimapBounds.Y + p1Data.Y));
                var p2 = convert(new PointF(minimapBounds.X + p2Data.X, minimapBounds.Y + p2Data.Y));

                float penWidth = isEdgeSelected ? 4.5f :
                    isEdgeHovered ? 4.0f : (isManual ? 2.5f : 1.8f);
                Color drawColor = isEdgeSelected ? Color.DeepSkyBlue :
                    isEdgeHovered ? Color.Red : lineColor;

                using (var pen = new Pen(drawColor, penWidth) { DashStyle = GetEdgeDashStyle(edge.ActionType) })
                {
                    g.DrawLine(pen, p1, p2);

                    float midX = (p1.X + p2.X) / 2;
                    float midY = (p1.Y + p2.Y) / 2;
                    using var arrowBrush = new SolidBrush(drawColor);
                    DrawArrow(g, p1, p2, new PointF(midX, midY), arrowBrush);
                }
            }
        }

        private void DrawNodesLayer(Graphics g, Func<PointF, PointF> convert)
        {
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
                bool isSelectedNode = _selection.Kind == MapEditorSelectionKind.RuntimeNode &&
                    _selection.RuntimeNodeIndex == i;

                if (isSelectedNode)
                {
                    nodeColor = Color.DeepSkyBlue;
                    float ring = radius + 4f;
                    using var ringPen = new Pen(Color.White, 2f);
                    g.DrawEllipse(ringPen, pos.X - ring, pos.Y - ring, ring * 2, ring * 2);
                }
                else if (i == _hoveredNodeIndex)
                {
                    nodeColor = Color.White;
                    float ring = radius + 3f;
                    using var ringPen = new Pen(Color.Gold, 1.5f);
                    g.DrawEllipse(ringPen, pos.X - ring, pos.Y - ring, ring * 2, ring * 2);
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
        }

        private void DrawRopesLayer(Graphics g, Func<PointF, PointF> convert)
        {
            if (_currentMapData.Ropes?.Any() != true)
                return;

            foreach (var rope in _currentMapData.Ropes)
            {
                if (rope.Length < 3) continue;

                float x = rope[0];
                float topY = rope[1];
                float bottomY = rope[2];

                var pTop = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + topY));
                var pBottom = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + bottomY));

                bool isRopeHovered = rope == _hoveredRope;
                bool isRopeSelected = _selection.Kind == MapEditorSelectionKind.Rope &&
                    _selection.RopeIndex >= 0 &&
                    _currentMapData.Ropes != null &&
                    _selection.RopeIndex < _currentMapData.Ropes.Count &&
                    ReferenceEquals(rope, _currentMapData.Ropes[_selection.RopeIndex]);

                DrawLineMarker(g, new LineMarker(
                    pTop, pBottom, RopeStyle, ResolveMarkerState(isRopeSelected, isRopeHovered)));
            }
        }

        private void DrawJumpLinksLayer(Graphics g, Func<PointF, PointF> convert)
        {
            if (_currentMapData.JumpLinks?.Any() != true)
                return;

            foreach (var link in _currentMapData.JumpLinks)
            {
                if (link.Length < 3) continue;

                float x = link[0];
                float topY = link[1];
                float bottomY = link[2];

                var pTop = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + topY));
                var pBottom = convert(new PointF(minimapBounds.X + x, minimapBounds.Y + bottomY));

                bool isHovered = link == _hoveredJumpLink;
                bool isSelected = _selection.Kind == MapEditorSelectionKind.JumpLink &&
                    _selection.JumpLinkIndex >= 0 &&
                    _currentMapData.JumpLinks != null &&
                    _selection.JumpLinkIndex < _currentMapData.JumpLinks.Count &&
                    ReferenceEquals(link, _currentMapData.JumpLinks[_selection.JumpLinkIndex]);

                DrawLineMarker(g, new LineMarker(
                    pTop, pBottom, JumpLinkStyle, ResolveMarkerState(isSelected, isHovered)));
            }
        }

        private void DrawSafeZonesLayer(Graphics g, Func<PointF, PointF> convert)
        {
            if (_currentMapData.PolylinePlatforms?.Any() != true)
                return;

            foreach (var plat in _currentMapData.PolylinePlatforms)
            {
                if (plat.Points == null || plat.Points.Count < 2)
                    continue;

                for (int i = 0; i < plat.Points.Count - 1; i++)
                {
                    var a = plat.Points[i];
                    var b = plat.Points[i + 1];
                    if (!a.IsSafeZone || !b.IsSafeZone)
                        continue;

                    var p1 = convert(new PointF(minimapBounds.X + a.X, minimapBounds.Y + a.Y));
                    var p2 = convert(new PointF(minimapBounds.X + b.X, minimapBounds.Y + b.Y));
                    DrawLineMarker(g, new LineMarker(p1, p2, SafeZoneStyle, MarkerVisualState.Normal));
                }

                for (int i = 0; i < plat.Points.Count; i++)
                {
                    if (!plat.Points[i].IsSafeZone)
                        continue;

                    var pt = convert(new PointF(
                        minimapBounds.X + plat.Points[i].X,
                        minimapBounds.Y + plat.Points[i].Y));
                    using var brush = new SolidBrush(Color.LimeGreen);
                    g.FillEllipse(brush, pt.X - 3.5f, pt.Y - 3.5f, 7f, 7f);
                }
            }
        }

        /// <summary>
        /// 單段線標記的統一繪製器：比照路徑標記的視覺層次
        /// （hover／select 發光底層 + 主線粗細分級 + 選取端點高亮環）。
        /// 繩索、跳點、安全區共用，確保外觀一致並消除重複繪製碼。
        /// </summary>
        private void DrawLineMarker(Graphics g, LineMarker marker)
        {
            Color mainColor = marker.State switch
            {
                MarkerVisualState.Selected => marker.Style.SelectColor,
                MarkerVisualState.Hovered => marker.Style.HoverColor,
                _ => marker.Style.NormalColor
            };
            float mainWidth = marker.State switch
            {
                MarkerVisualState.Selected => MarkerLineWidthSelected,
                MarkerVisualState.Hovered => MarkerLineWidthHovered,
                _ => MarkerLineWidthNormal
            };

            if (marker.State != MarkerVisualState.Normal)
            {
                using var glow = new Pen(Color.FromArgb(70, mainColor), mainWidth + MarkerGlowExtraWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                g.DrawLine(glow, marker.A, marker.B);
            }

            using (var pen = new Pen(mainColor, mainWidth)
            {
                DashStyle = marker.Style.Dashed ? DashStyle.Dash : DashStyle.Solid
            })
            {
                g.DrawLine(pen, marker.A, marker.B);
            }

            DrawMarkerCap(g, marker.A, marker.Style.StartCapColor, marker.State);
            DrawMarkerCap(g, marker.B, marker.Style.EndCapColor, marker.State);
        }

        private static void DrawMarkerCap(Graphics g, PointF p, Color capColor, MarkerVisualState state)
        {
            using (var brush = new SolidBrush(capColor))
            {
                g.FillRectangle(
                    brush,
                    p.X - MarkerCapHalfSize,
                    p.Y - MarkerCapHalfSize,
                    MarkerCapHalfSize * 2,
                    MarkerCapHalfSize * 2);
            }

            if (state == MarkerVisualState.Selected)
            {
                using var ringPen = new Pen(Color.White, 2f);
                g.DrawEllipse(
                    ringPen,
                    p.X - MarkerSelectionRingRadius,
                    p.Y - MarkerSelectionRingRadius,
                    MarkerSelectionRingRadius * 2,
                    MarkerSelectionRingRadius * 2);
            }
        }

        private static MarkerVisualState ResolveMarkerState(bool isSelected, bool isHovered) =>
            isSelected ? MarkerVisualState.Selected :
            isHovered ? MarkerVisualState.Hovered :
            MarkerVisualState.Normal;

        /// <summary>保留供舊註解對照；實際繪製改用 GetEdgePreviewColor。</summary>
        private static Color GetEdgeDrawColor(NavEdgeData edge) => GetEdgePreviewColor(edge, false);

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

                        // Snap indicator：預覽落點
                        using (var snapPen = new Pen(Color.White, 1.5f))
                        {
                            g.DrawEllipse(snapPen, pMouse.X - 3f, pMouse.Y - 3f, 6f, 6f);
                            g.DrawLine(snapPen, pMouse.X - 4f, pMouse.Y, pMouse.X + 4f, pMouse.Y);
                            g.DrawLine(snapPen, pMouse.X, pMouse.Y - 4f, pMouse.X, pMouse.Y + 4f);
                        }
                    }
                }
            }
            else
            {
                bool isLineMode = _currentEditMode
                    is EditMode.Rope or EditMode.JumpLink or EditMode.ManualEdge;

                if (_startPoint.HasValue && _previewPoint.HasValue && isLineMode)
                {
                    var startScreen = new PointF(
                        minimapBounds.X + _startPoint.Value.X,
                        minimapBounds.Y + _startPoint.Value.Y);
                    var previewScreen = new PointF(
                        minimapBounds.X + _previewPoint.Value.X,
                        minimapBounds.Y + _previewPoint.Value.Y);

                    // 安全區為策略圖層，預覽用 LimeGreen 與拓撲線段（Cyan）區隔。
                    var previewColor = Color.Cyan;
                    using (var pen = new Pen(previewColor, 2) { DashStyle = DashStyle.Dash })
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

        /// <summary>若點落在既有折線吸附範圍內，插入折點並重建拓樸。</summary>
        private bool TryInsertVertexOnNearbyPolyline(PointF relativePoint)
        {
            if (!TryFindNearbyPolylineInsertTarget(
                relativePoint,
                out var hitPlat,
                out var hitResult,
                out bool tooCloseToVertex))
            {
                if (tooCloseToVertex)
                    Logger.Warning("[編輯器] 點擊位置距離既有折點過近，忽略此次插點操作");
                return false;
            }

            var newPt = new PlatformPointData
            {
                X = (float)Math.Round(hitResult.ProjectionPoint.X, 1),
                Y = (float)Math.Round(hitResult.ProjectionPoint.Y, 1)
            };
            hitPlat.Points.Insert(hitResult.SegmentIndex + 1, newPt);
            Logger.Info(
                $"[編輯器] 於折線平台 {hitPlat.Id} 的段 {hitResult.SegmentIndex} 插入折點: ({newPt.X:F1}, {newPt.Y:F1})");
            CommitTopologyChange();
            return true;
        }

        /// <summary>
        /// 尋找可插點的最近折線投影。
        /// 若僅因「距既有頂點過近」而拒絕，<paramref name="tooCloseToVertex"/> 為 true。
        /// </summary>
        private bool TryFindNearbyPolylineInsertTarget(
            PointF relativePoint,
            out PolylinePlatformData hitPlat,
            out PolylineHitResult hitResult,
            out bool tooCloseToVertex)
        {
            hitPlat = null!;
            hitResult = null!;
            tooCloseToVertex = false;

            PolylinePlatformData? bestPlat = null;
            PolylineHitResult? bestHit = null;
            float bestDist = float.MaxValue;

            if (_currentMapData.PolylinePlatforms == null)
                return false;

            foreach (var plat in _currentMapData.PolylinePlatforms)
            {
                var hit = GetDistanceToPolyline(relativePoint, plat);
                if (hit.Distance < HitRadius && hit.Distance < bestDist)
                {
                    bestDist = hit.Distance;
                    bestPlat = plat;
                    bestHit = hit;
                }
            }

            if (bestPlat == null || bestHit == null)
                return false;

            foreach (var v in bestPlat.Points)
            {
                float dx = bestHit.ProjectionPoint.X - v.X;
                float dy = bestHit.ProjectionPoint.Y - v.Y;
                float distToVertex = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distToVertex <= HitRadius)
                {
                    tooCloseToVertex = true;
                    return false;
                }
            }

            hitPlat = bestPlat;
            hitResult = bestHit;
            return true;
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

            if (_currentMapData.ManualEdgeAnchors != null)
            {
                foreach (var anchor in _currentMapData.ManualEdgeAnchors)
                {
                    float dist = GetDistanceToManualEdgeAnchor(clickPosition, anchor);
                    if (dist < threshold && dist < bestManualEdgeDist)
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

            float[]? bestJumpLink = null;
            float bestJumpLinkDist = float.MaxValue;
            int bestJumpLinkIdx = -1;

            if (_currentMapData.JumpLinks != null)
            {
                for (int i = 0; i < _currentMapData.JumpLinks.Count; i++)
                {
                    var link = _currentMapData.JumpLinks[i];
                    if (link.Length < 3) continue;

                    float dist = GetDistanceToVerticalChannel(clickPosition, link);
                    if (dist < threshold && dist < bestJumpLinkDist)
                    {
                        bestJumpLinkDist = dist;
                        bestJumpLink = link;
                        bestJumpLinkIdx = i;
                    }
                }
            }

            if (bestJumpLink != null)
            {
                return new DeleteTarget(
                    DeleteTargetKind.JumpLink,
                    null,
                    bestJumpLink,
                    bestJumpLinkIdx,
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

                    float dist = GetDistanceToVerticalChannel(clickPosition, rope);
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
            if (target.Kind == DeleteTargetKind.None) return;

            InvalidateSelectionIfNeeded(target);

            switch (target.Kind)
            {
                case DeleteTargetKind.ManualEdge when target.ManualEdge != null:
                    _currentMapData.ManualEdgeAnchors?.Remove(target.ManualEdge);
                    Logger.Info(
                        $"[編輯器] 刪除手動例外邊錨點: {target.ManualEdge.FromPlatformId} -> {target.ManualEdge.ToPlatformId} Action={target.ManualEdge.ActionType}");
                    CommitTopologyChange();
                    break;

                case DeleteTargetKind.JumpLink when target.LineGeometry != null && target.LineIndex >= 0:
                    _currentMapData.JumpLinks?.RemoveAt(target.LineIndex);
                    Logger.Info(
                        $"[編輯器] 刪除跳點幾何: X={target.LineGeometry[0]:F1}, Y={target.LineGeometry[1]:F1}~{target.LineGeometry[2]:F1}");
                    CommitTopologyChange();
                    break;

                case DeleteTargetKind.Rope when target.LineGeometry != null && target.LineIndex >= 0:
                    _currentMapData.Ropes?.RemoveAt(target.LineIndex);
                    Logger.Info(
                        $"[編輯器] 刪除繩索幾何: X={target.LineGeometry[0]:F1}, Y={target.LineGeometry[1]:F1}~{target.LineGeometry[2]:F1}");
                    CommitTopologyChange();
                    break;


                case DeleteTargetKind.Platform when target.Platform != null:
                    string platformId = target.Platform.Id;
                    _currentMapData.PolylinePlatforms?.Remove(target.Platform);
                    PruneManualEdgeAnchorsForPlatform(platformId);
                    Logger.Info($"[編輯器] 刪除折線平台幾何: Id={platformId}");
                    CommitTopologyChange();
                    break;
            }
        }

        private void HandleDeleteAction(PointF clickPosition)
        {
            var target = ResolveDeleteTarget(clickPosition, HitRadius);

            if (target.Kind == DeleteTargetKind.None)
            {
                Logger.Info("[編輯器] 未命中任何可刪除的標記（手動邊、跳點、繩索、安全區、平台）");
                return;
            }

            if (ConfirmDestructiveAction != null &&
                !ConfirmDestructiveAction(DescribeDeleteTarget(target.Kind, target.Platform, target.LineIndex)))
            {
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

        private static float GetDistanceToVerticalChannel(PointF p, float[] channel)
        {
            float channelX = channel[0];
            float topY = channel[1];
            float bottomY = channel[2];

            if (p.Y >= topY && p.Y <= bottomY)
            {
                return Math.Abs(p.X - channelX);
            }

            if (p.Y < topY)
            {
                float dx = channelX - p.X;
                float dy = topY - p.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            float dxBot = channelX - p.X;
            float dyBot = bottomY - p.Y;
            return (float)Math.Sqrt(dxBot * dxBot + dyBot * dyBot);
        }

        private bool TryCommitVerticalChannelDrawing(PointF endPoint, List<float[]> targetList, string label)
        {
            var start = _startPoint!.Value;
            float length = Math.Abs(start.Y - endPoint.Y);
            if (length < 2.0f)
            {
                Logger.Warning($"[編輯器] {label}幾何長度過短 ({length:F1} < 2.0)，取消建立");
                _startPoint = null;
                _previewPoint = null;
                return false;
            }

            float topY = Math.Min(start.Y, endPoint.Y);
            float bottomY = Math.Max(start.Y, endPoint.Y);
            float x = start.X;

            targetList.Add(new[]
            {
                (float)Math.Round(x, 1),
                (float)Math.Round(topY, 1),
                (float)Math.Round(bottomY, 1)
            });

            Logger.Info($"[編輯器] 建立{label}幾何: X={x:F1}, Y={topY:F1}~{bottomY:F1}");
            return true;
        }

        /// <summary>
        /// 更新目前滑鼠所在的 Hover 物件狀態。
        /// Delete 模式命中順序：手動邊 → 跳點 → 繩索 → 安全區 → 平台（與 HandleDeleteAction 共用 ResolveDeleteTarget）。
        /// Select 模式：節點與幾何依距離競爭；Shift 時優先節點。
        /// </summary>
        public void UpdateHoveredNode(PointF screenPoint, bool preferRuntimeNode = false)
        {
            if (minimapBounds.IsEmpty) return;

            var relativePoint = new PointF(
               screenPoint.X - minimapBounds.X,
               screenPoint.Y - minimapBounds.Y);

            _hoveredNodeIndex = -1;
            _hoveredPlatform = null;
            _hoveredRope = null;
            _hoveredJumpLink = null;
            _hoveredManualEdgeAnchor = null;
            _hoveredSegmentIndex = -1;
            _hoveredProjectionPoint = PointF.Empty;

            bool isPlatformModeNotDrawing = _currentEditMode == EditMode.Platform && _activeDrawingPoints.Count == 0;

            if (_currentEditMode is EditMode.Delete or EditMode.Select)
            {
                if (_currentEditMode == EditMode.Select)
                {
                    ApplySelectHover(relativePoint, preferRuntimeNode);
                    return;
                }

                var pickTarget = ResolveDeleteTarget(relativePoint, HitRadius);
                switch (pickTarget.Kind)
                {
                    case DeleteTargetKind.ManualEdge:
                        _hoveredManualEdgeAnchor = pickTarget.ManualEdge;
                        break;
                    case DeleteTargetKind.JumpLink:
                        _hoveredJumpLink = pickTarget.LineGeometry;
                        break;
                    case DeleteTargetKind.Rope:
                        _hoveredRope = pickTarget.LineGeometry;
                        break;
                    case DeleteTargetKind.Platform:
                        _hoveredPlatform = pickTarget.Platform;
                        if (pickTarget.PlatformHit != null)
                        {
                            _hoveredSegmentIndex = pickTarget.PlatformHit.SegmentIndex;
                            _hoveredProjectionPoint = pickTarget.PlatformHit.ProjectionPoint;
                        }
                        break;
                }

                return;
            }

            if (isPlatformModeNotDrawing)
            {
                if (TryFindNearbyPolylineInsertTarget(
                    relativePoint,
                    out var bestPlatform,
                    out var bestHitResult,
                    out _))
                {
                    _hoveredPlatform = bestPlatform;
                    _hoveredSegmentIndex = bestHitResult.SegmentIndex;
                    _hoveredProjectionPoint = bestHitResult.ProjectionPoint;
                }
            }
            else if (_currentEditMode == EditMode.ManualEdge)
            {
                var hit = FindNearestPlatformProjection(relativePoint, HitRadius);
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

        private int FindNearestRuntimeNodeIndex(PointF relativePoint, float hitRadius = HitRadius)
        {
            return FindNearestRuntimeNodeIndex(relativePoint, hitRadius, out _);
        }

        private int FindNearestRuntimeNodeIndex(PointF relativePoint, float hitRadius, out float nearestDistance)
        {
            nearestDistance = float.MaxValue;
            if (_currentMapData.Nodes.Count == 0) return -1;

            int nearestIndex = -1;

            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var node = _currentMapData.Nodes[i];
                float dx = node.X - relativePoint.X;
                float dy = node.Y - relativePoint.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < hitRadius && dist < nearestDistance)
                {
                    nearestDistance = dist;
                    nearestIndex = i;
                }
            }

            return nearestIndex;
        }

        private List<(int Index, float Distance)> FindRuntimeNodesWithinRadius(PointF relativePoint, float hitRadius)
        {
            var results = new List<(int Index, float Distance)>();
            for (int i = 0; i < _currentMapData.Nodes.Count; i++)
            {
                var node = _currentMapData.Nodes[i];
                float dx = node.X - relativePoint.X;
                float dy = node.Y - relativePoint.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist < hitRadius)
                    results.Add((i, dist));
            }

            results.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return results;
        }

        private static float DistanceBetween(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private float GetDeleteTargetDistance(PointF relativePoint, DeleteTarget target)
        {
            return target.Kind switch
            {
                DeleteTargetKind.ManualEdge when target.ManualEdge != null =>
                    GetDistanceToManualEdgeAnchor(relativePoint, target.ManualEdge),
                DeleteTargetKind.JumpLink when target.LineGeometry != null =>
                    GetDistanceToVerticalChannel(relativePoint, target.LineGeometry),
                DeleteTargetKind.Rope when target.LineGeometry != null =>
                    GetDistanceToVerticalChannel(relativePoint, target.LineGeometry),
                DeleteTargetKind.Platform when target.Platform != null =>
                    GetDistanceToPolyline(relativePoint, target.Platform).Distance,
                _ => float.MaxValue
            };
        }

        private bool ShouldPreferRuntimeNode(
            int nodeIndex,
            float nodeDistance,
            DeleteTarget geometryPick,
            float geometryDistance,
            bool preferRuntimeNode)
        {
            if (nodeIndex < 0) return false;
            if (preferRuntimeNode) return true;
            if (geometryPick.Kind == DeleteTargetKind.None) return true;
            return nodeDistance < geometryDistance;
        }

        private void ApplySelectHover(PointF relativePoint, bool preferRuntimeNode)
        {
            var geometryPick = ResolveDeleteTarget(relativePoint, HitRadius);
            float geometryDistance = GetDeleteTargetDistance(relativePoint, geometryPick);

            int nodeIndex = FindNearestRuntimeNodeIndex(relativePoint, HitRadius, out float nodeDistance);
            if (ShouldPreferRuntimeNode(nodeIndex, nodeDistance, geometryPick, geometryDistance, preferRuntimeNode))
            {
                _hoveredNodeIndex = nodeIndex;
                return;
            }

            switch (geometryPick.Kind)
            {
                case DeleteTargetKind.ManualEdge:
                    _hoveredManualEdgeAnchor = geometryPick.ManualEdge;
                    break;
                case DeleteTargetKind.JumpLink:
                    _hoveredJumpLink = geometryPick.LineGeometry;
                    break;
                case DeleteTargetKind.Rope:
                    _hoveredRope = geometryPick.LineGeometry;
                    break;
                case DeleteTargetKind.Platform:
                    _hoveredPlatform = geometryPick.Platform;
                    if (geometryPick.PlatformHit != null)
                    {
                        _hoveredSegmentIndex = geometryPick.PlatformHit.SegmentIndex;
                        _hoveredProjectionPoint = geometryPick.PlatformHit.ProjectionPoint;
                    }
                    break;
            }
        }

        private void HandleSelectClick(PointF relativePoint, bool preferRuntimeNode, bool cycleRuntimeNodes)
        {
            var geometryPick = ResolveDeleteTarget(relativePoint, HitRadius);
            float geometryDistance = GetDeleteTargetDistance(relativePoint, geometryPick);

            int nodeIndex = -1;
            if (cycleRuntimeNodes)
            {
                nodeIndex = PickCycledRuntimeNode(relativePoint);
            }
            else
            {
                nodeIndex = FindNearestRuntimeNodeIndex(relativePoint, HitRadius, out _);
            }

            if (ShouldPreferRuntimeNode(
                    nodeIndex,
                    nodeIndex >= 0 ? DistanceBetween(relativePoint, GetNodePosition(nodeIndex)) : float.MaxValue,
                    geometryPick,
                    geometryDistance,
                    preferRuntimeNode))
            {
                SetSelection(MapEditorSelection.ForRuntimeNode(nodeIndex));
                return;
            }

            if (geometryPick.Kind != DeleteTargetKind.None)
            {
                ApplySelectionFromPick(geometryPick);
                return;
            }

            if (nodeIndex >= 0)
                SetSelection(MapEditorSelection.ForRuntimeNode(nodeIndex));
            else
                ClearSelection();
        }

        private PointF GetNodePosition(int nodeIndex)
        {
            var node = _currentMapData.Nodes[nodeIndex];
            return new PointF(node.X, node.Y);
        }

        private int PickCycledRuntimeNode(PointF relativePoint)
        {
            var candidates = FindRuntimeNodesWithinRadius(relativePoint, HitRadius);
            if (candidates.Count == 0)
            {
                _nodeCycleCandidates.Clear();
                _nodeCycleIndex = -1;
                return -1;
            }

            var candidateIndices = candidates.Select(c => c.Index).ToList();
            bool sameArea = _nodeCycleCandidates.Count > 0 &&
                DistanceBetween(relativePoint, _lastNodeCyclePoint) < HitRadius;
            bool sameSet = sameArea &&
                _nodeCycleCandidates.SequenceEqual(candidateIndices);

            if (!sameSet)
            {
                _nodeCycleCandidates = candidateIndices;
                _nodeCycleIndex = 0;
                _lastNodeCyclePoint = relativePoint;
            }
            else
            {
                _nodeCycleIndex = (_nodeCycleIndex + 1) % _nodeCycleCandidates.Count;
            }

            return _nodeCycleCandidates[_nodeCycleIndex];
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
