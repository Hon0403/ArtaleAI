using ArtaleAI.Config;
using ArtaleAI.GameWindow; // 確保引入
using ArtaleAI.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArtaleAI.GameCapture
{
    /// <summary>
    /// 即時顯示控制器 - 負責協調UI和服務
    /// </summary>
    public class LiveViewController : ILiveViewEventHandler, IDisposable
    {
        private readonly LiveViewService _liveViewService;
        private readonly PictureBox _displayPictureBox;
        private readonly TextBox _statusTextBox;
        private readonly Control _parentControl;
        public GraphicsCapturer? Capturer => _liveViewService.Capturer;

        public bool IsRunning => _liveViewService.IsRunning;

        // 維持您原本的建構函式不變
        public LiveViewController(PictureBox displayPictureBox, TextBox statusTextBox, Control parentControl)
        {
            _displayPictureBox = displayPictureBox ?? throw new ArgumentNullException(nameof(displayPictureBox));
            _statusTextBox = statusTextBox ?? throw new ArgumentNullException(nameof(statusTextBox));
            _parentControl = parentControl ?? throw new ArgumentNullException(nameof(parentControl));

            _liveViewService = new LiveViewService(this);
        }

        public async Task StartAsync(AppConfig config)
        {
            if (_displayPictureBox.InvokeRequired)
            {
                _displayPictureBox.Invoke(new Action(() => _displayPictureBox.Image = null));
            }
            else
            {
                _displayPictureBox.Image = null;
            }
            await _liveViewService.StartAsync(config);
        }

        public async Task StopAsync()
        {
            await _liveViewService.StopAsync();
        }

        // 用於在畫面上繪製怪物的位置
        public void DrawMonsterRectangles(List<Point> monsterLocations, Size monsterSize)
        {
            if (_displayPictureBox.Image == null || !_parentControl.Visible) return;

            // 使用 Invoke 確保在 UI 執行緒上操作
            _parentControl.Invoke(new Action(() =>
            {
                if (_displayPictureBox.Image == null) return;

                using (var graphics = Graphics.FromImage(_displayPictureBox.Image))
                {
                    using (var pen = new Pen(Color.Red, 2))
                    {
                        foreach (var loc in monsterLocations)
                        {
                            graphics.DrawRectangle(pen, loc.X, loc.Y, monsterSize.Width, monsterSize.Height);
                        }
                    }
                }
                // 刷新 PictureBox 以顯示新的繪圖
                _displayPictureBox.Invalidate();
            }));
        }

        #region ILiveViewEventHandler 實作
        public void OnFrameAvailable(Bitmap frame)
        {
            try
            {
                if (_displayPictureBox.InvokeRequired)
                {
                    _displayPictureBox.Invoke(new Action<Bitmap>(UpdateDisplay), frame);
                }
                else
                {
                    UpdateDisplay(frame);
                }
            }
            catch (Exception) { frame?.Dispose(); } // 忽略錯誤並釋放 frame
        }

        public void OnStatusMessage(string message)
        {
            try
            {
                if (_statusTextBox.InvokeRequired)
                {
                    _statusTextBox.Invoke(new Action<string>(AppendStatusMessage), message);
                }
                else
                {
                    AppendStatusMessage(message);
                }
            }
            catch (Exception) { } // 忽略錯誤
        }

        public void OnError(string errorMessage)
        {
            try
            {
                if (_parentControl.InvokeRequired)
                {
                    _parentControl.Invoke(new Action<string>(ShowErrorMessage), errorMessage);
                }
                else
                {
                    ShowErrorMessage(errorMessage);
                }
            }
            catch (Exception) { } // 忽略錯誤
        }
        #endregion

        #region 私有方法
        private void UpdateDisplay(Bitmap frame)
        {
            if (_displayPictureBox.IsDisposed)
            {
                frame?.Dispose();
                return;
            }
            var oldFrame = _displayPictureBox.Image;
            _displayPictureBox.Image = frame;
            oldFrame?.Dispose();
        }

        private void AppendStatusMessage(string message)
        {
            if (_statusTextBox.IsDisposed) return;
            _statusTextBox.AppendText(message + "\r\n");
            _statusTextBox.ScrollToCaret();
        }

        private void ShowErrorMessage(string errorMessage)
        {
            AppendStatusMessage($"❌ {errorMessage}");
            MessageBox.Show(errorMessage, "即時顯示發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        #endregion

        public void Dispose()
        {
            _liveViewService?.Dispose();
        }
    }
}
