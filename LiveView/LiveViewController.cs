using ArtaleAI.Config;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArtaleAI.LiveView
{
    /// <summary>
    /// 即時顯示控制器 - 負責協調UI和服務
    /// </summary>
    public class LiveViewController : ILiveViewEventHandler, IDisposable
    {
        private readonly LiveViewService _liveViewService;
        private readonly PictureBox _displayPictureBox;
        private readonly TextBox _statusTextBox;
        private readonly Control _parentControl; // 用於Invoke操作

        public bool IsRunning => _liveViewService.IsRunning;

        public LiveViewController(PictureBox displayPictureBox, TextBox statusTextBox, Control parentControl)
        {
            _displayPictureBox = displayPictureBox ?? throw new ArgumentNullException(nameof(displayPictureBox));
            _statusTextBox = statusTextBox ?? throw new ArgumentNullException(nameof(statusTextBox));
            _parentControl = parentControl ?? throw new ArgumentNullException(nameof(parentControl));

            _liveViewService = new LiveViewService(this);
        }

        /// <summary>
        /// 開始即時顯示
        /// </summary>
        public async Task StartAsync(AppConfig config)
        {
            // 清空顯示區域
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

        /// <summary>
        /// 停止即時顯示
        /// </summary>
        public async Task StopAsync()
        {
            await _liveViewService.StopAsync();
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
            catch (ObjectDisposedException)
            {
                // 控制項已被釋放，忽略
                frame?.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Invoke 失敗，忽略
                frame?.Dispose();
            }
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
            catch (ObjectDisposedException)
            {
                // 控制項已被釋放，忽略
            }
            catch (InvalidOperationException)
            {
                // Invoke 失敗，忽略
            }
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
            catch (ObjectDisposedException)
            {
                // 控制項已被釋放，忽略
            }
            catch (InvalidOperationException)
            {
                // Invoke 失敗，忽略
            }
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
