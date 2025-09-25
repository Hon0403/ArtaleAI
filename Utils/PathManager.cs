namespace ArtaleAI.Utils
{
    /// <summary>
    /// 路徑管理專用類別
    /// </summary>
    public static class PathManager
    {
        /// <summary>
        /// 配置檔目錄
        /// </summary>
        public static string ConfigDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

        /// <summary>
        /// 配置檔路徑 (config.yaml)
        /// </summary>
        public static string ConfigFilePath =>
            Path.Combine(ConfigDirectory, "config.yaml");

        /// <summary>
        /// 地圖資料目錄
        /// </summary>
        public static string MapDataDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MapData");

        /// <summary>
        /// 模板檔案目錄
        /// </summary>
        public static string TemplatesDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");

        /// <summary>
        /// 怪物模板目錄
        /// </summary>
        public static string MonstersDirectory =>
            Path.Combine(TemplatesDirectory, "Monsters");
    }
}
