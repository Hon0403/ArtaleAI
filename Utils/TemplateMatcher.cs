using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Generic;
using System.Drawing;
using SdPoint = System.Drawing.Point;

namespace ArtaleAI.Core
{
    /// <summary>
    /// 模板匹配工具類 - 純函數式工具
    /// </summary>
    public class TemplateMatcher : IDisposable
    {
        /// <summary>
        /// 在螢幕圖像中尋找所有匹配
        /// </summary>
        public List<SdPoint> FindAllMatches(Bitmap screenImage, Bitmap template, double threshold,
            bool useColorFilter = false, double colorTolerance = 30.0)
        {
            var results = new List<SdPoint>();

            using var screenMat = screenImage.ToMat();
            using var templateMat = template.ToMat();
            using var result = new Mat();

            if (useColorFilter)
            {
                var templateMean = Cv2.Mean(templateMat);

                // 建立與模板相同尺寸的遮罩
                using var templateMask = Mat.Ones(templateMat.Size(), MatType.CV_8UC1);

                // 根據模板顏色特徵來修改遮罩
                var lowerBound = new Scalar(
                    Math.Max(0, templateMean[0] - colorTolerance),
                    Math.Max(0, templateMean[1] - colorTolerance),
                    Math.Max(0, templateMean[2] - colorTolerance)
                );
                var upperBound = new Scalar(
                    Math.Min(255, templateMean[0] + colorTolerance),
                    Math.Min(255, templateMean[1] + colorTolerance),
                    Math.Min(255, templateMean[2] + colorTolerance)
                );

                // 創建模板的顏色遮罩
                using var colorMask = new Mat();
                Cv2.InRange(templateMat, lowerBound, upperBound, colorMask);

                // 現在遮罩尺寸正確了
                Cv2.MatchTemplate(screenMat, templateMat, result, TemplateMatchModes.CCoeffNormed, colorMask);
            }

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
