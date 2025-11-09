using ArtaleAI.API.Config;
using ArtaleAI.Config;
using ArtaleAI.Utils;
using System.Drawing.Drawing2D;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.UI
{
    /// <summary>
    /// 浮動放大鏡 - 獨立的放大鏡功能模組
    /// </summary>
    public class FloatingMagnifier : IDisposable
    {
        private readonly MainForm _mainForm;

        private readonly int _magnifierSize;
        private readonly int _magnifierOffset;
        private readonly int _crosshairSize;

        private Form? _zoomWindow;
        private PictureBox? _floatingZoomBox;
        private bool _isVisible;
        private bool _disposed = false;
        public bool IsVisible => _isVisible;

        public FloatingMagnifier(MainForm eventHandler, int magnifierSize, int magnifierOffset, int crosshairSize)
        {
            _mainForm = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));

            _magnifierSize = magnifierSize;
            _magnifierOffset = magnifierOffset;
            _crosshairSize = crosshairSize;

            InitializeMagnifier();
        }

        /// <summary>
        /// 初始化放大鏡視窗
        /// </summary>
        private void InitializeMagnifier()
        {
            _zoomWindow = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                TopMost = true,
                ShowInTaskbar = false,
                Size = new Size(_magnifierSize, _magnifierSize), 
                BackColor = Color.White,
                Visible = false
            };

            _floatingZoomBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.StretchImage,
                BorderStyle = BorderStyle.FixedSingle
            };

            _zoomWindow.Controls.Add(_floatingZoomBox);
            _floatingZoomBox.Paint += FloatingZoomBox_Paint;
        }

        private void FloatingZoomBox_Paint(object? sender, PaintEventArgs e)
        {
            if (_floatingZoomBox?.Image == null) return;

            var g = e.Graphics;
            int w = _floatingZoomBox.Width;
            int h = _floatingZoomBox.Height;

            using var pen = new Pen(Color.Red, _crosshairSize);
            g.DrawLine(pen, w / 2, 0, w / 2, h);
            g.DrawLine(pen, 0, h / 2, w, h / 2);

            using var borderPen = new Pen(Color.Black, 1);
            g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
        }

        /// <summary>
        /// 更新放大鏡位置和內容
        /// </summary>
        public void UpdateMagnifier(SdPoint mouseLocation, Control sourceControl)
        {
            if (_zoomWindow == null || _floatingZoomBox == null)
                return;

            var sourceImage = _mainForm.pictureBoxMinimap.Image as Bitmap;
            if (sourceImage == null)
                return;

            var zoomedImage = CreateZoomImage(sourceImage, mouseLocation);
            if (zoomedImage == null)
                return;

            UpdateMagnifierDisplay(zoomedImage, mouseLocation, sourceControl);
        }

        private void UpdateMagnifierDisplay(Bitmap zoomedImage, SdPoint mouseLocation, Control sourceControl)
        {
            _floatingZoomBox!.Image?.Dispose();
            _floatingZoomBox.Image = zoomedImage;

            var screenPoint = sourceControl.PointToScreen(mouseLocation);

            int offset = _magnifierOffset;
            var magnifierPosition = new SdPoint(
                screenPoint.X + offset,
                screenPoint.Y + offset
            );

            var screen = Screen.FromPoint(screenPoint);
            // 避免超出右邊界
            if (magnifierPosition.X + _zoomWindow!.Width > screen.WorkingArea.Right)
                magnifierPosition.X = screenPoint.X - _zoomWindow.Width - offset;
            // 避免超出下邊界
            if (magnifierPosition.Y + _zoomWindow.Height > screen.WorkingArea.Bottom)
                magnifierPosition.Y = screenPoint.Y - _zoomWindow.Height - offset;

            _zoomWindow.Location = magnifierPosition;

            if (!_zoomWindow.Visible)
            {
                _zoomWindow.Show();
                _isVisible = true;
            }
        }

        /// <summary>
        /// 隱藏放大鏡
        /// </summary>
        public void Hide()
        {
            if (_zoomWindow != null && _zoomWindow.Visible)
            {
                _zoomWindow.Hide();
                _isVisible = false;
            }
        }

        /// <summary>
        /// 生成放大鏡圖像
        /// </summary>
        private Bitmap? CreateZoomImage(Bitmap sourceImage, SdPoint mouseLocation)
        {
            if (sourceImage == null) return null;

            var pictureBox = _mainForm.pictureBoxMinimap;
            if (pictureBox.Image == null) return null;

            // 使用工具類別處理座標轉換 ✨
            var displayRect = CoordinateHelper.GetDisplayRect(pictureBox);

            // 檢查是否在顯示區域內
            if (!CoordinateHelper.IsPointInDisplayArea(mouseLocation, pictureBox))
                return null;

            // 轉換為圖片座標
            var imagePoint = CoordinateHelper.ControlToImagePoint(mouseLocation, pictureBox);

            // 計算放大區域
            int zoomAreaSize = 20; // 原圖 20x20 像素
            var sourceRect = new Rectangle(
                imagePoint.X - zoomAreaSize / 2,
                imagePoint.Y - zoomAreaSize / 2,
                zoomAreaSize,
                zoomAreaSize
            );

            // 限制在圖片範圍內
            sourceRect = Rectangle.Intersect(sourceRect,
                new Rectangle(0, 0, pictureBox.Image.Width, pictureBox.Image.Height));

            if (sourceRect.Width <= 0 || sourceRect.Height <= 0)
                return null;

            // 使用工具類別建立放大圖像 ✨
            return DrawingHelper.CreateZoomedImage(
                sourceImage as Bitmap,
                sourceRect,
                new Size(_magnifierSize, _magnifierSize)
            );
        }

        /// <summary>
        /// 釋放所有資源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 釋放資源的核心邏輯
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // ✅ 釋放託管資源
                Hide();  // 確保視窗已隱藏

                if (_floatingZoomBox != null)
                {
                    _floatingZoomBox.Image?.Dispose();
                    _floatingZoomBox.Dispose();
                    _floatingZoomBox = null;
                }

                if (_zoomWindow != null)
                {
                    _zoomWindow.Dispose();
                    _zoomWindow = null;
                }
            }

            _disposed = true;
        }

        /// <summary>
        /// 析構函式 (Finalizer)
        /// </summary>
        ~FloatingMagnifier()
        {
            Dispose(false);
        }
    }
}
