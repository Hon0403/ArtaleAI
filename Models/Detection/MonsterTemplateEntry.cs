using OpenCvSharp;

namespace ArtaleAI.Models.Detection
{
    /// <summary>單一怪物模板與載入時預計算的匹配用資源（對齊 KenYu MapleStoryAutoLevelUp）。</summary>
    public sealed class MonsterTemplateEntry : IDisposable
    {
        public const double CoarseScale = 0.25;
        public const int MinCoarseTemplateSize = 5;
        public const int MinContourPixels = 20;

        public Mat Template { get; }
        public Mat Mask { get; }
        public Mat GrayTemplate { get; }
        /// <summary>KenYu ContourOnly：模板上精確黑色輪廓經高斯模糊後的單通道圖。</summary>
        public Mat ContourMask { get; }
        public Mat CoarseTemplate { get; }
        public Mat CoarseMask { get; }
        public Mat CoarseGrayTemplate { get; }
        public Mat CoarseContourMask { get; }

        public bool SupportsCoarse =>
            CoarseTemplate.Width >= MinCoarseTemplateSize &&
            CoarseTemplate.Height >= MinCoarseTemplateSize;

        public bool HasUsableContour =>
            !ContourMask.Empty() && Cv2.CountNonZero(ContourMask) >= MinContourPixels;

        public MonsterTemplateEntry(
            Mat template,
            Mat mask,
            Mat grayTemplate,
            Mat contourMask,
            Mat coarseTemplate,
            Mat coarseMask,
            Mat coarseGrayTemplate,
            Mat coarseContourMask)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Mask = mask ?? throw new ArgumentNullException(nameof(mask));
            GrayTemplate = grayTemplate ?? throw new ArgumentNullException(nameof(grayTemplate));
            ContourMask = contourMask ?? throw new ArgumentNullException(nameof(contourMask));
            CoarseTemplate = coarseTemplate ?? throw new ArgumentNullException(nameof(coarseTemplate));
            CoarseMask = coarseMask ?? throw new ArgumentNullException(nameof(coarseMask));
            CoarseGrayTemplate = coarseGrayTemplate ?? throw new ArgumentNullException(nameof(coarseGrayTemplate));
            CoarseContourMask = coarseContourMask ?? throw new ArgumentNullException(nameof(coarseContourMask));
        }

        public void Dispose()
        {
            Template.Dispose();
            Mask.Dispose();
            GrayTemplate.Dispose();
            ContourMask.Dispose();
            CoarseTemplate.Dispose();
            CoarseMask.Dispose();
            CoarseGrayTemplate.Dispose();
            CoarseContourMask.Dispose();
        }
    }
}
