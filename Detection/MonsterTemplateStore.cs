using ArtaleAI.Utils;

namespace ArtaleAI.Detection
{
    /// <summary>
    /// 怪物模板存儲管理 - 純邏輯，無UI依賴
    /// </summary>
    public static class MonsterTemplateStore
    {
        private static readonly Dictionary<string, List<Bitmap>> _cachedMonsterTemplates = new();

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
        /// 載入指定怪物的模板
        /// </summary>
        public static async Task<List<Bitmap>> LoadMonsterTemplatesAsync(string monsterName, string monstersDirectory, Action<string> statusReporter)
        {
            try
            {
                // 檢查快取
                if (_cachedMonsterTemplates.TryGetValue(monsterName, out var cachedTemplates))
                {
					statusReporter($"從快取載入 {monsterName} BGR模板");
                    return cachedTemplates.Select(t => new Bitmap(t)).ToList(); // 返回副本
                }

                string monsterFolderPath = Path.Combine(monstersDirectory, monsterName);
                if (!Directory.Exists(monsterFolderPath))
                {
                    statusReporter($"找不到怪物資料夾: {monsterFolderPath}");
                    return new List<Bitmap>();
                }

				statusReporter($"正在從 '{monsterName}' 載入BGR格式模板...");
                var templateFiles = await Task.Run(() => Directory.GetFiles(monsterFolderPath, "*.png"));

                if (!templateFiles.Any())
                {
                    statusReporter($"在 '{monsterName}' 資料夾中未找到任何PNG模板檔案");
                    return new List<Bitmap>();
                }

                var loadedTemplates = new List<Bitmap>();
                foreach (var file in templateFiles)
                {
                    try
                    {
                        // 🚀 直接載入 Bitmap，不做額外轉換
                        var templateBitmap = new Bitmap(file);
                        if (IsValidTemplate(templateBitmap, Path.GetFileName(file)))
                        {
                            loadedTemplates.Add(templateBitmap);
                            Console.WriteLine($"✅ 載入模板: {Path.GetFileName(file)}");
                        }
                        else
                        {
                            templateBitmap.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        statusReporter($"載入模板檔案失敗: {Path.GetFileName(file)} - {ex.Message}");
                    }
                }

                // 快取模板
                _cachedMonsterTemplates[monsterName] = loadedTemplates.Select(t => new Bitmap(t)).ToList();
                statusReporter($"✅ 成功載入 {loadedTemplates.Count} 個 '{monsterName}' BGR模板");

                return loadedTemplates;
            }
            catch (Exception ex)
            {
                statusReporter($"載入怪物模板時發生錯誤: {ex.Message}");
                return new List<Bitmap>();
            }
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
        /// 清理特定怪物的快取
        /// </summary>
        public static void ClearMonsterCache(string monsterName)
        {
            if (_cachedMonsterTemplates.TryGetValue(monsterName, out var templates))
            {
                CacheManager.SafeDispose(templates.ToArray());
                _cachedMonsterTemplates.Remove(monsterName);
            }
        }


    }
}
