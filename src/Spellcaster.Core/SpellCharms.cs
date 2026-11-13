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

    /// <summary>
    /// Prefer the live Language.CurrentLanguage() code when it is available.
    /// Do not OR it with GameManager's saved gameLanguage — that stays on the
    /// previous setting through an in-session SwitchLanguage and would keep
    /// Chinese text after a switch to English (and vice versa).
    /// </summary>
    public static bool ResolveIsChinese(string? currentLanguage, string? gameManagerLanguage)
    {
        if (!string.IsNullOrEmpty(currentLanguage))
            return IsChineseLanguage(currentLanguage);
        return IsChineseLanguage(gameManagerLanguage);
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
            "轻点快捷施法向输入方向发射小复仇之魂（始终为白色，即使已解锁暗影之魂），伤害约为骨钉的一半，不耗魂；回魂为骨钉的一半。按住快捷施法蓄力（0.45 秒），蓄满后按住可保存并继续移动，第一段松开按方向施放原版已解锁法术（已解锁时为暗影之魂），大小和伤害保持原版，耗 25 魂；只有萨满大师第二段才是伤害 ×2 的大大法术。光效从 0.2 秒开始。",
        ShamanMasterId =>
            "需要已装备法师之握。第一段蓄满后继续按住快捷施法到第二段（1.5 秒；装备萨满之石时 1.1 秒），魂足够才再闪一次，同样可保存；第一段是原版法术，只有第二段是大大法术：伤害 ×2，范围 ×1.45，耗 48 魂（装备法术扭曲者时 36）。极速施法使第二段时间 ×1.5，为 2.25 / 1.65 秒。松开时魂不足第二段但至少 25 时施放第一段。",
        SoulCollectorId =>
            "法术命中每段固定回 9 魂，可与捕魂者、噬魂者一同生效。骨钉回魂减半。",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public static string EnglishDescription(int id) => id switch
    {
        MageGripId =>
            "Tap Quick Cast to fire a small Vengeful Spirit (always the white form, even with Shade Soul unlocked) in the input direction, dealing about half nail damage at no soul cost; the small shot restores half nail soul. Hold to charge (0.45s); once charged, keep holding to store it while moving, then release the first store to cast the vanilla unlocked spell (Shade Soul if unlocked) for 25 soul, with vanilla size and damage. Glow starts at 0.2s. Only Shaman Master's second store is the enlarged ×2 spell.",
        ShamanMasterId =>
            "Requires Mage's Grip to be equipped. After the first charge, keep holding Quick Cast to the second threshold (1.5s, or 1.1s with Shaman Stone) for a second flash only with enough soul. The first store is vanilla; only the second is the big spell: ×2 damage, ×1.45 range, 48 soul (36 with Spell Twister). Fast Casting multiplies the second time by 1.5: 2.25s / 1.65s. If soul falls below the second cost, release falls back to the first store with at least 25 soul.",
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

/// <summary>Lift only the added row, retaining clearance below the fourth row.</summary>
public static class CharmBoardLayout
{
    public const float FifthLift = 0.55f;
    public const float MinGap = 0.55f;

    public static float Lift(float fourthY, float fifthY) =>
        Math.Max(0f, Math.Min(FifthLift, fourthY - fifthY - MinGap));
}
