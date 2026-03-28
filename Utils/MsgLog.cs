using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// UI 訊息記錄器 - 提供統一的介面將訊息顯示在 WinForm 的 TextBox 中
    /// </summary>
    public static class MsgLog
    {
        /// <summary>
        /// 顯示一般狀態訊息
        /// </summary>
        public static void ShowStatus(TextBox? textBox, string message)
        {
            Log(textBox, message, Color.Black);
        }

        /// <summary>
        /// 顯示錯誤訊息 (紅色)
        /// </summary>
        public static void ShowError(TextBox? textBox, string message)
        {
            Log(textBox, $"[ERROR] {message}", Color.Red);
        }

        private static void Log(TextBox? textBox, string message, Color color)
        {
            if (textBox == null) return;

            try
            {
                if (textBox.InvokeRequired)
                {
                    textBox.Invoke(new Action(() => Log(textBox, message, color)));
                    return;
                }

                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string formattedMsg = $"[{timestamp}] {message}{Environment.NewLine}";

                // 簡單實作：直接附加文字
                textBox.AppendText(formattedMsg);
                
                // 限制長度
                if (textBox.TextLength > 10000)
                {
                    textBox.Text = textBox.Text.Substring(5000);
                }

                // 捲動到底部
                textBox.SelectionStart = textBox.TextLength;
                textBox.ScrollToCaret();
                
                // 記錄到系統日誌
                if (color == Color.Red)
                    Logger.Error(message);
                else
                    Logger.Info(message);
            }
            catch
            {
                // 忽略 UI 執行緒競爭導致的次要錯誤
            }
        }
    }
}
