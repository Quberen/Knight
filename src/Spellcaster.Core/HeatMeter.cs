using System;

namespace Spellcaster.Core;

public enum CastKind { SmallProjectile, NormalSpell, ChargedSpell, ChainDash }

/// <summary>Uses elapsed gameplay seconds; paused/loading time must not be supplied.</summary>
public sealed class HeatMeter
{
    public const double CooldownDelay = 0.5;
    public const double CoolingPerSecond = 20;
    private double idleSeconds;
    public double Heat { get; private set; }

    public void AddCast(CastKind kind)
    {
        double amount;
        switch (kind)
        {
            case CastKind.SmallProjectile: amount = 5; break;
            case CastKind.NormalSpell: amount = 25; break;
            case CastKind.ChargedSpell: amount = 40; break;
            case CastKind.ChainDash: amount = 15; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        Heat = Math.Min(100, Heat + amount);
        idleSeconds = 0;
    }

    /// <returns>True only on the transition from positive heat to zero.</returns>
    public bool Advance(double elapsedSeconds)
    {
        RequireFiniteNonnegative(elapsedSeconds, nameof(elapsedSeconds));
        double waiting = Math.Max(0, CooldownDelay - idleSeconds);
        double cooling = Math.Max(0, elapsedSeconds - waiting);
        idleSeconds = Math.Min(CooldownDelay, idleSeconds + elapsedSeconds);
        double before = Heat;
        Heat = Math.Max(0, Heat - cooling * CoolingPerSecond);
        if (Heat < 1e-9) Heat = 0; // Stable boundary under different frame partitions.
        return before > 0 && Heat == 0;
    }

    public void Reset() { Heat = 0; idleSeconds = 0; }

    internal static void RequireFiniteNonnegative(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
