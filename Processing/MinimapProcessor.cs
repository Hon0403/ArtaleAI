using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;
using ArtaleAI.Configuration;

namespace ArtaleAI.Processing
{
    public class MinimapProcessor : IDisposable
    {
        private readonly AppConfig _config;
        private readonly Dictionary<string, Mat> _templates;

        public MinimapProcessor(AppConfig config)
        {
            _config = config;
            _templates = new Dictionary<string, Mat>();
            LoadAllTemplates();
        }

        private void LoadAllTemplates()
        {
            var templateConfigs = new Dictionary<string, TemplateConfig>
            {
                ["PlayerMarker"] = _config.Templates.Minimap.PlayerMarker,
                ["OtherPlayers"] = _config.Templates.Minimap.OtherPlayers,
                ["TopLeft"] = _config.Templates.Minimap.Corners.TopLeft,
                ["TopRight"] = _config.Templates.Minimap.Corners.TopRight,
                ["BottomLeft"] = _config.Templates.Minimap.Corners.BottomLeft,
                ["BottomRight"] = _config.Templates.Minimap.Corners.BottomRight
            };

            foreach (var kvp in templateConfigs)
            {
                if (File.Exists(kvp.Value.Path))
                {
                    bool isCornerTemplate = kvp.Key.StartsWith("Top") || kvp.Key.StartsWith("Bottom");

                    if (isCornerTemplate)
                    {
                        _templates[kvp.Key] = Cv2.ImRead(kvp.Value.Path, ImreadModes.Grayscale);
                    }
                    else
                    {
                        _templates[kvp.Key] = Cv2.ImRead(kvp.Value.Path, ImreadModes.Color);
                    }
                    Console.WriteLine($"✅ 已載入模板: {kvp.Key} (通道數: {_templates[kvp.Key].Channels()})");
                }
                else
                {
                    Console.WriteLine($"❌ 找不到模板: {kvp.Key} - {kvp.Value.Path}");
                }
            }
        }

        public Rectangle? FindMinimapOnScreen(Bitmap fullFrameBitmap)
        {
            using (Mat frameMat = BitmapConverter.ToMat(fullFrameBitmap))
            {
                var cornerThreshold = _config.Templates.Minimap.CornerThreshold;
                var topLeft = MatchTemplateInternal(frameMat, "TopLeft", cornerThreshold, true);
                var bottomRight = MatchTemplateInternal(frameMat, "BottomRight", cornerThreshold, true);

                if (topLeft.HasValue && bottomRight.HasValue)
                {
                    var tl = topLeft.Value.Location;
                    var br = bottomRight.Value.Location;

                    var brTemplate = _templates["BottomRight"];
                    int width = (br.X + brTemplate.Width) - tl.X;
                    int height = (br.Y + brTemplate.Height) - tl.Y;

                    if (width > 0 && height > 0)
                    {
                        return new Rectangle(tl.X, tl.Y, width, height);
                    }
                }
            }
            return null;
        }

        public System.Drawing.Point? FindPlayerPosition(Bitmap minimapImage)
        {
            using (Mat mat = BitmapConverter.ToMat(minimapImage))
            {
                var playerThreshold = _config.Templates.Minimap.PlayerThreshold;
                var matchResult = MatchTemplateInternal(mat, "PlayerMarker", playerThreshold, false);
                if (matchResult.HasValue)
                {
                    var loc = matchResult.Value.Location;
                    var template = _templates["PlayerMarker"];
                    return new System.Drawing.Point(loc.X + template.Width / 2, loc.Y + template.Height / 2);
                }
                return null;
            }
        }

        private (System.Drawing.Point Location, double MaxValue)? MatchTemplateInternal(Mat inputMat, string templateName, double threshold, bool useGrayscale)
        {
            if (!_templates.TryGetValue(templateName, out var template) || template.Empty())
            {
                return null;
            }

            using Mat processedInput = new Mat();

            if (useGrayscale)
            {
                if (inputMat.Channels() != 1) Cv2.CvtColor(inputMat, processedInput, ColorConversionCodes.BGRA2GRAY);
                else inputMat.CopyTo(processedInput);
            }
            else
            {
                if (inputMat.Channels() != 3) Cv2.CvtColor(inputMat, processedInput, ColorConversionCodes.BGRA2BGR);
                else inputMat.CopyTo(processedInput);
            }

            using (Mat result = new Mat())
            {
                if (template.Width > processedInput.Width || template.Height > processedInput.Height)
                {
                    return null;
                }

                Cv2.MatchTemplate(processedInput, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal >= threshold)
                {
                    return (new System.Drawing.Point(maxLoc.X, maxLoc.Y), maxVal);
                }
            }
            return null;
        }

        public void Dispose()
        {
            foreach (var template in _templates.Values)
            {
                template?.Dispose();
            }
            _templates.Clear();
        }
    }
}
