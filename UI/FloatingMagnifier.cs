using ArtaleAI.UI;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ArtaleAI.UI
{
    /// <summary>
    /// 浮動放大鏡 - 獨立的放大鏡功能模組
    /// </summary>
    public class FloatingMagnifier : IDisposable
    {
        private readonly IApplicationEventHandler _eventHandler;
        private Form? _zoomWindow;
        private PictureBox? _floatingZoomBox;
        private bool _isVisible;

        public bool IsVisible => _isVisible;

        public FloatingMagnifier(IApplicationEventHandler eventHandler)
        {
            _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
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
                Size = new Size(150, 150),
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

        /// <summary>
        /// 更新放大鏡位置和內容
        /// </summary>
        public void UpdateMagnifier(Point mouseLocation, Control sourceControl)
        {
            if (_zoomWindow == null || _floatingZoomBox == null)
            {
                Hide();
                return;
            }

            var sourceImage = _eventHandler.GetSourceImage();
            if (sourceImage == null)
            {
                Hide();
                return;
            }

            var zoomedImage = CreateZoomImage(sourceImage, mouseLocation);
            if (zoomedImage != null)
            {
                // 更新放大鏡圖像
                _floatingZoomBox.Image?.Dispose();
                _floatingZoomBox.Image = zoomedImage;

                // 計算放大鏡視窗位置
                var screenPoint = sourceControl.PointToScreen(mouseLocation);
                var magnifierPosition = CalculateMagnifierPosition(screenPoint);

                _zoomWindow.Location = magnifierPosition;

                if (!_zoomWindow.Visible)
                {
                    _zoomWindow.Show();
                    _isVisible = true;
                }
            }
            else
            {
                Hide();
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
        private Bitmap? CreateZoomImage(Bitmap sourceImage, Point mouseLocation)
        {
            if (sourceImage == null) return null;

            var imagePoint = _eventHandler.ConvertToImageCoordinates(mouseLocation);
            if (!imagePoint.HasValue) return null;

            var zoomFactor = _eventHandler.GetZoomFactor();
            if (zoomFactor <= 0) return null;

            var magnifierSize = _floatingZoomBox!.Size;
            int cropSize = (int)(Math.Min(magnifierSize.Width, magnifierSize.Height) / zoomFactor);

            var cropRect = new Rectangle(
                imagePoint.Value.X - cropSize / 2,
                imagePoint.Value.Y - cropSize / 2,
                cropSize,
                cropSize);

            cropRect = ValidateCropRect(cropRect, sourceImage.Size);

            var result = new Bitmap(magnifierSize.Width, magnifierSize.Height);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.White);
                g.DrawImage(sourceImage,
                    new Rectangle(0, 0, magnifierSize.Width, magnifierSize.Height),
                    cropRect,
                    GraphicsUnit.Pixel);
            }

            return result;
        }

        /// <summary>
        /// 計算放大鏡視窗位置，避免超出螢幕邊界
        /// </summary>
        private Point CalculateMagnifierPosition(Point screenPoint)
        {
            if (_zoomWindow == null) return screenPoint;

            var magnifierPosition = new Point(
                screenPoint.X + 20,
                screenPoint.Y + 20
            );

            var screen = Screen.FromPoint(screenPoint);

            // 避免超出右邊界
            if (magnifierPosition.X + _zoomWindow.Width > screen.WorkingArea.Right)
                magnifierPosition.X = screenPoint.X - _zoomWindow.Width - 20;

            // 避免超出下邊界
            if (magnifierPosition.Y + _zoomWindow.Height > screen.WorkingArea.Bottom)
                magnifierPosition.Y = screenPoint.Y - _zoomWindow.Height - 20;

            return magnifierPosition;
        }

        /// <summary>
        /// 驗證裁切矩形，確保不超出圖像邊界
        /// </summary>
        private Rectangle ValidateCropRect(Rectangle cropRect, Size imageSize)
        {
            int x = Math.Max(0, Math.Min(cropRect.X, imageSize.Width - cropRect.Width));
            int y = Math.Max(0, Math.Min(cropRect.Y, imageSize.Height - cropRect.Height));
            int w = Math.Min(cropRect.Width, imageSize.Width - x);
            int h = Math.Min(cropRect.Height, imageSize.Height - y);

            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// 繪製放大鏡的十字線和邊框
        /// </summary>
        private void FloatingZoomBox_Paint(object? sender, PaintEventArgs e)
        {
            if (_floatingZoomBox?.Image == null) return;

            var g = e.Graphics;
            int w = _floatingZoomBox.Width;
            int h = _floatingZoomBox.Height;

            // 繪製十字線
            using (var pen = new Pen(Color.Red, 2))
            {
                g.DrawLine(pen, w / 2, 0, w / 2, h);
                g.DrawLine(pen, 0, h / 2, w, h / 2);
            }

            // 繪製邊框
            using (var borderPen = new Pen(Color.Black, 1))
            {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }
        }

        public void Dispose()
        {
            _floatingZoomBox?.Image?.Dispose();
            _floatingZoomBox?.Dispose();
            _zoomWindow?.Dispose();
        }
    }
}
