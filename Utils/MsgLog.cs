using System;
using System.Windows.Forms;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 訊息記錄器 - 處理狀態訊息和錯誤訊息的 UI 顯示
    /// </summary>
    public static class MsgLog
    {
        #region 統一狀態訊息處理

        /// <summary>
        /// 顯示狀態訊息到指定的文字框
        /// </summary>
        /// <param name="textBox">目標文字框</param>  // ✅ 新增參數
        /// <param name="message">訊息內容</param>
        public static void ShowStatus(TextBox textBox, string message)
        {
            if (textBox.InvokeRequired)
            {
                textBox.Invoke(new Action<TextBox, string>(ShowStatus), textBox, message);
                return;
            }

            textBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\r\n");
            textBox.ScrollToCaret();
        }

        /// <summary>
        /// 顯示錯誤訊息到指定的文字框並彈出對話框
        /// </summary>
        /// <param name="textBox">目標文字框</param>  // ✅ 新增參數
        /// <param name="errorMessage">錯誤訊息內容</param>
        public static void ShowError(TextBox textBox, string errorMessage) 
        {
            if (textBox.InvokeRequired)
            {
                textBox.Invoke(new Action<TextBox, string>(ShowError), textBox, errorMessage);
                return;
            }

            textBox.AppendText($"{DateTime.Now:HH:mm:ss} - ❌ {errorMessage}\r\n");
            textBox.ScrollToCaret();
            MessageBox.Show(errorMessage, "發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion
    }
}
