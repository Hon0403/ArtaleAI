using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;
using ArtaleAI.Config;
using System.IO;
using System.Collections.Generic;
using ArtaleAI.Utils;

namespace ArtaleAI.Minimap
{
    public class MapDetector : IDisposable
    {
        private readonly AppConfig _config;
        private readonly Dictionary<string, Mat> _templates;

        public MapDetector(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _templates = new Dictionary<string, Mat>();
            LoadAllTemplates();
        }

        /// <summary>
        /// 載入所有模板 - 四通道版本
        /// </summary>
        private void LoadAllTemplates()
        {
            var minimap = _config.Templates?.Minimap;
            var corners = minimap?.Corners;

            if (minimap == null)
            {
                Console.WriteLine("⚠️ Minimap 配置為空，跳過模板載入");
                return;
            }

            var templateConfigs = new Dictionary<string, TemplateConfig?>
            {
                ["PlayerMarker"] = minimap.PlayerMarker,
                ["OtherPlayers"] = minimap.OtherPlayers,
                ["TopLeft"] = corners?.TopLeft,
                ["TopRight"] = corners?.TopRight,
                ["BottomLeft"] = corners?.BottomLeft,
                ["BottomRight"] = corners?.BottomRight
            };

            foreach (var kvp in templateConfigs)
            {
                var templateConfig = kvp.Value;
                // 檢查 TemplateConfig 是否為 null 或路徑是否為空
                if (templateConfig?.Path == null)
                {
                    Console.WriteLine($"⚠️ 跳過模板 {kvp.Key}：配置為空或路徑未設定");
                    continue;
                }

                // 處理相對路徑
                string templatePath = templateConfig.Path;
                if (!Path.IsPathRooted(templatePath))
                {
                    templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, templateConfig.Path);
                }

                if (File.Exists(templatePath))
                {
                    try
                    {
                        // 🔧 使用 ImageUtils 載入為四通道
                        var originalTemplate = Cv2.ImRead(templatePath, ImreadModes.Unchanged);
                        if (!originalTemplate.Empty())
                        {
                            var template = ImageUtils.EnsureFourChannels(originalTemplate);
                            originalTemplate.Dispose();

                            _templates[kvp.Key] = template;
                            ImageUtils.LogImageInfo(template, $"Template-{kvp.Key}");
                            Console.WriteLine($"✅ 已載入四通道模板: {kvp.Key} ({template.Width}x{template.Height}, {template.Channels()} 通道)");
                        }
                        else
                        {
                            originalTemplate.Dispose();
                            Console.WriteLine($"❌ 模板載入失敗 (空圖片): {kvp.Key} - {templatePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ 載入模板時發生錯誤: {kvp.Key} - {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ 找不到模板檔案: {kvp.Key} - {templatePath}");
                }
            }

            Console.WriteLine($"模板載入完成，成功載入 {_templates.Count} 個四通道模板");
        }

        /// <summary>
        /// 在螢幕上尋找小地圖 - 四通道版本
        /// </summary>
        public Rectangle? FindMinimapOnScreen(Bitmap fullFrameBitmap)
        {
            if (fullFrameBitmap == null)
                return null;

            try
            {
                // 🔧 使用 ImageUtils 轉換為四通道
                using var frameMat = ImageUtils.BitmapToFourChannelMat(fullFrameBitmap);

                var cornerThreshold = _config.Templates.Minimap.CornerThreshold;
                Console.WriteLine($"🔍 開始小地圖檢測 (四通道)");
                Console.WriteLine($"📊 捕捉畫面大小: {fullFrameBitmap.Width}x{fullFrameBitmap.Height}");
                Console.WriteLine($"🎯 使用閾值: {cornerThreshold}");

                ImageUtils.LogImageInfo(frameMat, "FullFrame");

                var topLeft = MatchTemplateInternal(frameMat, "TopLeft", cornerThreshold, true);
                var bottomRight = MatchTemplateInternal(frameMat, "BottomRight", cornerThreshold, true);

                Console.WriteLine($"🔍 TopLeft 四通道匹配結果: {(topLeft.HasValue ? $"成功 ({topLeft.Value.Location.X}, {topLeft.Value.Location.Y})" : "失敗")}");
                Console.WriteLine($"🔍 BottomRight 四通道匹配結果: {(bottomRight.HasValue ? $"成功 ({bottomRight.Value.Location.X}, {bottomRight.Value.Location.Y})" : "失敗")}");

                // 🔧 確保變數名稱一致
                if (topLeft.HasValue && bottomRight.HasValue)
                {
                    var tl = topLeft.Value.Location;
                    var br = bottomRight.Value.Location;

                    if (_templates.TryGetValue("BottomRight", out var brTemplate))
                    {
                        int width = (br.X + brTemplate.Width) - tl.X;
                        int height = (br.Y + brTemplate.Height) - tl.Y;
                        Console.WriteLine($"📐 計算出的小地圖區域: ({tl.X}, {tl.Y}) -> {width}x{height}");

                        if (width > 0 && height > 0)
                        {
                            Console.WriteLine($"✅ 四通道小地圖檢測成功！");
                            return new Rectangle(tl.X, tl.Y, width, height);
                        }
                    }
                }

                Console.WriteLine($"❌ 四通道小地圖檢測失敗");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 四通道小地圖檢測異常: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 尋找玩家位置 - 四通道版本
        /// </summary>
        public System.Drawing.Point? FindPlayerPosition(Bitmap minimapImage)
        {
            if (minimapImage == null)
                return null;

            try
            {
                // 🔧 使用 ImageUtils 轉換為四通道
                using var mat = ImageUtils.BitmapToFourChannelMat(minimapImage);

                ImageUtils.LogImageInfo(mat, "MinimapForPlayer");

                var playerThreshold = _config.Templates.Minimap.PlayerThreshold;
                var matchResult = MatchTemplateInternal(mat, "PlayerMarker", playerThreshold, false);

                if (matchResult.HasValue && _templates.TryGetValue("PlayerMarker", out var template))
                {
                    var loc = matchResult.Value.Location;
                    var playerPos = new System.Drawing.Point(
                        loc.X + template.Width / 2,
                        loc.Y + template.Height / 2
                    );

                    Console.WriteLine($"✅ 四通道玩家位置檢測成功: ({playerPos.X}, {playerPos.Y})");
                    return playerPos;
                }

                Console.WriteLine($"❌ 四通道玩家位置檢測失敗");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"尋找玩家位置時發生錯誤: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 內部模板匹配方法 - 四通道版本
        /// </summary>
        private (System.Drawing.Point Location, double MaxValue)? MatchTemplateInternal(
            Mat inputMat, string templateName, double threshold, bool useGrayscale)
        {
            if (inputMat?.Empty() != false || !_templates.TryGetValue(templateName, out var template) || template.Empty())
            {
                Console.WriteLine($"⚠️ 模板匹配失敗：輸入或模板無效 ({templateName})");
                return null;
            }

            try
            {
                // 🔧 輸入已經是四通道，模板也是四通道
                ImageUtils.LogImageInfo(inputMat, $"Input-{templateName}");
                ImageUtils.LogImageInfo(template, $"Template-{templateName}");

                // 檢查尺寸
                if (template.Width > inputMat.Width || template.Height > inputMat.Height)
                {
                    Console.WriteLine($"⚠️ 模板 {templateName} 尺寸過大");
                    return null;
                }

                using (Mat result = new Mat())
                {
                    // 🔧 直接使用四通道進行匹配
                    if (useGrayscale)
                    {
                        // 如果需要灰階匹配，轉換為四通道灰階
                        using var inputGray = new Mat();
                        using var templateGray = new Mat();
                        ConvertToFourChannelGrayscale(inputMat, inputGray);
                        ConvertToFourChannelGrayscale(template, templateGray);

                        Cv2.MatchTemplate(inputGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                    }
                    else
                    {
                        // 彩色匹配，直接使用四通道
                        Cv2.MatchTemplate(inputMat, template, result, TemplateMatchModes.CCoeffNormed);
                    }

                    Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                    Console.WriteLine($"🎯 {templateName} 四通道匹配分數: {maxVal:F4} (閾值: {threshold:F4})");

                    if (maxVal >= threshold)
                    {
                        return (new System.Drawing.Point(maxLoc.X, maxLoc.Y), maxVal);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"模板匹配時發生錯誤 ({templateName}): {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 將四通道圖像轉換為四通道灰階（保持 Alpha）
        /// </summary>
        private static void ConvertToFourChannelGrayscale(Mat source, Mat dest)
        {
            if (source.Channels() == 4)
            {
                Mat[] channels = null;
                try
                {
                    channels = Cv2.Split(source);
                    using var grayChannel = new Mat();
                    // 使用前三個通道計算灰階
                    Cv2.CvtColor(source, grayChannel, ColorConversionCodes.BGRA2GRAY);
                    // 合併為四通道灰階
                    Cv2.Merge(new[] { grayChannel, grayChannel, grayChannel, channels[3] }, dest);
                }
                finally
                {
                    if (channels != null)
                    {
                        foreach (var ch in channels)
                            ch?.Dispose();
                    }
                }
            }
            else
            {
                // 如果不是四通道，先轉為四通道再處理
                using var temp4Ch = ImageUtils.EnsureFourChannels(source);
                ConvertToFourChannelGrayscale(temp4Ch, dest);
            }
        }

        public void Dispose()
        {
            foreach (var template in _templates.Values)
            {
                template?.Dispose();
            }
            _templates.Clear();
            Console.WriteLine("🗑️ MapDetector 四通道模板已釋放");
        }
    }
}
