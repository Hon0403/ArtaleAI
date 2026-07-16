using System.Windows.Forms;
using ArtaleAI.Vision;
using ArtaleAI.Models.Detection;

namespace ArtaleAI.Infrastructure.Persistence
{
    /// <summary>怪物模板 bundle 與 Catalog 的 UI 層單一來源；Mat 生命週期由 <see cref="GameVisionCore"/> 快取管理。</summary>
    public sealed class MonsterTemplateStore : IDisposable
    {
        /// <summary>UI 建議上限：再多效能與誤打風險上升。</summary>
        public const int SoftSelectLimit = 3;

        private readonly GameVisionCore _gameVision;
        private readonly MonsterDetectionCatalog _catalog = new();
        private readonly List<string> _selectedMonsterNames = new();

        public MonsterTemplateStore(GameVisionCore gameVision)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
        }

        public MonsterDetectionCatalog Catalog => _catalog;

        public MonsterTemplateBundle? ActiveBundle =>
            _catalog.Bundles.Count > 0 ? _catalog.Bundles[0] : null;

        public IReadOnlyList<string> SelectedMonsterNames => _selectedMonsterNames;

        /// <summary>日誌／狀態列用白話摘要。</summary>
        public string SelectedMonsterNamesDisplay =>
            _selectedMonsterNames.Count == 0
                ? string.Empty
                : string.Join("、", _selectedMonsterNames);

        public bool HasSelection => _selectedMonsterNames.Count > 0;

        public int ActiveTemplateCount => _catalog.TotalTemplateCount;

        public static List<string> EnumerateMonsterFolderNames(string monstersDirectory)
        {
            var names = new List<string>();
            if (!Directory.Exists(monstersDirectory)) return names;

            foreach (var path in Directory.GetDirectories(monstersDirectory))
            {
                var name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static void PopulateMonsterList(CheckedListBox list, string monstersDirectory)
        {
            var previouslyChecked = list.CheckedItems
                .Cast<object>()
                .Select(item => item.ToString() ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var name in EnumerateMonsterFolderNames(monstersDirectory))
                {
                    int index = list.Items.Add(name);
                    if (previouslyChecked.Contains(name))
                        list.SetItemChecked(index, true);
                }
            }
            finally
            {
                list.EndUpdate();
            }
        }

        public async Task LoadSelectionsAsync(
            IReadOnlyList<string> selectedNames,
            string monstersDirectory)
        {
            ReleaseSelection();

            if (selectedNames == null || selectedNames.Count == 0)
                return;

            var uniqueNames = selectedNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MonsterDetectionCatalog.HardSelectLimit)
                .ToList();

            var bundles = new List<MonsterTemplateBundle>();
            foreach (var name in uniqueNames)
            {
                var bundle = await _gameVision
                    .LoadMonsterTemplateBundleAsync(name, monstersDirectory)
                    .ConfigureAwait(true);

                if (bundle == null || bundle.IsEmpty)
                    continue;

                bundles.Add(bundle);
                _selectedMonsterNames.Add(name);
            }

            _catalog.SetMany(bundles);
        }

        public void ReleaseSelection()
        {
            _catalog.Clear();
            _selectedMonsterNames.Clear();
        }

        public void Dispose()
        {
            ReleaseSelection();
        }
    }
}
