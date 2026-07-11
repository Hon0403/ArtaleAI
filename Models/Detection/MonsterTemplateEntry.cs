using OpenCvSharp;

namespace ArtaleAI.Models.Detection
{
    /// <summary>單一怪物模板與載入時預計算的匹配用資源（mask、灰階、粗篩縮圖）。</summary>
    public sealed class MonsterTemplateEntry : IDisposable
    {
        public const double CoarseScale = 0.25;
        public const int MinCoarseTemplateSize = 5;

        public Mat Template { get; }
        public Mat Mask { get; }
        public Mat GrayTemplate { get; }
        public Mat CoarseTemplate { get; }
        public Mat CoarseMask { get; }
        public Mat CoarseGrayTemplate { get; }

        public bool SupportsCoarse =>
            CoarseTemplate.Width >= MinCoarseTemplateSize &&
            CoarseTemplate.Height >= MinCoarseTemplateSize;

        public MonsterTemplateEntry(
            Mat template,
            Mat mask,
            Mat grayTemplate,
            Mat coarseTemplate,
            Mat coarseMask,
            Mat coarseGrayTemplate)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Mask = mask ?? throw new ArgumentNullException(nameof(mask));
            GrayTemplate = grayTemplate ?? throw new ArgumentNullException(nameof(grayTemplate));
            CoarseTemplate = coarseTemplate ?? throw new ArgumentNullException(nameof(coarseTemplate));
            CoarseMask = coarseMask ?? throw new ArgumentNullException(nameof(coarseMask));
            CoarseGrayTemplate = coarseGrayTemplate ?? throw new ArgumentNullException(nameof(coarseGrayTemplate));
        }

        public void Dispose()
        {
            Template.Dispose();
            Mask.Dispose();
            GrayTemplate.Dispose();
            CoarseTemplate.Dispose();
            CoarseMask.Dispose();
            CoarseGrayTemplate.Dispose();
        }
    }
}
