using System.Collections.Generic;
using UnityEngine;
using Spellcaster.Core;

namespace Spellcaster.Mod;

internal sealed class CastRecord
{
    public readonly long Id;
    public readonly CastKind Kind;
    public float Angle;
    public float Speed;
    public int Damage;
    public bool ShamanStone;
    public bool PogoUsed;
    public readonly HashSet<int> RewardedTargets = new HashSet<int>();
    public readonly int CreatedFrame = Time.frameCount;
    public int NextGroup = 10;
    public CastRecord(long id, CastKind kind) { Id = id; Kind = kind; }
}

/// <summary>
/// Shared by all colliders of a native damage source. Ordinals of accepted hits on
/// each HealthManager align AoE hits into segments; colliders never own counters.
/// Native HealthManager evasion gates repeated hits before this code runs.
/// </summary>
internal sealed class DamageSource
{
    private readonly Dictionary<int, (double Tick, int Ordinal)> targets = new Dictionary<int, (double, int)>();
    public readonly CastRecord Cast;
    public readonly int Group;
    public DamageSource(CastRecord cast, int group) { Cast = cast; Group = group; }

    public int Segment(int targetId, double physicsTick)
    {
        int ordinal = 0;
        if (targets.TryGetValue(targetId, out var last))
            ordinal = physicsTick == last.Tick ? last.Ordinal : last.Ordinal + 1;
        targets[targetId] = (physicsTick, ordinal);
        // A separate range per native damage phase / fluke, independent of collider IDs.
        return checked(Group * 100000 + ordinal);
    }
}
