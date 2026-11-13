using System;
using System.IO;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>
/// Builds charm icons by recoloring vanilla sprites (Spell Twister, Shaman Stone, Soul Catcher).
/// Three variants per charm so the player can pick an appearance. No artwork is drawn from scratch.
/// </summary>
internal static class CharmIcons
{
    // Variant tints: (hue shift in turns, saturation scale, value scale, unused).
    private static readonly Vector4[] GripTint =
    {
        new Vector4(0.55f, 1.15f, 1.05f, 0), // icy cyan
        new Vector4(0.72f, 1.20f, 1.10f, 0), // violet
        new Vector4(0.08f, 1.25f, 1.15f, 0), // warm gold
    };
    private static readonly Vector4[] ShamanTint =
    {
        new Vector4(0.12f, 1.20f, 1.15f, 0), // amber
        new Vector4(0.85f, 1.15f, 1.10f, 0), // rose
        new Vector4(0.45f, 1.10f, 1.05f, 0), // teal
    };
    private static readonly Vector4[] CollectorTint =
    {
        new Vector4(0.48f, 1.15f, 1.10f, 0), // soul teal
        new Vector4(0.33f, 1.20f, 1.05f, 0), // leaf green
        new Vector4(0.62f, 1.10f, 1.20f, 0), // pale blue
    };

    private static Sprite[][]? variants; // [charmIndex][variant]
    private static bool built;

    internal static bool Ready => built && variants != null;

    internal static string DebugInfo()
    {
        var list = CharmIconList.Instance;
        string listInfo = list == null ? "no-list" : ("len=" + (list.spriteList != null ? list.spriteList.Length.ToString() : "null"));
        string dims = "not-built";
        if (variants != null)
        {
            dims = "";
            for (int c = 0; c < variants.Length; c++)
            {
                if (variants[c] == null) { dims += c + "=null "; continue; }
                for (int v = 0; v < variants[c].Length; v++)
                {
                    var s = variants[c][v];
                    dims += c + "v" + v + "=" + (s == null || s.texture == null ? "null" : s.texture.width + "x" + s.texture.height) + " ";
                }
            }
        }
        return "built=" + built + " " + listInfo + " " + dims;
    }

    internal static void Ensure()
    {
        if (built && variants != null && HasSubstance()) return;
        built = false;
        variants = null;
        var list = CharmIconList.Instance;
        if (list == null || list.spriteList == null || list.spriteList.Length <= 33)
        {
            Modding.Logger.Log("[Spellcaster] CharmIcons: CharmIconList not ready (" + DebugInfo() + ")");
            return;
        }
        Sprite? twister = Pick(list, 33);
        Sprite? stone = Pick(list, 19);
        Sprite? catcher = Pick(list, 20);
        if (twister == null || stone == null || catcher == null)
        {
            Modding.Logger.Log("[Spellcaster] CharmIcons: missing source sprite twister=" + (twister != null)
                + " stone=" + (stone != null) + " catcher=" + (catcher != null));
            return;
        }
        LogSprite("twister", twister);
        LogSprite("stone", stone);
        LogSprite("catcher", catcher);
        variants = new Sprite[SpellCharms.Count][];
        variants[0] = BuildSet(twister, GripTint);
        variants[1] = BuildSet(stone, ShamanTint);
        variants[2] = BuildSet(catcher, CollectorTint);
        built = HasSubstance();
        Modding.Logger.Log("[Spellcaster] CharmIcons.Ensure " + DebugInfo());
    }

    internal static Sprite? For(int id, int variant)
    {
        Ensure();
        if (variants == null || !SpellCharms.IsOurs(id)) return null;
        var set = variants[SpellCharms.IndexOf(id)];
        if (set == null) return null;
        return set[SpellCharms.ClampVariant(variant)];
    }

    internal static void SaveComparison(string directory)
    {
        Ensure();
        if (variants == null) return;
        Directory.CreateDirectory(directory);
        for (int c = 0; c < SpellCharms.Count; c++)
        {
            if (variants[c] == null) continue;
            for (int v = 0; v < SpellCharms.IconVariants; v++)
            {
                var sprite = variants[c][v];
                if (sprite == null) continue;
                var tex = sprite.texture;
                if (tex == null) continue;
                // The variant textures are readable (we created them).
                string name = SpellCharms.Ids[c] + "-v" + v + ".png";
                try
                {
                    var copy = ReadableCopy(sprite);
                    if (copy == null) continue;
                    File.WriteAllBytes(Path.Combine(directory, name), copy.EncodeToPNG());
                    UnityEngine.Object.Destroy(copy);
                }
                catch (Exception e)
                {
                    Modding.Logger.Log("[Spellcaster] CharmIcons save " + name + ": " + e.Message);
                }
            }
        }
    }

    private static bool HasSubstance()
    {
        if (variants == null) return false;
        for (int c = 0; c < variants.Length; c++)
        {
            if (variants[c] == null || variants[c].Length == 0) return false;
            for (int v = 0; v < variants[c].Length; v++)
            {
                var s = variants[c][v];
                if (s == null || s.texture == null || s.texture.width < 8 || s.texture.height < 8) return false;
            }
        }
        return true;
    }

    private static Sprite? Pick(CharmIconList list, int id)
    {
        Sprite? s = null;
        try { s = list.GetSprite(id); } catch { }
        if (s == null && list.spriteList != null && id >= 0 && id < list.spriteList.Length)
            s = list.spriteList[id];
        return s;
    }

