using ArtaleAI.Config;
using ArtaleAI.GameWindow;
using ArtaleAI.Minimap;
using ArtaleAI.Services;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Capture;

namespace ArtaleAI
{
    public partial class MainForm : Form
    {
        private AppConfig _config;
        private readonly EditorMinimap _editorMinimap = new();

        public MainForm()
        {
            InitializeComponent();

            // ★ 步驟 1: 程式啟動時載入設定檔
            LoadConfiguration();

            // ★ 步驟 2: 綁定事件
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;
            numericUpDownZoom.ValueChanged += numericUpDownZoom_ValueChanged; // 綁定儲存事件
        }

        /// <summary>
        /// 從檔案載入設定，並更新 UI。
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                _config = ConfigLoader.LoadConfig();
                // ★ 將設定檔中的值，設定到 UI 控制項上
                // 使用 try-catch 避免設定值超出控制項範圍
                try
                {
                    numericUpDownZoom.Value = _config.General.ZoomFactor;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 如果設定檔的值無效，則使用控制項的預設值
                    _config.General.ZoomFactor = numericUpDownZoom.Value;
                    ConfigSaver.SaveConfig(_config); // 並回存
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"讀取設定檔失敗: {ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _config = new AppConfig(); // 若失敗，則使用預設設定
            }
        }

        /// <summary>
        /// 當 NumericUpDown 的值改變時，更新設定物件並儲存檔案。
        /// </summary>
        private void numericUpDownZoom_ValueChanged(object? sender, EventArgs e)
        {
            if (_config != null)
            {
                _config.General.ZoomFactor = numericUpDownZoom.Value;
                ConfigSaver.SaveConfig(_config); // 即時儲存變更
            }
        }

        private async void TabControl1_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (tabControl1.SelectedIndex == 1)
            {
                await UpdateMinimapSnapshotAsync();
            }
        }

        private async Task UpdateMinimapSnapshotAsync()
        {
            tabControl1.Enabled = false;
            pictureBoxMinimap.Image = null;
            textBox1.AppendText("正在載入小地圖快照...\r\n");

            try
            {
                _config = ConfigLoader.LoadConfig();

                Action<string> reporter = message =>
                {
                    this.Invoke((Action)(() =>
                    {
                        textBox1.AppendText(message + "\r\n");
                    }));
                };

                // ✅ 直接使用原有的返回類型
                var result = await _editorMinimap.LoadSnapshotAsync(this.Handle, _config, reporter);

                if (result?.MinimapImage != null && result.CaptureItem != null)
                {
                    pictureBoxMinimap.Image?.Dispose();
                    pictureBoxMinimap.Image = result.MinimapImage;
                    textBox1.AppendText("小地圖快照載入成功。\r\n");
                }
                else
                {
                    textBox1.AppendText("小地圖載入操作已取消或失敗。\r\n");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                textBox1.AppendText($"錯誤: {ex.Message}\r\n");
            }
            finally
            {
                tabControl1.Enabled = true;
            }
        }


        /// <summary>
        /// 更新放大鏡的顯示內容。
        /// </summary>
        private void UpdateZoomBox(Point mouseLocation)
        {
            // ★ 此處邏輯不變，直接讀取 numericUpDownZoom 的值即可
            // 因為它的值已經和設定檔同步
            decimal zoomFactor = numericUpDownZoom.Value;

            if (pictureBoxMinimap.Image == null || zoomFactor <= 0)
            {
                pictureBoxZoom.Visible = false;
                return;
            }

            pictureBoxZoom.Visible = true;
            Point? originalImagePoint = PointToImage(pictureBoxMinimap, mouseLocation);
            if (!originalImagePoint.HasValue) return;

            int zoomWidth = (int)(pictureBoxZoom.Width / zoomFactor);
            int zoomHeight = (int)(pictureBoxZoom.Height / zoomFactor);

            var cropRect = new Rectangle(
                originalImagePoint.Value.X - zoomWidth / 2,
                originalImagePoint.Value.Y - zoomHeight / 2,
                zoomWidth,
                zoomHeight);

            var zoomedBitmap = new Bitmap(pictureBoxZoom.Width, pictureBoxZoom.Height);
            using (var g = Graphics.FromImage(zoomedBitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(pictureBoxMinimap.Image, new Rectangle(0, 0, zoomedBitmap.Width, zoomedBitmap.Height), cropRect, GraphicsUnit.Pixel);
            }

            pictureBoxZoom.Image?.Dispose();
            pictureBoxZoom.Image = zoomedBitmap;
        }

        private void pictureBoxZoom_Paint(object sender, PaintEventArgs e)
        {
            if (pictureBoxZoom.Image == null) return;
            var g = e.Graphics;
            int w = pictureBoxZoom.ClientSize.Width;
            int h = pictureBoxZoom.ClientSize.Height;
            using (var pen = new Pen(Color.Red, 1))
            {
                g.DrawLine(pen, w / 2, 0, w / 2, h);
                g.DrawLine(pen, 0, h / 2, w, h / 2);
            }
        }

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxZoom.Visible = false;
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateZoomBox(e.Location);
        }

        private void pictureBoxMinimap_MouseClick(object sender, MouseEventArgs e)
        {
            Point? originalImagePoint = PointToImage(pictureBoxMinimap, e.Location);
            if (!originalImagePoint.HasValue) return;
            textBox1.AppendText($"📍 已在原始圖片座標 ({originalImagePoint.Value.X}, {originalImagePoint.Value.Y}) 新增路徑點。\r\n");
            using (var g = pictureBoxMinimap.CreateGraphics())
            {
                Point displayPoint = ImageToPoint(pictureBoxMinimap, originalImagePoint.Value);
                g.FillEllipse(Brushes.Aqua, displayPoint.X - 4, displayPoint.Y - 4, 8, 8);
            }
        }

        private Point? PointToImage(PictureBox pb, Point point)
        {
            if (pb.Image == null) return null;
            var clientSize = pb.ClientSize;
            var imageSize = pb.Image.Size;
            float ratioX = (float)clientSize.Width / imageSize.Width;
            float ratioY = (float)clientSize.Height / imageSize.Height;
            float ratio = Math.Min(ratioX, ratioY);
            int displayWidth = (int)(imageSize.Width * ratio);
            int displayHeight = (int)(imageSize.Height * ratio);
            int offsetX = (clientSize.Width - displayWidth) / 2;
            int offsetY = (clientSize.Height - displayHeight) / 2;
            var displayRect = new Rectangle(offsetX, offsetY, displayWidth, displayHeight);
            if (!displayRect.Contains(point)) return null;
            float imageX = point.X - offsetX;
            float imageY = point.Y - offsetY;
            float originalX = imageX / ratio;
            float originalY = imageY / ratio;
            return new Point((int)originalX, (int)originalY);
        }

        private Point ImageToPoint(PictureBox pb, Point imagePoint)
        {
            if (pb.Image == null) return Point.Empty;
            var clientSize = pb.ClientSize;
            var imageSize = pb.Image.Size;
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
    }
}
