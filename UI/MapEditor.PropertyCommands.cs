using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.UI.MapEditing;

namespace ArtaleAI.UI
{
    public sealed class MapEditorMapSummary
    {
        public int PlatformCount { get; init; }
        public int RopeCount { get; init; }
        public int JumpLinkCount { get; init; }
        public int ManualEdgeCount { get; init; }
        public int RuntimeNodeCount { get; init; }
        public int RuntimeEdgeCount { get; init; }
    }

    public sealed class MapEditorPlatformStats
    {
        public int PointCount { get; init; }
        public float TotalLength { get; init; }
        public int DependentManualEdgeCount { get; init; }
        public int RuntimeNodeCount { get; init; }
        public int WalkEdgeCount { get; init; }
        public int? SelectedSegmentIndex { get; init; }
    }

    public sealed class MapEditorRopeStats
    {
        public int ClimbEdgeCount { get; init; }
    }

    public sealed class MapEditorJumpLinkStats
    {
        public int JumpEdgeCount { get; init; }
    }

    public sealed class MapEditorManualEdgeStats
    {
        public bool Resolved { get; init; }
        public string? FromNodeId { get; init; }
        public string? ToNodeId { get; init; }
        public bool HasReverse { get; init; }
    }

    public partial class MapEditor
    {
        public event Action? MapMutated;

        /// <summary>刪除前確認；回傳 false 則取消。參數為提示訊息。</summary>
        public Func<string, bool>? ConfirmDestructiveAction { get; set; }

        public MapEditorMapSummary GetMapSummary()
        {
            return new MapEditorMapSummary
            {
                PlatformCount = _currentMapData.PolylinePlatforms?.Count ?? 0,
                RopeCount = _currentMapData.Ropes?.Count ?? 0,
                JumpLinkCount = _currentMapData.JumpLinks?.Count ?? 0,
                ManualEdgeCount = _currentMapData.ManualEdgeAnchors?.Count ?? 0,
                RuntimeNodeCount = _currentMapData.Nodes.Count,
                RuntimeEdgeCount = _currentMapData.Edges.Count
            };
        }

        public MapEditorPlatformStats GetPlatformStats(PolylinePlatformData platform, int segmentIndex = -1)
        {
            int dependent = _currentMapData.ManualEdgeAnchors?.Count(a =>
                string.Equals(a.FromPlatformId, platform.Id, StringComparison.Ordinal) ||
                string.Equals(a.ToPlatformId, platform.Id, StringComparison.Ordinal)) ?? 0;

            int nodeCount = _currentMapData.Nodes.Count(n =>
                string.Equals(n.PlatformId, platform.Id, StringComparison.Ordinal));

            int walkCount = _currentMapData.Edges.Count(e => e.ActionType == NavigationActionType.Walk);

            return new MapEditorPlatformStats
            {
                PointCount = platform.Points?.Count ?? 0,
                TotalLength = ComputePolylineLength(platform),
                DependentManualEdgeCount = dependent,
                RuntimeNodeCount = nodeCount,
                WalkEdgeCount = walkCount,
                SelectedSegmentIndex = segmentIndex >= 0 ? segmentIndex : null
            };
        }

        public MapEditorRopeStats GetRopeStats(int ropeIndex)
        {
            int climb = _currentMapData.Edges.Count(e =>
                e.ActionType is NavigationActionType.ClimbUp or NavigationActionType.ClimbDown);
            return new MapEditorRopeStats { ClimbEdgeCount = climb };
        }

        public MapEditorJumpLinkStats GetJumpLinkStats(int jumpLinkIndex)
        {
            int jumpEdges = _currentMapData.Edges.Count(e =>
                e.ActionType is NavigationActionType.Jump or NavigationActionType.JumpDown);
            return new MapEditorJumpLinkStats { JumpEdgeCount = jumpEdges };
        }

        public bool TryUpdateJumpLink(int jumpLinkIndex, float linkX, float topY, float bottomY, out string? error)
        {
            error = null;
            _currentMapData.JumpLinks ??= new List<float[]>();
            if (jumpLinkIndex < 0 || jumpLinkIndex >= _currentMapData.JumpLinks.Count)
            {
                error = "跳點索引無效。";
                return false;
            }

            if (topY > bottomY)
                (topY, bottomY) = (bottomY, topY);

            if (bottomY - topY < 2.0f)
            {
                error = "跳點垂直跨度至少 2px。";
                return false;
            }

            _currentMapData.JumpLinks[jumpLinkIndex] = new[]
            {
                RoundCoord(linkX),
                RoundCoord(topY),
                RoundCoord(bottomY)
            };

            CommitMutation();
            SetSelection(MapEditorSelection.ForJumpLink(jumpLinkIndex));
            return true;
        }

