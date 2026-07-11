namespace ArtaleAI.Models.Detection
{
    /// <summary>單一怪物種類的全部模板（含翻轉變體）；為多怪 Catalog 的基本單元。</summary>
    public sealed class MonsterTemplateBundle : IDisposable
    {
        public MonsterTemplateBundle(string monsterName, IReadOnlyList<MonsterTemplateEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(monsterName))
                throw new ArgumentException("怪物名稱不可為空", nameof(monsterName));

            MonsterName = monsterName;
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public string MonsterName { get; }

        public IReadOnlyList<MonsterTemplateEntry> Entries { get; }

        public bool IsEmpty => Entries.Count == 0;

        public int TemplateCount => Entries.Count;

        public void Dispose()
        {
            foreach (var entry in Entries)
                entry.Dispose();
        }
    }
}
