using System;

namespace Spellcaster.Core;

public static class SpellDamage
{
    // D is the native damage before Shaman Stone, per spell damage phase.
    // Stage 3's temporary fast-casting switch changes charge time only.
    public static int Calculate(int baseDamage, bool charged, bool shamanStone, double fastMultiplier = 1)
    {
        Numbers.RequireFiniteNonnegative(fastMultiplier, nameof(fastMultiplier));
        return checked((int)Math.Round(Math.Max(0, baseDamage) * fastMultiplier *
            (charged ? 2 : 1) * (shamanStone ? 1.5 : 1)));
    }
}

public static class QuakeInvulnerability
{
    // Used only if a live HeroController cannot supply a valid native duration.
    public const double FallbackSeconds = 0.4;

    public static bool IsValid(double seconds) =>
        !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds >= 0 && seconds <= float.MaxValue;

    public static double Resolve(double? configured, double? native)
    {
        if (configured.HasValue && IsValid(configured.Value)) return configured.Value;
        if (native.HasValue && IsValid(native.Value)) return native.Value;
        return FallbackSeconds;
    }
}