        public MapEditorManualEdgeStats GetManualEdgeStats(ManualEdgeAnchor anchor)
        {
            string fromNodeId = MapGenerationService.BuildVirtualNodeId(
                anchor.FromPlatformId, anchor.FromX, anchor.FromY);
            string toNodeId = MapGenerationService.BuildVirtualNodeId(
                anchor.ToPlatformId, anchor.ToX, anchor.ToY);

            bool resolved = _currentMapData.Edges.Any(e =>
                string.Equals(e.FromNodeId, fromNodeId, StringComparison.Ordinal) &&
                string.Equals(e.ToNodeId, toNodeId, StringComparison.Ordinal));

            bool hasReverse = _currentMapData.ManualEdgeAnchors?.Any(a =>
                !ReferenceEquals(a, anchor) &&
                string.Equals(a.FromPlatformId, anchor.ToPlatformId, StringComparison.Ordinal) &&
                string.Equals(a.ToPlatformId, anchor.FromPlatformId, StringComparison.Ordinal)) == true;

            return new MapEditorManualEdgeStats
            {
                Resolved = resolved,
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                HasReverse = hasReverse
            };
        }

        public bool TryRenamePlatformId(PolylinePlatformData platform, string newId, out string? error)
        {
            error = null;
            newId = newId.Trim();
            if (string.IsNullOrEmpty(newId))
            {
                error = "平台 Id 不可為空白。";
                return false;
            }

            if (string.Equals(platform.Id, newId, StringComparison.Ordinal))
                return true;

            if (_currentMapData.PolylinePlatforms?.Any(p =>
                    !ReferenceEquals(p, platform) &&
                    string.Equals(p.Id, newId, StringComparison.Ordinal)) == true)
            {
                error = $"平台 Id「{newId}」已存在。";
                return false;
            }

            string oldId = platform.Id;
            platform.Id = newId;

            if (_currentMapData.ManualEdgeAnchors != null)
            {
                foreach (var anchor in _currentMapData.ManualEdgeAnchors)
                {
                    if (string.Equals(anchor.FromPlatformId, oldId, StringComparison.Ordinal))
                        anchor.FromPlatformId = newId;
                    if (string.Equals(anchor.ToPlatformId, oldId, StringComparison.Ordinal))
                        anchor.ToPlatformId = newId;
                }
            }

            CommitMutation();
            RefreshPlatformSelection(platform);
            return true;
        }

        public bool TryUpdatePlatformPoint(
            PolylinePlatformData platform,
            int pointIndex,
            float x,
            float y,
            out string? error)
        {
            error = null;
            if (platform.Points == null || pointIndex < 0 || pointIndex >= platform.Points.Count)
            {
                error = "折點索引無效。";
                return false;
            }

            platform.Points[pointIndex].X = RoundCoord(x);
            platform.Points[pointIndex].Y = RoundCoord(y);
            CommitMutation();
            RefreshPlatformSelection(platform);
            return true;
        }

        public bool TryRemovePlatformPoint(PolylinePlatformData platform, int pointIndex, out string? error)
        {
            error = null;
            if (platform.Points == null || pointIndex < 0 || pointIndex >= platform.Points.Count)
            {
                error = "折點索引無效。";
                return false;
            }

            if (platform.Points.Count <= 2)
            {
                error = "平台至少需保留 2 個折點。";
                return false;
            }

            platform.Points.RemoveAt(pointIndex);
            CommitMutation();
            RefreshPlatformSelection(platform);
            return true;
        }

        public bool TryUpdateRope(int ropeIndex, float ropeX, float topY, float bottomY, out string? error)
        {
            error = null;
            if (_currentMapData.Ropes == null || ropeIndex < 0 || ropeIndex >= _currentMapData.Ropes.Count)
            {
                error = "繩索索引無效。";
                return false;
            }

            if (topY > bottomY)
                (topY, bottomY) = (bottomY, topY);

            _currentMapData.Ropes[ropeIndex] = new[]
            {
                RoundCoord(ropeX),
                RoundCoord(topY),
                RoundCoord(bottomY)
            };

            CommitMutation();
            SetSelection(MapEditorSelection.ForRope(ropeIndex));
            return true;
        }

