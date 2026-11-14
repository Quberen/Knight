using System;

namespace Spellcaster.Core;

/// <summary>
/// Improved King's Soul (charm 44): a separate Spellcaster charm, not a replacement of vanilla
/// charm 36. Regen rate depends on the current integer soul amount.
/// Bands (DESIGN.md): 0–23 → 6/s, 24–65 → 3/s, 66+ → 2/s.
/// Prerequisite for equip/effect is vanilla King's Soul: <see cref="VanillaKingsoulState"/>.
/// </summary>
public static class ImprovedKingSoul
{
    public const int LowBandMax = 23;
    public const int MidBandMax = 65;
    public const double LowRate = 6;
    public const double MidRate = 3;
    public const double HighRate = 2;

    /// <summary>Vanilla <c>royalCharmState</c> for assembled King's Soul (not fragments, not Void Heart).</summary>
    public const int VanillaKingsoulState = 3;

    public static bool PrerequisiteMet(int royalCharmState) =>
        royalCharmState == VanillaKingsoulState;

    /// <summary>Soul regenerated per second at the given current (integer) soul amount.</summary>
    public static double RatePerSecond(int soul)
    {
        if (soul <= LowBandMax) return LowRate;
        if (soul <= MidBandMax) return MidRate;
        return HighRate;
    }
}

/// <summary>
/// Accumulates fractional soul and grants whole points. Rate is re-evaluated after every
/// whole soul (so a tick that crosses 24 or 66 splits at the boundary). <paramref name="soul"/>
/// is used only for the rate; <paramref name="maxGrant"/> is remaining capacity (orb + reserve).
/// Once the simulated amount reaches 66+ the rate stays at 2/s, including while filling reserve.
/// </summary>
public sealed class KingSoulRegen
{
    private const double Epsilon = 1e-9;

    public double Remainder { get; private set; }

    public void Reset() => Remainder = 0;

    public int Tick(double dt, int soul, int maxGrant, bool active)
    {
        Numbers.RequireFiniteNonnegative(dt, nameof(dt));
        if (!active || dt <= 0 || maxGrant <= 0) return 0;

        int visible = soul;
        int granted = 0;
        double time = dt;
        while (time > 0 && granted < maxGrant)
        {
            double rate = ImprovedKingSoul.RatePerSecond(visible);
            if (rate <= 0) break;
            double need = 1.0 - Remainder;
            if (need <= Epsilon)
            {
                Remainder = 0;
                granted++;
                if (visible < int.MaxValue) visible++;
                continue;
            }
            double timeNeeded = need / rate;
            if (time + Epsilon < timeNeeded)
            {
                Remainder += rate * time;
                if (Remainder >= 1.0 - Epsilon)
                {
                    Remainder = 0;
                    granted++;
                    if (visible < int.MaxValue) visible++;
                }
                break;
            }
            time = Math.Max(0, time - timeNeeded);
            Remainder = 0;
            granted++;
            if (visible < int.MaxValue) visible++;
        }
        if (granted >= maxGrant) Remainder = 0;
        return granted;
    }
}
