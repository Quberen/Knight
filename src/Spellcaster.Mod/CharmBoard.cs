using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>
/// Injects charms 41–44 onto the collected-charm board and the equipped-charm tray,
/// and clones extra total-notch pips so a 16-notch cap is visible. Self-contained;
/// does not require SFCore.
/// </summary>
internal sealed class CharmBoard
{
    private bool boardReady;
    private bool trayReady;
    private bool notchesReady;
    private readonly List<GameObject> extraNotches = new List<GameObject>();

    internal void Install() { }
    internal void Uninstall()
    {
        foreach (var go in extraNotches)
            if (go != null) UnityEngine.Object.Destroy(go);
        extraNotches.Clear();
        boardReady = trayReady = notchesReady = false;
    }

    internal void Ensure()
    {
        CharmIcons.Ensure();
        ApplySprites();
        InitBoard();
        InitTray();
    }

    internal void RefreshIcons()
    {
        CharmIcons.Ensure();
        ApplySprites();
    }

    private static void ApplySprites()
    {
        var list = CharmIconList.Instance;
        if (list == null || list.spriteList == null) return;
        var sprites = new List<Sprite>(list.spriteList);
        while (sprites.Count <= SpellCharms.LastId) sprites.Add(null!);
        foreach (int id in SpellCharms.Ids)
        {
            var icon = CharmIcons.For(id, SpellCharms.DefaultIconVariant(id));
            if (icon != null) sprites[id] = icon;
        }
        list.spriteList = sprites.ToArray();
    }

    private void InitBoard()
    {
        if (boardReady) return;
        var cameras = GameCameras.instance;
        if (cameras == null || cameras.hudCamera == null) return;
        var inv = Descendant(cameras.hudCamera.gameObject, "Inventory");
        var charms = inv != null ? Descendant(inv, "Charms") : null;
        if (charms == null) return;
        var fsm = Fsm(charms, "UI Charms");
        var backboards = Descendant(charms, "Backboards");
        var collected = Descendant(charms, "Collected Charms");
        if (fsm == null || backboards == null || collected == null) return;

        EditNavigation(fsm);

        // Snapshot vanilla row-4 positions BEFORE shifting, then clone a 5th row,
        // then compress all 5 rows toward the bottom anchor (same as 0.5.0, which
        // was visually verified). Only 41–44 are visible; 45–50 stay blanked.
        const int rows = 5;
        float rowScale = 4f / rows;
        float charmScale = Mathf.Sqrt(rowScale);
        const float bbAnchor = -8.37f;
        const float ccAnchor = -8.34f;

        var bbRow4 = new Vector3[10];
        var ccRow4 = new Vector3[10];
        for (int j = 1; j <= 10; j++)
        {
            var bb = Child(backboards, "BB " + (30 + j));
            if (bb != null) bbRow4[j - 1] = bb.transform.localPosition;
            var cc = Child(collected, (30 + j).ToString());
            if (cc != null) ccRow4[j - 1] = cc.transform.localPosition;
        }
        float bbDy = (Child(backboards, "BB 31") != null && Child(backboards, "BB 21") != null)
            ? Child(backboards, "BB 31")!.transform.localPosition.y - Child(backboards, "BB 21")!.transform.localPosition.y
            : -1.42f;
        float ccDy = (Child(collected, "31") != null && Child(collected, "21") != null)
            ? Child(collected, "31")!.transform.localPosition.y - Child(collected, "21")!.transform.localPosition.y
            : -1.42f;

        for (int j = 1; j <= 10; j++)
        {
            int n = 40 + j;
            var bb = Child(backboards, "BB " + n) ?? CloneBackboard(backboards, n);
            if (bb != null)
            {
                var src = bbRow4[j - 1];
                bb.transform.localPosition = new Vector3(src.x, src.y + bbDy, src.z);
                ConfigureBackboard(bb, n);
            }
            if (n >= SpellCharms.FirstId && n <= SpellCharms.LastId)
            {
                var cc = Child(collected, n.ToString()) ?? CloneCollected(collected, n);
                if (cc != null)
                {
                    var src = ccRow4[j - 1];
                    cc.transform.localPosition = new Vector3(src.x, src.y + ccDy, src.z);
                }
            }
        }

        for (int i = 0; i < rows; i++)
        {
            for (int j = 1; j <= 10; j++)
            {
                int n = 10 * i + j;
                var bb = Child(backboards, "BB " + n);
                if (bb != null)
                {
                    var pos = bb.transform.localPosition;
                    bb.transform.localPosition = new Vector3(pos.x, (pos.y - bbAnchor) * rowScale + bbAnchor, pos.z);
                    var sc = bb.transform.localScale;
                    bb.transform.localScale = new Vector3(sc.x * charmScale, sc.y * charmScale, sc.z);
                    var sr = bb.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        var c = sr.color;
                        c.a = n > SpellCharms.LastId ? 0f : 1f;
                        sr.color = c;
                    }
                }
                var cc = Child(collected, n.ToString());
                if (cc != null)
                {
                    var pos = cc.transform.localPosition;
                    cc.transform.localPosition = new Vector3(pos.x, (pos.y - ccAnchor) * rowScale + ccAnchor, pos.z);
                    var sc = cc.transform.localScale;
                    cc.transform.localScale = new Vector3(sc.x * charmScale, sc.y * charmScale, sc.z);
                }
            }
        }

