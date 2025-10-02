using ArtaleAI.API.Models;
using ArtaleAI.Utils;
using Newtonsoft.Json;
using OpenCvSharp;
using System.IO.Compression;


namespace ArtaleAI.API
{
    /// <summary>
    /// 怪物模板下載器 - 從MapleStory.io API下載並處理怪物圖片 (OpenCvSharp 版本)
    /// </summary>
    public class MonsterImageFetcher : IDisposable
    {
        private readonly MainForm _mainForm;
        private readonly HttpClient _httpClient;
        private readonly MonsterDownloadSettings _downloadSettings;
        private readonly ImageProcessingSettings _imageSettings;
        private bool _disposed = false;

        public MonsterImageFetcher(MainForm eventHandler)
        {
            _mainForm = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
            _httpClient = new HttpClient();

            var config = (eventHandler as MainForm)?._configManager?.CurrentConfig;

            if (config?.MonsterDownload == null)
                throw new InvalidOperationException("MonsterDownload 設定未載入，請檢查 config.yaml");
            if (config?.ImageProcessing == null)
                throw new InvalidOperationException("ImageProcessing 設定未載入，請檢查 config.yaml");

            _downloadSettings = config.MonsterDownload;
            _imageSettings = config.ImageProcessing;

            _httpClient.Timeout = TimeSpan.FromSeconds(_downloadSettings.TimeoutSeconds);
        }

        public MonsterImageFetcher(
            MainForm eventHandler,
            ImageProcessingSettings imageSettings,
            MonsterDownloadSettings downloadSettings)
        {
            _mainForm = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
            _imageSettings = imageSettings ?? throw new ArgumentNullException(nameof(imageSettings));
            _downloadSettings = downloadSettings ?? throw new ArgumentNullException(nameof(downloadSettings));

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(_downloadSettings.TimeoutSeconds)
            };
        }

        /// <summary>
        /// 下載指定怪物的模板 - 支援兩種參數格式
        /// </summary>
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
                MsgLog.ShowStatus(_mainForm.textBox1,$"開始下載怪物模板: {monsterName}");

                // 1. 獲取所有怪物資料
                var mobs = await GetAllMobsAsync();
                if (mobs == null)
                {
                    result.ErrorMessage = "無法獲取怪物資料庫";
                    return result;
                }

                // 2. 查找怪物ID
                var mobId = FindMobId(mobs, monsterName);
                if (!mobId.HasValue)
                {
                    result.ErrorMessage = $"找不到名為 '{monsterName}' 的怪物";
                    return result;
                }

                // 3. 下載並處理怪物圖片
                var processedCount = await SaveMobAsync(mobId.Value, monsterName);

                result.Success = true;
                result.DownloadedCount = processedCount;
                result.DownloadDuration = DateTime.Now - startTime;

