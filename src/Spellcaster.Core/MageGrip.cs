using System;

namespace Spellcaster.Core;

public enum GripRelease { None, SmallProjectile, NormalSpell, ChargedSpell }

/// <summary>Gameplay timestamps, independent of render/physics frame partitioning.</summary>
public sealed class GripAttack
{
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

    public GripRelease Release(double now, int soul, bool spellUnlocked,
        bool shamanMaster = false, bool shamanStone = false, bool fastCasting = false, bool spellTwister = false)
    {
        Numbers.RequireFiniteNonnegative(now, nameof(now));
        var start = pressedAt;
        pressedAt = null;
        if (!start.HasValue || !CanAttack(now)) return GripRelease.None;
        double held = now - start.Value + 1e-9;
        if (!spellUnlocked) return GripRelease.SmallProjectile;
        if (shamanMaster && held >= SecondChargeSeconds(shamanStone, fastCasting) &&
            soul >= Cost(GripRelease.ChargedSpell, spellTwister)) return GripRelease.ChargedSpell;
        return held >= ChargeSeconds && soul >= SoulCost ? GripRelease.NormalSpell : GripRelease.SmallProjectile;
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
