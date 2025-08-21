using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 負責管理所有地圖編輯的核心邏輯、數據狀態和繪製。
    /// </summary>
    public class MapEditor
    {
        private MapData _currentMapData = new MapData();
        private EditMode _currentEditMode = EditMode.None;

        // 狀態變數，用來記錄第一次點擊的位置
        private MapPath? _activePath = null;
        private PointF? _firstClickPoint = null;
        private PointF? _previewPoint = null;

        public void LoadMapData(MapData data)
        {
            _currentMapData = data ?? new MapData();
            _activePath = null;
        }

        public MapData GetCurrentMapData()
        {
            return _currentMapData;
        }

        public void SetEditMode(EditMode mode)
        {
            _currentEditMode = mode;
            ResetDrawingState();
        }

        /// <summary>
        /// 處理滑鼠移動事件，僅用於更新預覽點的位置。
        /// </summary>
        public void HandleMouseMove(PointF imagePoint)
        {
            _previewPoint = imagePoint;
        }

        /// <summary>
        /// [修改] 處理滑鼠單次點擊事件。所有標記的建立邏輯都集中於此。
        /// </summary>
        public void HandleMouseClick(PointF imagePoint)
        {
            switch (_currentEditMode)
            {
                case EditMode.Waypoint:
                    HandleWaypointClick(imagePoint);
                    break;
                case EditMode.SafeZone:
                case EditMode.Rope:
                    HandleTwoClickDrawing(imagePoint);
                    break;
                case EditMode.RestrictedZone:
                    var newRestrictedPoint = new Waypoint { Position = imagePoint };
                    _currentMapData.RestrictedPoints.Add(newRestrictedPoint);
                    break;
                case EditMode.Delete:
                    HandleDeleteAction(imagePoint);
                    break;
            }
        }
        public void HandleRightClick()
        {
            if (_currentEditMode == EditMode.Waypoint)
            {
                _activePath = null;
                ResetDrawingState();
            }
        }

        public void Render(Graphics g, Func<PointF, Point> convertToDisplay)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawCompletedShapes(g, convertToDisplay);
            DrawPreviewShapes(g, convertToDisplay);
        }

        // --- 私有輔助方法 ---

        /// <summary>
        /// 處理需要兩次點擊（起點和終點）的繪圖模式。
        /// </summary>
        private void HandleTwoClickDrawing(PointF clickPoint)
        {
            if (_firstClickPoint == null)
            {
                // 這是第一次點擊，記錄起點
                _firstClickPoint = clickPoint;
            }
            else
            {
                // 這是第二次點擊，建立物件
                if (_currentEditMode == EditMode.SafeZone)
                {
                    var newSafeZone = new MapArea();
                    newSafeZone.Points.Add(_firstClickPoint.Value);
                    newSafeZone.Points.Add(clickPoint);
                    _currentMapData.SafeZone.Add(newSafeZone);
                }
                else if (_currentEditMode == EditMode.Rope)
                {
                    var newRope = new Rope { Start = _firstClickPoint.Value, End = clickPoint };
                    _currentMapData.Ropes.Add(newRope);
                }

                // 完成繪製後，重置狀態以準備繪製下一條線
                ResetDrawingState();
            }
        }

        private void DrawCompletedShapes(Graphics g, Func<PointF, Point> convert)
        {
            // 繪製安全區域
            foreach (var area in _currentMapData.SafeZone)
            {
                DrawPolygon(g, area.Points, Color.Green, 2, convert);
            }
            // 繪製禁止點
            foreach (var point in _currentMapData.RestrictedPoints)
            {
                DrawWaypoint(g, point.Position, convert, Color.Red);
            }
            // 繪製繩索
            foreach (var rope in _currentMapData.Ropes)
            {
                DrawPolygon(g, new List<PointF> { rope.Start, rope.End }, Color.Yellow, 3, convert);
            }

            // 繪製連續的路徑標記
            foreach (var path in _currentMapData.WaypointPaths)
            {
                if (path.Points.Count >= 2)
                {
                    var pathPoints = path.Points.Select(wp => wp.Position).ToList();
                    DrawPolygon(g, pathPoints, Color.White, 2, convert);
                }
                foreach (var waypoint in path.Points)
                {
                    DrawWaypoint(g, waypoint.Position, convert, Color.Blue);
                }
            }
        }
        private void HandleWaypointClick(PointF clickPoint)
        {
            if (_activePath == null)
            {
                _activePath = new MapPath();
                _currentMapData.WaypointPaths.Add(_activePath);
            }
            var newWaypoint = new Waypoint { Position = clickPoint };
            _activePath.Points.Add(newWaypoint);
        }

        private void DrawPreviewShapes(Graphics g, Func<PointF, Point> convert)
        {
            if (!_previewPoint.HasValue) return;

            // 為 Waypoint 模式新增預覽線，從最後一個點連到滑鼠位置
            if (_currentEditMode == EditMode.Waypoint && _activePath != null && _activePath.Points.Any())
            {
                PointF lastPoint = _activePath.Points.Last().Position;
                using (var pen = new Pen(Color.Cyan, 2) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, convert(lastPoint), convert(_previewPoint.Value));
                }
            }
            else if ((_currentEditMode == EditMode.SafeZone || _currentEditMode == EditMode.Rope) && _firstClickPoint.HasValue)
            {
                using (var pen = new Pen(Color.Cyan, 2) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, convert(_firstClickPoint.Value), convert(_previewPoint.Value));
                }
            }
        }

        private void DrawPolygon(Graphics g, List<PointF> points, Color color, float penWidth, Func<PointF, Point> convert)
        {
            if (points.Count < 2) return;
            var displayPoints = points.Select(p => convert(p)).ToArray();
            using (var pen = new Pen(color, penWidth))
            {
                g.DrawLines(pen, displayPoints);
            }
        }

        private void DrawWaypoint(Graphics g, PointF position, Func<PointF, Point> convert, Color dotColor)
        {
            Point displayPoint = convert(position);
            using (var brush = new SolidBrush(Color.FromArgb(150, dotColor))) { g.FillEllipse(brush, displayPoint.X - 5, displayPoint.Y - 5, 10, 10); }
            using (var pen = new Pen(Color.White, 2)) { g.DrawEllipse(pen, displayPoint.X - 5, displayPoint.Y - 5, 10, 10); }
        }

        private void ResetDrawingState()
        {
            _firstClickPoint = null;
            _previewPoint = null;
        }

        private void HandleDeleteAction(PointF clickPosition)
        {
            const float deletionRadius = 10.0f;

            // 刪除單個的路徑點
            foreach (var path in _currentMapData.WaypointPaths.ToList())
            {
                var waypointToDelete = path.Points.FirstOrDefault(wp => CalculateDistance(wp.Position, clickPosition) <= deletionRadius);
                if (waypointToDelete != null)
                {
                    path.Points.Remove(waypointToDelete);
                    if (!path.Points.Any())
                    {
                        _currentMapData.WaypointPaths.Remove(path);
                    }
                    return;
                }
            }

            var safeZoneToDelete = _currentMapData.SafeZone.OrderBy(area => area.Points.Min(p => CalculateDistance(p, clickPosition))).FirstOrDefault();
            if (safeZoneToDelete != null && safeZoneToDelete.Points.Any(p => CalculateDistance(p, clickPosition) <= deletionRadius))
            {
                _currentMapData.SafeZone.Remove(safeZoneToDelete);
                return;
            }

            var ropeToDelete = _currentMapData.Ropes
                .OrderBy(rope => Math.Min(CalculateDistance(rope.Start, clickPosition), CalculateDistance(rope.End, clickPosition)))
                .FirstOrDefault();
            if (ropeToDelete != null && (CalculateDistance(ropeToDelete.Start, clickPosition) <= deletionRadius || CalculateDistance(ropeToDelete.End, clickPosition) <= deletionRadius))
            {
                _currentMapData.Ropes.Remove(ropeToDelete);
                return;
            }

            var restrictedPointToDelete = _currentMapData.RestrictedPoints
                .FirstOrDefault(rp => CalculateDistance(rp.Position, clickPosition) <= deletionRadius);
            if (restrictedPointToDelete != null)
            {
                _currentMapData.RestrictedPoints.Remove(restrictedPointToDelete);
                return;
            }
        }

        private float CalculateDistance(PointF p1, PointF p2)
        {
            return (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }
    }
}
