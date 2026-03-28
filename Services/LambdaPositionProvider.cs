using System;
using SdPointF = System.Drawing.PointF;

namespace ArtaleAI.Services
{
    /// <summary>
    /// IPlayerPositionProvider 的 Lambda 實作。
    /// 架構考量：允許 UI 層以閉包方式提供位置資料來源，
    /// 避免建立不必要的介面卡類別，同時保持 NavigationExecutor 的可測試性。
    /// </summary>
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
