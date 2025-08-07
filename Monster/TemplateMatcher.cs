using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Generic;
using System.Drawing;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Monster
{
    /// <summary>
    /// 模板匹配工具類 - 純函數式工具
    /// </summary>
    public class TemplateMatcher : IDisposable
    {
        /// <summary>
        /// 在螢幕圖像中尋找所有匹配
        /// </summary>
        public List<SdPoint> FindAllMatches(Bitmap screenImage, Bitmap template, double threshold)
        {
            var results = new List<SdPoint>();

            using var screenMat = BitmapConverter.ToMat(screenImage);
            using var templateMat = BitmapConverter.ToMat(template);
            using var result = new Mat();

            Cv2.MatchTemplate(screenMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out var maxLoc);

            if (maxVal >= threshold)
            {
                results.Add(new SdPoint(maxLoc.X, maxLoc.Y));
            }

            return results;
        }

        public void Dispose()
        {
            // 清理資源
        }
    }
}