        public bool TryUpdateManualEdge(
            ManualEdgeAnchor anchor,
            string fromPlatformId,
            string toPlatformId,
            float fromX,
            float fromY,
            float toX,
            float toY,
            NavigationActionType actionType,
            out string? error)
        {
            error = null;
            fromPlatformId = fromPlatformId.Trim();
            toPlatformId = toPlatformId.Trim();

            if (string.IsNullOrEmpty(fromPlatformId) || string.IsNullOrEmpty(toPlatformId))
            {
                error = "From / To 平台 Id 不可為空白。";
                return false;
            }

            if (actionType is NavigationActionType.Walk or NavigationActionType.ClimbUp or NavigationActionType.ClimbDown)
            {
                error = "ManualEdge 不可使用 Walk / Climb 類型。";
                return false;
            }

            anchor.FromPlatformId = fromPlatformId;
            anchor.ToPlatformId = toPlatformId;
            anchor.FromX = RoundCoord(fromX);
            anchor.FromY = RoundCoord(fromY);
            anchor.ToX = RoundCoord(toX);
            anchor.ToY = RoundCoord(toY);
            anchor.ActionType = actionType;

            CommitMutation();
            SetSelection(MapEditorSelection.ForManualEdge(anchor));
            return true;
        }

        public bool TryUpdateManualEdgeActionType(
            ManualEdgeAnchor anchor,
            NavigationActionType actionType,
            out string? error)
        {
            error = null;
            if (actionType is NavigationActionType.Walk or NavigationActionType.ClimbUp or NavigationActionType.ClimbDown)
            {
                error = "ManualEdge 不可使用 Walk / Climb 類型。";
                return false;
            }

            anchor.ActionType = actionType;
            CommitMutation();
            SetSelection(MapEditorSelection.ForManualEdge(anchor));
            return true;
        }

        private string DescribeDeleteTarget(DeleteTargetKind kind, PolylinePlatformData? platform, int lineIndex)
        {
            return kind switch
            {
                DeleteTargetKind.Platform when platform != null =>
                    $"確定刪除平台「{platform.Id}」？\n相依的手動邊錨點也會一併移除。",
                DeleteTargetKind.JumpLink when lineIndex >= 0 && _currentMapData.JumpLinks != null &&
                    lineIndex < _currentMapData.JumpLinks.Count =>
                    $"確定刪除跳點 #{lineIndex}（X={_currentMapData.JumpLinks[lineIndex][0]:F1}）？",
                DeleteTargetKind.Rope when lineIndex >= 0 && _currentMapData.Ropes != null &&
                    lineIndex < _currentMapData.Ropes.Count =>
                    $"確定刪除繩索 #{lineIndex}（X={_currentMapData.Ropes[lineIndex][0]:F1}）？",
                DeleteTargetKind.ManualEdge =>
                    "確定刪除此手動邊錨點？",
                _ => "確定刪除選取的物件？"
            };
        }

        private void CommitMutation() => CommitTopologyChange();

        private void RefreshPlatformSelection(PolylinePlatformData platform)
        {
            if (_selection.Kind == MapEditorSelectionKind.Platform &&
                ReferenceEquals(_selection.Platform, platform))
            {
                SetSelection(MapEditorSelection.ForPlatform(
                    platform,
                    _selection.SegmentIndex,
                    _selection.ProjectionPoint));
            }
        }

        private static float RoundCoord(float value) =>
            (float)Math.Round(value, 1, MidpointRounding.AwayFromZero);

        private static float ComputePolylineLength(PolylinePlatformData platform)
        {
            if (platform.Points == null || platform.Points.Count < 2) return 0f;

            float total = 0f;
            for (int i = 0; i < platform.Points.Count - 1; i++)
            {
                float dx = platform.Points[i + 1].X - platform.Points[i].X;
                float dy = platform.Points[i + 1].Y - platform.Points[i].Y;
                total += (float)Math.Sqrt(dx * dx + dy * dy);
            }

            return total;
        }
    }
}
