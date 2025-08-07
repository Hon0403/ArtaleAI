using System;
using System.IO;
using System.Linq;

namespace ArtaleAI.Utils
{
    /// <summary>
    /// 專案路徑工具類 - 統一管理所有路徑相關邏輯
    /// </summary>
    public static class PathUtils
    {
        private static string? _projectRoot;

        /// <summary>
        /// 獲取專案根目錄
        /// </summary>
        public static string GetProjectRootDirectory()
        {
            if (_projectRoot != null) return _projectRoot;

            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            var projectDir = currentDir;

            // 向上查找包含 .csproj 檔案的目錄
            while (projectDir != null && !Directory.GetFiles(projectDir, "*.csproj").Any())
            {
                projectDir = Directory.GetParent(projectDir)?.FullName;
            }

            if (projectDir == null)
                throw new DirectoryNotFoundException("找不到專案根目錄");

            _projectRoot = projectDir;
            return _projectRoot;
        }

        /// <summary>
        /// 獲取配置檔案目錄
        /// </summary>
        public static string GetConfigDirectory() =>
            Path.Combine(GetProjectRootDirectory(), "Config");

        /// <summary>
        /// 獲取地圖資料目錄
        /// </summary>
        public static string GetMapDataDirectory() =>
            Path.Combine(GetProjectRootDirectory(), "MapData");

        /// <summary>
        /// 獲取模板根目錄
        /// </summary>
        public static string GetTemplatesDirectory() =>
            Path.Combine(GetProjectRootDirectory(), "Templates");

        /// <summary>
        /// 獲取怪物模板目錄
        /// </summary>
        public static string GetMonstersDirectory() =>
            Path.Combine(GetTemplatesDirectory(), "Monsters");

        /// <summary>
        /// 獲取配置檔案完整路徑
        /// </summary>
        public static string GetConfigFilePath() =>
            Path.Combine(GetConfigDirectory(), "config.yaml");
    }
}
