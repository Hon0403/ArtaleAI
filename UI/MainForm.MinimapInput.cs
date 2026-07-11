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
        #region PictureBox 滑鼠事件

        private readonly struct MinimapViewportLayout
        {
            public float Scale { get; init; }
            public float OffsetX { get; init; }
            public float OffsetY { get; init; }
            public float ImageWidth { get; init; }
            public float ImageHeight { get; init; }

            public PointF ImageToPictureBox(PointF imagePoint) =>
                new(imagePoint.X * Scale + OffsetX, imagePoint.Y * Scale + OffsetY);

            public PointF PictureBoxToImage(PointF pictureBoxPoint) =>
                new(
                    (pictureBoxPoint.X - OffsetX) / Scale,
                    (pictureBoxPoint.Y - OffsetY) / Scale);
        }

        private bool TryGetMinimapViewportLayout(out MinimapViewportLayout layout)
        {
            layout = default;
            if (_mapEditor == null || pictureBoxMinimap.Image == null)
                return false;

            float pbWidth = pictureBoxMinimap.ClientSize.Width;
            float pbHeight = pictureBoxMinimap.ClientSize.Height;
            float imageWidth = pictureBoxMinimap.Image.Width;
            float imageHeight = pictureBoxMinimap.Image.Height;
            if (pbWidth <= 0 || pbHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return false;

            float fitScale = Math.Min(pbWidth / imageWidth, pbHeight / imageHeight);
            float scale = fitScale * _mapEditor.ZoomScale;
            float displayWidth = imageWidth * scale;
            float displayHeight = imageHeight * scale;

            layout = new MinimapViewportLayout
            {
                Scale = scale,
                OffsetX = (pbWidth - displayWidth) / 2f + _mapEditor.PanOffsetX,
                OffsetY = (pbHeight - displayHeight) / 2f + _mapEditor.PanOffsetY,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight
            };
            return scale > 0f;
        }

        private void ClampMinimapPanOffset()
        {
            if (_mapEditor == null || pictureBoxMinimap.Image == null)
                return;

            float pbWidth = pictureBoxMinimap.ClientSize.Width;
            float pbHeight = pictureBoxMinimap.ClientSize.Height;
            float imageWidth = pictureBoxMinimap.Image.Width;
            float imageHeight = pictureBoxMinimap.Image.Height;
            float fitScale = Math.Min(pbWidth / imageWidth, pbHeight / imageHeight);
            float scale = fitScale * _mapEditor.ZoomScale;
            float displayWidth = imageWidth * scale;
            float displayHeight = imageHeight * scale;

            float minPanX = displayWidth > pbWidth ? (pbWidth - displayWidth) / 2f : 0f;
            float maxPanX = displayWidth > pbWidth ? (displayWidth - pbWidth) / 2f : 0f;
            float minPanY = displayHeight > pbHeight ? (pbHeight - displayHeight) / 2f : 0f;
            float maxPanY = displayHeight > pbHeight ? (displayHeight - pbHeight) / 2f : 0f;

            _mapEditor.PanOffsetX = Math.Clamp(_mapEditor.PanOffsetX, minPanX, maxPanX);
            _mapEditor.PanOffsetY = Math.Clamp(_mapEditor.PanOffsetY, minPanY, maxPanY);
        }

        private void SyncPathEditorMinimapBounds()
        {
            if (pictureBoxMinimap.Image == null)
                return;

            minimapBounds = new Rectangle(0, 0, pictureBoxMinimap.Image.Width, pictureBoxMinimap.Image.Height);
            _mapEditor?.SetMinimapBounds(minimapBounds);
        }

        private PointF TranslatePictureBoxPointToImage(PointF pbPoint, PictureBox pb)
        {
            if (!TryGetMinimapViewportLayout(out var layout))
                return pbPoint;

            return layout.PictureBoxToImage(pbPoint);
        }

        private void pictureBoxMinimap_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMinimapPanning && _mapEditor != null)
            {
                _mapEditor.PanOffsetX = _minimapPanStartOffset.X + (e.X - _minimapPanStartClient.X);
                _mapEditor.PanOffsetY = _minimapPanStartOffset.Y + (e.Y - _minimapPanStartClient.Y);
                ClampMinimapPanOffset();
                pictureBoxMinimap.Invalidate();
                lbl_MouseCoords.Text = "座標: (-, -) | Ctrl+拖曳平移";
                return;
            }

            if (_mapEditor != null && !minimapBounds.IsEmpty && pictureBoxMinimap.Image != null)
            {
                var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
                var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);

                if (_mapEditor.IsVertexDragging)
                {
                    _mapEditor.UpdateVertexDrag(screenPoint);
                    pictureBoxMinimap.Invalidate();
                    lbl_MouseCoords.Text =
                        $"座標: ({imagePoint.X:F1}, {imagePoint.Y:F1}) | 拖曳折點";
                    return;
                }

                bool preferNode = (ModifierKeys & Keys.Shift) != 0;
                _mapEditor.UpdateMousePosition(screenPoint);
                _mapEditor.UpdateHoveredNode(screenPoint, preferNode);
                pictureBoxMinimap.Invalidate();

                var hover = _mapEditor.GetHoverInfo();
                string segmentText = hover.HasSegmentContext ? $" | Seg {hover.SegmentIndex}" : string.Empty;
                string projText = hover.HasProjection
                    ? $" | Proj ({hover.ProjectionPoint.X:F1},{hover.ProjectionPoint.Y:F1})"
                    : string.Empty;
                string nodeText = hover.HasRuntimeNode ? $" | Node #{hover.RuntimeNodeIndex}" : string.Empty;
                string hintText = preferNode ? " | Shift:節點優先" : string.Empty;
                lbl_MouseCoords.Text =
                    $"座標: ({imagePoint.X:F1}, {imagePoint.Y:F1}){segmentText}{projText}{nodeText}{hintText}";
            }

            RefreshMapEditorStatusBar();
        }

        private void pictureBoxMinimap_Paint(object sender, PaintEventArgs e)
        {
            if (_mapEditor == null || minimapBounds.IsEmpty || pictureBoxMinimap.Image == null)
                return;

            if (!TryGetMinimapViewportLayout(out var layout))
                return;

            e.Graphics.Clear(pictureBoxMinimap.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            e.Graphics.DrawImage(
                pictureBoxMinimap.Image,
                layout.OffsetX,
                layout.OffsetY,
                layout.ImageWidth * layout.Scale,
                layout.ImageHeight * layout.Scale);

            PointF ConvertScreenToDisplay(PointF screenPoint)
            {
                float relX = screenPoint.X - minimapBounds.X;
                float relY = screenPoint.Y - minimapBounds.Y;
                return layout.ImageToPictureBox(new PointF(relX, relY));
            }

            DrawPathEditorGrid(
                e.Graphics,
                layout.ImageWidth,
                layout.ImageHeight,
                layout.Scale,
                layout.Scale,
                layout.OffsetX,
                layout.OffsetY);

            _mapEditor.Render(e.Graphics, ConvertScreenToDisplay);

            DrawPathEditorRuler(
                e.Graphics,
                layout.ImageWidth,
                layout.ImageHeight,
                layout.Scale,
                layout.Scale,
                layout.OffsetX,
                layout.OffsetY);
        }



        /// <summary>路徑編輯小地圖底圖上的對齊網格。</summary>
        private void DrawPathEditorGrid(Graphics g, float imgWidth, float imgHeight,
            float scaleX, float scaleY, float offsetX, float offsetY)
        {
            const int MajorTickInterval = 5;
            const int RulerSize = 18;

            using var gridPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1);

            for (int x = 0; x <= (int)imgWidth; x++)
            {
                if (x % MajorTickInterval == 0 && x != 0)
                {
                    float screenX = offsetX + x * scaleX;
                    g.DrawLine(gridPen, screenX, offsetY + RulerSize, screenX, offsetY + imgHeight * scaleY);
                }
            }

            for (int y = 0; y <= (int)imgHeight; y++)
            {
                if (y % MajorTickInterval == 0 && y != 0)
                {
                    float screenY = offsetY + y * scaleY;
                    g.DrawLine(gridPen, offsetX + RulerSize, screenY, offsetX + imgWidth * scaleX, screenY);
                }
            }
        }

        /// <summary>路徑編輯小地圖上方與左側的座標刻度尺。</summary>
        private void DrawPathEditorRuler(Graphics g, float imgWidth, float imgHeight,
            float scaleX, float scaleY, float offsetX, float offsetY)
        {
            const int RulerSize = 18;
            const int MajorTickInterval = 5;
            const int MinorTickInterval = 2;

            var bgColor = Color.FromArgb(30, 30, 30);
            var tickColor = Color.FromArgb(100, 100, 100);
            var textColor = Color.FromArgb(200, 200, 200);
            var majorTickLength = RulerSize - 4;
            var minorTickLength = RulerSize / 2;

            using var bgBrush = new SolidBrush(bgColor);
            using var tickPen = new Pen(tickColor, 1);
            using var textBrush = new SolidBrush(textColor);
            using var font = new Font("Consolas", 7f, FontStyle.Regular);

            var topRulerRect = new RectangleF(offsetX, 0, imgWidth * scaleX, RulerSize);
            g.FillRectangle(bgBrush, topRulerRect);

            for (int x = 0; x <= (int)imgWidth; x++)
            {
                if (x % MajorTickInterval == 0 || x % MinorTickInterval == 0)
                {
                    float screenX = offsetX + x * scaleX;

                    if (x % MajorTickInterval == 0)
                    {
                        g.DrawLine(tickPen, screenX, RulerSize - majorTickLength, screenX, RulerSize);
                        var text = x.ToString();
                        var textSize = g.MeasureString(text, font);
                        g.DrawString(text, font, textBrush, screenX - textSize.Width / 2, 1);
                    }
                    else
                    {
                        g.DrawLine(tickPen, screenX, RulerSize - minorTickLength, screenX, RulerSize);
                    }
                }
            }

            var leftRulerRect = new RectangleF(0, offsetY + RulerSize, RulerSize, imgHeight * scaleY - RulerSize);
            g.FillRectangle(bgBrush, leftRulerRect);

            for (int y = 0; y <= (int)imgHeight; y++)
            {
                if (y % MajorTickInterval == 0 || y % MinorTickInterval == 0)
                {
                    float screenY = offsetY + y * scaleY;

                    if (y % MajorTickInterval == 0)
                    {
                        g.DrawLine(tickPen, RulerSize - majorTickLength, screenY, RulerSize, screenY);
                        var text = y.ToString();
                        var textSize = g.MeasureString(text, font);
                        g.DrawString(text, font, textBrush, 1, screenY - textSize.Height / 2);
                    }
                    else
                    {
                        g.DrawLine(tickPen, RulerSize - minorTickLength, screenY, RulerSize, screenY);
                    }
                }
            }

            g.FillRectangle(bgBrush, 0, 0, RulerSize, RulerSize);
        }


        private void pictureBoxMinimap_MouseDown(object sender, MouseEventArgs e)
        {
            if (_mapEditor == null || minimapBounds.IsEmpty || e.Button != MouseButtons.Left)
                return;

            if ((ModifierKeys & Keys.Control) != 0)
            {
                _isMinimapPanning = true;
                _minimapPanStartClient = e.Location;
                _minimapPanStartOffset = new PointF(_mapEditor.PanOffsetX, _mapEditor.PanOffsetY);
                pictureBoxMinimap.Capture = true;
                pictureBoxMinimap.Cursor = Cursors.Hand;
                _skipNextMapClick = true;
                return;
            }

            var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
            var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);
            if (_mapEditor.TryBeginVertexDrag(screenPoint))
                _skipNextMapClick = true;
        }

        private void pictureBoxMinimap_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isMinimapPanning)
            {
                _isMinimapPanning = false;
                pictureBoxMinimap.Capture = false;
                pictureBoxMinimap.Cursor = Cursors.Default;
                _skipNextMapClick = true;
                pictureBoxMinimap.Invalidate();
                return;
            }

            if (_mapEditor == null || !_mapEditor.IsVertexDragging)
                return;

            _mapEditor.EndVertexDrag();
            _skipNextMapClick = true;
            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void pictureBoxMinimap_Click(object sender, MouseEventArgs e)
        {
            if (_skipNextMapClick)
            {
                _skipNextMapClick = false;
                return;
            }
            if (_mapEditor == null || minimapBounds.IsEmpty) return;
            var imagePoint = TranslatePictureBoxPointToImage(new PointF(e.X, e.Y), pictureBoxMinimap);
            var screenPoint = new PointF(minimapBounds.X + imagePoint.X, minimapBounds.Y + imagePoint.Y);
            bool preferNode = (ModifierKeys & Keys.Shift) != 0;
            _mapEditor.HandleClick(screenPoint, e.Button, preferNode, preferNode);
            pictureBoxMinimap.Invalidate();
            RefreshMapEditorPropertyPanel();
        }

        private void pictureBoxMinimap_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (ModifierKeys != Keys.Control || _mapEditor == null) return;

            float oldZoom = _mapEditor.ZoomScale;
            if (e.Delta > 0)
                _mapEditor.ZoomScale = Math.Min(10.0f, _mapEditor.ZoomScale + 0.1f);
            else
                _mapEditor.ZoomScale = Math.Max(0.5f, _mapEditor.ZoomScale - 0.1f);

            if (Math.Abs(oldZoom - _mapEditor.ZoomScale) > 0.001f)
            {
                ClampMinimapPanOffset();
                pictureBoxMinimap.Invalidate();
            }
        }

        private void pictureBoxMinimap_MouseLeave(object sender, EventArgs e)
        {
            try
            {
                if (_isMinimapPanning)
                {
                    _isMinimapPanning = false;
                    pictureBoxMinimap.Capture = false;
                    pictureBoxMinimap.Cursor = Cursors.Default;
                }

                lbl_MouseCoords.Text = "座標: (-, -)";

                if (_mapEditor != null)
                {
                    _mapEditor.UpdateMousePosition(new PointF(-1000, -1000));
                    _mapEditor.UpdateHoveredNode(new PointF(-1000, -1000));
                }

                pictureBoxMinimap.Invalidate();
            }
            catch (Exception ex)
            {
                Logger.Debug($"[UI] MouseLeave 錯誤: {ex.Message}");
            }
        }

        #endregion
    }
}
