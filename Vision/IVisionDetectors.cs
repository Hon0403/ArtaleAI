using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using OpenCvSharp;

namespace ArtaleAI.Vision
{
    /// <summary>視覺偵測器共同契約。</summary>
    public interface IVisionDetector : IDisposable
    {
    }

    /// <summary>視窗底部固定 UI 列之玩家 HP／MP 填充比例。</summary>
    public interface IPlayerVitalsDetector : IVisionDetector
    {
        PlayerVitalsSnapshot Detect(Mat frameMat, PlayerVitalsSettings settings);
    }
}
