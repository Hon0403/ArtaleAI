using System.Windows.Forms;
using ArtaleAI.Core;
using OpenCvSharp;

namespace ArtaleAI.Services
{
    /// <summary>怪物資料夾列舉與 OpenCV 模板清單之單一來源；釋放責任集中於此以避免 MainForm 與 GamePipeline 雙重擁有 Mat。</summary>
    public sealed class MonsterTemplateStore : IDisposable
    {
        private readonly GameVisionCore _gameVision;
        private readonly List<Mat> _templates = new();
        private string? _selectedMonsterName;

        public MonsterTemplateStore(GameVisionCore gameVision)
        {
            _gameVision = gameVision ?? throw new ArgumentNullException(nameof(gameVision));
        }

        /// <summary>目前載入的模板（同一個 <see cref="List{Mat}"/> 參考供 <see cref="GamePipeline"/> 每幀指派）。</summary>
        public List<Mat> Templates => _templates;

        public string? SelectedMonsterName => _selectedMonsterName;

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

        /// <summary>清空後填入「null」選項與各怪物資料夾名稱。</summary>
        public static void PopulateMonsterCombo(ComboBox combo, string monstersDirectory)
        {
            combo.Items.Clear();
            combo.Items.Add("null");
            foreach (var name in EnumerateMonsterFolderNames(monstersDirectory))
                combo.Items.Add(name);
        }

        /// <summary>依選取下載入模板，或選「null」時清空。</summary>
        public async Task LoadSelectionAsync(string selectedItemText, string monstersDirectory)
        {
            if (selectedItemText == "null")
            {
                ReleaseTemplates();
                _selectedMonsterName = null;
                return;
            }

            ReleaseTemplates();

            var loaded = await _gameVision.LoadMonsterTemplatesAsync(selectedItemText, monstersDirectory)
                .ConfigureAwait(true);
            if (loaded != null && loaded.Count > 0)
                _templates.AddRange(loaded);

            _selectedMonsterName = selectedItemText;
        }

        public void ReleaseTemplates()
        {
            foreach (var t in _templates)
                t?.Dispose();
            _templates.Clear();
            _selectedMonsterName = null;
        }

        public void Dispose()
        {
            ReleaseTemplates();
        }
    }
}
