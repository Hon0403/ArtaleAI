using System;
using System.Drawing;
using ArtaleAI.Models.Config;
using ArtaleAI.Utils;

namespace ArtaleAI.Core.Domain.Navigation
{
    /// <summary>依 <see cref="ArrivalPolicy"/> 驗收玩家是否到達執行目標。</summary>
    public static class ArrivalValidator
    {
        public static bool IsArrived(
            PointF playerPos,
            ExecutionTarget target,
            PlatformGeometryIndex? geometry)
        {
            return Diagnose(playerPos, target, geometry).Passed;
        }

        public static ArrivalDiagnostic Diagnose(
            PointF playerPos,
            ExecutionTarget target,
            PlatformGeometryIndex? geometry)
        {
            return target.Policy switch
            {
                ArrivalPolicy.PointHitbox => DiagnosePointHitbox(playerPos, target),
                ArrivalPolicy.PlatformStand => DiagnosePlatformStand(playerPos, target, geometry, strictX: false),
                ArrivalPolicy.JumpTakeoff => DiagnosePlatformStand(playerPos, target, geometry, strictX: true),
                ArrivalPolicy.RopeLanding => DiagnoseRopeLanding(playerPos, target, geometry),
                _ => new ArrivalDiagnostic
                {
                    Passed = false,
                    Policy = target.Policy,
                    NodeId = target.NodeId,
                    PlatformId = target.PlatformId,
                    PlayerPos = playerPos,
                    TargetX = target.TargetX,
                    AnchorY = target.AnchorY,
                    FailReason = "UNKNOWN_POLICY",
                    Attribution = "程式-未知策略"
                }
            };
        }

        public static void LogDiagnostic(ArrivalDiagnostic diagnostic, bool asWarning = false)
        {
            string line = diagnostic.FormatLine();
            if (asWarning)
                Logger.Warning(line);
            else
                Logger.Debug(line);
        }

        public static void LogRejection(PointF playerPos, ExecutionTarget target, PlatformGeometryIndex? geometry)
        {
            LogDiagnostic(Diagnose(playerPos, target, geometry), asWarning: true);
        }

        private static ArrivalDiagnostic DiagnosePointHitbox(PointF playerPos, ExecutionTarget target)
        {
            bool inside = target.PointHitbox?.Contains(playerPos.X, playerPos.Y) ?? false;
            float xErr = Math.Abs(playerPos.X - target.TargetX);
            float yErrAnchor = Math.Abs(playerPos.Y - target.AnchorY);

            return new ArrivalDiagnostic
            {
                Passed = inside,
                Policy = ArrivalPolicy.PointHitbox,
                NodeId = target.NodeId,
                PlatformId = target.PlatformId,
                PlayerPos = playerPos,
                TargetX = target.TargetX,
                AnchorY = target.AnchorY,
                XErr = xErr,
                YErrVsAnchor = yErrAnchor,
                XTol = (float)AppConfig.Instance.Navigation.PlatformHitboxWidth,
                YTol = (float)AppConfig.Instance.Navigation.PlatformHitboxHeight,
                FailReason = inside ? "" : "HITBOX_MISS",
                Attribution = inside ? "通過" : ClassifyHitboxAttribution(xErr, yErrAnchor)
            };
        }

        private static ArrivalDiagnostic DiagnosePlatformStand(
            PointF playerPos,
            ExecutionTarget target,
            PlatformGeometryIndex? geometry,
            bool strictX)
        {
            var nav = AppConfig.Instance.Navigation;
            float xTol = (float)nav.WalkAlignTolerancePx;
            float yTol = (float)nav.SlopeStandYTolerancePx;
            float xErr = Math.Abs(playerPos.X - target.TargetX);
            float yErrAnchor = Math.Abs(playerPos.Y - target.AnchorY);

            float? expectedY = null;
            bool hasProjection = false;
            bool extrapolated = false;
            float yErrExp = yErrAnchor;

            if (!string.IsNullOrEmpty(target.PlatformId) &&
                geometry != null &&
                geometry.TryProjectStandY(target.PlatformId, target.TargetX, out float projectedY, out extrapolated))
            {
                hasProjection = true;
                expectedY = projectedY;
                yErrExp = Math.Abs(playerPos.Y - projectedY);
            }

            if (xErr > xTol)
            {
                return BuildPlatformDiagnostic(
                    false, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                    expectedY, hasProjection, extrapolated, strictX,
                    "X_OVER", "程式-X未對齊");
            }

            if (hasProjection)
            {
                float effectiveYTol = extrapolated ? yTol * 1.5f : yTol;
                if (yErrExp > effectiveYTol)
                {
                    string attribution = extrapolated
                        ? "標記-折線外推區"
                        : (xErr <= xTol ? "標記-落點偏離投影" : "混合-XY偏差");
                    return BuildPlatformDiagnostic(
                        false, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, effectiveYTol,
                        expectedY, hasProjection, extrapolated, strictX,
                        "Y_PROJECTION_OVER", attribution);
                }

                return BuildPlatformDiagnostic(
                    true, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, effectiveYTol,
                    expectedY, hasProjection, extrapolated, strictX, "", "通過");
            }

            if (strictX)
            {
                return BuildPlatformDiagnostic(
                    true, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                    expectedY, hasProjection, extrapolated, strictX, "", "通過");
            }

            if (yErrAnchor > yTol)
            {
                return BuildPlatformDiagnostic(
                    false, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                    expectedY, hasProjection, extrapolated, strictX,
                    "Y_ANCHOR_OVER", "標記-錨點Y偏差");
            }

            return BuildPlatformDiagnostic(
                true, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                expectedY, hasProjection, extrapolated, strictX, "", "通過");
        }

