using System;

namespace Spellcaster.Core;

public enum ShadowDashKind { Normal, FreeShadow, PaidShadow, UnaffordableChain }

public readonly struct ShadowDashDecision
{
    public ShadowDashKind Kind { get; }
    public int SoulCost { get; }
    public bool IsChain => Kind == ShadowDashKind.PaidShadow || Kind == ShadowDashKind.UnaffordableChain;
    public bool IsShadow => Kind == ShadowDashKind.FreeShadow || Kind == ShadowDashKind.PaidShadow;
    internal ShadowDashDecision(ShadowDashKind kind, int soulCost = 0) { Kind = kind; SoulCost = soulCost; }
}

/// <summary>Decide without consuming anything; commit only an actual dash. Uses gameplay timestamps.</summary>
public sealed class ShadowChain
{
    public const double WindowSeconds = 0.3;
    public const double IdleResetSeconds = 1.0;
    public const double StationarySpeed = 0.01;
    public const int MaxAirChains = 1;
    private const double TimeTolerance = 1e-9;
    private double? lastShadowAt;
    private double? idleSince;
    private bool? wasGrounded;
    private int costIndex;
    public int AirChains { get; private set; }

    // Zero-based paid-chain index; the third and every later chain costs 33.
    public static int Cost(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return index == 0 ? 11 : index == 1 ? 22 : 33;
    }

    public static int SharpDamage(int baseDamage) => (int)Math.Round(Math.Max(0, baseDamage) * 0.5);

    /// <summary>
    /// Not an independent charm. Active only with Shade Cloak and both Dashmaster
    /// (charm 31) and Shaman Stone (charm 19) equipped.
    /// </summary>
    public static bool Active(bool hasShadowDash, bool dashmaster, bool shamanStone) =>
        hasShadowDash && dashmaster && shamanStone;


    public bool InWindow(double now)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        return lastShadowAt.HasValue && now >= lastShadowAt.Value &&
            now - lastShadowAt.Value <= WindowSeconds + TimeTolerance;
    }

    public bool CanExtraAirDash(double now) => InWindow(now) && AirChains < MaxAirChains;

    public ShadowDashDecision Decide(double now, bool nativeShadowReady, int soul, bool grounded)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        if (nativeShadowReady) return new ShadowDashDecision(ShadowDashKind.FreeShadow);
        if (!InWindow(now) || (!grounded && AirChains >= MaxAirChains))
            return new ShadowDashDecision(ShadowDashKind.Normal);
        int cost = Cost(costIndex);
        return soul >= cost ? new ShadowDashDecision(ShadowDashKind.PaidShadow, cost)
            : new ShadowDashDecision(ShadowDashKind.UnaffordableChain);
    }

    public void Commit(double now, ShadowDashDecision decision, bool grounded, bool actualShadow)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        idleSince = null;
        if (actualShadow && decision.Kind == ShadowDashKind.FreeShadow)
        {
            ResetSequence();
            lastShadowAt = now;
        }
        else if (decision.IsChain)
        {
            if (!grounded) AirChains = Math.Min(MaxAirChains, AirChains + 1);
            if (actualShadow)
            {
                costIndex = Math.Min(2, costIndex + 1);
                lastShadowAt = now;
            }
        }
    }

    public void Observe(double now, bool grounded, double horizontalVelocity, bool dashing)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        if (grounded && wasGrounded == false) ResetSequence();
        wasGrounded = grounded;
        if (dashing || Math.Abs(horizontalVelocity) > StationarySpeed)
        { idleSince = null; return; }
        if (!idleSince.HasValue) idleSince = now;
        if (now - idleSince.Value + TimeTolerance >= IdleResetSeconds) ResetSequence();
    }

    private void ResetSequence()
    {
        lastShadowAt = null;
        idleSince = null;
        costIndex = 0;
        AirChains = 0;
    }

    public void Reset() { ResetSequence(); wasGrounded = null; }

    /// <summary>
    /// Restart the 0.3s chain window from this instant (call when a shadow dash ends).
    /// Vanilla dash cooldown is longer than the window, so the window must be usable after the dash finishes.
    /// </summary>
    public void ExtendWindow(double now)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        if (lastShadowAt.HasValue) lastShadowAt = now;
    }
}
