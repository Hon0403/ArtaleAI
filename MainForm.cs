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

        // ✅ 新增：浮動放大鏡相關變數
        private Form? _zoomWindow;
        private PictureBox? _floatingZoomBox;

        public MainForm()
        {
            InitializeComponent();
            LoadConfiguration();
            tabControl1.SelectedIndexChanged += TabControl1_SelectedIndexChanged;
            numericUpDownZoom.ValueChanged += numericUpDownZoom_ValueChanged;

            InitializeFloatingMagnifier();
        }

        /// <summary>
        /// 初始化浮動放大鏡視窗
        /// </summary>
        private void InitializeFloatingMagnifier()
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
        /// 繪製浮動放大鏡的十字準線
        /// </summary>
        private void FloatingZoomBox_Paint(object sender, PaintEventArgs e)
        {
            if (_floatingZoomBox?.Image == null) return;

            var g = e.Graphics;
            int w = _floatingZoomBox.Width;
            int h = _floatingZoomBox.Height;

            using (var pen = new Pen(Color.Red, 2))
            {
                g.DrawLine(pen, w / 2, 0, w / 2, h);
                g.DrawLine(pen, 0, h / 2, w, h / 2);
            }

            using (var borderPen = new Pen(Color.Black, 1))
            {
                g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);
            }
        }

        /// <summary>
        /// 更新跟隨滑鼠的放大鏡
        /// </summary>
        private void UpdateFloatingMagnifier(Point mouseLocation)
        {
            if (_zoomWindow == null || _floatingZoomBox == null) return;

            decimal zoomFactor = numericUpDownZoom.Value;

            if (pictureBoxMinimap.Image == null || zoomFactor <= 0)
            {
                _zoomWindow.Visible = false;
                return;
            }

            var zoomedImage = _editorMinimap.CreateZoomImage(
                (Bitmap)pictureBoxMinimap.Image,
                mouseLocation,
                pictureBoxMinimap,
                _floatingZoomBox.Size,
                zoomFactor);

            if (zoomedImage != null)
            {
                _floatingZoomBox.Image?.Dispose();
                _floatingZoomBox.Image = zoomedImage;

                var screenPoint = pictureBoxMinimap.PointToScreen(mouseLocation);
                var magnifierPosition = new Point(
                    screenPoint.X + 20,
                    screenPoint.Y + 20
                );

                var screen = Screen.FromPoint(screenPoint);
                if (magnifierPosition.X + _zoomWindow.Width > screen.WorkingArea.Right)
                    magnifierPosition.X = screenPoint.X - _zoomWindow.Width - 20;
                if (magnifierPosition.Y + _zoomWindow.Height > screen.WorkingArea.Bottom)
                    magnifierPosition.Y = screenPoint.Y - _zoomWindow.Height - 20;

                _zoomWindow.Location = magnifierPosition;
                _zoomWindow.Visible = true;
            }
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
                try
                {
                    numericUpDownZoom.Value = _config.General.ZoomFactor;
                }
                catch (ArgumentOutOfRangeException)
                {
                    _config.General.ZoomFactor = numericUpDownZoom.Value;
                    ConfigSaver.SaveConfig(_config);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"讀取設定檔失敗: {ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _config = new AppConfig();
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
                ConfigSaver.SaveConfig(_config);
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

                var result = await _editorMinimap.LoadSnapshotAsync(this.Handle, _config, reporter);

                if (result?.MinimapImage != null && result.CaptureItem != null)
                {
                    pictureBoxMinimap.Image?.Dispose();
                    pictureBoxMinimap.Image = result.MinimapImage;
                    textBox1.AppendText("✅ 小地圖快照載入成功。\r\n");
                }
                else
                {
                    textBox1.AppendText("⚠️ 小地圖載入操作已取消或失敗。\r\n");
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

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            _zoomWindow?.Hide();  // 隱藏浮動放大鏡
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            UpdateFloatingMagnifier(e.Location);  // 使用新的浮動放大鏡
        }

        private void pictureBoxMinimap_MouseClick(object sender, MouseEventArgs e)
        {
            // 使用 EditorMinimap 的座標轉換方法
            Point? originalImagePoint = _editorMinimap.ConvertToImageCoordinates(pictureBoxMinimap, e.Location);
            if (!originalImagePoint.HasValue) return;

            textBox1.AppendText($"📍 已在原始圖片座標 ({originalImagePoint.Value.X}, {originalImagePoint.Value.Y}) 新增路徑點。\r\n");

            using (var g = pictureBoxMinimap.CreateGraphics())
            {
                Point displayPoint = _editorMinimap.ConvertToDisplayCoordinates(pictureBoxMinimap, originalImagePoint.Value);
                g.FillEllipse(Brushes.Aqua, displayPoint.X - 4, displayPoint.Y - 4, 8, 8);
            }
        }

        /// <summary>
        /// 資源清理
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _zoomWindow?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
