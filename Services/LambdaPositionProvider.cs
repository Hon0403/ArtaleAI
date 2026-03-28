using System;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>以委派提供玩家座標，避免額外轉接類別。</summary>
    public class LambdaPositionProvider : IPlayerPositionProvider
    {
        private readonly Func<SdPointF?> _positionGetter;

        public LambdaPositionProvider(Func<SdPointF?> positionGetter)
        {
            _positionGetter = positionGetter ?? throw new ArgumentNullException(nameof(positionGetter));
        }

        public SdPointF? GetCurrentPosition() => _positionGetter();
    }
}
