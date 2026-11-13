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
/// these times. Fast Casting multiplies only the second total threshold by 1.5.
/// The first store is unaffected by either modifier. Glow begins at 0.2s of
/// accumulated gameplay time; the second flash also requires its soul cost.
/// </summary>
public sealed class GripAttack
{
    public const double SmallShotIntervalFactor = 0.9;
    public static double SmallShotInterval(double quickSlashCooldown)
    {
        Numbers.RequireFiniteNonnegative(quickSlashCooldown, nameof(quickSlashCooldown));
        return SmallShotIntervalFactor * quickSlashCooldown;
    }

    public const double TapSeconds = 0.2;
    public const double GlowSeconds = 0.2;
    public const double ChargeSeconds = 0.45;
    public const int SoulCost = 25;
    private double? pressedAt;
    private double nextAttack;
    public bool Holding => pressedAt.HasValue;

    public static bool MustDropCharge(bool canPlay, bool transitioning, bool dead,
        bool hazardDeath, bool hazardRespawning, bool noInput) =>
        !canPlay || transitioning || dead || hazardDeath || hazardRespawning || noInput;

    public bool CanAttack(double now) => now + 1e-9 >= nextAttack;
    private double lastTick;
    public double ChargeElapsed { get; private set; }
    private bool tapEligible;

    public static bool CanCharge(bool attacking, bool controlRelinquished, bool recoiling,
        bool recoilingLeft, bool recoilingRight) =>
        !attacking && !controlRelinquished && !recoiling && !recoilingLeft && !recoilingRight;

    // now is unscaled wall time; deltaSeconds is gameplay time for this frame.
    // A missing press edge is intentionally irrelevant to charge accumulation.
    public void Tick(double now, bool isPressed, bool canCharge, bool paused = false,
        bool reset = false, double? deltaSeconds = null)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        double dt = deltaSeconds ?? (Holding ? Math.Max(0, now - lastTick) : 0);
        Numbers.RequireFiniteNonnegative(dt, nameof(deltaSeconds));
        lastTick = now;
        if (isPressed && !Holding) { pressedAt = now; tapEligible = true; }
        if (reset || (paused && !isPressed)) ResetCharge();
        if (paused && !isPressed) CancelHold();
        if (isPressed && canCharge && !paused && !reset) ChargeElapsed += dt;
    }

    // Preserve the physical hold and its original age across a reset. It must
    // not turn a long, interrupted hold into a fresh tap.
    public void ResetCharge() { ChargeElapsed = 0; tapEligible = false; }

    public double Held(double now)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        return pressedAt.HasValue ? Math.Max(0, now - pressedAt.Value) : 0;
    }

    /// <summary>0 = not stored, 1 = first charge stored, 2 = Shaman Master second charge stored.</summary>
    public int StoredTier(double now, bool shamanMaster = false, bool shamanStone = false, bool fastCasting = false,
        int soul = int.MaxValue, bool spellTwister = false)
    {
        if (!Holding) return 0;
        double held = ChargeElapsed + 1e-9;
        if (shamanMaster && held >= SecondChargeSeconds(shamanStone, fastCasting) &&
            soul >= Cost(GripRelease.ChargedSpell, spellTwister)) return 2;
        if (held >= ChargeSeconds) return 1;
        return 0;
    }

    /// <summary>Winding particles: from GlowSeconds, until the next stored tier.</summary>
    public bool Winding(double now, bool shamanMaster = false, bool shamanStone = false, bool fastCasting = false,
        int soul = int.MaxValue, bool spellTwister = false)
    {
        if (!Holding) return false;
        double held = ChargeElapsed + 1e-9;
        if (held < GlowSeconds) return false;
        int tier = StoredTier(now, shamanMaster, shamanStone, fastCasting, soul, spellTwister);
        if (tier >= 2) return false;
        if (tier == 1) return shamanMaster && held < SecondChargeSeconds(shamanStone, fastCasting);
        return held < ChargeSeconds;
    }

    public GripRelease Release(double now, int soul, bool spellUnlocked,
        bool shamanMaster = false, bool shamanStone = false, bool fastCasting = false, bool spellTwister = false,
        bool canFire = true, bool deferCharged = false)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        var start = pressedAt;
        double charged = ChargeElapsed + 1e-9;
        bool tap = tapEligible;
        CancelHold();
        if (!start.HasValue || !CanAttack(now)) return GripRelease.None;
        double held = now - start.Value + 1e-9;
        // Genuine tap only. Past the tap window an unfinished charge is a no-op,
        // matching vanilla nail arts (release before full charge does nothing extra).
        if (tap && held < TapSeconds) return canFire ? GripRelease.SmallProjectile : GripRelease.None;
        if (!canFire && !deferCharged) return GripRelease.None;
        if (!spellUnlocked) return GripRelease.None;
        if (shamanMaster && charged >= SecondChargeSeconds(shamanStone, fastCasting) &&
            soul >= Cost(GripRelease.ChargedSpell, spellTwister)) return GripRelease.ChargedSpell;
        if (charged >= ChargeSeconds && soul >= SoulCost) return GripRelease.NormalSpell;
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

    public void CancelHold() { pressedAt = null; ResetCharge(); }
    public void Reset() { CancelHold(); nextAttack = 0; lastTick = 0; }

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


/// <summary>A released spell retains its direction while waiting for the spell FSM.</summary>
public sealed class GripPendingCast
{
    private GripRelease release;
    private int direction;
    private double until;

    public void Queue(double now, int releaseDirection, GripRelease kind)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        direction = releaseDirection;
        release = kind == GripRelease.NormalSpell || kind == GripRelease.ChargedSpell
            ? kind : GripRelease.None;
        until = now + 1;
    }

    public bool TryGet(double now, out int releaseDirection, out GripRelease kind, bool canFire = true)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        if (now > until) Clear();
        releaseDirection = direction;
        kind = release;
        return canFire && release != GripRelease.None;
    }

    public void Clear() => release = GripRelease.None;
}
