using System;
using Modding;
using Spellcaster.Core;

namespace Spellcaster.Mod;

/// <summary>
/// Present an expanded charm-notch total (up to 16) through GetPlayerIntHook only.
/// The PlayerData.charmSlots field is never written, so a save loaded without the
/// mod still has the vanilla earned count (capped at 11).
/// </summary>
internal sealed class CharmNotchRuntime
{
    private readonly Func<int> menuTotal;
    internal CharmNotchRuntime(Func<int> menuTotal) => this.menuTotal = menuTotal;

    internal void Install() => ModHooks.GetPlayerIntHook += OnGet;
    internal void Uninstall() => ModHooks.GetPlayerIntHook -= OnGet;

    private int OnGet(string name, int orig) =>
        name == "charmSlots" ? CharmNotches.Effective(orig, menuTotal()) : orig;
}
