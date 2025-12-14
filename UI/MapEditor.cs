using ArtaleAI.Config;
using ArtaleAI.Core;
using ArtaleAI.Models.Map;
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

        /// <summary>
        /// 初始化地圖編輯器
        /// </summary>
        /// <param name="settings">應用程式設定</param>
        public MapEditor(AppConfig settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// 設定小地圖的邊界範圍（螢幕座標）
        /// </summary>
        /// <param name="bounds">小地圖在螢幕上的矩形區域</param>
        public void SetMinimapBounds(Rectangle bounds)
        {
            minimapBounds = bounds;
        }

        /// <summary>
        /// 載入地圖資料到編輯器
        /// </summary>
        /// <param name="data">要載入的地圖資料（null 時會建立空地圖）</param>
        public void LoadMapData(MapData data)
        {
            _currentMapData = data ?? new MapData();
        }

        /// <summary>
        /// 取得當前正在編輯的地圖資料
        /// </summary>
        /// <returns>目前的地圖資料物件</returns>
        public MapData GetCurrentMapData()
        {
            return _currentMapData;
        }

        /// <summary>
        /// 設定當前的編輯模式（路徑點、安全區、限制區、繩索、刪除）
        /// </summary>
        /// <param name="mode">要切換的編輯模式</param>
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

        /// <summary>
        /// 更新滑鼠懸停位置（用於預覽線條）
        /// </summary>
        /// <param name="screenPoint">滑鼠位置的螢幕座標</param>
        public void UpdateMousePosition(PointF screenPoint)
        {
            if (!_startPoint.HasValue || minimapBounds.IsEmpty)
            {
                _previewPoint = null;
                return;
            }
            
            // ✅ 修正：轉換為相對座標（與 _startPoint 一致）
            _previewPoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);
        }

        /// <summary>
        /// 處理使用者在小地圖上的點擊事件
        /// 根據當前編輯模式執行對應操作：設定路徑點、安全區、限制區、繩索或刪除標記
        /// </summary>
        /// <param name="screenPoint">點擊位置的螢幕座標</param>
        public void HandleClick(PointF screenPoint)
        {
            if (minimapBounds.IsEmpty) return;

            // ✅ 修正：將螢幕座標轉換為小地圖相對座標（與錄製路徑格式統一）
            var relativePoint = new PointF(
                screenPoint.X - minimapBounds.X,
                screenPoint.Y - minimapBounds.Y);

            if (_currentEditMode == EditMode.Waypoint ||
                _currentEditMode == EditMode.SafeZone ||
                _currentEditMode == EditMode.Rope)
            {
                if (!_startPoint.HasValue)
                {
                    _startPoint = relativePoint;  // 存儲相對座標
                }
                else
                {
                    var points = new List<PointF>();
                    // 簡化：計算兩點間的距離（避免 Math.Pow）
                    var dx = relativePoint.X - _startPoint.Value.X;
                    var dy = relativePoint.Y - _startPoint.Value.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    // 根據距離決定插值點數量 (每5個像素一個點)
                    var steps = Math.Max(1, (int)(distance / 5));

                    for (int i = 0; i <= steps; i++)
                    {
                        var t = steps == 0 ? 0 : (double)i / steps;
                        var x = _startPoint.Value.X + (relativePoint.X - _startPoint.Value.X) * t;
                        var y = _startPoint.Value.Y + (relativePoint.Y - _startPoint.Value.Y) * t;
                        points.Add(new PointF((float)x, (float)y));
                    }

                    // 座標已經是相對座標，直接保留一位小數
                    var coordinates = points
                        .Select(p => new float[] {
                            (float)Math.Round(p.X, 1),
                            (float)Math.Round(p.Y, 1)
                        })
                        .ToList();

                    switch (_currentEditMode)
                    {
                        case EditMode.Waypoint:
                            _currentMapData.WaypointPaths ??= new List<float[]>();
                            _currentMapData.WaypointPaths.AddRange(coordinates);
                            break;
                        case EditMode.SafeZone:
                            _currentMapData.SafeZones ??= new List<float[]>();
                            _currentMapData.SafeZones.AddRange(coordinates);
                            break;
                        case EditMode.Rope:
                            _currentMapData.Ropes ??= new List<float[]>();
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
                // ✅ 修正：使用相對座標
                var coord = new float[] {
                    (float)Math.Round(relativePoint.X, 1),
                    (float)Math.Round(relativePoint.Y, 1)
                };
                _currentMapData.RestrictedZones ??= new List<float[]>();
                _currentMapData.RestrictedZones.Add(coord);
            }
            else if (_currentEditMode == EditMode.Delete)
            {
                // ✅ 修正：刪除時也使用相對座標
                HandleDeleteAction(relativePoint);
            }
        }

        /// <summary>
        /// 渲染地圖編輯器的所有視覺元素
        /// 包括完成的路徑、預覽線條、起點標記等
        /// </summary>
        /// <param name="g">GDI+ 繪圖物件</param>
        /// <param name="convertToDisplay">座標轉換函式（將螢幕座標轉換為顯示座標）</param>
        public void Render(Graphics g, Func<PointF, PointF> convertToDisplay)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawCompletedShapes(g, convertToDisplay);
            DrawPreviewShapes(g, convertToDisplay);
        }

        /// <summary>
        /// 繪製完成的路徑形狀（路徑點、安全區、繩索、限制區）
        /// 不同類型的路徑使用不同顏色繪製：藍色=路徑點、綠色=安全區、黃色=繩索、紅色=限制區
        /// </summary>
        /// <param name="g">GDI+ 繪圖物件</param>
        /// <param name="convert">座標轉換函式（將螢幕座標轉換為顯示座標）</param>
        private void DrawCompletedShapes(Graphics g, Func<PointF, PointF> convert)
        {
            // 檢查是否有有效的小地圖邊界
            if (minimapBounds.IsEmpty)
            {
                Logger.Warning("[MapEditor] minimapBounds \u70ba\u7a7a\uff0c\u7121\u6cd5\u7e6a\u88fd\u8def\u5f91");
                return;
            }

            // 繪製路點路徑 
            if (_currentMapData.WaypointPaths?.Any() == true)
            {
                // 支援新舊格式：[x, y] 或 [x, y, actionCode]
                // 重要：路徑檔案中的座標是小地圖相對座標，需要轉換為螢幕絕對座標
                var points = _currentMapData.WaypointPaths
                    .Where(coord => coord.Length >= 2) // 至少要有 x, y
                    .Select(coord => 
                    {
                        // 將小地圖相對座標轉換為螢幕絕對座標
                        var screenPoint = new PointF(
                            minimapBounds.X + coord[0],
                            minimapBounds.Y + coord[1]
                        );
                        // 再轉換為 PictureBox 顯示座標
                        return convert(screenPoint);
                    });

                DrawingHelper.DrawPath(g, points, Color.Blue, 2f, 3f);
            }

            // 繪製安全區 
            if (_currentMapData.SafeZones?.Any() == true)
            {
                var points = _currentMapData.SafeZones
                    .Where(coord => coord.Length >= 2)
                    .Select(coord => 
                    {
                        var screenPoint = new PointF(
                            minimapBounds.X + coord[0],
                            minimapBounds.Y + coord[1]
                        );
                        return convert(screenPoint);
                    });

                DrawingHelper.DrawPath(g, points, Color.Green, 2f, 3f);
            }

            // 繪製繩索 
            if (_currentMapData.Ropes?.Any() == true)
            {
                var points = _currentMapData.Ropes
                    .Where(coord => coord.Length >= 2)
                    .Select(coord => 
                    {
                        var screenPoint = new PointF(
                            minimapBounds.X + coord[0],
                            minimapBounds.Y + coord[1]
                        );
                        return convert(screenPoint);
                    });

                DrawingHelper.DrawPath(g, points, Color.Yellow, 3f, 3f);
            }

            if (_currentMapData.RestrictedZones?.Any() == true)
            {
                var points = _currentMapData.RestrictedZones
                    .Where(coord => coord.Length >= 2)
                    .Select(coord => 
                    {
                        var screenPoint = new PointF(
                            minimapBounds.X + coord[0],
                            minimapBounds.Y + coord[1]
                        );
                        return convert(screenPoint);
                    })
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

        /// <summary>
        /// 繪製預覽形狀（編輯過程中的即時視覺回饋）
        /// 包括預覽線條（虛線）和起點標記（紅色圓點）
        /// </summary>
        /// <param name="g">GDI+ 繪圖物件</param>
        /// <param name="convert">座標轉換函式</param>
        private void DrawPreviewShapes(Graphics g, Func<PointF, PointF> convert)
        {
            bool isLineMode = _currentEditMode == EditMode.Waypoint ||
                              _currentEditMode == EditMode.SafeZone ||
                              _currentEditMode == EditMode.Rope;

            // 顯示預覽線
            if (_startPoint.HasValue && _previewPoint.HasValue && isLineMode)
            {
                // ✅ 修正：_startPoint 和 _previewPoint 現在都是相對座標，需要加回小地圖偏移
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

            // 顯示起點
            if (_startPoint.HasValue && isLineMode)
            {
                var startScreen = new PointF(
                    minimapBounds.X + _startPoint.Value.X,
                    minimapBounds.Y + _startPoint.Value.Y);
                var converted = convert(startScreen);
                using (var brush = new SolidBrush(Color.Red))
                {
                    g.FillEllipse(brush, converted.X - 4, converted.Y - 4, 8, 8);
                }
            }
        }

        /// <summary>
        /// 處理刪除操作
        /// 在點擊位置的指定半徑範圍內搜尋並刪除最近的路徑點
        /// </summary>
        /// <param name="clickPosition">點擊位置的座標</param>
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
