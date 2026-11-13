using System;
using System.Collections.Generic;

namespace Spellcaster.Core;

public enum CastKind { SmallProjectile, NormalSpell, ChargedSpell }

public static class SoulFormula
{
    /// <summary>Soul awarded per confirmed spell-hit segment. Easy to tweak.</summary>
    public const double SpellHitSoul = 9;

    // Vanilla Soul Catcher (+3) and Soul Eater (+8) stack additively on the flat per-hit value.
    public static double Spell(bool soulCatcher = false, bool soulEater = false) =>
        SpellHitSoul + (soulCatcher ? 3 : 0) + (soulEater ? 8 : 0);
}

/// <summary>Carry fractional soul rather than rounding every damage tick.</summary>
public sealed class SoulAccumulator
{
    public double Remainder { get; private set; }
    public int Add(double amount)
    {
        Numbers.RequireFiniteNonnegative(amount, nameof(amount));
        double total = Remainder + amount;
        if (total > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(amount));
        int whole = (int)Math.Floor(total + 1e-9);
        Remainder = Math.Max(0, total - whole);
        return whole;
    }
    public void Reset() => Remainder = 0;
}

/// <summary>
/// A segment is one damage pulse of a cast, regardless of target/collider count.
/// Projectiles retain their cast ID, even after the next spell starts.
/// Dedup is a correctness rule: the same (castId, segment) never pays twice.
/// </summary>
public sealed class SoulLedger
{
    private readonly Dictionary<long, HashSet<int>> casts = new Dictionary<long, HashSet<int>>();
    private long nextId;
    public int ActiveCastCount => casts.Count;

    public long BeginCast()
    {
        long id = checked(++nextId);
        casts.Add(id, new HashSet<int>());
        return id;
    }

    public double Hit(long castId, int segmentId, bool soulCatcher = false, bool soulEater = false)
    {
        if (segmentId < 0) throw new ArgumentOutOfRangeException(nameof(segmentId));
        if (!casts.TryGetValue(castId, out var segments) || segments.Contains(segmentId)) return 0;
        double amount = SoulFormula.Spell(soulCatcher, soulEater);
        segments.Add(segmentId);
        return amount;
    }

    public void EndCast(long castId) => casts.Remove(castId);
    // Never reuse IDs across scenes, reloads or charm toggles: stale projectiles stay invalid.
    public void Reset() => casts.Clear();
}
