using System.Windows.Forms;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 應用程式路徑管理工具
    /// 所有路徑以執行檔所在目錄（Application.StartupPath）為基準，
    /// 確保開發偵錯與正式部署時行為一致。
    /// </summary>
    public static class PathManager
    {
        /// <summary>地圖資料目錄（存放 .json 地圖檔案）</summary>
        public static string MapDataDirectory =>
            Path.Combine(Application.StartupPath, "MapData");

        /// <summary>怪物模板目錄（存放各怪物的圖片模板）</summary>
        public static string MonstersDirectory =>
            Path.Combine(Application.StartupPath, "Monsters");
    }
}
