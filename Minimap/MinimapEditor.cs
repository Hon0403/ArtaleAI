using ArtaleAI.Config;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Capture;

namespace ArtaleAI.Minimap
{
    /// <summary>
    /// 編輯器小地圖服務 - 包含座標轉換和放大鏡功能
    /// </summary>
    public class MinimapEditor
    {
        private GraphicsCaptureItem? _selectedCaptureItem;
        private readonly ConfigManager? _configManager;


        /// <summary>
        /// 載入小地圖快照
        /// </summary>
        public async Task<MinimapSnapshotResult?> LoadSnapshotAsync(
            nint windowHandle,
            AppConfig config,
            Action<string>? progressReporter = null)
        {
            var result = await MapAnalyzer.GetSnapshotAsync(
                windowHandle,
                config,
                _selectedCaptureItem,
                progressReporter);

            if (result?.MinimapImage != null && result.CaptureItem != null)
            {
                _selectedCaptureItem = result.CaptureItem;

                if (config.General.LastSelectedWindowName != result.CaptureItem.DisplayName)
                {
                    config.General.LastSelectedWindowName = result.CaptureItem.DisplayName;
                    _configManager?.Save();
                    progressReporter?.Invoke($"提示：已將預設捕捉視窗更新為 '{result.CaptureItem.DisplayName}'。");
                }
            }
            else if (_selectedCaptureItem != null && result?.CaptureItem == null)
            {
                _selectedCaptureItem = null;
            }

            return result;
        }

        /// <summary>
        /// 將 PictureBox 顯示座標轉換為原始圖像座標
        /// </summary>
        public Point? ConvertToImageCoordinates(PictureBox pictureBox, Point displayPoint)
        {
            if (pictureBox.Image == null) return null;

            var clientSize = pictureBox.ClientSize;
            var imageSize = pictureBox.Image.Size;
            float ratioX = (float)clientSize.Width / imageSize.Width;
            float ratioY = (float)clientSize.Height / imageSize.Height;
            float ratio = Math.Min(ratioX, ratioY);

            int displayWidth = (int)(imageSize.Width * ratio);
            int displayHeight = (int)(imageSize.Height * ratio);
            int offsetX = (clientSize.Width - displayWidth) / 2;
            int offsetY = (clientSize.Height - displayHeight) / 2;

            var displayRect = new Rectangle(offsetX, offsetY, displayWidth, displayHeight);
            if (!displayRect.Contains(displayPoint)) return null;

            float imageX = displayPoint.X - offsetX;
            float imageY = displayPoint.Y - offsetY;
            float originalX = imageX / ratio;
            float originalY = imageY / ratio;

            return new Point((int)originalX, (int)originalY);
        }

        /// <summary>
        /// 將原始圖像座標轉換為 PictureBox 顯示座標
        /// </summary>
        public Point ConvertToDisplayCoordinates(PictureBox pictureBox, Point imagePoint)
        {
            if (pictureBox.Image == null) return Point.Empty;

            var clientSize = pictureBox.ClientSize;
            var imageSize = pictureBox.Image.Size;
            float ratioX = (float)clientSize.Width / imageSize.Width;
            float ratioY = (float)clientSize.Height / imageSize.Height;
            float ratio = Math.Min(ratioX, ratioY);

            int displayWidth = (int)(imageSize.Width * ratio);
            int displayHeight = (int)(imageSize.Height * ratio);
            int offsetX = (clientSize.Width - displayWidth) / 2;
            int offsetY = (clientSize.Height - displayHeight) / 2;

            int controlX = (int)(imagePoint.X * ratio) + offsetX;
            int controlY = (int)(imagePoint.Y * ratio) + offsetY;

            return new Point(controlX, controlY);
        }

        /// <summary>
        /// 生成跟隨滑鼠的放大鏡圖像
        /// </summary>
        public Bitmap? CreateZoomImage(
            Bitmap sourceImage,
            Point mouseLocation,
            PictureBox sourcePictureBox,
            Size magnifierSize,
            decimal zoomFactor)
        {
            if (sourceImage == null || zoomFactor <= 0) return null;

            var imagePoint = ConvertToImageCoordinates(sourcePictureBox, mouseLocation);
            if (!imagePoint.HasValue) return null;

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

        private Rectangle ValidateCropRect(Rectangle cropRect, Size imageSize)
        {
            int x = Math.Max(0, Math.Min(cropRect.X, imageSize.Width - cropRect.Width));
            int y = Math.Max(0, Math.Min(cropRect.Y, imageSize.Height - cropRect.Height));
            int w = Math.Min(cropRect.Width, imageSize.Width - x);
            int h = Math.Min(cropRect.Height, imageSize.Height - y);
            return new Rectangle(x, y, w, h);
        }
    }
}
