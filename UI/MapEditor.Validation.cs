using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.Services;
using ArtaleAI.UI.MapEditing;

namespace ArtaleAI.UI
{
    public partial class MapEditor
    {
        private MapEditorValidationResult _lastValidation = MapEditorValidationResult.Empty;
        private (PolylinePlatformData Platform, int PointIndex)? _vertexDrag;

        public event Action? ValidationChanged;

        public MapEditorValidationResult LastValidation => _lastValidation;
        public bool IsVertexDragging => _vertexDrag != null;

        public void RunValidation()
        {
            _lastValidation = MapEditorValidationService.Validate(_currentMapData);
            ValidationChanged?.Invoke();
        }

        private void RebuildTopology()
        {
            MapGenerationService.BuildHTopology(_currentMapData);
            RunValidation();
        }

        private void CommitTopologyChange(bool raiseMutated = true, bool recordUndo = true)
        {
            if (recordUndo)
                RecordUndoSnapshot();

            RebuildTopology();
            MarkDirty();
            if (raiseMutated)
                MapMutated?.Invoke();
        }

        public void FocusValidationIssue(MapEditorValidationIssue issue)
        {
            switch (issue.TargetKind)
            {
                case MapEditorValidationTargetKind.Platform when issue.TargetPlatform != null:
                    SetSelection(MapEditorSelection.ForPlatform(issue.TargetPlatform, -1, PointF.Empty));
                    break;
                case MapEditorValidationTargetKind.JumpLink when issue.TargetJumpLinkIndex >= 0:
                    SetSelection(MapEditorSelection.ForJumpLink(issue.TargetJumpLinkIndex));
                    break;
                case MapEditorValidationTargetKind.Rope when issue.TargetRopeIndex >= 0:
                    SetSelection(MapEditorSelection.ForRope(issue.TargetRopeIndex));
                    break;
                case MapEditorValidationTargetKind.ManualEdge when issue.TargetManualEdge != null:
                    SetSelection(MapEditorSelection.ForManualEdge(issue.TargetManualEdge));
                    break;
            }
        }

        public bool TryBeginVertexDrag(PointF screenPoint)
        {
            if (_currentEditMode != EditMode.Select || minimapBounds.IsEmpty)
                return false;

            var relativePoint = ToRelativePoint(screenPoint);
            if (!TryFindNearestVertex(relativePoint, out var platform, out int pointIndex))
                return false;

            _vertexDrag = (platform, pointIndex);
            RecordUndoSnapshot();
            return true;
        }

        public void UpdateVertexDrag(PointF screenPoint)
        {
            if (_vertexDrag == null || minimapBounds.IsEmpty)
                return;

            var relativePoint = ToRelativePoint(screenPoint);
            var (platform, pointIndex) = _vertexDrag.Value;
            if (platform.Points == null || pointIndex < 0 || pointIndex >= platform.Points.Count)
                return;

            platform.Points[pointIndex].X = RoundCoord(relativePoint.X);
            platform.Points[pointIndex].Y = RoundCoord(relativePoint.Y);
            MarkDirty();
        }

        public void EndVertexDrag()
        {
            if (_vertexDrag == null)
                return;

            _vertexDrag = null;
            CommitTopologyChange(recordUndo: false);
            if (_selection.Kind == MapEditorSelectionKind.Platform && _selection.Platform != null)
                RefreshPlatformSelection(_selection.Platform);
        }

        private PointF ToRelativePoint(PointF screenPoint) =>
            new(screenPoint.X - minimapBounds.X, screenPoint.Y - minimapBounds.Y);

        private bool TryFindNearestVertex(
            PointF relativePoint,
            out PolylinePlatformData platform,
            out int pointIndex)
        {
            platform = null!;
            pointIndex = -1;
            float bestDist = SelectionRadius * 2.0f;
            PolylinePlatformData? bestPlatform = null;
            int bestIndex = -1;

            IEnumerable<PolylinePlatformData> candidates =
                _selection.Kind == MapEditorSelectionKind.Platform && _selection.Platform != null
                    ? new[] { _selection.Platform }
                    : _currentMapData.PolylinePlatforms ?? Enumerable.Empty<PolylinePlatformData>();

            foreach (var plat in candidates)
            {
                if (plat.Points == null)
                    continue;

                for (int i = 0; i < plat.Points.Count; i++)
                {
                    var p = plat.Points[i];
                    float dx = p.X - relativePoint.X;
                    float dy = p.Y - relativePoint.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist <= bestDist)
                    {
                        bestDist = dist;
                        bestPlatform = plat;
                        bestIndex = i;
                    }
                }
            }

            if (bestPlatform == null)
                return false;

            platform = bestPlatform;
            pointIndex = bestIndex;
            return true;
        }
    }
}
