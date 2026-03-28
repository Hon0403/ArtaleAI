using ArtaleAI.API.Config;
using ArtaleAI.API.Models;
using ArtaleAI.Utils;
using Newtonsoft.Json;
using OpenCvSharp;
using System.IO.Compression;


namespace ArtaleAI.API
{
    /// <summary>從 MapleStory.io 拉 ZIP、解壓並可選將透明底轉為指定 BGR。</summary>
    public class MonsterImageFetcher : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly HttpClient _httpClient;
        private readonly MonsterDownloadSettings _downloadSettings;
        private readonly ImageProcessingSettings _imageSettings;
        private bool _disposed = false;

        public MonsterImageFetcher(MainForm eventHandler)
        {
            _mainForm = eventHandler;
            _httpClient = new HttpClient();

            _downloadSettings = ApiConfig.MonsterDownload;
            _imageSettings = ApiConfig.ImageProcessing;

            _httpClient.Timeout = TimeSpan.FromSeconds(_downloadSettings.TimeoutSeconds);
        }

        public MonsterImageFetcher(
            MainForm eventHandler,
            ImageProcessingSettings imageSettings,
            MonsterDownloadSettings downloadSettings)
        {
            _mainForm = eventHandler;
            _imageSettings = imageSettings;
            _downloadSettings = downloadSettings;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(_downloadSettings.TimeoutSeconds)
            };
        }

        public async Task<DownloadResult> DownloadMonsterAsync(string monsterName)
        {
            var result = new DownloadResult
            {
                MonsterName = monsterName,
                ProcessedFiles = new List<string>()
            };

            var startTime = DateTime.Now;

            try
            {
                Logger.Info($"開始下載怪物模板: {monsterName}");

                var mobs = await GetAllMobsAsync();
                if (mobs == null)
                {
                    result.ErrorMessage = "無法獲取怪物資料庫";
                    return result;
                }

                var mobId = FindMobId(mobs, monsterName);
                if (!mobId.HasValue)
                {
                    result.ErrorMessage = $"找不到名為 '{monsterName}' 的怪物";
                    return result;
                }

                var processedCount = await SaveMobAsync(mobId.Value, monsterName);

                result.Success = true;
                result.DownloadedCount = processedCount;
                result.DownloadDuration = DateTime.Now - startTime;

                Logger.Info($" 成功下載 {processedCount} 個 '{monsterName}' 模板");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.DownloadDuration = DateTime.Now - startTime;
                Logger.Error($"下載怪物模板失敗: {ex.Message}");
                return result;
            }
        }

        public async Task<DownloadResult> DownloadMonsterAsync(string monsterName, int monsterId)
        {
            var result = new DownloadResult
            {
                MonsterName = monsterName,
                ProcessedFiles = new List<string>()
            };

            var startTime = DateTime.Now;

            try
            {
                Logger.Info($"開始下載怪物模板: {monsterName} (ID: {monsterId})");

                var processedCount = await SaveMobAsync(monsterId, monsterName);

                result.Success = true;
                result.DownloadedCount = processedCount;
                result.DownloadDuration = DateTime.Now - startTime;

                Logger.Info($" 成功下載 {processedCount} 個 '{monsterName}' 模板");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.DownloadDuration = DateTime.Now - startTime;
                Logger.Error($"下載怪物模板失敗: {ex.Message}");
                return result;
            }
        }

        private async Task<List<ArtaleMonster>?> GetAllMobsAsync()
        {
            string url = $"{_downloadSettings.BaseUrl}/api/{_downloadSettings.DefaultRegion}/{_downloadSettings.DefaultVersion}/mob";
            Logger.Info($"正在從以下網址獲取怪物資料: {url}");

            for (int attempt = 1; attempt <= _downloadSettings.MaxRetryAttempts; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string jsonContent = await response.Content.ReadAsStringAsync();
                    var mobs = JsonConvert.DeserializeObject<List<ArtaleMonster>>(jsonContent);
                    if (mobs == null)
                    {
                        Logger.Info($"反序列化怪物清單為 null (嘗試 {attempt}/{_downloadSettings.MaxRetryAttempts})");
                        if (attempt < _downloadSettings.MaxRetryAttempts)
                            await Task.Delay(1000 * attempt);
                        continue;
                    }

                    Logger.Info($"成功獲取 {mobs.Count} 個怪物資料");
                    return mobs;
                }
                catch (HttpRequestException ex)
                {
                    Logger.Info($"HTTP錯誤 (嘗試 {attempt}/{_downloadSettings.MaxRetryAttempts}): {ex.Message}");
                    if (attempt == _downloadSettings.MaxRetryAttempts) throw;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    Logger.Info($"請求超時 (嘗試 {attempt}/{_downloadSettings.MaxRetryAttempts})");
                    if (attempt == _downloadSettings.MaxRetryAttempts) throw;
                }

                if (attempt < _downloadSettings.MaxRetryAttempts)
                {
                    await Task.Delay(1000 * attempt);
                }
            }

            return null;
        }

        private int? FindMobId(List<ArtaleMonster> allMobs, string mobName)
        {
            if (allMobs == null) return null;

            string mobNameLower = mobName.ToLower();
            var mob = allMobs.FirstOrDefault(m => m.Name.ToLower() == mobNameLower);
            return mob?.Id;
        }

        private async Task<int> SaveMobAsync(int mobId, string mobName)
        {
            string monstersDirectory = PathManager.MonstersDirectory;
            string mobFileName = string.Join("_", mobName.ToLower().Split(' '));
            string outputDir = Path.Combine(monstersDirectory, mobFileName);
            Directory.CreateDirectory(outputDir);

            string downloadUrl = $"{_downloadSettings.BaseUrl}/api/{_downloadSettings.DefaultRegion}/{_downloadSettings.DefaultVersion}/mob/{mobId}/download";
            Logger.Info($"正在下載怪物圖片包: {downloadUrl}");

            var response = await _httpClient.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();
            byte[] zipBytes = await response.Content.ReadAsByteArrayAsync();

            int processedCount = 0;

            using (var zipStream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                int index = 1;
                foreach (var entry in archive.Entries)
                {
                    if (_downloadSettings.SkipDeathAnimations &&
                        entry.Name.ToLower().Contains("die1"))
                        continue;

                    string extension = Path.GetExtension(entry.Name).ToLower().TrimStart('.');
                    if (!_downloadSettings.SupportedImageFormats.Contains(extension))
                        continue;

                    try
                    {
                        using (var entryStream = entry.Open())
                        using (var memoryStream = new MemoryStream())
                        {
                            await entryStream.CopyToAsync(memoryStream);
                            byte[] imageBytes = memoryStream.ToArray();

                            using (var processedImage = ProcessImageWithOpenCvSharp(imageBytes, entry.Name))
                            {
                                if (processedImage != null && !processedImage.Empty())
                                {
                                    string fileName = $"{mobFileName}_{index}.png";
                                    string savePath = Path.Combine(outputDir, fileName);

                                    Cv2.ImWrite(savePath, processedImage);
                                    Logger.Info($"已儲存: {fileName}");
                                    processedCount++;
                                    index++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"處理圖片 {entry.Name} 時發生錯誤: {ex.Message}");
                    }
                }
            }

            return processedCount;
        }

        private Mat? ProcessImageWithOpenCvSharp(byte[] imageBytes, string fileName)
        {
            try
            {
                var image = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
                if (image.Empty())
                {
                    Logger.Info($"無法解碼圖片: {fileName}");
                    return null;
                }

                if (image.Channels() == 4 && _imageSettings.ConvertTransparentPixels)
                {
                    Mat[]? channels = null;
                    Mat? alphaChannel = null;
                    Mat? transparentMask = null;

                    try
                    {
                        channels = Cv2.Split(image);
                        alphaChannel = channels[3];

                        transparentMask = new Mat();
                        Cv2.Threshold(alphaChannel, transparentMask, 0, 255, ThresholdTypes.Binary);
                        Cv2.BitwiseNot(transparentMask, transparentMask);

                        var colorValues = _imageSettings.ReplacementColorRgb.Split(',')
                            .Select(s => byte.Parse(s.Trim())).ToArray();

                        if (colorValues.Length >= 3)
                        {
                            var backgroundColor = new Scalar(colorValues[2], colorValues[1], colorValues[0]);
                            image.SetTo(backgroundColor, transparentMask);
                        }

                        if (!_imageSettings.PreserveAlphaChannel)
                        {
                            var bgrImage = new Mat();
                            Cv2.CvtColor(image, bgrImage, ColorConversionCodes.BGRA2BGR);
                            image.Dispose();
                            image = bgrImage;
                        }
                    }
                    finally
                    {
                        if (channels != null)
                        {
                            foreach (var channel in channels)
                            {
                                channel?.Dispose();
                            }
                        }
                        alphaChannel?.Dispose();
                        transparentMask?.Dispose();
                    }
                }

                return image;
            }
            catch (Exception ex)
            {
                Logger.Info($"圖片處理錯誤 {fileName}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
