using ArtaleAI.Application.Pipeline;
using ArtaleAI.Models.Config;
using ArtaleAI.Models.Detection;
using Xunit;

namespace ArtaleAI.Tests;

public sealed class AutoHealCoordinatorTests
{
    [Fact]
    public void SuccessfulRecovery_ClearsFailureCount()
    {
        var sut = new AutoHealCoordinator();
        var settings = CreateSettings(hpEnabled: true, mpEnabled: false);
        int taps = 0;

        Assert.False(sut.EvaluateAndHeal(settings, Snapshot(0.30, 1.0, 1), Utc(0), _ => taps++).ShouldRetreat);
        Assert.Equal(1, taps);

        // 回升 10%：視為有效，失敗計數清零
        Assert.False(sut.EvaluateAndHeal(settings, Snapshot(0.40, 1.0, 2), Utc(1000), _ => taps++).ShouldRetreat);

        // 再次掉血應可再按（不是被舊失敗卡住）
        Assert.False(sut.EvaluateAndHeal(settings, Snapshot(0.30, 1.0, 3), Utc(2000), _ => taps++).ShouldRetreat);
        Assert.Equal(2, taps);
    }

    [Fact]
    public void ThreeFailedHpHeals_TriggersRetreat()
    {
        var sut = new AutoHealCoordinator();
        var settings = CreateSettings(hpEnabled: true, mpEnabled: false);
        int taps = 0;
        HealRetreatSignal signal = default;

        // 每次：新讀值結算上一次失敗後，若仍低於門檻會立刻再按
        // t0 id1: tap#1
        // t1000 id2: fail1 + tap#2
        // t2000 id3: fail2 + tap#3
        // t3000 id4: fail3 → 撤退
        long readingId = 1;
        for (int step = 0; step < 4; step++)
        {
            signal = sut.EvaluateAndHeal(
                settings,
                Snapshot(0.30, 1.0, readingId++),
                Utc(step * 1000),
                _ => taps++);
        }

        Assert.Equal(3, taps);
        Assert.True(signal.ShouldRetreat);
        Assert.Equal(HealResourceKind.Hp, signal.FailedResource);
    }

    [Fact]
    public void HpAndMpFailures_AreIndependent()
    {
        var sut = new AutoHealCoordinator();
        var settings = CreateSettings(hpEnabled: true, mpEnabled: true);
        int taps = 0;

        // MP 維持高於門檻，只有 HP 累積失敗
        HealRetreatSignal signal = default;
        long readingId = 1;
        for (int step = 0; step < 4; step++)
        {
            signal = sut.EvaluateAndHeal(
                settings,
                Snapshot(0.30, 0.90, readingId++),
                Utc(step * 1000),
                _ => taps++);
        }

        Assert.True(signal.ShouldRetreat);
        Assert.Equal(HealResourceKind.Hp, signal.FailedResource);
        Assert.Equal(3, taps);
    }

    [Fact]
    public void SameReadingId_DoesNotCountAsFailure()
    {
        var sut = new AutoHealCoordinator();
        var settings = CreateSettings(hpEnabled: true, mpEnabled: false);
        int taps = 0;

        Assert.False(sut.EvaluateAndHeal(settings, Snapshot(0.30, 1.0, 1), Utc(0), _ => taps++).ShouldRetreat);
        Assert.Equal(1, taps);

        for (int i = 0; i < 5; i++)
        {
            Assert.False(sut.EvaluateAndHeal(
                settings,
                Snapshot(0.30, 1.0, 1),
                Utc(1000 + i * 100),
                _ => taps++).ShouldRetreat);
        }

        Assert.Equal(1, taps);
    }

    [Fact]
    public void AreEnabledVitalsAboveThreshold_RequiresAllEnabled()
    {
        var sut = new AutoHealCoordinator();
        var settings = CreateSettings(hpEnabled: true, mpEnabled: true);

        Assert.False(sut.AreEnabledVitalsAboveThreshold(settings, Snapshot(0.50, 0.20, 1)));
        Assert.False(sut.AreEnabledVitalsAboveThreshold(settings, Snapshot(0.20, 0.50, 2)));
        Assert.True(sut.AreEnabledVitalsAboveThreshold(settings, Snapshot(0.50, 0.50, 3)));
    }

    [Fact]
    public void DisabledResource_IsIgnoredForRecovery()
    {
        var sut = new AutoHealCoordinator();
        var settings = CreateSettings(hpEnabled: true, mpEnabled: false);

        Assert.True(sut.AreEnabledVitalsAboveThreshold(settings, Snapshot(0.50, 0.05, 1)));
    }

    [Fact]
    public void ClearFailureState_AllowsFreshAttemptsAfterRetreat()
    {
        var sut = new AutoHealCoordinator();
        var settings = CreateSettings(hpEnabled: true, mpEnabled: false);
        int taps = 0;

        long readingId = 1;
        for (int step = 0; step < 4; step++)
        {
            sut.EvaluateAndHeal(settings, Snapshot(0.30, 1.0, readingId++), Utc(step * 1000), _ => taps++);
        }

        Assert.Equal(3, taps);
        sut.ClearFailureState();

        var signal = sut.EvaluateAndHeal(
            settings,
            Snapshot(0.30, 1.0, readingId),
            Utc(10000),
            _ => taps++);

        Assert.False(signal.ShouldRetreat);
        Assert.Equal(4, taps);
    }

    private static AutoFarmSettings CreateSettings(bool hpEnabled, bool mpEnabled) =>
        new()
        {
            HealHpEnabled = hpEnabled,
            HealHpThresholdPercent = 40,
            HealHpHotkey = "Insert",
            HealMpEnabled = mpEnabled,
            HealMpThresholdPercent = 30,
            HealMpHotkey = "Delete",
            HealCooldownMs = 800,
            HealFailureAttempts = 3,
            HealMinRecoveryPercent = 3,
            HealObserveMs = 800
        };

    private static PlayerVitalsSnapshot Snapshot(double hp, double mp, long readingId) =>
        new()
        {
            HpRatio = hp,
            MpRatio = mp,
            HasFillReading = true,
            IsLayoutValid = true,
            ReadingId = readingId,
            MeasuredAtUtc = DateTime.UtcNow
        };

    private static DateTime Utc(int ms) =>
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
}
