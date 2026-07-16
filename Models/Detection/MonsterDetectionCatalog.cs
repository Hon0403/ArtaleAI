namespace ArtaleAI.Models.Detection
{
    /// <summary>執行期啟用的怪物模板集合；僅持有參考，生命週期由 <see cref="ArtaleAI.Vision.GameVisionCore"/> 快取管理。</summary>
    public sealed class MonsterDetectionCatalog
    {
        /// <summary>程式硬上限；超過視為無效並截斷。</summary>
        public const int HardSelectLimit = 5;

        private readonly List<MonsterTemplateBundle> _bundles = new();

        public IReadOnlyList<MonsterTemplateBundle> Bundles => _bundles;

        public bool IsEmpty => _bundles.Count == 0 || _bundles.All(b => b.IsEmpty);

        public int TotalTemplateCount => _bundles.Sum(b => b.TemplateCount);

        public int BundleCount => _bundles.Count;

        public void SetSingle(MonsterTemplateBundle? bundle)
        {
            if (bundle == null || bundle.IsEmpty)
            {
                Clear();
                return;
            }

            SetMany(new[] { bundle });
        }

        /// <summary>以多個 bundle 取代目前選擇；超過 <see cref="HardSelectLimit"/> 時截斷。</summary>
        public void SetMany(IEnumerable<MonsterTemplateBundle> bundles)
        {
            Clear();
            if (bundles == null) return;

            foreach (var bundle in bundles)
            {
                if (bundle == null || bundle.IsEmpty) continue;
                _bundles.Add(bundle);
                if (_bundles.Count >= HardSelectLimit)
                    break;
            }
        }

        public void Clear() => _bundles.Clear();
    }
}