    private static void LogSprite(string label, Sprite sprite)
    {
        if (sprite == null) { Modding.Logger.Log("[Spellcaster] CharmIcons " + label + "=null"); return; }
        var t = sprite.texture;
        Modding.Logger.Log("[Spellcaster] CharmIcons " + label + " name=" + sprite.name
            + " tex=" + (t != null ? t.name + " " + t.width + "x" + t.height + " fmt=" + t.format + " readable=" + t.isReadable : "null")
            + " rect=" + sprite.rect + " texRect=" + sprite.textureRect
            + " packed=" + sprite.packed + " packMode=" + sprite.packingMode);
    }

    private static Sprite[] BuildSet(Sprite source, Vector4[] tints)
    {
        var set = new Sprite[tints.Length];
        var pixels = Read(source);
        if (pixels == null)
        {
            Modding.Logger.Log("[Spellcaster] CharmIcons: failed to read pixels from " + (source != null ? source.name : "null"));
            return set;
        }
        for (int i = 0; i < tints.Length; i++)
        {
            var tinted = Recolor(pixels.Value, tints[i]);
            tinted.name = source.name + "_spellcaster_" + i;
            tinted.filterMode = FilterMode.Bilinear;
            tinted.wrapMode = TextureWrapMode.Clamp;
            float ppu = source.pixelsPerUnit > 0 ? source.pixelsPerUnit : 64f;
            set[i] = Sprite.Create(tinted, new Rect(0, 0, tinted.width, tinted.height),
                new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
            set[i].name = tinted.name;
            UnityEngine.Object.DontDestroyOnLoad(tinted);
            UnityEngine.Object.DontDestroyOnLoad(set[i]);
        }
        return set;
    }

    private struct PixelSource
    {
        public Color[] Pixels;
        public int Width, Height;
    }

    private static PixelSource? Read(Sprite sprite)
    {
        var tex = ReadableCopy(sprite);
        if (tex == null) return null;
        var src = new PixelSource { Pixels = tex.GetPixels(), Width = tex.width, Height = tex.height };
        UnityEngine.Object.Destroy(tex);
        if (src.Width < 8 || src.Height < 8) return null;
        return src;
    }

    /// <summary>
    /// GPU-copy a sprite's region to a readable Texture2D. Tries textureRect, then rect, then UV bounds.
    /// </summary>
    private static Texture2D? ReadableCopy(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return null;
        var source = sprite.texture;
        if (source.width < 2 || source.height < 2) return null;

        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var full = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            full.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            full.Apply();

            // Tight-packed sprites report textureRect = (0,0,0,0) and sprite.rect is
            // only the local mesh size (origin 0,0) — not an atlas location. UVs
            // are the authoritative atlas region.
            Rect region = UvRegion(sprite, source);
            if (region.width < 8 || region.height < 8) region = sprite.textureRect;
            int x = Mathf.Clamp(Mathf.FloorToInt(region.x), 0, full.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(region.y), 0, full.height - 1);
            int w = Mathf.Clamp(Mathf.FloorToInt(region.width), 1, full.width - x);
            int h = Mathf.Clamp(Mathf.FloorToInt(region.height), 1, full.height - y);
            if (w < 8 || h < 8)
            {
                // Last resort: the whole texture, if it is already a single-icon sheet.
                if (full.width >= 8 && full.width <= 256 && full.height >= 8 && full.height <= 256)
                {
                    w = full.width; h = full.height; x = 0; y = 0;
                }
                else
                {
                    UnityEngine.Object.Destroy(full);
                    return null;
                }
            }
            var crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
            crop.SetPixels(full.GetPixels(x, y, w, h));
            crop.Apply();
            UnityEngine.Object.Destroy(full);
            return crop;
        }
        catch (Exception e)
        {
            Modding.Logger.Log("[Spellcaster] CharmIcons.ReadableCopy: " + e.Message);
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }


    private static Rect UvRegion(Sprite sprite, Texture source)
    {
        var uvs = sprite.uv;
        if (uvs == null || uvs.Length == 0) return default;
        float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
        foreach (var uv in uvs)
        {
            if (uv.x < uMin) uMin = uv.x;
            if (uv.x > uMax) uMax = uv.x;
            if (uv.y < vMin) vMin = uv.y;
            if (uv.y > vMax) vMax = uv.y;
        }
        return new Rect(uMin * source.width, vMin * source.height,
            (uMax - uMin) * source.width, (vMax - vMin) * source.height);
    }

    private static Texture2D Recolor(PixelSource src, Vector4 tint)
    {
        var dest = new Texture2D(src.Width, src.Height, TextureFormat.RGBA32, false);
        dest.filterMode = FilterMode.Bilinear;
        dest.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[src.Pixels.Length];
        for (int i = 0; i < src.Pixels.Length; i++)
        {
            Color c = src.Pixels[i];
            if (c.a < 0.01f) { pixels[i] = c; continue; }
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + tint.x, 1f);
            s = Mathf.Clamp01(s * tint.y);
            v = Mathf.Clamp01(v * tint.z);
            Color next = Color.HSVToRGB(h, s, v);
            next.a = c.a;
            pixels[i] = next;
        }
        dest.SetPixels(pixels);
        dest.Apply();
        return dest;
    }
}
