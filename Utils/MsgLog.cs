using System;
using System.Windows.Forms;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 訊息記錄器 - 處理狀態訊息和錯誤訊息的 UI 顯示
    /// </summary>
    public static class MsgLog
    {
        /// <summary>
        /// 安全顯示狀態訊息到指定的文字框
        /// </summary>
        public static void ShowStatus(TextBox textBox, string message)
        {
            SafeUpdateTextBox(textBox, $"{DateTime.Now:HH:mm:ss} - {message}", false);
        }

        /// <summary>
        /// 安全顯示錯誤訊息到指定的文字框並彈出對話框
        /// </summary>
        public static void ShowError(TextBox textBox, string errorMessage)
        {
            SafeUpdateTextBox(textBox, $"{DateTime.Now:HH:mm:ss} - ❌ {errorMessage}", true);

            // 在主執行緒中顯示 MessageBox
            try
            {
                if (textBox?.InvokeRequired == true)
                {
                    textBox.BeginInvoke(new Action(() =>
                        MessageBox.Show(errorMessage, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
                else
                {
                    MessageBox.Show(errorMessage, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing MessageBox: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全的文字框更新方法
        /// </summary>
        private static void SafeUpdateTextBox(TextBox textBox, string text, bool isError)
        {
            if (textBox == null)
            {
                System.Diagnostics.Debug.WriteLine($"[NULL TextBox] {text}");
                return;
            }

            try
            {
                if (textBox.IsDisposed)
                {
                    System.Diagnostics.Debug.WriteLine($"[DISPOSED TextBox] {text}");
                    return;
                }

                if (textBox.InvokeRequired)
                {
                    textBox.BeginInvoke(new Action(() => SafeUpdateTextBox(textBox, text, isError)));
                    return;
                }

                // 實際更新文字框
                textBox.AppendText(text + "\r\n");
                textBox.ScrollToCaret();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TextBox update error: {ex.Message} | Text: {text}");
            }
        }
    }

}
