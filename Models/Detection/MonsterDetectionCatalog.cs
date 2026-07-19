namespace ArtaleAI.Models.Detection
{
    /// <summary>
    /// 執行期啟用的怪物模板集合；僅持有參考，生命週期由 <see cref="ArtaleAI.Vision.GameVisionCore"/> 快取管理。
    /// UI 執行緒會在下載／重選怪物時改寫選擇，背景偵測執行緒同時列舉，
    /// 故採「不可變快照 + 原子交換」：寫入端整組換掉陣列參考，讀取端永遠拿到一致版本，
    /// 無鎖即可避免 Collection was modified。
    /// </summary>
    public sealed class MonsterDetectionCatalog
    {
        /// <summary>程式硬上限；超過視為無效並截斷。</summary>
        public const int HardSelectLimit = 5;

        private volatile MonsterTemplateBundle[] _bundles = Array.Empty<MonsterTemplateBundle>();

        /// <summary>目前選擇的快照；取得後即使選擇被換掉，列舉仍安全。</summary>
        public IReadOnlyList<MonsterTemplateBundle> Bundles => _bundles;

        public bool IsEmpty => _bundles.Length == 0;

        public int TotalTemplateCount => _bundles.Sum(b => b.TemplateCount);

        public int BundleCount => _bundles.Length;

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
            if (bundles == null)
            {
                Clear();
                return;
            }

            var next = new List<MonsterTemplateBundle>(HardSelectLimit);
            foreach (var bundle in bundles)
            {
                if (bundle == null || bundle.IsEmpty) continue;
                next.Add(bundle);
                if (next.Count >= HardSelectLimit)
                    break;
            }

            _bundles = next.ToArray();
        }

        public void Clear() => _bundles = Array.Empty<MonsterTemplateBundle>();
    }
}
