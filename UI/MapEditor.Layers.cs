using ArtaleAI.Core.Domain.Navigation;
using ArtaleAI.Models.Map;
using ArtaleAI.UI.MapEditing;
using System.Drawing.Drawing2D;

namespace ArtaleAI.UI
{
    public partial class MapEditor
    {
        public MapEditorLayerVisibility Layers { get; } = new();

        public string FormatStatusSummary()
        {
            var validation = _lastValidation;
            string dirty = _isDirty ? "未儲存" : "已儲存";
            string warnings = validation.HasIssues
                ? $"警告 {validation.ErrorCount + validation.WarningCount}"
                : "無警告";
            return $"子圖 {validation.ConnectedComponentCount} | {warnings} | {dirty}";
        }

        private void DrawValidationOverlays(Graphics g, Func<PointF, PointF> convert)
        {
            if (!Layers.ShowValidationOverlays || !_lastValidation.HasIssues)
                return;

            var orphanPlatforms = _lastValidation.Issues
                .Where(i => i.Code == "V-Orphan" && i.TargetPlatform != null)
                .Select(i => i.TargetPlatform!)
                .Distinct()
                .ToList();

            foreach (var plat in orphanPlatforms)
                DrawPlatformHighlight(g, convert, plat, Color.FromArgb(90, Color.OrangeRed), DashStyle.Dash);

            var unresolvedAnchors = _lastValidation.Issues
                .Where(i => i.Code == "V-Anchor-Resolve" && i.TargetManualEdge != null)
                .Select(i => i.TargetManualEdge!)
                .Distinct()
                .ToList();

            foreach (var anchor in unresolvedAnchors)
            {
                if (!TryGetManualEdgeDrawSegment(anchor, out PointF from, out PointF to))
                    continue;

                var p1 = convert(new PointF(minimapBounds.X + from.X, minimapBounds.Y + from.Y));
                var p2 = convert(new PointF(minimapBounds.X + to.X, minimapBounds.Y + to.Y));
                using var pen = new Pen(Color.FromArgb(200, Color.Red), 3f) { DashStyle = DashStyle.DashDot };
                g.DrawLine(pen, p1, p2);
            }
        }

        private void DrawPlatformHighlight(
            Graphics g,
            Func<PointF, PointF> convert,
            PolylinePlatformData plat,
            Color color,
            DashStyle dashStyle)
        {
            if (plat.Points == null || plat.Points.Count < 2)
                return;

            using var pen = new Pen(color, 5f) { DashStyle = dashStyle };
            for (int i = 0; i < plat.Points.Count - 1; i++)
            {
                var p1 = convert(new PointF(minimapBounds.X + plat.Points[i].X, minimapBounds.Y + plat.Points[i].Y));
                var p2 = convert(new PointF(minimapBounds.X + plat.Points[i + 1].X, minimapBounds.Y + plat.Points[i + 1].Y));
                g.DrawLine(pen, p1, p2);
            }
        }

        private void DrawManualEdgeAnchors(Graphics g, Func<PointF, PointF> convert)
        {
            if (_currentMapData.ManualEdgeAnchors == null)
                return;

            foreach (var anchor in _currentMapData.ManualEdgeAnchors)
            {
                if (!TryGetManualEdgeDrawSegment(anchor, out PointF from, out PointF to))
                    continue;

                bool isSelected = _selection.Kind == MapEditorSelectionKind.ManualEdge &&
                    ReferenceEquals(anchor, _selection.ManualEdge);
                bool isHovered = ReferenceEquals(anchor, _hoveredManualEdgeAnchor);

                var p1 = convert(new PointF(minimapBounds.X + from.X, minimapBounds.Y + from.Y));
                var p2 = convert(new PointF(minimapBounds.X + to.X, minimapBounds.Y + to.Y));

                Color lineColor = isSelected ? Color.DeepSkyBlue :
                    isHovered ? Color.Gold : Color.FromArgb(220, Color.OrangeRed);
                float width = isSelected ? 3f : isHovered ? 2.5f : 2f;

                using (var pen = new Pen(lineColor, width) { DashStyle = DashStyle.Dot })
                {
                    g.DrawLine(pen, p1, p2);
                }

                using var brush = new SolidBrush(lineColor);
                g.FillEllipse(brush, p1.X - 4f, p1.Y - 4f, 8f, 8f);
                g.FillEllipse(brush, p2.X - 4f, p2.Y - 4f, 8f, 8f);
                DrawArrow(g, p1, p2, new PointF((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f), brush);
            }
        }

        private static DashStyle GetEdgeDashStyle(NavigationActionType actionType) =>
            actionType switch
            {
                NavigationActionType.Walk => DashStyle.Solid,
                NavigationActionType.ClimbUp or NavigationActionType.ClimbDown => DashStyle.Solid,
                NavigationActionType.Jump => DashStyle.Dash,
                NavigationActionType.SideJump => DashStyle.Dot,
                NavigationActionType.JumpDown => DashStyle.DashDot,
                NavigationActionType.Teleport => DashStyle.DashDotDot,
                _ => DashStyle.Solid
            };

        private static Color GetEdgePreviewColor(NavEdgeData edge, bool isManual)
        {
            if (isManual)
                return Color.OrangeRed;

            return edge.ActionType switch
            {
                NavigationActionType.Walk => Color.FromArgb(140, 160, 160, 160),
                NavigationActionType.ClimbUp or NavigationActionType.ClimbDown => Color.Cyan,
                NavigationActionType.Jump => Color.DeepSkyBlue,
                NavigationActionType.SideJump => Color.MediumPurple,
                NavigationActionType.JumpDown => Color.Gold,
                NavigationActionType.Teleport => Color.Magenta,
                _ => Color.White
            };
        }
    }
}
