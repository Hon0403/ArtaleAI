using ArtaleAI.Vision;
using OpenCvSharp;

namespace ArtaleAI.Application.Pipeline
{
    /// <summary>換頻流程可觀測的畫面相位（錨點優先序由「最下游」往上判）。</summary>
    public enum ChangeChannelPhase
    {
        Unknown = 0,
        InGame = 1,
        GameMenu = 2,
        ChannelList = 3,
        Confirm = 4,
        Login = 5,
        SelectCharacter = 6
    }

    public readonly record struct ChangeChannelPhaseSnapshot(
        ChangeChannelPhase Phase,
        double BestScore,
        string AnchorName);

    /// <summary>
    /// 以已載入換頻模板＋小地圖有無推斷畫面相位。
    /// 主條件＝相位；清彈窗只在 Unknown（讀取／被擋）時當次條件。
    /// </summary>
    public static class ChangeChannelScreenProbe
    {
        public static ChangeChannelPhaseSnapshot Probe(
            Mat frame,
            double classifyThreshold,
            Mat? menuTemplate,
            Mat? pickTemplate,
            Mat? confirmTemplate,
            Mat? loginTemplate,
            Mat? selectCharacterTemplate,
            bool hasMinimap)
        {
            if (frame.Empty())
                return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.Unknown, 0, "empty");

            double threshold = Math.Clamp(classifyThreshold, 0.45, 0.95);

            // 下游優先：避免選角畫面同時殘留登入字樣時判錯。
            if (TryScore(frame, selectCharacterTemplate, threshold, out double selectScore))
                return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.SelectCharacter, selectScore, "選擇角色");

            if (TryScore(frame, loginTemplate, threshold, out double loginScore))
                return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.Login, loginScore, "登入");

            if (TryScore(frame, confirmTemplate, threshold, out double confirmScore))
                return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.Confirm, confirmScore, "確定");

            if (TryScore(frame, pickTemplate, threshold, out double pickScore))
                return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.ChannelList, pickScore, "頻道列表");

            if (TryScore(frame, menuTemplate, threshold, out double menuScore))
                return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.GameMenu, menuScore, "頻道");

            if (hasMinimap)
                return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.InGame, 1.0, "小地圖");

            return new ChangeChannelPhaseSnapshot(ChangeChannelPhase.Unknown, 0, "無錨點");
        }

        /// <summary>本步「應該」看到的相位。</summary>
        public static ChangeChannelPhase ExpectedPhase(int flowStepOrdinal) =>
            flowStepOrdinal switch
            {
                0 => ChangeChannelPhase.GameMenu,       // OpenMenu：Esc 後應見頻道
                1 => ChangeChannelPhase.GameMenu,       // ClickChannel
                2 => ChangeChannelPhase.ChannelList,    // SelectCell
                3 => ChangeChannelPhase.Confirm,        // ClickConfirm
                4 => ChangeChannelPhase.Login,          // ClickLogin
                5 => ChangeChannelPhase.SelectCharacter,
                _ => ChangeChannelPhase.Unknown
            };

        /// <summary>本步成功後下一相位（用於「讀取完成但模板不穩」軟前進）。</summary>
        public static ChangeChannelPhase NextPhase(int flowStepOrdinal) =>
            flowStepOrdinal switch
            {
                0 => ChangeChannelPhase.GameMenu,
                1 => ChangeChannelPhase.ChannelList,
                2 => ChangeChannelPhase.Confirm,
                3 => ChangeChannelPhase.Login,
                4 => ChangeChannelPhase.SelectCharacter,
                5 => ChangeChannelPhase.InGame,
                _ => ChangeChannelPhase.Unknown
            };

        public static bool IsBehind(ChangeChannelPhase actual, ChangeChannelPhase expected)
        {
            if (actual is ChangeChannelPhase.Unknown or ChangeChannelPhase.InGame)
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