        // Collected-charm IDs are not arranged in numerical rows. Snap 41–44 onto their BBs.
        foreach (int id in SpellCharms.Ids)
        {
            var bb = Child(backboards, "BB " + id);
            var cc = Child(collected, id.ToString());
            if (bb == null || cc == null) continue;
            var wp = bb.transform.position;
            cc.transform.position = new Vector3(wp.x, wp.y, cc.transform.position.z);
            cc.transform.localScale = bb.transform.localScale;
        }

        // Small lift of ONLY the fifth row (41–44), not the vanilla 4 rows, so the
        // new charms sit clear of the bottom flourish. Keep a gap under row 4.
        var bb31 = Child(backboards, "BB 31");
        var bb41 = Child(backboards, "BB 41");
        float y31 = bb31 != null ? bb31.transform.localPosition.y : 0f;
        float y41before = bb41 != null ? bb41.transform.localPosition.y : 0f;
        float lift = CharmBoardLayout.Lift(y31, y41before);
        if (lift > 0.01f)
        {
            foreach (int id in SpellCharms.Ids)
            {
                var bb = Child(backboards, "BB " + id);
                if (bb != null)
                {
                    var pos = bb.transform.localPosition;
                    bb.transform.localPosition = new Vector3(pos.x, pos.y + lift, pos.z);
                }
                var cc = Child(collected, id.ToString());
                if (cc != null && bb != null)
                {
                    var wp = bb.transform.position;
                    cc.transform.position = new Vector3(wp.x, wp.y, cc.transform.position.z);
                }
            }
        }

