using System;

namespace Spellcaster.Core;

/// <summary>
/// Expand the equipped-charm notch total up to 16 without writing PlayerData.charmSlots.
/// A menu total of 0 means "use earned (vanilla)"; 12–16 is a requested total.
/// Effective never falls below the earned vanilla count and never exceeds ExpandedCap.
/// Because the PlayerData field is left untouched, disabling the mod leaves a vanilla save.
/// </summary>
public static class CharmNotches
{
    public const int VanillaCap = 11;
    public const int ExpandedCap = 16;

    /// <summary>
    /// Normalize a menu value: 0 or anything at or below the vanilla cap means "vanilla";
    /// higher requests are clamped to ExpandedCap.
    /// </summary>
    public static int ClampTotal(int menuTotal) =>
        menuTotal <= VanillaCap ? 0 : Math.Min(ExpandedCap, menuTotal);

    /// <summary>
    /// Runtime notch total presented to the game via GetPlayerIntHook.
    /// <paramref name="earned"/> is the real PlayerData value (vanilla upgrades, cap 11).
    /// </summary>
    public static int Effective(int earned, int menuTotal)
    {
        int safeEarned = Math.Max(0, earned);
        int target = ClampTotal(menuTotal) == 0 ? safeEarned : ClampTotal(menuTotal);
        int value = Math.Max(safeEarned, target);
        return Math.Min(ExpandedCap, value);
    }
}
