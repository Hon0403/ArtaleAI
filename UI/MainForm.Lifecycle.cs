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
    public partial class MainForm
    {
        #region 清理與釋放

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_mapEditor?.IsDirty == true && e.CloseReason == CloseReason.UserClosing)
            {
                var result = MessageBox.Show(
                    "目前有未儲存的地圖變更，確定要離開嗎？",
                    "未儲存變更",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                    e.Cancel = true;
                else if (result == DialogResult.No)
                    e.Cancel = false;
            }

            if (e.Cancel)
                return;

            try
            {
                AppConfig.Instance.Save();
            }
            catch (Exception ex)
            {
                Logger.Error($"[系統] OnFormClosing 存檔失敗: {ex.Message}");
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                var liveViewImage = pictureBoxLiveView.Image;
                var minimapImage = pictureBoxMinimap.Image;
                var consoleMinimapImage = pictureBox_ConsoleMinimap.Image;
                var consoleGameImage = pictureBox_ConsoleGameView.Image;
                pictureBoxLiveView.Image = null;
                pictureBoxMinimap.Image = null;
                pictureBox_ConsoleMinimap.Image = null;
                pictureBox_ConsoleGameView.Image = null;
                lbl_ConsoleMinimapPlaceholder.Visible = true;
                lbl_ConsoleGamePlaceholder.Visible = true;
                System.Windows.Forms.Application.DoEvents();

                liveViewImage?.Dispose();
                minimapImage?.Dispose();
                consoleMinimapImage?.Dispose();
                consoleGameImage?.Dispose();


                _monsterTemplates?.Dispose();


                if (_mapFileManager != null)
                {
                    _mapFileManager.MapSaved -= OnMapSaved;
                    _mapFileManager.MapLoaded -= OnMapFileLoaded;
                    _mapFileManager.StatusMessage -= OnMapFileManagerStatusMessage;
                    _mapFileManager.ErrorMessage -= OnMapFileManagerErrorMessage;

                }

                if (liveViewManager != null)
                {
                    liveViewManager.OnFrameReady -= OnFrameAvailable;
                }

                _monsterDownloader?.Dispose();
                gameVision?.Dispose();

                _pathPlanningManager?.Dispose();
                liveViewManager?.Dispose();
                _movementController?.Dispose();
                DisposeClientSizeGuardTimer();

                MsgLog.ShowStatus(textBox1, "所有資源已清理");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Form關閉錯誤: {ex.Message}");
                Logger.Error("[系統] Form關閉錯誤", ex);
            }

            Logger.Shutdown();

            base.OnFormClosed(e);
        }

        #endregion
    }
}
