using System;

namespace Spellcaster.Core;

/// <summary>
/// The three Spellcaster charms that appear on the charm board.
/// IDs are fixed at 41–43 (immediately after vanilla 1–40) so saves and language keys stay stable.
/// Ownership and equipped flags are stored in the mod's local settings, not in PlayerData fields.
/// </summary>
public static class SpellCharms
{
    public const int MageGripId = 41;
    public const int ShamanMasterId = 42;
    public const int SoulCollectorId = 43;
    public const int FirstId = MageGripId;
    public const int LastId = SoulCollectorId;
    public const int Count = 3;

    public const int DefaultGripCost = 1;
    public const int DefaultShamanCost = 3;
    public const int DefaultCollectorCost = 2; // DESIGN.md

    public const int IconVariants = 3;

    // User-picked appearances: Grip C (warm gold), Shaman A (amber), Collector A (soul teal).
    public const int DefaultGripIcon = 2;
    public const int DefaultShamanIcon = 0;
    public const int DefaultCollectorIcon = 0;

    public static readonly int[] Ids = { MageGripId, ShamanMasterId, SoulCollectorId };

    public static bool IsOurs(int id) => id >= FirstId && id <= LastId;

    public static int IndexOf(int id)
    {
        if (!IsOurs(id)) throw new ArgumentOutOfRangeException(nameof(id), id, "Not a Spellcaster charm id.");
        return id - FirstId;
    }

    /// <summary>Parse gotCharm_N / equippedCharm_N / newCharm_N / charmCost_N / CHARM_NAME_N / CHARM_DESC_N.</summary>
    public static bool TryParse(string? key, out int id)
    {
        id = 0;
        if (string.IsNullOrEmpty(key)) return false;
        int under = key!.LastIndexOf('_');
        if (under < 0 || under == key.Length - 1) return false;
        if (!int.TryParse(key.Substring(under + 1), out int parsed) || !IsOurs(parsed)) return false;
        string prefix = key.Substring(0, under + 1);
        if (prefix != "gotCharm_" && prefix != "equippedCharm_" && prefix != "newCharm_"
            && prefix != "charmCost_" && prefix != "CHARM_NAME_" && prefix != "CHARM_DESC_")
            return false;
        id = parsed;
        return true;
    }

    public static bool IsNameKey(string key, out int id) =>
        TryParse(key, out id) && key.StartsWith("CHARM_NAME_", StringComparison.Ordinal);

    public static bool IsDescKey(string key, out int id) =>
        TryParse(key, out id) && key.StartsWith("CHARM_DESC_", StringComparison.Ordinal);

    /// <summary>True for ZH and regional ZH_* codes (ZH_CN, ZH_TW, …).</summary>
    public static bool IsChineseLanguage(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) return false;
        string u = languageCode!.ToUpperInvariant();
        return u == "ZH" || u.StartsWith("ZH_", StringComparison.Ordinal);
    }

    public static string EnglishName(int id) => id switch
    {
        MageGripId => "Mage's Grip",
        ShamanMasterId => "Shaman Master",
        SoulCollectorId => "Soul Collector",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public static string ChineseName(int id) => id switch
    {
        MageGripId => "法师之握",
        ShamanMasterId => "萨满大师",
        SoulCollectorId => "灵魂收集者",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    /// <summary>Name in a single language: Chinese when <paramref name="chinese"/> is true, otherwise English.</summary>
    public static string DisplayName(int id, bool chinese) =>
        chinese ? ChineseName(id) : EnglishName(id);

    public static string ChineseDescription(int id) => id switch
    {
        MageGripId =>
            "轻点攻击向输入方向发射小复仇之魂（始终为白色，即使已解锁暗影之魂），伤害约为骨钉的一半，不耗魂；回魂为骨钉的一半。按住 0.45 秒松开施放已解锁法术，耗 25 魂。禁用骨钉技艺。",
        ShamanMasterId =>
            "需要已装备法师之握。按住攻击到第二段（1.5 秒；装备萨满之石时 1.1 秒）施放更强蓄力法术：伤害 ×2，范围增大，耗 48 魂（装备法术扭曲者时 36）。",
        SoulCollectorId =>
            "法术命中每段固定回 9 魂，可与捕魂者、噬魂者一同生效。骨钉回魂减半。",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public static string EnglishDescription(int id) => id switch
    {
        MageGripId =>
            "Tap attack to fire a small Vengeful Spirit (always the white form, even with Shade Soul unlocked) in the input direction, dealing about half nail damage at no soul cost; nail soul gain is halved. Hold 0.45s and release to cast the unlocked spell for 25 soul. Nail arts are disabled.",
        ShamanMasterId =>
            "Requires Mage's Grip to be equipped. Hold attack to the second threshold (1.5s, or 1.1s with Shaman Stone) for a stronger charged spell: double damage, larger range, 48 soul (36 with Spell Twister).",
        SoulCollectorId =>
            "Each damaging spell hit restores a flat 9 soul, stacking with Soul Catcher and Soul Eater. Nail soul gain is halved.",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public static string DisplayDescription(int id, bool chinese) =>
        chinese ? ChineseDescription(id) : EnglishDescription(id);

    /// <summary>User-picked default icon variant for this charm (0=A, 1=B, 2=C).</summary>
    public static int DefaultIconVariant(int id) => id switch
    {
        MageGripId => DefaultGripIcon,
        ShamanMasterId => DefaultShamanIcon,
        SoulCollectorId => DefaultCollectorIcon,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public static int ClampCost(int id, int cost)
    {
        int fallback = DefaultCost(id);
        if (id == MageGripId) return Math.Max(1, Math.Min(2, cost <= 0 ? fallback : cost));
        if (id == ShamanMasterId) return Math.Max(1, Math.Min(5, cost <= 0 ? fallback : cost));
        if (id == SoulCollectorId) return Math.Max(1, Math.Min(3, cost <= 0 ? fallback : cost));
        throw new ArgumentOutOfRangeException(nameof(id));
    }

    public static int DefaultCost(int id) => id switch
    {
        MageGripId => DefaultGripCost,
        ShamanMasterId => DefaultShamanCost,
        SoulCollectorId => DefaultCollectorCost,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public static int ClampVariant(int variant) =>
        Math.Max(0, Math.Min(IconVariants - 1, variant));
}
