using System.Windows.Forms;

namespace ArtaleAI.Utils
{
    /// <summary>以 <see cref="Application.StartupPath"/> 為根的路徑常數。</summary>
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
