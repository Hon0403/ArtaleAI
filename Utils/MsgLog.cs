using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArtaleAI.Utils
{
    /// <summary>將訊息附加至 <see cref="TextBox"/> 並同步寫入 <see cref="Logger"/>。</summary>
    public static class MsgLog
    {
        public static void ShowStatus(TextBox? textBox, string message)
        {
            Log(textBox, message, Color.Black);
        }

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

                textBox.AppendText(formattedMsg);

                if (textBox.TextLength > 10000)
                {
                    textBox.Text = textBox.Text.Substring(5000);
                }

                textBox.SelectionStart = textBox.TextLength;
                textBox.ScrollToCaret();

                if (color == Color.Red)
                    Logger.Error(message);
                else
                    Logger.Info(message);
            }
            catch
            {
            }
        }
    }
}
