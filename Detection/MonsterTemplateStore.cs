using ArtaleAI.Models;
using ArtaleAI.Utils;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 怪物模板存儲管理 - 純邏輯，無UI依賴
    /// </summary>
    public static class MonsterTemplateStore
    {
        private static readonly Dictionary<string, Dictionary<MonsterDetectionMode, List<Mat>>> _matTemplateCache = new();
        private static readonly Dictionary<string, List<Mat>> _matCache = new();

        /// <summary>
        /// 獲取可用的怪物名稱列表
        /// </summary>
        public static List<string> GetAvailableMonsterNames(string monstersDirectory)
        {
            if (!Directory.Exists(monstersDirectory)) return new List<string>();
            return Directory.GetDirectories(monstersDirectory)
                           .Select(folder => new DirectoryInfo(folder).Name)
                           .ToList();
        }

        /// <summary>
        /// 模板驗證工具
        /// </summary>
        public static bool IsValidTemplate(Bitmap template, string name = "")
        {
            if (template == null)
            {
                Console.WriteLine($"❌ 模板無效: {name} - 為 null");
                return false;
            }

            if (template.Width < 5 || template.Height < 5)
            {
                Console.WriteLine($"❌ 模板太小: {name} - {template.Width}x{template.Height}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 載入指定怪物的 Mat 模板 (一次性轉換)
        /// </summary>
        public static async Task<List<Mat>> LoadMonsterMatTemplatesAsync(
            string monsterName,
            string monstersDirectory,
            Action<string> statusReporter)
        {
            try
            {
                // 🎯 檢查快取
                if (_matCache.TryGetValue(monsterName, out var cachedMats))
                {
                    statusReporter($"從快取載入 {monsterName} Mat模板");
                    return cachedMats;
                }

                string monsterFolderPath = Path.Combine(monstersDirectory, monsterName);
                if (!Directory.Exists(monsterFolderPath))
                {
                    statusReporter($"找不到怪物資料夾: {monsterFolderPath}");
                    return new List<Mat>();
                }

                statusReporter($"正在載入 '{monsterName}' Mat模板...");
                var templateFiles = await Task.Run(() => Directory.GetFiles(monsterFolderPath, "*.png"));

                if (!templateFiles.Any())
                {
                    statusReporter($"在 '{monsterName}' 資料夾中未找到PNG檔案");
                    return new List<Mat>();
                }

                var loadedMatTemplates = new List<Mat>();
                foreach (var file in templateFiles)
                {
                    try
                    {
                        // 🎯 PNG → Bitmap → Mat (一次性轉換)
                        using var tempBitmap = new Bitmap(file);
                        if (IsValidTemplate(tempBitmap, Path.GetFileName(file)))
                        {
                            var templateMat = OpenCvProcessor.BitmapToThreeChannelMat(tempBitmap);
                            if (templateMat != null && !templateMat.Empty())
                            {
                                loadedMatTemplates.Add(templateMat.Clone());
                                templateMat.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        statusReporter($"載入模板檔案失敗: {Path.GetFileName(file)} - {ex.Message}");
                    }
                }

                // 🎯 快取 Mat 版本
                _matCache[monsterName] = loadedMatTemplates;
                statusReporter($"✅ 成功載入並快取 {loadedMatTemplates.Count} 個 '{monsterName}' Mat模板");

                return loadedMatTemplates;
            }
            catch (Exception ex)
            {
                statusReporter($"載入Mat模板時發生錯誤: {ex.Message}");
                return new List<Mat>();
            }
        }

        /// <summary>
        /// 清理所有快取
        /// </summary>
        public static void ClearAllMatCache()
        {
            foreach (var templates in _matCache.Values)
            {
                foreach (var mat in templates)
                    mat?.Dispose();
            }
            _matCache.Clear();
        }

    }
}