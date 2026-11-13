using System;

namespace Spellcaster.Core;

public enum GripRelease { None, SmallProjectile, NormalSpell, ChargedSpell }

/// <summary>
/// Quick Cast charge, independent of render/physics frame partitioning.
/// Tap (&lt; <see cref="TapSeconds"/>) fires a small shot. Holding past the tap
/// window but not to <see cref="ChargeSeconds"/> releases nothing, like an
/// unfinished vanilla nail art. At 0.45s the first charge is stored for as
/// long as the button stays held; Shaman Master then stores a second charge
/// at 1.5s (1.1s with Shaman Stone). Nailmaster's Glory does not shorten
/// these times.
/// </summary>
public sealed class GripAttack
{
    public const double TapSeconds = 0.2;
    public const double ChargeSeconds = 0.45;
    public const int SoulCost = 25;
    private double? pressedAt;
    private double nextAttack;
    public bool Holding => pressedAt.HasValue;

    public bool CanAttack(double now) => now + 1e-9 >= nextAttack;
    public void Press(double now)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        if (!Holding && CanAttack(now)) pressedAt = now;
    }

    public double Held(double now)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        return pressedAt.HasValue ? Math.Max(0, now - pressedAt.Value) : 0;
    }

    /// <summary>0 = not stored, 1 = first charge stored, 2 = Shaman Master second charge stored.</summary>
    public int StoredTier(double now, bool shamanMaster = false, bool shamanStone = false, bool fastCasting = false)
    {
        if (!Holding) return 0;
        double held = Held(now) + 1e-9;
        if (shamanMaster && held >= SecondChargeSeconds(shamanStone, fastCasting)) return 2;
        if (held >= ChargeSeconds) return 1;
        return 0;
    }

    /// <summary>Winding particles: after the tap window, until the next stored tier.</summary>
    public bool Winding(double now, bool shamanMaster = false, bool shamanStone = false, bool fastCasting = false)
    {
        if (!Holding) return false;
        double held = Held(now) + 1e-9;
        if (held < TapSeconds) return false;
        int tier = StoredTier(now, shamanMaster, shamanStone, fastCasting);
        if (tier >= 2) return false;
        if (tier == 1) return shamanMaster && held < SecondChargeSeconds(shamanStone, fastCasting);
        return held < ChargeSeconds;
    }

    public GripRelease Release(double now, int soul, bool spellUnlocked,
        bool shamanMaster = false, bool shamanStone = false, bool fastCasting = false, bool spellTwister = false)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        var start = pressedAt;
        pressedAt = null;
        if (!start.HasValue || !CanAttack(now)) return GripRelease.None;
        double held = now - start.Value + 1e-9;
        // Genuine tap only. Past the tap window an unfinished charge is a no-op,
        // matching vanilla nail arts (release before full charge does nothing extra).
        if (held < TapSeconds) return GripRelease.SmallProjectile;
        if (!spellUnlocked) return GripRelease.None;
        if (shamanMaster && held >= SecondChargeSeconds(shamanStone, fastCasting) &&
            soul >= Cost(GripRelease.ChargedSpell, spellTwister)) return GripRelease.ChargedSpell;
        if (held >= ChargeSeconds && soul >= SoulCost) return GripRelease.NormalSpell;
        return GripRelease.None;
    }

    public static double SecondChargeSeconds(bool shamanStone, bool fastCasting) =>
        (shamanStone ? 1.1 : 1.5) * (fastCasting ? 1.5 : 1);

    public static int Cost(GripRelease release, bool spellTwister = false) => release switch
    {
        GripRelease.ChargedSpell => spellTwister ? 36 : 48,
        GripRelease.NormalSpell => SoulCost,
        _ => 0
    };

    public static CastKind Kind(GripRelease release) => release switch
    {
        GripRelease.ChargedSpell => CastKind.ChargedSpell,
        GripRelease.NormalSpell => CastKind.NormalSpell,
        GripRelease.SmallProjectile => CastKind.SmallProjectile,
        _ => throw new ArgumentOutOfRangeException(nameof(release))
    };

    public void Commit(double now, double interval)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        Numbers.RequireFiniteNonnegative(interval, nameof(interval));
        nextAttack = now + interval;
    }

    public void CancelHold() => pressedAt = null;
    public void Reset() { pressedAt = null; nextAttack = 0; }

    public static int Damage(int nailDamage) => (int)Math.Round(Math.Max(0, nailDamage) * 0.5);

    public static double Soul(bool mainOrbFull, bool catcher, bool eater) =>
        ((mainOrbFull ? 6 : 11) + (catcher ? (mainOrbFull ? 2 : 3) : 0)
        + (eater ? (mainOrbFull ? 6 : 8) : 0)) * 0.5;
}

/// <summary>Only a real landing or a successful pogo restores the first-shot lift.</summary>
public sealed class GripRecoil
{
    public int DownShots { get; private set; }

    public double Down(double velocityY, double nailPogo, double gravity, double attackInterval)
    {
        Numbers.RequireFiniteNonnegative(nailPogo, nameof(nailPogo));
        Numbers.RequireFiniteNonnegative(gravity, nameof(gravity));
        Numbers.RequireFiniteNonnegative(attackInterval, nameof(attackInterval));
        if (DownShots++ == 0)
        {
            // v*T - g*T*T/2 < 0 even for the first shot at the fastest allowed rate.
            return Math.Min(nailPogo * 0.25, gravity * attackInterval * 0.4);
        }
        // A repeated shot can brake a fall, but can never create upward/zero velocity.
        return Math.Min(-nailPogo * 0.1, velocityY + nailPogo * 0.15);
    }

    public double Up(double velocityY, double nailPogo, bool grounded) =>
        grounded ? velocityY : velocityY - nailPogo * 0.12;

    public double Pogo(double nailPogo) { Reset(); return nailPogo; }
    public void Land() => Reset();
    public void Reset() => DownShots = 0;
}

/// <summary>
/// Small shots always use the white Vengeful Spirit prefab, even after Shade Soul is unlocked.
/// Charged Quick Cast fireballs keep the unlocked tier.
/// </summary>
public static class GripProjectiles
{
    public const string VengefulSpiritState = "Fireball 1";
    public const string ShadeSoulState = "Fireball 2";

    public static string? SmallShotState(int fireballLevel) =>
        fireballLevel < 1 ? null : VengefulSpiritState;

    public static string? ChargedFireballState(int fireballLevel) =>
        fireballLevel < 1 ? null : fireballLevel > 1 ? ShadeSoulState : VengefulSpiritState;
}

