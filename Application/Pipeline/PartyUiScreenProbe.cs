using ArtaleAI.Vision;
using OpenCvSharp;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>隊伍重建可觀測的畫面相位（主條件＝相位，按鍵只是動作）。</summary>
    public enum PartyUiPhase
    {
        Unknown = 0,
        /// <summary>遊戲中、無隊伍面板、尚無血條（需建隊）。</summary>
        InGameNoParty = 1,
        /// <summary>隊伍面板開著（穩定見到「新建」）。</summary>
        PartyPanel = 2,
        /// <summary>遊戲中且已偵測到隊伍血條（成功）。</summary>
        InGameWithParty = 3
    }

    public readonly record struct PartyUiPhaseSnapshot(
        PartyUiPhase Phase,
        double BestScore,
        string AnchorName);

    /// <summary>
    /// 以「新建」模板＋小地圖＋血條有無推斷隊伍 UI 相位。
    /// 優先序：面板錨點 → 血條 → 小地圖 → Unknown。
    /// </summary>
    public static class PartyUiScreenProbe
    {
        public static PartyUiPhaseSnapshot Probe(
            Mat frame,
            double classifyThreshold,
            Mat? createTemplate,
            bool hasMinimap,
            bool hasBloodBar)
        {
            if (frame.Empty())
                return new PartyUiPhaseSnapshot(PartyUiPhase.Unknown, 0, "empty");

            double threshold = Math.Clamp(classifyThreshold, 0.45, 0.95);

            if (TryScore(frame, createTemplate, threshold, out double createScore))
                return new PartyUiPhaseSnapshot(PartyUiPhase.PartyPanel, createScore, "新建");

            if (hasBloodBar)
                return new PartyUiPhaseSnapshot(PartyUiPhase.InGameWithParty, 1.0, "隊伍血條");

            if (hasMinimap)
                return new PartyUiPhaseSnapshot(PartyUiPhase.InGameNoParty, 1.0, "小地圖");

            return new PartyUiPhaseSnapshot(PartyUiPhase.Unknown, 0, "無錨點");
        }

        /// <summary>本步「應該」看到的相位。</summary>
        public static PartyUiPhase ExpectedPhase(int flowStepOrdinal) =>
            flowStepOrdinal switch
            {
                0 => PartyUiPhase.PartyPanel,       // OpenWindow
                1 => PartyUiPhase.PartyPanel,       // ClickCreate
                2 => PartyUiPhase.InGameNoParty,    // CloseWindow：面板應關，血條可能尚未回來
                3 => PartyUiPhase.InGameWithParty,  // AwaitBloodBar
                _ => PartyUiPhase.Unknown
            };

        /// <summary>本步成功後可接受的下一相位（軟前進）。</summary>
        public static bool IsAtOrBeyondGoal(int flowStepOrdinal, PartyUiPhase actual)
        {
            if (actual == PartyUiPhase.Unknown)
                return false;

            return flowStepOrdinal switch
            {
                0 => actual == PartyUiPhase.PartyPanel
                     || actual == PartyUiPhase.InGameWithParty,
                1 => actual is PartyUiPhase.InGameNoParty
                     or PartyUiPhase.InGameWithParty,
                2 => actual is PartyUiPhase.InGameNoParty
                     or PartyUiPhase.InGameWithParty,
                3 => actual == PartyUiPhase.InGameWithParty,
                _ => false
            };
        }

        public static bool IsBehind(PartyUiPhase actual, PartyUiPhase expected)
        {
            if (actual is PartyUiPhase.Unknown)
                return false;
            return (int)actual < (int)expected;
        }

        private static bool TryScore(Mat frame, Mat? template, double threshold, out double score)
        {
            score = 0;
            if (template == null || template.Empty())
                return false;

            var peek = GameVisionCore.PeekBestMatch(frame, template);
            if (!peek.HasValue)
                return false;

            score = peek.Value.MaxValue;
            return score >= threshold;
        }
    }
}
