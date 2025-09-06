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

            // 啟用視覺樣式
            Application.EnableVisualStyles();

            // 設定文字轉譯相容性
            Application.SetCompatibleTextRenderingDefault(false);

            // 執行主表單
            Application.Run(new MainForm());
        }
    }
}