        private static ArrivalDiagnostic DiagnoseRopeLanding(
            PointF playerPos,
            ExecutionTarget target,
            PlatformGeometryIndex? geometry)
        {
            var nav = AppConfig.Instance.Navigation;
            float xTol = (float)nav.WalkAlignTolerancePx;
            float yTol = (float)nav.RopeLandingYTolerancePx;
            float ropeXTol = (float)nav.RopeSegmentXTolerancePx;
            float xErr = Math.Abs(playerPos.X - target.TargetX);
            float yErrAnchor = Math.Abs(playerPos.Y - target.AnchorY);

            float expectedY = target.AnchorY;
            bool hasProjection = false;
            bool extrapolated = false;

            if (!string.IsNullOrEmpty(target.PlatformId) &&
                geometry != null &&
                geometry.TryProjectStandY(target.PlatformId, target.TargetX, out float projectedY, out extrapolated))
            {
                hasProjection = true;
                expectedY = projectedY;
            }

            float yErrExp = Math.Abs(playerPos.Y - expectedY);

            if (xErr > xTol)
            {
                return BuildRopeDiagnostic(
                    false, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                    expectedY, hasProjection, extrapolated, "X_OVER", "程式-X未對齊");
            }

            if (target.RopeX.HasValue && Math.Abs(playerPos.X - target.RopeX.Value) > ropeXTol)
            {
                return BuildRopeDiagnostic(
                    false, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                    expectedY, hasProjection, extrapolated, "ROPE_X_OVER", "程式-繩X未對齊");
            }

            if (yErrExp > yTol)
            {
                return BuildRopeDiagnostic(
                    false, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                    expectedY, hasProjection, extrapolated, "Y_LANDING_OVER",
                    yErrExp > yTol * 2 ? "標記-繩落點Y偏差" : "程式-尚未離繩落地");
            }

            return BuildRopeDiagnostic(
                true, target, playerPos, xErr, yErrExp, yErrAnchor, xTol, yTol,
                expectedY, hasProjection, extrapolated, "", "通過");
        }

        private static ArrivalDiagnostic BuildPlatformDiagnostic(
            bool passed,
            ExecutionTarget target,
            PointF playerPos,
            float xErr,
            float yErrExp,
            float yErrAnchor,
            float xTol,
            float yTol,
            float? expectedY,
            bool hasProjection,
            bool extrapolated,
            bool strictX,
            string failReason,
            string attribution)
        {
            return new ArrivalDiagnostic
            {
                Passed = passed,
                Policy = strictX ? ArrivalPolicy.JumpTakeoff : ArrivalPolicy.PlatformStand,
                NodeId = target.NodeId,
                PlatformId = target.PlatformId,
                PlayerPos = playerPos,
                TargetX = target.TargetX,
                AnchorY = target.AnchorY,
                ExpectedY = expectedY,
                XErr = xErr,
                YErrVsExpected = yErrExp,
                YErrVsAnchor = yErrAnchor,
                XTol = xTol,
                YTol = yTol,
                HasProjection = hasProjection,
                Extrapolated = extrapolated,
                FailReason = failReason,
                Attribution = attribution
            };
        }

        private static ArrivalDiagnostic BuildRopeDiagnostic(
            bool passed,
            ExecutionTarget target,
            PointF playerPos,
            float xErr,
            float yErrExp,
            float yErrAnchor,
            float xTol,
            float yTol,
            float expectedY,
            bool hasProjection,
            bool extrapolated,
            string failReason,
            string attribution)
        {
            return new ArrivalDiagnostic
            {
                Passed = passed,
                Policy = ArrivalPolicy.RopeLanding,
                NodeId = target.NodeId,
                PlatformId = target.PlatformId,
                RopeX = target.RopeX,
                PlayerPos = playerPos,
                TargetX = target.TargetX,
                AnchorY = target.AnchorY,
                ExpectedY = expectedY,
                XErr = xErr,
                YErrVsExpected = yErrExp,
                YErrVsAnchor = yErrAnchor,
                XTol = xTol,
                YTol = yTol,
                HasProjection = hasProjection,
                Extrapolated = extrapolated,
                FailReason = failReason,
                Attribution = attribution
            };
        }

        private static string ClassifyHitboxAttribution(float xErr, float yErrAnchor)
        {
            float xTol = (float)AppConfig.Instance.Navigation.PlatformHitboxWidth;
            float yTol = (float)AppConfig.Instance.Navigation.PlatformHitboxHeight;
            if (xErr <= xTol && yErrAnchor <= yTol)
                return "標記-Hitbox過嚴";
            if (xErr > xTol && yErrAnchor <= yTol)
                return "標記-X偏差";
            if (xErr <= xTol && yErrAnchor > yTol)
                return "標記-Y偏差";
            return "混合-XY偏差";
        }
    }
}