                MsgLog.ShowStatus(_mainForm.textBox1,$" 成功下載 {processedCount} 個 '{monsterName}' 模板");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.DownloadDuration = DateTime.Now - startTime;
                MsgLog.ShowError(_mainForm.textBox1,$"下載怪物模板失敗: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// 支援兩參數的下載方法（為了向後相容）
        /// </summary>
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
                MsgLog.ShowStatus(_mainForm.textBox1,$"開始下載怪物模板: {monsterName} (ID: {monsterId})");

                // 直接使用提供的 ID 下載
                var processedCount = await SaveMobAsync(monsterId, monsterName);

                result.Success = true;
                result.DownloadedCount = processedCount;
                result.DownloadDuration = DateTime.Now - startTime;

                MsgLog.ShowStatus(_mainForm.textBox1,$" 成功下載 {processedCount} 個 '{monsterName}' 模板");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.DownloadDuration = DateTime.Now - startTime;
                MsgLog.ShowError(_mainForm.textBox1,$"下載怪物模板失敗: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// 從MapleStory API獲取所有怪物資料
        /// </summary>
        private async Task<List<ArtaleMonster>?> GetAllMobsAsync()
        {
            string url = $"{_downloadSettings.BaseUrl}/api/{_downloadSettings.DefaultRegion}/{_downloadSettings.DefaultVersion}/mob";
            MsgLog.ShowStatus(_mainForm.textBox1,$"正在從以下網址獲取怪物資料: {url}");

            for (int attempt = 1; attempt <= _downloadSettings.MaxRetryAttempts; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string jsonContent = await response.Content.ReadAsStringAsync();
                    var mobs = JsonConvert.DeserializeObject<List<ArtaleMonster>>(jsonContent);

                    MsgLog.ShowStatus(_mainForm.textBox1,$"成功獲取 {mobs.Count} 個怪物資料");
                    return mobs;
                }
                catch (HttpRequestException ex)
                {
                    MsgLog.ShowStatus(_mainForm.textBox1,$"HTTP錯誤 (嘗試 {attempt}/{_downloadSettings.MaxRetryAttempts}): {ex.Message}");
                    if (attempt == _downloadSettings.MaxRetryAttempts) throw;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    MsgLog.ShowStatus(_mainForm.textBox1,$"請求超時 (嘗試 {attempt}/{_downloadSettings.MaxRetryAttempts})");
                    if (attempt == _downloadSettings.MaxRetryAttempts) throw;
                }

                // 重試前等待
                if (attempt < _downloadSettings.MaxRetryAttempts)
                {
                    await Task.Delay(1000 * attempt); // 遞增延遲
                }
            }

            return null;
        }

        /// <summary>
        /// 根據怪物名稱查找怪物ID
        /// </summary>
        private int? FindMobId(List<ArtaleMonster> allMobs, string mobName)
        {
            if (allMobs == null) return null;

            string mobNameLower = mobName.ToLower();
            var mob = allMobs.FirstOrDefault(m => m.Name.ToLower() == mobNameLower);
            return mob?.Id;
        }

        /// <summary>
        /// 下載並處理怪物圖片
        /// </summary>
        private async Task<int> SaveMobAsync(int mobId, string mobName)
        {
            // 建立輸出目錄
            string monstersDirectory = PathManager.MonstersDirectory;
            string mobFileName = string.Join("_", mobName.ToLower().Split(' '));
            string outputDir = Path.Combine(monstersDirectory, mobFileName);
            Directory.CreateDirectory(outputDir);

            // 建構下載URL
            string downloadUrl = $"{_downloadSettings.BaseUrl}/api/{_downloadSettings.DefaultRegion}/{_downloadSettings.DefaultVersion}/mob/{mobId}/download";
            MsgLog.ShowStatus(_mainForm.textBox1,$"正在下載怪物圖片包: {downloadUrl}");

            // 下載zip檔案
            var response = await _httpClient.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();
            byte[] zipBytes = await response.Content.ReadAsByteArrayAsync();

            int processedCount = 0;

            // 處理zip檔案
            using (var zipStream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                int index = 1;
                foreach (var entry in archive.Entries)
                {
                    // 跳過包含"die1"的檔案（死亡動畫）
                    if (_downloadSettings.SkipDeathAnimations &&
                        entry.Name.ToLower().Contains("die1"))
                        continue;

                    // 檢查支援的檔案格式
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
                                    // 儲存處理後的圖片
                                    string fileName = $"{mobFileName}_{index}.png";
                                    string savePath = Path.Combine(outputDir, fileName);

                                    Cv2.ImWrite(savePath, processedImage);
                                    MsgLog.ShowStatus(_mainForm.textBox1,$"已儲存: {fileName}");
                                    processedCount++;
                                    index++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MsgLog.ShowStatus(_mainForm.textBox1,$"處理圖片 {entry.Name} 時發生錯誤: {ex.Message}");
                    }
                }
            }

            return processedCount;
        }

        /// <summary>
        /// 使用 OpenCvSharp 處理圖片：將透明背景轉換為指定顏色
        /// </summary>
        private Mat? ProcessImageWithOpenCvSharp(byte[] imageBytes, string fileName)
        {
            try
            {
                // 從 byte array 載入圖片
                var image = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
                if (image.Empty())
                {
                    MsgLog.ShowStatus(_mainForm.textBox1,$"無法解碼圖片: {fileName}");
                    return null;
                }

                // 檢查圖片是否有Alpha通道（4個通道）
                if (image.Channels() == 4 && _imageSettings.ConvertTransparentPixels)
                {
                    // 手動管理 Mat 陣列
                    Mat[] channels = null;
                    Mat alphaChannel = null;
                    Mat transparentMask = null;

                    try
                    {
                        // 分離通道
                        channels = Cv2.Split(image);
                        // 取得Alpha通道 (索引 3)
                        alphaChannel = channels[3];

                        // 建立遮罩：找出完全透明的像素（alpha == 0）
                        transparentMask = new Mat();
                        Cv2.Threshold(alphaChannel, transparentMask, 0, 255, ThresholdTypes.Binary);
                        Cv2.BitwiseNot(transparentMask, transparentMask);

                        // 解析背景顏色 (BGR格式)
                        var colorValues = _imageSettings.ReplacementColorRgb.Split(',')
                            .Select(s => byte.Parse(s.Trim())).ToArray();

                        if (colorValues.Length >= 3)
                        {
                            // 將透明像素設定為指定顏色
                            var backgroundColor = new Scalar(colorValues[0], colorValues[1], colorValues[2]);
                            image.SetTo(backgroundColor, transparentMask);
                        }
                    }
                    finally
                    {
                        // 手動釋放資源
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
                MsgLog.ShowStatus(_mainForm.textBox1,$"圖片處理錯誤 {fileName}: {ex.Message}");
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
