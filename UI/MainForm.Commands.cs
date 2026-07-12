using ArtaleAI.Infrastructure.External;
using ArtaleAI.Infrastructure.External.Config;
using ArtaleAI.Models.Config;
using ArtaleAI.Vision;
using ArtaleAI.Models.Detection;
using ArtaleAI.Models.Map;
using ArtaleAI.Models.Minimap;
using ArtaleAI.Models.PathPlanning;
using ArtaleAI.Application.Pipeline;
using ArtaleAI.Application.Navigation;
using ArtaleAI.Application.Movement;
using ArtaleAI.Infrastructure.Capture;
using ArtaleAI.Infrastructure.Persistence;
using ArtaleAI.Infrastructure.Input;
using ArtaleAI.Contracts;
using ArtaleAI.UI;
using ArtaleAI.UI.MapEditor;
using ArtaleAI.Models.Visualization;
using ArtaleAI.Shared;
using ArtaleAI.Domain.Navigation;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Graphics.Capture;
using SdPoint = System.Drawing.Point;
using SdPointF = System.Drawing.PointF;
using SdRect = System.Drawing.Rectangle;
using SdSize = System.Drawing.Size;
using Timer = System.Threading.Timer;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArtaleAI
{
    public partial class MainForm : Form
    {
        #region 按鈕事件

        private void btn_SaveMap_Click(object sender, EventArgs e)
        {
            try
            {
                if (_mapFileManager == null) return;

                if (!_mapFileManager.HasCurrentMap)
                {
                    using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                    {
                        saveFileDialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                        saveFileDialog.InitialDirectory = PathManager.MapDataDirectory;
                        saveFileDialog.Title = "另存新地圖檔案";

                        if (saveFileDialog.ShowDialog() == DialogResult.OK)
                        {
                            _mapFileManager.SaveMapToPath(saveFileDialog.FileName);
                        }
                    }
                }
                else
                {
                    _mapFileManager.SaveCurrentMap();
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"儲存地圖時發生錯誤: {ex.Message}");
            }
        }

        private void btn_New_Click(object sender, EventArgs e)
        {
            try
            {
                if (!ConfirmDiscardUnsavedChanges("建立新地圖"))
                    return;

                var result = MessageBox.Show("確定要清空當前地圖並建立新檔案嗎？",
                    "建立新地圖", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    _mapFileManager?.CreateNewMap();
                    MessageBox.Show("已建立新地圖，您可以開始進行錄製。", "地圖編輯器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"建立新地圖時發生錯誤: {ex.Message}");
            }
        }

        #endregion
        private async void OnMonsterSelectionChanged(object? sender, EventArgs e)
        {
            if (_monsterTemplates == null) return;

            try
            {
                if (cbo_MonsterTemplates.SelectedItem == null) return;
                string selectedMonster = cbo_MonsterTemplates.SelectedItem.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(selectedMonster)) return;

                if (selectedMonster == "null")
                {
                    _monsterTemplates.ReleaseSelection();
                    MsgLog.ShowStatus(textBox1, "已清除怪物模板選擇");
                    return;
                }

                MsgLog.ShowStatus(textBox1, $"載入怪物模板: {selectedMonster}");
                await _monsterTemplates.LoadSelectionAsync(selectedMonster, PathManager.MonstersDirectory);
                MsgLog.ShowStatus(textBox1, $"已載入 {_monsterTemplates.ActiveTemplateCount} 個模板");
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"載入模板錯誤: {ex.Message}");
                _monsterTemplates.ReleaseSelection();
            }
        }

        private async void btn_DownloadMonster_Click(object sender, EventArgs e)
        {
            try
            {
                string monsterName = Microsoft.VisualBasic.Interaction.InputBox(
                    "請輸入怪物名稱:", "下載怪物模板", "");

                if (string.IsNullOrWhiteSpace(monsterName)) return;

                if (_monsterDownloader == null)
                {
                    MsgLog.ShowError(textBox1, "下載器尚未初始化");
                    return;
                }

                btn_DownloadMonster.Enabled = false;
                btn_DownloadMonster.Text = "下載中...";

                var result = await _monsterDownloader.DownloadMonsterAsync(monsterName);

                if (result is { Success: true } ok)
                {
                    MonsterTemplateStore.PopulateMonsterCombo(cbo_MonsterTemplates, PathManager.MonstersDirectory);
                    MsgLog.ShowStatus(textBox1, $" 成功下載 {ok.DownloadedCount} 個模板");
                }
            }
            catch (Exception ex)
            {
                MsgLog.ShowError(textBox1, $"下載怪物模板失敗: {ex.Message}");
            }
            finally
            {
                btn_DownloadMonster.Enabled = true;
                btn_DownloadMonster.Text = "下載怪物";
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F4:
                    pictureBoxMinimap.Invalidate();
                    MsgLog.ShowStatus(textBox1, "已更新路徑編輯畫面");
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
