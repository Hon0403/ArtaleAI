using System;
using System.IO;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 專案路徑工具類 - 簡化版本，直接使用執行目錄
    /// </summary>
    public static class PathUtils
    {
        /// <summary>
        /// 獲取執行目錄
        /// </summary>
        public static string GetProjectRootDirectory() =>
            AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// 獲取配置檔案目錄
        /// </summary>
        public static string GetConfigDirectory() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

        /// <summary>
        /// 獲取地圖資料目錄
        /// </summary>
        public static string GetMapDataDirectory() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MapData");

        /// <summary>
        /// 獲取模板根目錄
        /// </summary>
        public static string GetTemplatesDirectory() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");

        /// <summary>
        /// 獲取怪物模板目錄
        /// </summary>
        public static string GetMonstersDirectory() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", "Monsters");

        /// <summary>
        /// 獲取配置檔案完整路徑
        /// </summary>
        public static string GetConfigFilePath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "config.yaml");
    }
}