        boardReady = true;
        bb41 = Child(backboards, "BB 41");
        Modding.Logger.Log("[Spellcaster] charm board: injected ids 41–44 (5th row only lift="
            + lift.ToString("0.00")
            + " localY 31=" + (bb31 != null ? bb31.transform.localPosition.y.ToString("0.00") : "?")
            + " 41before=" + y41before.ToString("0.00")
            + " 41=" + (bb41 != null ? bb41.transform.localPosition.y.ToString("0.00") : "?") + ").");
    }

    private static void EditNavigation(PlayMakerFSM fsm)
    {
        // Fifth row is 41–44. Down from 31–40 adds 10 then Update Cursor clamps to LastId.
        // Left wraps at 1/11/21/31/41; Right wraps at 10/20/30/40/LastId (not 50).
        try
        {
            var down = Action<IntCompare>(fsm, "Down", 0);
            if (down != null)
            {
                if (down.integer2 != null) { down.integer2.Value = 40; down.integer2.UseVariable = false; }
                else down.integer2 = 40;
                Modding.Logger.Log("[Spellcaster] UI Charms Down integer2=" + down.integer2.Value);
            }
        }
        catch (Exception e) { Modding.Logger.Log("[Spellcaster] UI Charms Down: " + e.Message); }
        try
        {
            var left = Action<IntSwitch>(fsm, "Left", 0);
            if (left != null)
            {
                int[] edges = { 1, 11, 21, 31, SpellCharms.FirstId };
                var compare = new FsmInt[edges.Length];
                var ev = new FsmEvent[edges.Length];
                var toLeft = FsmEvent.FindEvent("TO LEFT");
                for (int i = 0; i < edges.Length; i++)
                {
                    compare[i] = edges[i];
                    ev[i] = toLeft;
                }
                left.compareTo = compare;
                left.sendEvent = ev;
            }
        }
        catch (Exception e) { Modding.Logger.Log("[Spellcaster] UI Charms Left: " + e.Message); }
        try
        {
            var right = Action<IntSwitch>(fsm, "Right", 0);
            if (right != null)
            {
                int[] edges = { 10, 20, 30, 40, SpellCharms.LastId };
                var compare = new FsmInt[edges.Length];
                var ev = new FsmEvent[edges.Length];
                var toRight = FsmEvent.FindEvent("TO RIGHT");
                for (int i = 0; i < edges.Length; i++)
                {
                    compare[i] = edges[i];
                    ev[i] = toRight;
                }
                right.compareTo = compare;
                right.sendEvent = ev;
            }
        }
        catch (Exception e) { Modding.Logger.Log("[Spellcaster] UI Charms Right: " + e.Message); }
        try
        {
            var clamp = Action<IntClamp>(fsm, "Update Cursor", 1);
            if (clamp != null)
            {
                if (clamp.maxValue != null) { clamp.maxValue.Value = SpellCharms.LastId; clamp.maxValue.UseVariable = false; }
                else clamp.maxValue = SpellCharms.LastId;
                Modding.Logger.Log("[Spellcaster] UI Charms clamp max=" + clamp.maxValue.Value);
            }
        }
        catch (Exception e) { Modding.Logger.Log("[Spellcaster] UI Charms clamp: " + e.Message); }
    }

    private static GameObject? CloneBackboard(GameObject parent, int n)
    {
        int prefabNum = ((n - 1) % 20) + 21;
        var src = Child(parent, "BB " + prefabNum) ?? Child(parent, "BB 40");
        if (src == null) return null;
        bool was = src.activeSelf;
        src.SetActive(true);
        var clone = UnityEngine.Object.Instantiate(src, parent.transform, true);
        src.SetActive(was);
        clone.name = "BB " + n;
        clone.SetActive(was);
        AddFade(clone, parent.transform.parent != null ? parent.transform.parent.gameObject : parent);
        return clone;
    }

    private static void ConfigureBackboard(GameObject bb, int n)
    {
        bool was = bb.activeSelf;
        bb.SetActive(false);
        var icb = bb.GetComponent<InvCharmBackboard>();
        if (icb != null)
        {
            icb.charmNum = n;
            icb.charmNumString = n.ToString();
            if (SpellCharms.IsOurs(n))
            {
                icb.gotCharmString = "gotCharm_" + n;
                icb.newCharmString = "newCharm_" + n;
                SetField(icb, "blanked", true);
            }
            else
            {
                icb.gotCharmString = "";
                icb.newCharmString = "";
                var sr = GetField<SpriteRenderer>(icb, "spriteRenderer") ?? bb.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sprite = icb.blankSprite;
                SetField(icb, "blanked", false);
            }
        }
        bb.SetActive(was);
    }

    private static GameObject? CloneCollected(GameObject parent, int n)
    {
        int prefabNum = ((n - 1) % 20) + 21;
        var src = Child(parent, prefabNum.ToString()) ?? Child(parent, "40");
        if (src == null) return null;
        bool was = src.activeSelf;
        src.SetActive(true);
        var clone = UnityEngine.Object.Instantiate(src, parent.transform, true);
        src.SetActive(was);
        clone.name = n.ToString();
        clone.SetActive(false);
        var sprite = Child(clone, "Sprite");
        var glow = Child(clone, "Glow");
        if (sprite != null) AddFade(sprite, parent.transform.parent != null ? parent.transform.parent.gameObject : parent);
        if (glow != null) AddFade(glow, parent.transform.parent != null ? parent.transform.parent.gameObject : parent);
        var show = Fsm(clone, "charm_show_if_collected");
        if (show != null)
        {
            var vars = show.FsmVariables;
            vars.GetFsmInt("ID").Value = n;
            vars.GetFsmString("Name").Value = "Charm " + n;
            vars.GetFsmString("PD Bool Name").Value = "gotCharm_" + n;
            if (sprite != null) vars.GetFsmGameObject("Charm Sprite").Value = sprite;
            if (glow != null) vars.GetFsmGameObject("Glow").Value = glow;
        }
        clone.SetActive(was);
        return clone;
    }

    private void InitTray()
    {
        if (trayReady) return;
        var build = UnityEngine.Object.FindObjectOfType<BuildEquippedCharms>();
        if (build == null || build.gameObjectList == null) return;
        var list = new List<GameObject>(build.gameObjectList);
        if (list.Count == 0) return;
        int need = SpellCharms.LastId;
        while (list.Count < need)
        {
            int next = list.Count + 1;
            var prefab = UnityEngine.Object.Instantiate(list[0]);
            UnityEngine.Object.DontDestroyOnLoad(prefab);
            prefab.transform.position = new Vector3(-20000f, -20000f, -20000f);
            prefab.SetActive(list[0].activeSelf);
            prefab.name = next.ToString();
            var display = prefab.GetComponent<CharmDisplay>();
            if (display != null) display.id = next;
            list.Add(prefab);
        }
        build.gameObjectList = list;
        trayReady = true;
    }

    /// <summary>
    /// Clone extra total-notch pips so charmSlots 12–16 are visible on the board.
    /// The vanilla layout only has pips up to 11; we copy the last pip along its row.
    /// </summary>
    internal void ExpandNotches(int total)
    {
        total = Math.Max(0, Math.Min(CharmNotches.ExpandedCap, total));
        var cameras = GameCameras.instance;
        if (cameras == null || cameras.hudCamera == null) return;
        var inv = Descendant(cameras.hudCamera.gameObject, "Inventory");
        var charms = inv != null ? Descendant(inv, "Charms") : null;
        if (charms == null) return;

        var pips = FindNotchPips(charms);
        if (pips.Count == 0)
        {
            if (!notchesReady)
                Modding.Logger.Log("[Spellcaster] notch pips: none found; will retry. Charms children: " + Names(charms));
            return;
        }
        // Sort along X so we can extend the row.
        pips.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        var parent = pips[0].transform.parent;
        Vector3 step = pips.Count >= 2
            ? pips[pips.Count - 1].transform.localPosition - pips[pips.Count - 2].transform.localPosition
            : new Vector3(0.55f, 0f, 0f);

        while (pips.Count < CharmNotches.ExpandedCap)
        {
            var last = pips[pips.Count - 1];
            var clone = UnityEngine.Object.Instantiate(last, parent, true);
            clone.name = last.name + " extra " + (pips.Count + 1);
            clone.transform.localPosition = last.transform.localPosition + step;
            clone.transform.localScale = last.transform.localScale;
            clone.name = "Charm Cost " + (pips.Count + 1);
            clone.SetActive(false);
            AddFade(clone, parent.gameObject);
            pips.Add(clone);
            extraNotches.Add(clone);
        }
        for (int i = 0; i < pips.Count; i++)
            if (pips[i] != null) pips[i].SetActive(i < total);
        notchesReady = true;
    }

    private static List<GameObject> FindNotchPips(GameObject charms)
    {
        var found = new List<GameObject>();
        // Prefer a dedicated "Notches" / "Charm Notch" folder.
        foreach (var folderName in new[] { "Notches", "Charm Notches", "Notch Dots", "Dots", "Opened Notches", "CharmNotches" })
        {
            var folder = Descendant(charms, folderName);
            if (folder == null || folder.transform.childCount < 3) continue;
            foreach (Transform c in folder.transform)
                if (c != null && LooksLikePip(c.gameObject)) found.Add(c.gameObject);
            if (found.Count >= 3) return found;
            found.Clear();
        }
        // Fallback: any siblings whose name contains "Notch" and that share a parent, 8+ of them.
        var groups = new Dictionary<Transform, List<GameObject>>();
        foreach (var go in charms.GetComponentsInChildren<Transform>(true))
        {
            if (go == null || go.parent == null) continue;
            string n = go.name;
            if (n.IndexOf("Notch", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("Pip", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("Dot", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (go == charms.transform) continue;
            if (!groups.TryGetValue(go.parent, out var list))
            {
                list = new List<GameObject>();
                groups[go.parent] = list;
            }
            list.Add(go.gameObject);
        }
        List<GameObject>? best = null;
        foreach (var kv in groups)
            if (kv.Value.Count >= 8 && (best == null || kv.Value.Count > best.Count))
                best = kv.Value;
        return best ?? found;
    }

    private static bool LooksLikePip(GameObject go)
    {
        if (go == null) return false;
        string n = go.name ?? "";
        if (n.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (n.IndexOf("Indicator", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return go.GetComponent<SpriteRenderer>() != null
            || go.GetComponentInChildren<SpriteRenderer>(true) != null
            || go.GetComponent<UnityEngine.UI.Image>() != null
            || go.GetComponentInChildren<UnityEngine.UI.Image>(true) != null
            || n.IndexOf("Charm Cost", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Notch", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Names(GameObject go)
    {
        var parts = new List<string>();
        foreach (Transform c in go.transform) parts.Add(c.name);
        return string.Join(", ", parts);
    }

    private static void AddFade(GameObject spriteGo, GameObject fadeHost)
    {
        var sr = spriteGo.GetComponent<SpriteRenderer>() ?? spriteGo.GetComponentInChildren<SpriteRenderer>();
        var fg = fadeHost.GetComponent<FadeGroup>() ?? fadeHost.GetComponentInChildren<FadeGroup>();
        if (sr == null || fg == null) return;
        var list = new List<SpriteRenderer>(fg.spriteRenderers ?? Array.Empty<SpriteRenderer>());
        if (!list.Contains(sr)) list.Add(sr);
        fg.spriteRenderers = list.ToArray();
    }

    private static T? Action<T>(PlayMakerFSM fsm, string state, int index) where T : FsmStateAction
    {
        var st = fsm.FsmStates.FirstOrDefault(s => s.Name == state);
        if (st?.Actions == null || index >= st.Actions.Length) return null;
        return st.Actions[index] as T;
    }

    private static PlayMakerFSM? Fsm(GameObject go, string name) =>
        go.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == name);

    private static GameObject? Child(GameObject parent, string name)
    {
        var t = parent.transform.Find(name);
        return t != null ? t.gameObject : null;
    }

    private static GameObject? Descendant(GameObject parent, string name)
    {
        var t = parent.transform.Find(name);
        if (t != null) return t.gameObject;
        foreach (Transform c in parent.transform)
        {
            var found = Descendant(c.gameObject, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void SetField(object obj, string name, object value)
    {
        var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        f?.SetValue(obj, value);
    }

    private static T? GetField<T>(object obj, string name) where T : class
    {
        var f = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return f?.GetValue(obj) as T;
    }
}
