using System.Windows.Forms;
using ArtaleAI.Vision;
using ArtaleAI.Models.Detection;
using OpenCvSharp;

namespace ArtaleAI.Infrastructure.Persistence
{
    /// <summary>怪物模板 bundle 與 Catalog 的 UI 層單一來源；Mat 生命週期由 <see cref="GameVisionCore"/> 快取管理。</summary>
    public sealed class MonsterTemplateStore : IDisposable
    {
        private readonly GameVisionCore _gameVision;
        private readonly MonsterDetectionCatalog _catalog = new();
        private string? _selectedMonsterName;

        public MonsterTemplateStore(GameVisionCore gameVision)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
        }

        public MonsterDetectionCatalog Catalog => _catalog;

        public MonsterTemplateBundle? ActiveBundle =>
            _catalog.Bundles.Count > 0 ? _catalog.Bundles[0] : null;

        public string? SelectedMonsterName => _selectedMonsterName;

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

            return names;
        }

        public static void PopulateMonsterCombo(ComboBox combo, string monstersDirectory)
        {
            combo.Items.Clear();
            combo.Items.Add("null");
            foreach (var name in EnumerateMonsterFolderNames(monstersDirectory))
                combo.Items.Add(name);
        }

        public async Task LoadSelectionAsync(string selectedItemText, string monstersDirectory)
        {
            if (selectedItemText == "null")
            {
                ReleaseSelection();
                return;
            }

            ReleaseSelection();

            var bundle = await _gameVision.LoadMonsterTemplateBundleAsync(selectedItemText, monstersDirectory)
                .ConfigureAwait(true);

            if (bundle != null && !bundle.IsEmpty)
                _catalog.SetSingle(bundle);

            _selectedMonsterName = bundle != null && !bundle.IsEmpty ? selectedItemText : null;
        }

        public void ReleaseSelection()
        {
            _catalog.Clear();
            _selectedMonsterName = null;
        }

        public void Dispose()
        {
            ReleaseSelection();
        }
    }
}
