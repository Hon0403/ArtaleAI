using ArtaleAI.Models.Config;
using ArtaleAI.Services;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;
using ArtaleAI.Models.Visualization;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.UI
{
    /// <summary>
    /// 小地圖放大視窗 - 獨立視窗顯示放大的小地圖，自動跟隨主視窗
    /// </summary>
    public class MinimapViewer : IDisposable
    {
        private readonly Form _parentForm;
        private Form? _viewerWindow;
        private PictureBox? _minimapPicture;
        private Label? _statusLabel;
        private bool _disposed = false;

        // 配置參數
        private readonly int _zoomFactor;
        private readonly int _offsetX;
        private readonly int _offsetY;
        private readonly int _initialWidth;
        private readonly int _initialHeight;
        private readonly int _baseSize;
        private readonly bool _enabled;

        private DateTime _lastViewerUpdate = DateTime.MinValue;
        private const int ViewerUpdateIntervalMs = 33; // 更新間隔（33ms = 約 30Hz）
        private volatile bool _isViewerUpdatePending = false;

        // 📏 刻度尺配置
        private const int RulerSize = 20;              // 刻度尺寬度/高度 (像素)
        private const int MajorTickInterval = 10;      // 主刻度間距 (座標)
        private const int MinorTickInterval = 5;       // 次刻度間距 (座標)
        private bool _showRuler = true;                // 是否顯示刻度尺

        public bool IsVisible => _viewerWindow?.Visible == true;

        /// <summary>
        /// 初始化小地圖放大視窗
        /// </summary>
        /// <param name="parentForm">主視窗（用於跟隨移動）</param>
        /// <param name="config">應用程式配置</param>
        public MinimapViewer(Form parentForm, AppConfig config)
        {
            _parentForm = parentForm ?? throw new ArgumentNullException(nameof(parentForm));

            var viewerStyle = config.Appearance.MinimapViewer;
            _enabled = viewerStyle.Enabled;
            _zoomFactor = viewerStyle.ZoomFactor;
            _offsetX = viewerStyle.OffsetX;
            _offsetY = viewerStyle.OffsetY;
            _initialWidth = viewerStyle.Width;
            _initialHeight = viewerStyle.Height;
            _baseSize = viewerStyle.BaseSize > 0 ? viewerStyle.BaseSize : 5; // 預設 5

            Logger.Info($"[MinimapViewer] 初始化: Enabled={_enabled}, ZoomFactor={_zoomFactor}, BaseSize={_baseSize}");

            if (_enabled)
            {
                InitializeViewer();

                _parentForm.LocationChanged += OnParentFormMoved;
                _parentForm.SizeChanged += OnParentFormResized;
                _parentForm.FormClosing += OnParentFormClosing;

                Logger.Info("[MinimapViewer] 視窗初始化完成");
            }
            else
            {
                Logger.Info("[MinimapViewer] 功能已停用");
            }
        }

        /// <summary>
        /// 初始化視窗和控制項
        /// </summary>
        private void InitializeViewer()
        {
            _viewerWindow = new Form
            {
                Text = "🗺️ 小地圖放大視圖",
                FormBorderStyle = FormBorderStyle.None, // 無邊框，完全貼合
                TopMost = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Size = new System.Drawing.Size(_initialWidth, _initialHeight),
                MinimumSize = new System.Drawing.Size(200, 150),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
            };

            // 添加 1px 邊框效果
            _viewerWindow.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, 60, 60), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, _viewerWindow!.Width - 1, _viewerWindow.Height - 1);
            };

            // 標題列（可拖動視窗）
            var titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 24,
                BackColor = Color.FromArgb(45, 45, 48),
                Cursor = Cursors.SizeAll
            };

            var titleLabel = new Label
            {
                Text = "🗺️ 小地圖放大視圖",
                ForeColor = Color.White,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 0, 0)
            };

            // 拖動功能
            bool isDragging = false;
            SdPoint dragStart = SdPoint.Empty;

            titleBar.MouseDown += (s, e) => { isDragging = true; dragStart = e.Location; };
            titleBar.MouseUp += (s, e) => { isDragging = false; };
            titleBar.MouseMove += (s, e) =>
            {
                if (isDragging && _viewerWindow != null)
                {
                    _viewerWindow.Location = new SdPoint(
                        _viewerWindow.Left + e.X - dragStart.X,
                        _viewerWindow.Top + e.Y - dragStart.Y
                    );
                }
            };

            titleLabel.MouseDown += (s, e) => { isDragging = true; dragStart = e.Location; };
            titleLabel.MouseUp += (s, e) => { isDragging = false; };
            titleLabel.MouseMove += (s, e) =>
            {
                if (isDragging && _viewerWindow != null)
                {
                    _viewerWindow.Location = new SdPoint(
                        _viewerWindow.Left + e.X - dragStart.X,
                        _viewerWindow.Top + e.Y - dragStart.Y
                    );
                }
            };

            titleBar.Controls.Add(titleLabel);

            // 狀態列標籤
            _statusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Text = $"放大倍率: {_zoomFactor}x | 等待小地圖...",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            // 小地圖顯示區（使用 Zoom 模式自動適應視窗大小，保持比例）
            _minimapPicture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage, // 拉伸填滿視窗，放大倍率影響解析度
                BackColor = Color.Black
            };

            _viewerWindow.Controls.Add(_minimapPicture);
            _viewerWindow.Controls.Add(_statusLabel);
            _viewerWindow.Controls.Add(titleBar);

            UpdateViewerPosition();
        }

        /// <summary>
        /// 主視窗移動事件
        /// </summary>
        private void OnParentFormMoved(object? sender, EventArgs e)
        {
            UpdateViewerPosition();
        }

        /// <summary>
        /// 主視窗大小改變事件
        /// </summary>
        private void OnParentFormResized(object? sender, EventArgs e)
        {
            UpdateViewerPosition();
        }

        /// <summary>
        /// 主視窗關閉事件
        /// </summary>
        private void OnParentFormClosing(object? sender, FormClosingEventArgs e)
        {
            Hide();
        }

        /// <summary>
        /// 更新視窗位置（跟隨主視窗）
        /// </summary>
        private void UpdateViewerPosition()
        {
            if (_viewerWindow == null || _parentForm == null) return;

            int newX = _parentForm.Right + _offsetX;
            int newY = _parentForm.Top + _offsetY;

            // 邊界檢查：如果超出螢幕右側，放在主視窗左側
            var screen = Screen.FromControl(_parentForm);
            if (newX + _viewerWindow.Width > screen.WorkingArea.Right)
            {
                newX = _parentForm.Left - _viewerWindow.Width - _offsetX;
            }

            // 如果左側也放不下，重疊在主視窗內
            if (newX < screen.WorkingArea.Left)
            {
                newX = _parentForm.Left + 20;
                newY = _parentForm.Top + 20;
            }

            // 確保不超出螢幕底部
            if (newY + _viewerWindow.Height > screen.WorkingArea.Bottom)
            {
                newY = screen.WorkingArea.Bottom - _viewerWindow.Height;
            }

            _viewerWindow.Location = new SdPoint(newX, newY);
        }

        /// <summary>
        /// 顯示小地圖視窗
        /// </summary>
        public void Show()
        {
            if (!_enabled || _viewerWindow == null) return;

            if (!_viewerWindow.Visible)
            {
                UpdateViewerPosition();
                _viewerWindow.Show(_parentForm);
            }
        }

        /// <summary>
        /// 隱藏小地圖視窗
        /// </summary>
        public void Hide()
        {
            _viewerWindow?.Hide();
        }

        /// <summary>
        /// 更新小地圖內容（不含路徑可視化）
        /// </summary>
        /// <param name="minimapMat">原始小地圖 Mat（呼叫者負責 Dispose）</param>
        public void UpdateMinimap(Mat? minimapMat)
        {
            UpdateMinimapWithPath(minimapMat, null);
        }

        /// <summary>
        /// 更新小地圖內容（含路徑可視化）
        /// 🔧 性能優化：加入 UI 節流以避免更新太頻繁造成卡頓
        /// </summary>
        /// <param name="minimapMat">原始小地圖 Mat（呼叫者負責 Dispose）</param>
        /// <param name="pathData">路徑可視化資料（可為 null）</param>
        public void UpdateMinimapWithPath(Mat? minimapMat, PathVisualizationData? pathData)
        {
            if (!_enabled || _disposed || _viewerWindow == null || _minimapPicture == null)
                return;

            if (minimapMat == null || minimapMat.IsDisposed || minimapMat.Empty())
                return;

            // UI 節流
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastViewerUpdate).TotalMilliseconds;
            if (elapsed < ViewerUpdateIntervalMs || _isViewerUpdatePending)
                return;

            _lastViewerUpdate = now;

            try
            {
                if (minimapMat.Rows <= 0 || minimapMat.Cols <= 0)
                    return;

                int originalWidth = minimapMat.Width;
                int originalHeight = minimapMat.Height;

                using var resized = new Mat();
                Cv2.Resize(minimapMat, resized,
                    new OpenCvSharp.Size(originalWidth * _zoomFactor, originalHeight * _zoomFactor),
                    interpolation: InterpolationFlags.Linear);

                var newBitmap = resized.ToBitmap();
                using var graphics = Graphics.FromImage(newBitmap);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                if (_showRuler)
                {
                    DrawGrid(graphics, newBitmap.Width, newBitmap.Height);
                }

                if (pathData != null)
                {
                    // 1. 路徑點
                    if (pathData.WaypointPaths != null)
                    {
                        foreach (var wp in pathData.WaypointPaths)
                        {
                            if (wp.Position.X < 0 || wp.Position.Y < 0) continue;
                            var pointColor = GetPriorityColor(wp.Priority, wp.IsBlacklisted, wp.IsCurrentTarget);
                            float sx = wp.Position.X * _zoomFactor;
                            float sy = wp.Position.Y * _zoomFactor;
                            int size = wp.IsCurrentTarget ? (_baseSize + _zoomFactor + 2) : (_baseSize + _zoomFactor / 2);
                            using var brush = new SolidBrush(pointColor);
                            graphics.FillEllipse(brush, sx - size / 2f, sy - size / 2f, size, size);
                        }
                    }

                    // 2. 繩索
                    if (pathData.Ropes != null)
                    {
                        foreach (var rope in pathData.Ropes)
                        {
                            var color = GetRopeAccessibilityColor(rope.DistanceToPlayer, rope.IsPlayerOnRope, rope.IsTargetRope);
                            int thick = rope.IsTargetRope ? Math.Max(4, _baseSize + 2) : Math.Max(2, _baseSize);
                            float sx = rope.X * _zoomFactor;
                            float st = rope.TopY * _zoomFactor;
                            float sb = rope.BottomY * _zoomFactor;

                            using var pen = new Pen(color, thick);
                            graphics.DrawLine(pen, sx, st, sx, sb);

                            int eSize = rope.IsTargetRope ? Math.Max(6, _baseSize + 2) : Math.Max(4, _baseSize);
                            using var eBrush = new SolidBrush(color);
                            graphics.FillEllipse(eBrush, sx - eSize / 2f, st - eSize / 2f, eSize, eSize);
                            graphics.FillEllipse(eBrush, sx - eSize / 2f, sb - eSize / 2f, eSize, eSize);
                        }
                    }

                    // 3. 路徑終點高亮（紅色圓 + 白色外環）
                    if (pathData.FinalDestination.HasValue)
                    {
                        var fd = pathData.FinalDestination.Value;
                        float fx = fd.X * _zoomFactor;
                        float fy = fd.Y * _zoomFactor;
                        int fSize = _baseSize + _zoomFactor * 2 + 4;   // 更大的填充圓

                        // 紅色填充（與一般節點同色）
                        using var fillBrush = new SolidBrush(Color.Red);
                        graphics.FillEllipse(fillBrush, fx - fSize / 2f, fy - fSize / 2f, fSize, fSize);

                        // 白色外環高亮（更粗更大）
                        using var ringPen = new Pen(Color.White, 3.5f);
                        int ringSize = fSize + 10;
                        graphics.DrawEllipse(ringPen, fx - ringSize / 2f, fy - ringSize / 2f, ringSize, ringSize);
                    }

                    // 4. 診斷層：目標 Hitbox（半透明，不與主終點搶語意）
                    if (pathData.TargetHitbox.HasValue)
                    {
                        var hb = pathData.TargetHitbox.Value;
                        var hbRect = new RectangleF(
                            hb.X * _zoomFactor,
                            hb.Y * _zoomFactor,
                            hb.Width * _zoomFactor,
                            hb.Height * _zoomFactor);

                        var inside = pathData.IsPlayerInsideTargetHitbox == true;
                        using var hbFill = new SolidBrush(inside ? Color.FromArgb(60, 0, 200, 120) : Color.FromArgb(40, 220, 30, 30));
                        using var hbPen = new Pen(inside ? Color.Lime : Color.OrangeRed, 1.6f)
                        {
                            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash
                        };
                        graphics.FillRectangle(hbFill, hbRect);
                        graphics.DrawRectangle(hbPen, hbRect.X, hbRect.Y, hbRect.Width, hbRect.Height);
                    }

                    // 5. 診斷層：繩索對位帶（僅在爬繩動作有效）
                    if (pathData.RopeAlignCenterX.HasValue && pathData.RopeAlignTolerance.HasValue)
                    {
                        float centerX = pathData.RopeAlignCenterX.Value * _zoomFactor;
                        float halfTol = pathData.RopeAlignTolerance.Value * _zoomFactor;
                        var ropeBandRect = new RectangleF(centerX - halfTol, 0, halfTol * 2f, newBitmap.Height);
                        using var bandBrush = new SolidBrush(Color.FromArgb(35, 60, 190, 255));
                        using var bandPen = new Pen(Color.FromArgb(150, 100, 210, 255), 1f)
                        {
                            DashStyle = System.Drawing.Drawing2D.DashStyle.Dot
                        };
                        graphics.FillRectangle(bandBrush, ropeBandRect);
                        graphics.DrawRectangle(bandPen, ropeBandRect.X, ropeBandRect.Y, ropeBandRect.Width, ropeBandRect.Height);
                    }

                    // 6. 玩家與十字準心
                    if (pathData.PlayerPosition.HasValue)
                    {
                        var pp = pathData.PlayerPosition.Value;
                        var style = AppConfig.Instance.Appearance.MinimapPlayer;
                        float px = pp.X * _zoomFactor;
                        float py = pp.Y * _zoomFactor;
                        float cSize = 5 * _zoomFactor;
                        DrawingHelper.DrawCrosshair(graphics, new SdPointF(px, py), cSize, ArtaleAI.Core.GameVisionCore.ParseColor(style.FrameColor), style.FrameThickness);
                    }

                    // 7. 臨時目標
                    if (pathData.TemporaryTarget.HasValue)
                    {
                        var tt = pathData.TemporaryTarget.Value;
                        float tx = tt.X * _zoomFactor;
                        float ty = tt.Y * _zoomFactor;
                        int size = (_baseSize + _zoomFactor + 4);
                        using var tBrush = new SolidBrush(Color.Cyan);
                        graphics.FillEllipse(tBrush, tx - size / 2f, ty - size / 2f, size, size);
                    }

                    // 8. 診斷層文字（低干擾右上角）
                    if (!string.IsNullOrWhiteSpace(pathData.CurrentAction))
                    {
                        var actionText = $"ACT:{pathData.CurrentAction}";
                        using var font = new Font("Consolas", 8, FontStyle.Bold);
                        var textSize = graphics.MeasureString(actionText, font);
                        var textRect = new RectangleF(newBitmap.Width - textSize.Width - 8, 6, textSize.Width + 4, textSize.Height + 2);
                        using var textBg = new SolidBrush(Color.FromArgb(120, 20, 20, 20));
                        using var textBrush = new SolidBrush(Color.WhiteSmoke);
                        graphics.FillRectangle(textBg, textRect);
                        graphics.DrawString(actionText, font, textBrush, textRect.X + 2, textRect.Y + 1);
                    }
                }

                if (_showRuler)
                {
                    DrawRuler(graphics, newBitmap.Width, newBitmap.Height);
                }

                if (_viewerWindow != null)
                {
                    if (_viewerWindow.InvokeRequired)
                    {
                        _viewerWindow.BeginInvoke(new Action(() => UpdatePictureBox(newBitmap, originalWidth, originalHeight)));
                    }
                    else
                    {
                        UpdatePictureBox(newBitmap, originalWidth, originalHeight);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[MinimapViewer] 渲染失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新 PictureBox 圖片（UI 線程）
        /// </summary>
        private void UpdatePictureBox(Bitmap newBitmap, int originalWidth, int originalHeight)
{
    try
    {
        if (_minimapPicture == null || _statusLabel == null) return;

        var oldImage = _minimapPicture.Image;
        _minimapPicture.Image = newBitmap;
        oldImage?.Dispose();

        // 顯示渲染尺寸資訊
        int renderedWidth = originalWidth * _zoomFactor;
        int renderedHeight = originalHeight * _zoomFactor;
        _statusLabel.Text = $"放大倍率: {_zoomFactor}x | 渲染: {renderedWidth}×{renderedHeight}";
        // 確保視窗可見 (主線程執行)
        if (!_viewerWindow.Visible)
        {
            Show();
        }
    }
    finally
    {
        _isViewerUpdatePending = false;
    }
}

/// <summary>
/// 釋放資源
/// </summary>
public void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}

/// <summary>
/// 根據優先級取得熱力圖顏色
/// 🔧 使用高對比度顏色，更容易辨識
/// </summary>
/// <param name="priority">優先級分數</param>
/// <param name="isBlacklisted">是否在黑名單</param>
/// <param name="isCurrentTarget">是否為當前目標</param>
/// <returns>對應的顏色</returns>
private static Color GetPriorityColor(float priority, bool isBlacklisted, bool isCurrentTarget)
{
    // 黑名單：純黑色
    if (isBlacklisted) return Color.Black;

    // 根據優先級映射顏色（高對比度）
    if (priority < 0.5f) return Color.Red;           // 低優先級：純紅
    if (priority < 1.5f) return Color.Orange;        // 中等優先級：橙色
    if (priority < 3.0f) return Color.Yellow;        // 高優先級：黃色
    return Color.Lime;                               // 極高優先級：亮綠
}

/// <summary>
/// 根據繩索可達性取得顏色
/// 🔧 簡化邏輯：僅依據「目標」與「所在」變色，移除距離判斷
/// </summary>
/// <param name="distance">與玩家的 X 距離 (已不使用)</param>
/// <param name="isPlayerOnRope">玩家是否在繩索上</param>
/// <param name="isTargetRope">是否為目標繩索</param>
/// <returns>對應的顏色</returns>
private static Color GetRopeAccessibilityColor(float distance, bool isPlayerOnRope, bool isTargetRope)
{
    // 正在爬：亮青色 (Active State)
    if (isPlayerOnRope) return Color.Cyan;

    // 目標：亮綠色 (Target State) - 用光圈區分
    if (isTargetRope) return Color.Lime;

    // 其他：路徑點綠色 (Static State)
    return Color.Lime;
}

/// <summary>
/// 繪製網格線
/// </summary>
private void DrawGrid(Graphics g, int width, int height)
{
    // 網格樣式 (非常淡的白色)
    using var gridPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1);

    // X 軸網格 (垂直線)
    for (int x = 0; x < width / _zoomFactor; x++)
    {
        if (x % MajorTickInterval == 0 && x != 0)
        {
            int screenX = x * _zoomFactor;
            g.DrawLine(gridPen, screenX, RulerSize, screenX, height);
        }
    }

    // Y 軸網格 (水平線)
    for (int y = 0; y < height / _zoomFactor; y++)
    {
        if (y % MajorTickInterval == 0 && y != 0)
        {
            int screenY = y * _zoomFactor;
            g.DrawLine(gridPen, RulerSize, screenY, width, screenY);
        }
    }
}

/// <summary>
/// 繪製座標刻度尺（上方和左側）
/// 📏 主刻度每 10 座標，次刻度每 5 座標
/// </summary>
/// <param name="g">Graphics 物件</param>
/// <param name="width">圖片寬度（放大後）</param>
/// <param name="height">圖片高度（放大後）</param>
private void DrawRuler(Graphics g, int width, int height)
{
    // 刻度尺樣式
    var bgColor = Color.FromArgb(30, 30, 30);            // 刻度尺背景（更深）
    var tickColor = Color.FromArgb(100, 100, 100);       // 刻度線（柔和灰）
    var textColor = Color.FromArgb(200, 200, 200);       // 文字（亮灰）
    var majorTickLength = RulerSize - 4;                 // 主刻度長度
    var minorTickLength = RulerSize / 2;                 // 次刻度長度

    using var bgBrush = new SolidBrush(bgColor);
    using var tickPen = new Pen(tickColor, 1);
    using var textBrush = new SolidBrush(textColor);
    using var font = new Font("Consolas", 7f, FontStyle.Regular);

    // ===== 上方刻度尺 (X 軸) =====
    g.FillRectangle(bgBrush, 0, 0, width, RulerSize);

    // 繪製刻度
    for (int x = 0; x < width / _zoomFactor; x++)
    {
        int screenX = x * _zoomFactor;

        if (x % MajorTickInterval == 0)
        {
            // 主刻度 + 數字
            g.DrawLine(tickPen, screenX, RulerSize - majorTickLength, screenX, RulerSize);

            // 數字（每 10 座標顯示）
            var text = x.ToString();
            var textSize = g.MeasureString(text, font);
            g.DrawString(text, font, textBrush, screenX - textSize.Width / 2, 2);
        }
        else if (x % MinorTickInterval == 0)
        {
            // 次刻度（短線，不標數字）
            g.DrawLine(tickPen, screenX, RulerSize - minorTickLength, screenX, RulerSize);
        }
    }

    // ===== 左側刻度尺 (Y 軸) =====
    g.FillRectangle(bgBrush, 0, RulerSize, RulerSize, height - RulerSize);

    // 繪製刻度
    for (int y = 0; y < height / _zoomFactor; y++)
    {
        int screenY = y * _zoomFactor + RulerSize;

        if (y % MajorTickInterval == 0)
        {
            // 主刻度 + 數字
            g.DrawLine(tickPen, RulerSize - majorTickLength, screenY, RulerSize, screenY);

            // 數字（每 10 座標顯示）
            var text = y.ToString();
            var textSize = g.MeasureString(text, font);
            g.DrawString(text, font, textBrush, 2, screenY - textSize.Height / 2);
        }
        else if (y % MinorTickInterval == 0)
        {
            // 次刻度（短線，不標數字）
            g.DrawLine(tickPen, RulerSize - minorTickLength, screenY, RulerSize, screenY);
        }
    }

    // 左上角方塊（交接處）
    g.FillRectangle(bgBrush, 0, 0, RulerSize, RulerSize);
}

/// <summary>
/// 釋放資源的核心邏輯
/// </summary>
protected virtual void Dispose(bool disposing)
{
    if (_disposed) return;

    if (disposing)
    {
        // 取消事件訂閱
        if (_parentForm != null)
        {
            _parentForm.LocationChanged -= OnParentFormMoved;
            _parentForm.SizeChanged -= OnParentFormResized;
            _parentForm.FormClosing -= OnParentFormClosing;
        }

        Hide();

        if (_minimapPicture != null)
        {
            _minimapPicture.Image?.Dispose();
            _minimapPicture.Dispose();
            _minimapPicture = null;
        }

        if (_statusLabel != null)
        {
            _statusLabel.Dispose();
            _statusLabel = null;
        }

        if (_viewerWindow != null)
        {
            _viewerWindow.Dispose();
            _viewerWindow = null;
        }
    }

    _disposed = true;
}

/// <summary>
/// 析構函式
/// </summary>
~MinimapViewer()
        {
    Dispose(false);
}
    }
}
