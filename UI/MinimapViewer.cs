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
    /// <summary>可拖曳浮動視窗：放大小地圖、網格刻度與路徑／繩索／玩家疊繪。</summary>
    public class MinimapViewer : IDisposable
    {
        private readonly Form _parentForm;
        private Form? _viewerWindow;
        private PictureBox? _minimapPicture;
        private Label? _statusLabel;
        private bool _disposed = false;

        private readonly int _zoomFactor;
        private readonly int _offsetX;
        private readonly int _offsetY;
        private readonly int _initialWidth;
        private readonly int _initialHeight;
        private readonly int _baseSize;
        private readonly bool _enabled;

        private DateTime _lastViewerUpdate = DateTime.MinValue;
        private const int ViewerUpdateIntervalMs = 33;
        private volatile bool _isViewerUpdatePending = false;

        private const int RulerSize = 20;
        private const int MajorTickInterval = 10;
        private const int MinorTickInterval = 5;
        private bool _showRuler = true;

        public bool IsVisible => _viewerWindow?.Visible == true;

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
            _baseSize = viewerStyle.BaseSize > 0 ? viewerStyle.BaseSize : 5;

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

        private void InitializeViewer()
        {
            _viewerWindow = new Form
            {
                Text = "小地圖放大視圖",
                FormBorderStyle = FormBorderStyle.None,
                TopMost = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Size = new System.Drawing.Size(_initialWidth, _initialHeight),
                MinimumSize = new System.Drawing.Size(200, 150),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White
            };

            _viewerWindow.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, 60, 60), 1);
                e.Graphics.DrawRectangle(pen, 0, 0, _viewerWindow!.Width - 1, _viewerWindow.Height - 1);
            };

            var titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 24,
                BackColor = Color.FromArgb(45, 45, 48),
                Cursor = Cursors.SizeAll
            };

            var titleLabel = new Label
            {
                Text = "小地圖放大視圖",
                ForeColor = Color.White,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 0, 0)
            };

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

            _minimapPicture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Black
            };

            _viewerWindow.Controls.Add(_minimapPicture);
            _viewerWindow.Controls.Add(_statusLabel);
            _viewerWindow.Controls.Add(titleBar);

            UpdateViewerPosition();
        }

        private void OnParentFormMoved(object? sender, EventArgs e)
        {
            UpdateViewerPosition();
        }

        private void OnParentFormResized(object? sender, EventArgs e)
        {
            UpdateViewerPosition();
        }

        private void OnParentFormClosing(object? sender, FormClosingEventArgs e)
        {
            Hide();
        }

        private void UpdateViewerPosition()
        {
            if (_viewerWindow == null || _parentForm == null) return;

            int newX = _parentForm.Right + _offsetX;
            int newY = _parentForm.Top + _offsetY;

            var screen = Screen.FromControl(_parentForm);
            if (newX + _viewerWindow.Width > screen.WorkingArea.Right)
            {
                newX = _parentForm.Left - _viewerWindow.Width - _offsetX;
            }

            if (newX < screen.WorkingArea.Left)
            {
                newX = _parentForm.Left + 20;
                newY = _parentForm.Top + 20;
            }

            if (newY + _viewerWindow.Height > screen.WorkingArea.Bottom)
            {
                newY = screen.WorkingArea.Bottom - _viewerWindow.Height;
            }

            _viewerWindow.Location = new SdPoint(newX, newY);
        }

        public void Show()
        {
            if (!_enabled || _viewerWindow == null) return;

            if (!_viewerWindow.Visible)
            {
                UpdateViewerPosition();
                _viewerWindow.Show(_parentForm);
            }
        }

        public void Hide()
        {
            _viewerWindow?.Hide();
        }

        public void UpdateMinimap(Mat? minimapMat)
        {
            UpdateMinimapWithPath(minimapMat, null);
        }

        /// <summary>縮放後疊繪網格、路徑與診斷層；約 30Hz UI 節流。</summary>
        public void UpdateMinimapWithPath(Mat? minimapMat, PathVisualizationData? pathData)
        {
            if (!_enabled || _disposed || _viewerWindow == null || _minimapPicture == null)
                return;

            if (minimapMat == null || minimapMat.IsDisposed || minimapMat.Empty())
                return;

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

                    if (pathData.FinalDestination.HasValue)
                    {
                        var fd = pathData.FinalDestination.Value;
                        float fx = fd.X * _zoomFactor;
                        float fy = fd.Y * _zoomFactor;
                        int fSize = _baseSize + _zoomFactor * 2 + 4;

                        using var fillBrush = new SolidBrush(Color.Red);
                        graphics.FillEllipse(fillBrush, fx - fSize / 2f, fy - fSize / 2f, fSize, fSize);

                        using var ringPen = new Pen(Color.White, 3.5f);
                        int ringSize = fSize + 10;
                        graphics.DrawEllipse(ringPen, fx - ringSize / 2f, fy - ringSize / 2f, ringSize, ringSize);
                    }

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

                    if (pathData.PlayerPosition.HasValue)
                    {
                        var pp = pathData.PlayerPosition.Value;
                        var style = AppConfig.Instance.Appearance.MinimapPlayer;
                        float px = pp.X * _zoomFactor;
                        float py = pp.Y * _zoomFactor;
                        float cSize = 5 * _zoomFactor;
                        DrawingHelper.DrawCrosshair(graphics, new SdPointF(px, py), cSize, ArtaleAI.Core.GameVisionCore.ParseColor(style.FrameColor), style.FrameThickness);
                    }

                    if (pathData.TemporaryTarget.HasValue)
                    {
                        var tt = pathData.TemporaryTarget.Value;
                        float tx = tt.X * _zoomFactor;
                        float ty = tt.Y * _zoomFactor;
                        int size = (_baseSize + _zoomFactor + 4);
                        using var tBrush = new SolidBrush(Color.Cyan);
                        graphics.FillEllipse(tBrush, tx - size / 2f, ty - size / 2f, size, size);
                    }

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

        private void UpdatePictureBox(Bitmap newBitmap, int originalWidth, int originalHeight)
        {
            try
            {
                if (_minimapPicture == null || _statusLabel == null) return;

                var oldImage = _minimapPicture.Image;
                _minimapPicture.Image = newBitmap;
                oldImage?.Dispose();

                int renderedWidth = originalWidth * _zoomFactor;
                int renderedHeight = originalHeight * _zoomFactor;
                _statusLabel.Text = $"放大倍率: {_zoomFactor}x | 渲染: {renderedWidth}×{renderedHeight}";
                if (_viewerWindow != null && !_viewerWindow.Visible)
                {
                    Show();
                }
            }
            finally
            {
                _isViewerUpdatePending = false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private static Color GetPriorityColor(float priority, bool isBlacklisted, bool isCurrentTarget)
        {
            if (isBlacklisted) return Color.Black;

            if (priority < 0.5f) return Color.Red;
            if (priority < 1.5f) return Color.Orange;
            if (priority < 3.0f) return Color.Yellow;
            return Color.Lime;
        }

        private static Color GetRopeAccessibilityColor(float distance, bool isPlayerOnRope, bool isTargetRope)
        {
            if (isPlayerOnRope) return Color.Cyan;

            if (isTargetRope) return Color.Lime;

            return Color.Lime;
        }

        private void DrawGrid(Graphics g, int width, int height)
        {
            using var gridPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1);

            for (int x = 0; x < width / _zoomFactor; x++)
            {
                if (x % MajorTickInterval == 0 && x != 0)
                {
                    int screenX = x * _zoomFactor;
                    g.DrawLine(gridPen, screenX, RulerSize, screenX, height);
                }
            }

            for (int y = 0; y < height / _zoomFactor; y++)
            {
                if (y % MajorTickInterval == 0 && y != 0)
                {
                    int screenY = y * _zoomFactor;
                    g.DrawLine(gridPen, RulerSize, screenY, width, screenY);
                }
            }
        }

        private void DrawRuler(Graphics g, int width, int height)
        {
            var bgColor = Color.FromArgb(30, 30, 30);
            var tickColor = Color.FromArgb(100, 100, 100);
            var textColor = Color.FromArgb(200, 200, 200);
            var majorTickLength = RulerSize - 4;
            var minorTickLength = RulerSize / 2;

            using var bgBrush = new SolidBrush(bgColor);
            using var tickPen = new Pen(tickColor, 1);
            using var textBrush = new SolidBrush(textColor);
            using var font = new Font("Consolas", 7f, FontStyle.Regular);

            g.FillRectangle(bgBrush, 0, 0, width, RulerSize);

            for (int x = 0; x < width / _zoomFactor; x++)
            {
                int screenX = x * _zoomFactor;

                if (x % MajorTickInterval == 0)
                {
                    g.DrawLine(tickPen, screenX, RulerSize - majorTickLength, screenX, RulerSize);

                    var text = x.ToString();
                    var textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, textBrush, screenX - textSize.Width / 2, 2);
                }
                else if (x % MinorTickInterval == 0)
                {
                    g.DrawLine(tickPen, screenX, RulerSize - minorTickLength, screenX, RulerSize);
                }
            }

            g.FillRectangle(bgBrush, 0, RulerSize, RulerSize, height - RulerSize);

            for (int y = 0; y < height / _zoomFactor; y++)
            {
                int screenY = y * _zoomFactor + RulerSize;

                if (y % MajorTickInterval == 0)
                {
                    g.DrawLine(tickPen, RulerSize - majorTickLength, screenY, RulerSize, screenY);

                    var text = y.ToString();
                    var textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, textBrush, 2, screenY - textSize.Height / 2);
                }
                else if (y % MinorTickInterval == 0)
                {
                    g.DrawLine(tickPen, RulerSize - minorTickLength, screenY, RulerSize, screenY);
                }
            }

            g.FillRectangle(bgBrush, 0, 0, RulerSize, RulerSize);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
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

        ~MinimapViewer()
        {
            Dispose(false);
        }
    }
}
