namespace ArtaleAI.Models.Detection
{
    /// <summary>執行期啟用的怪物模板集合；僅持有參考，生命週期由 <see cref="ArtaleAI.Core.GameVisionCore"/> 快取管理。</summary>
    public sealed class MonsterDetectionCatalog
    {
        private readonly List<MonsterTemplateBundle> _bundles = new();

        public IReadOnlyList<MonsterTemplateBundle> Bundles => _bundles;

        public bool IsEmpty => _bundles.Count == 0 || _bundles.All(b => b.IsEmpty);

        public int TotalTemplateCount => _bundles.Sum(b => b.TemplateCount);

        public void SetSingle(MonsterTemplateBundle? bundle)
        {
            Clear();
            if (bundle != null && !bundle.IsEmpty)
                _bundles.Add(bundle);
        }

        public void Clear() => _bundles.Clear();
    }
}
