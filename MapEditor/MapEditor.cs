using ArtaleAI.Config;
using ArtaleAI.Models;
using System.Drawing.Drawing2D;
using PathData = ArtaleAI.Models.PathData;


namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 負責管理所有地圖編輯的核心邏輯、數據狀態和繪製。
    /// </summary>
    public class MapEditor
    {
        private MapData _currentMapData = new MapData();
        private EditMode _currentEditMode = EditMode.None;
        private readonly MapEditorSettings? _settings;

        private bool _isDrawing = false;
        private PointF? _startPoint = null;
        private PointF? _previewPoint = null;

        public MapEditor(MapEditorSettings? settings = null, TrajectorySettings? trajectorySettings = null)
        {
            _settings = settings;
        }

        public void LoadMapData(MapData data)
        {
            _currentMapData = data ?? new MapData();
        }

        public MapData GetCurrentMapData()
        {
            return _currentMapData;
        }

        public void SetEditMode(EditMode mode)
        {
            _currentEditMode = mode;
            ResetDrawing();
        }

        /// <summary>
        /// 開始繪製 (MouseDown)
        /// </summary>
        public void StartDrawing(PointF point)
        {
            if (IsLineMode())
            {
                _isDrawing = true;
                _startPoint = point;
            }
            else
            {
                // 單擊模式直接處理
                HandleSingleClick(point);
            }
        }

        /// <summary>
        /// 更新預覽 (MouseMove)
        /// </summary>
        public void UpdatePreview(PointF point)
        {
            _previewPoint = point;
        }

        /// <summary>
        /// 完成繪製 (MouseUp)
        /// </summary>
        public void FinishDrawing(PointF point)
        {
            if (_isDrawing && _startPoint.HasValue)
            {
                CreateLine(_startPoint.Value, point);
                ResetDrawing();
            }
        }

        /// <summary>
        /// 檢查是否為線段繪製模式
        /// </summary>
        private bool IsLineMode()
        {
            return _currentEditMode == EditMode.Waypoint ||
                   _currentEditMode == EditMode.SafeZone ||
                   _currentEditMode == EditMode.Rope;
        }

        /// <summary>
        /// 創建線段
        /// </summary>
        private void CreateLine(PointF start, PointF end)
        {
            Console.WriteLine($"🎯 CreateLine: {_currentEditMode} 模式，從 ({start.X:F1}, {start.Y:F1}) 到 ({end.X:F1}, {end.Y:F1})");

            var points = GenerateLinearPath(start, end);

            switch (_currentEditMode)
            {
                case EditMode.Waypoint:
                    _currentMapData.WaypointPaths ??= new List<PathData>();
                    _currentMapData.WaypointPaths.Add(new PathData { Points = points });
                    Console.WriteLine($"✅ 新增路徑，共 {points.Count} 個點");
                    break;

                case EditMode.SafeZone:
                    _currentMapData.SafeZones ??= new List<PathData>();
                    _currentMapData.SafeZones.Add(new PathData { Points = points });
                    Console.WriteLine($"🛡️ 新增安全區域，共 {points.Count} 個點");
                    break;

                case EditMode.Rope:
                    // 繩索現在也使用 points 格式（只有兩個點：起點和終點）
                    _currentMapData.Ropes ??= new List<PathData>();
                    _currentMapData.Ropes.Add(new PathData { Points = new List<PointF> { start, end } });
                    Console.WriteLine($"🪢 新增繩索路徑（起點到終點）");
                    break;
            }
        }

        /// <summary>
        /// 線性插值生成路徑點 - 簡化版
        /// </summary>
        private List<PointF> GenerateLinearPath(PointF start, PointF end)
        {
            var points = new List<PointF> { start };

            // 計算距離
            float distance = CalculateDistance(start, end);
            Console.WriteLine($"🛠️ 起終點距離: {distance:F1} 像素");

            // 根據距離決定中間點數量
            int pointCount = GetOptimalPointCount(distance);
            Console.WriteLine($"📊 預計生成 {pointCount} 個中間點");

            // 線性插值生成中間點
            for (int i = 1; i < pointCount - 1; i++)
            {
                float ratio = (float)i / (pointCount - 1);
                var interpolatedPoint = new PointF(
                    start.X + (end.X - start.X) * ratio,
                    start.Y + (end.Y - start.Y) * ratio
                );
                points.Add(interpolatedPoint);
            }

            points.Add(end);
            Console.WriteLine($"✅ 線性插值完成，總計: {points.Count} 個點");

            return points;
        }

        /// <summary>
        /// 根據距離計算最佳點數
        /// </summary>
        private int GetOptimalPointCount(float distance)
        {
            // 每隔一定像素插入一個點
            const float pixelsPerPoint = 10.0f; // 每10像素一個點
            int pointCount = Math.Max(2, (int)(distance / pixelsPerPoint) + 2);

            // 限制最大點數，避免太密集
            return Math.Min(pointCount, 20);
        }

        /// <summary>
        /// 處理單擊
        /// </summary>
        private void HandleSingleClick(PointF point)
        {
            switch (_currentEditMode)
            {
                case EditMode.RestrictedZone:
                    if (_currentMapData.RestrictedPoints == null)
                        _currentMapData.RestrictedPoints = new List<PointF>();
                    _currentMapData.RestrictedPoints.Add(point);
                    Console.WriteLine($"🚫 新增限制點: ({point.X:F1}, {point.Y:F1})");
                    break;

                case EditMode.Delete:
                    HandleDeleteAction(point);
                    break;
            }
        }

        public void Render(Graphics g, Func<PointF, Point> convertToDisplay)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawCompletedShapes(g, convertToDisplay);
            DrawPreviewShapes(g, convertToDisplay);
        }

        private void DrawCompletedShapes(Graphics g, Func<PointF, Point> convert)
        {
            // 繪製安全區域
            if (_currentMapData.SafeZones?.Any() == true)
            {
                foreach (var area in _currentMapData.SafeZones)
                {
                    DrawPolygon(g, area.Points, Color.Green, 2, convert);
                }
            }

            // 繪製限制點
            if (_currentMapData.RestrictedPoints?.Any() == true)
            {
                foreach (var point in _currentMapData.RestrictedPoints)
                {
                    DrawWaypoint(g, point, convert, Color.Red);
                }
            }

            // 繪製繩索（現在也是 points 陣列）
            if (_currentMapData.Ropes?.Any() == true)
            {
                foreach (var rope in _currentMapData.Ropes)
                {
                    DrawPolygon(g, rope.Points, Color.Yellow, 3, convert);
                }
            }

            // 繪製路徑
            if (_currentMapData.WaypointPaths?.Any() == true)
            {
                foreach (var path in _currentMapData.WaypointPaths)
                {
                    if (path.Points.Count >= 2)
                    {
                        DrawPolygon(g, path.Points, Color.Blue, 2, convert);
                    }
                }
            }
        }

        private void DrawPreviewShapes(Graphics g, Func<PointF, Point> convert)
        {
            if (_isDrawing && _startPoint.HasValue && _previewPoint.HasValue)
            {
                using (var pen = new Pen(Color.Cyan, 2) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, convert(_startPoint.Value), convert(_previewPoint.Value));
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

        private void ResetDrawing()
        {
            _isDrawing = false;
            _startPoint = null;
            _previewPoint = null;
        }

        private void HandleDeleteAction(PointF clickPosition)
        {
            float deletionRadius = _settings.DeletionRadius;

            // 刪除路徑點
            if (_currentMapData.WaypointPaths?.Any() == true)
            {
                foreach (var path in _currentMapData.WaypointPaths.ToList())
                {
                    var pointToDelete = path.Points.FirstOrDefault(pt => CalculateDistance(pt, clickPosition) <= deletionRadius);
                    if (pointToDelete != default(PointF))
                    {
                        path.Points.Remove(pointToDelete);
                        if (!path.Points.Any())
                        {
                            _currentMapData.WaypointPaths.Remove(path);
                        }
                        return;
                    }
                }
            }

            // 刪除安全區域
            if (_currentMapData.SafeZones?.Any() == true)
            {
                var safeZoneToDelete = _currentMapData.SafeZones.FirstOrDefault(area =>
                    area.Points.Any(p => CalculateDistance(p, clickPosition) <= deletionRadius));
                if (safeZoneToDelete != null)
                {
                    _currentMapData.SafeZones.Remove(safeZoneToDelete);
                    return;
                }
            }

            // 刪除繩索
            if (_currentMapData.Ropes?.Any() == true)
            {
                var ropeToDelete = _currentMapData.Ropes.FirstOrDefault(rope =>
                    rope.Points.Any(p => CalculateDistance(p, clickPosition) <= deletionRadius));
                if (ropeToDelete != null)
                {
                    _currentMapData.Ropes.Remove(ropeToDelete);
                    return;
                }
            }

            // 刪除限制點
            if (_currentMapData.RestrictedPoints?.Any() == true)
            {
                var restrictedPointToDelete = _currentMapData.RestrictedPoints
                    .FirstOrDefault(pt => CalculateDistance(pt, clickPosition) <= deletionRadius);
                if (restrictedPointToDelete != default(PointF))
                {
                    _currentMapData.RestrictedPoints.Remove(restrictedPointToDelete);
                    return;
                }
            }
        }

        public static float CalculateDistance(PointF p1, PointF p2)
        {
            return (float)Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }
    }
}
