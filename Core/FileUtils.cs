using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace ArtaleAI.Core
{
    /// <summary>
    /// 檔案操作工具類 - 統一管理所有檔案相關操作
    /// </summary>
    public static class FileUtils
    {
        /// <summary>
        /// 安全地讀取文字檔案
        /// </summary>
        public static string ReadTextFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"找不到檔案: {filePath}");

            return File.ReadAllText(filePath);
        }

        /// <summary>
        /// 安全地寫入文字檔案
        /// </summary>
        public static void WriteTextFile(string filePath, string content)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, content);
        }

        /// <summary>
        /// 獲取目錄中的所有指定擴展名檔案
        /// </summary>
        public static string[] GetFilesByExtension(string directoryPath, string extension)
        {
            if (!Directory.Exists(directoryPath))
                return Array.Empty<string>();

            return Directory.GetFiles(directoryPath, $"*.{extension.TrimStart('.')}");
        }

        /// <summary>
        /// 安全地刪除檔案
        /// </summary>
        public static bool TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch (Exception)
            {
                // 忽略刪除錯誤
            }
            return false;
        }

        /// <summary>
        /// 確保目錄存在，不存在則建立
        /// </summary>
        public static void EnsureDirectoryExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        /// <summary>
        /// 複製檔案到指定目錄
        /// </summary>
        public static void CopyFileToDirectory(string sourceFile, string targetDirectory, bool overwrite = false)
        {
            EnsureDirectoryExists(targetDirectory);
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, targetFile, overwrite);
        }
    }
}
