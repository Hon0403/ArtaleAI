using ArtaleAI.Config;
using ArtaleAI.Engine;
using ArtaleAI.Utils;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using YamlDotNet.Serialization;


namespace ArtaleAI.UI
{
    /// <summary>
    /// 負責管理所有地圖編輯的核心邏輯、數據狀態和繪製。
    /// </summary>
    public class MapEditor
    {
        private MapData _currentMapData = new MapData();
        private EditMode _currentEditMode = EditMode.None;
        private readonly AppConfig _settings;

        private bool _isDrawing = false;
        private PointF? _startPoint = null;
        private PointF? _previewPoint = null;
        private Rectangle minimapBounds = Rectangle.Empty;

        public MapEditor(AppConfig settings)
        {
            _settings = settings;
        }

        public void SetMinimapBounds(Rectangle bounds)
        {
            minimapBounds = bounds;
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
            if ((_currentEditMode == EditMode.Waypoint ||
                 _currentEditMode == EditMode.SafeZone ||
                 _currentEditMode == EditMode.Rope) &&
                _startPoint.HasValue)
            {
                Console.WriteLine($"放棄未完成的繪製: {_currentEditMode}");

                _isDrawing = false;
                _startPoint = null;
                _previewPoint = null;
            }
            _currentEditMode = mode;
        }


        public void HandleClick(PointF screenPoint)
        {
            if (minimapBounds.IsEmpty) return;

            if (_currentEditMode == EditMode.Waypoint ||
                _currentEditMode == EditMode.SafeZone ||
                _currentEditMode == EditMode.Rope)
            {
                if (!_startPoint.HasValue)
                {
                    _startPoint = screenPoint;
                }
                else
                {
                    var points = new List<PointF>();
                    var distance = Math.Sqrt(
                        Math.Pow(screenPoint.X - _startPoint.Value.X, 2) +
                        Math.Pow(screenPoint.Y - _startPoint.Value.Y, 2));
                    var steps = Math.Max(1, (int)(distance / 5));

                    for (int i = 0; i <= steps; i++)
                    {
                        var t = steps == 0 ? 0 : (double)i / steps;
                        var x = _startPoint.Value.X + (screenPoint.X - _startPoint.Value.X) * t;
                        var y = _startPoint.Value.Y + (screenPoint.Y - _startPoint.Value.Y) * t;
                        points.Add(new PointF((float)x, (float)y));
                    }

                    var coordinates = points
                        .Select(p => new int[] { (int)Math.Round(p.X), (int)Math.Round(p.Y) })
                        .ToList();

                    switch (_currentEditMode)
                    {
                        case EditMode.Waypoint:
                            _currentMapData.WaypointPaths ??= new List<int[]>();
                            _currentMapData.WaypointPaths.AddRange(coordinates);
                            break;
                        case EditMode.SafeZone:
                            _currentMapData.SafeZones ??= new List<int[]>();
                            _currentMapData.SafeZones.AddRange(coordinates);
                            break;
                        case EditMode.Rope:
                            _currentMapData.Ropes ??= new List<int[]>();
                            _currentMapData.Ropes.AddRange(coordinates);
                            break;
                    }

                    _isDrawing = false;
                    _startPoint = null;
                    _previewPoint = null;
                }
            }
            else if (_currentEditMode == EditMode.RestrictedZone)
            {
                var coord = new int[] { (int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y) };
                _currentMapData.RestrictedZones ??= new List<int[]>();
                _currentMapData.RestrictedZones.Add(coord);
            }
            else if (_currentEditMode == EditMode.Delete)
            {
                HandleDeleteAction(screenPoint);
            }
        }


        public void Render(Graphics g, Func<PointF, PointF> convertToDisplay)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawCompletedShapes(g, convertToDisplay);
            DrawPreviewShapes(g, convertToDisplay);
        }

        /// <summary>
        /// 繪製完成的路徑形狀 (路徑點/安全區/繩索/禁區)
        /// </summary>
        private void DrawCompletedShapes(Graphics g, Func<PointF, PointF> convert)
        {
            // 繪製路點路徑 
            if (_currentMapData.WaypointPaths?.Any() == true)
            {
                var points = _currentMapData.WaypointPaths
                    .Where(coord => coord.Length == 2)
                    .Select(coord => convert(new PointF(coord[0], coord[1])));

                DrawingHelper.DrawPath(g, points, Color.Blue, 2f, 3f);
            }

            // 繪製安全區 
            if (_currentMapData.SafeZones?.Any() == true)
            {
                var points = _currentMapData.SafeZones
                    .Where(coord => coord.Length == 2)
                    .Select(coord => convert(new PointF(coord[0], coord[1])));

                DrawingHelper.DrawPath(g, points, Color.Green, 2f, 3f);
            }

            // 繪製繩索 
            if (_currentMapData.Ropes?.Any() == true)
            {
                var points = _currentMapData.Ropes
                    .Where(coord => coord.Length == 2)
                    .Select(coord => convert(new PointF(coord[0], coord[1])));

                DrawingHelper.DrawPath(g, points, Color.Yellow, 3f, 3f);
            }

            if (_currentMapData.RestrictedZones?.Any() == true)
            {
                var points = _currentMapData.RestrictedZones
                    .Where(coord => coord.Length == 2)
                    .Select(coord => convert(new PointF(coord[0], coord[1])))
                    .ToList();

                using var fillBrush = new SolidBrush(Color.Red);
                using var pen = new Pen(Color.Black, 1.5f);

                foreach (var pt in points)
                {
                    float size = 10;
                    var rect = new RectangleF(pt.X - size / 2, pt.Y - size / 2, size, size);
                    g.FillRectangle(fillBrush, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }

        private void DrawPreviewShapes(Graphics g, Func<PointF, PointF> convert)
        {
            bool isLineMode = _currentEditMode == EditMode.Waypoint ||
                              _currentEditMode == EditMode.SafeZone ||
                              _currentEditMode == EditMode.Rope;

            // 顯示預覽線
            if (_startPoint.HasValue && _previewPoint.HasValue && isLineMode)
            {
                using (var pen = new Pen(Color.Cyan, 2) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(pen, convert(_startPoint.Value), convert(_previewPoint.Value));
                }
            }

            // 顯示起點
            if (_startPoint.HasValue && isLineMode)
            {
                var converted = convert(_startPoint.Value);
                using (var brush = new SolidBrush(Color.Red))
                {
                    g.FillEllipse(brush, converted.X - 4, converted.Y - 4, 8, 8);
                }
            }
        }

        private void HandleDeleteAction(PointF clickPosition)
        {
            float deletionRadius = (float)_settings.DeletionRadius;

            var pathGroups = new[]
            {
                ("Waypoint", _currentMapData.WaypointPaths),
                ("SafeZone", _currentMapData.SafeZones),
                ("Rope", _currentMapData.Ropes),
                ("RestrictedZone", _currentMapData.RestrictedZones)
            };

            foreach (var (name, pathList) in pathGroups)
            {
                if (pathList?.Any() != true) continue;

                for (int i = pathList.Count - 1; i >= 0; i--)
                {
                    var coord = pathList[i];
                    if (coord.Length != 2) continue;

                    var point = new PointF(coord[0], coord[1]); 
                    var distance = (float)Math.Sqrt(
                        Math.Pow(point.X - clickPosition.X, 2) +
                        Math.Pow(point.Y - clickPosition.Y, 2));

                    if (distance <= deletionRadius)
                    {
                        pathList.RemoveAt(i);
                        Console.WriteLine($"刪除 {name}: ({coord[0]}, {coord[1]})");
                        return;
                    }
                }
            }
        }

    }
}
