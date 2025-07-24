using System;
using System.Windows.Forms;

namespace ArtaleAI
{
    static class Program
    {
        /// <summary>
        /// 應用程式的主要進入點。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 初始化 COM，確保正確的公寓模式
            System.Threading.Thread.CurrentThread.SetApartmentState(System.Threading.ApartmentState.STA);

            // 啟用視覺樣式
            Application.EnableVisualStyles();

            // 設定文字轉譯相容性
            Application.SetCompatibleTextRenderingDefault(false);

            // 執行主表單
            Application.Run(new Form1());
        }
    }
}
