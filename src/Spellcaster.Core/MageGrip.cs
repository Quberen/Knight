using System;

namespace Spellcaster.Core;

public enum GripRelease { None, SmallProjectile, NormalSpell }

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
        HeatMeter.RequireFiniteNonnegative(now, nameof(now));
        if (!Holding && CanAttack(now)) pressedAt = now;
    }

    public GripRelease Release(double now, int soul, bool spellUnlocked)
    {
        HeatMeter.RequireFiniteNonnegative(now, nameof(now));
        var start = pressedAt;
        pressedAt = null;
        if (!start.HasValue || !CanAttack(now)) return GripRelease.None;
        return now - start.Value + 1e-9 >= ChargeSeconds && soul >= SoulCost && spellUnlocked
            ? GripRelease.NormalSpell : GripRelease.SmallProjectile;
    }

    public void Commit(double now, double interval)
    {
        HeatMeter.RequireFiniteNonnegative(now, nameof(now));
        HeatMeter.RequireFiniteNonnegative(interval, nameof(interval));
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
        HeatMeter.RequireFiniteNonnegative(nailPogo, nameof(nailPogo));
        HeatMeter.RequireFiniteNonnegative(gravity, nameof(gravity));
        HeatMeter.RequireFiniteNonnegative(attackInterval, nameof(attackInterval));
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
