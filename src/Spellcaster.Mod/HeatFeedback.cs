using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>
/// Vanilla-only overheat feedback: the soul vessel tints white to orange-red,
/// the Knight uses the Fury red pulse above 80 heat, and cooling to zero
/// plays the soul vessel's White Flash (the Kingsoul-style blink).
/// </summary>
internal sealed class HeatFeedback
{
    private const double FuryThreshold = 80;
    private static readonly Color Hot = new Color(1f, 0.31f, 0f, 1f);
    private static readonly FieldInfo? FlashState = typeof(SpriteFlash).GetField("flashingState", BindingFlags.Instance | BindingFlags.NonPublic);

    private SpriteFlash? heroFlash;
    private tk2dSprite? liquid;
    private SpriteRenderer? orbFull;
    private tk2dSprite? fill;
    private PlayMakerFSM? whiteFlash;
    private Color liquidBase = Color.white;
    private Color orbFullBase = Color.white;
    private Color fillBase = Color.white;
    private bool bound;
    private bool furyOwned;

    public void Reset()
    {
        StopFury();
        RestoreOrb();
        bound = false;
        heroFlash = null;
        liquid = null;
        orbFull = null;
        fill = null;
        whiteFlash = null;
    }

    public void FlashCooled()
    {
        Bind(null);
        whiteFlash?.SendEvent("FLASH");
    }

    public void Update(HeroController hero, double heat)
    {
        Bind(hero);
        TintOrb(heat);
        UpdateFury(hero, heat);
    }

    private void Bind(HeroController? hero)
    {
        if (hero != null)
        {
            var flash = hero.GetComponent<SpriteFlash>();
            if (flash != heroFlash)
            {
                StopFury();
                heroFlash = flash;
            }
        }

        if (bound && liquid == null && orbFull == null && whiteFlash == null)
            bound = false;
        if (bound) return;

        var cameras = GameCameras.instance;
        var fsm = cameras != null ? cameras.soulOrbFSM : null;
        if (fsm == null) return;

        var root = fsm.transform;
        liquid = FindComponent<tk2dSprite>(root, "Liquid");
        fill = FindComponent<tk2dSprite>(root, "SoulOrb_fill");
        orbFull = FindComponent<SpriteRenderer>(root, "Orb Full");
        var flashGo = FindTransform(root, "White Flash");
        whiteFlash = null;
        if (flashGo != null)
        {
            foreach (var candidate in flashGo.GetComponents<PlayMakerFSM>())
            {
                if (candidate.FsmName == "flash")
                {
                    whiteFlash = candidate;
                    break;
                }
            }
            if (whiteFlash == null) whiteFlash = flashGo.GetComponent<PlayMakerFSM>();
        }

        if (liquid != null) liquidBase = liquid.color;
        if (fill != null) fillBase = fill.color;
        if (orbFull != null) orbFullBase = orbFull.color;
        bound = liquid != null || orbFull != null || whiteFlash != null;
    }

    private void TintOrb(double heat)
    {
        float t = Mathf.Clamp01((float)(heat / 100.0));
        if (t <= 0f)
        {
            if (liquid != null) liquidBase = liquid.color;
            if (fill != null) fillBase = fill.color;
            if (orbFull != null) orbFullBase = orbFull.color;
            return;
        }
        if (liquid != null) liquid.color = Color.Lerp(liquidBase, Hot, t);
        if (fill != null) fill.color = Color.Lerp(fillBase, Hot, t);
        if (orbFull != null) orbFull.color = Color.Lerp(orbFullBase, Hot, t);
    }

    private void RestoreOrb()
    {
        if (liquid != null) liquid.color = liquidBase;
        if (fill != null) fill.color = fillBase;
        if (orbFull != null) orbFull.color = orbFullBase;
    }

    private void UpdateFury(HeroController hero, double heat)
    {
        bool want = heat > FuryThreshold && !VanillaFuryActive(hero);
        if (want)
        {
            if (heroFlash != null && !IsFlashing() && !furyOwned)
            {
                heroFlash.FlashingFury();
                furyOwned = true;
            }
            else if (heroFlash != null && !IsFlashing() && furyOwned)
            {
                // A one-shot vanilla flash cancelled the loop; put Fury back.
                heroFlash.FlashingFury();
            }
        }
        else if (furyOwned)
        {
            StopFury();
        }
    }

    private void StopFury()
    {
        if (furyOwned && heroFlash != null && !VanillaFuryActive(HeroController.instance))
            heroFlash.CancelFlash();
        furyOwned = false;
    }

    private bool IsFlashing()
    {
        if (heroFlash == null || FlashState == null) return furyOwned;
        return (int)FlashState.GetValue(heroFlash)! != 0;
    }

    private static bool VanillaFuryActive(HeroController? hero)
    {
        var data = PlayerData.instance;
        return hero != null && data != null && data.GetBool("equippedCharm_16") && data.GetInt("health") == 1;
    }

    private static T? FindComponent<T>(Transform root, string name) where T : Component
    {
        var t = FindTransform(root, name);
        return t != null ? t.GetComponent<T>() : null;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindTransform(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
