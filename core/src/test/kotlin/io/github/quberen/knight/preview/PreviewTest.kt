package io.github.quberen.knight.preview

import io.github.quberen.knight.emoji.EmojiData
import io.github.quberen.knight.english.EnglishDict
import io.github.quberen.knight.pinyin.PinyinDict
import io.github.quberen.knight.pinyin.PinyinEngine
import io.github.quberen.knight.ui.EmojiBoard
import io.github.quberen.knight.ui.Icons
import io.github.quberen.knight.ui.KeyLayouts
import io.github.quberen.knight.ui.LayoutMode
import io.github.quberen.knight.ui.Screen
import io.github.quberen.knight.ui.Theme
import org.junit.Assume
import org.junit.Test
import java.awt.image.BufferedImage
import java.io.File
import javax.imageio.ImageIO

/**
 * Draws the keyboard in several states to PNG files, for checking the design
 * without a device. Runs only when asked:
 *
 *   ./gradlew :core:test --tests '*PreviewTest' -Dknight.preview=/some/dir
 */
class PreviewTest {
    private val assets = listOf("../app/src/main/assets", "app/src/main/assets").map(::File).first { it.exists() }
    private val engine by lazy {
        PinyinEngine(PinyinDict().also { d -> File(assets, "dict.txt").reader().use { d.load(it) } })
    }
    private val english by lazy { EnglishDict().also { d -> File(assets, "english.txt").reader().use { d.load(it) } } }
    private val emoji by lazy {
        EmojiData().also { d ->
            File(assets, "emoji.txt").reader().use { d.loadEmoji(it) }
            File(assets, "emoji_words.txt").reader().use { d.loadWords(it) }
        }
    }

    private fun screen(mode: LayoutMode, zh: Boolean = true, symbols: Boolean = false): Screen {
        val s = Screen()
        s.keys.blocks = if (symbols) KeyLayouts.symbols(mode, zh, zh) else KeyLayouts.letters(mode, zh, true)
        s.keys.chinese = zh
        s.keys.spaceLabel = if (zh) "拼音" else "English"
        s.strip.layoutIcon = when (mode) {
            LayoutMode.STANDARD -> Icons.LAYOUT_STANDARD
            LayoutMode.SPLIT -> Icons.LAYOUT_SPLIT
            LayoutMode.TRAPEZOID -> Icons.LAYOUT_TRAPEZOID
        }
        s.emoji.categories = categories()
        return s
    }

    private fun categories() = listOf(
        EmojiBoard.Category(Icons.RECENT, "最近", listOf("😄", "👍", "❤️", "🎉", "😂")),
        EmojiBoard.Category(Icons.EMOJI, "笑脸", emoji.groups[EmojiData.SMILEYS].orEmpty()),
        EmojiBoard.Category(Icons.PERSON, "人物", emoji.groups[EmojiData.PEOPLE].orEmpty()),
        EmojiBoard.Category(Icons.LEAF, "自然", emoji.groups[EmojiData.NATURE].orEmpty()),
        EmojiBoard.Category(Icons.CUP, "食物", emoji.groups[EmojiData.FOOD].orEmpty()),
        EmojiBoard.Category(Icons.CAR, "出行", emoji.groups[EmojiData.TRAVEL].orEmpty()),
        EmojiBoard.Category(Icons.BALL, "活动", emoji.groups[EmojiData.ACTIVITIES].orEmpty()),
        EmojiBoard.Category(Icons.BULB, "物品", emoji.groups[EmojiData.OBJECTS].orEmpty()),
        EmojiBoard.Category(Icons.HEART, "符号", emoji.groups[EmojiData.SYMBOLS].orEmpty()),
        EmojiBoard.Category(Icons.FLAG, "旗帜", emoji.groups[EmojiData.FLAGS].orEmpty()),
        EmojiBoard.Category(Icons.KAOMOJI, "颜文字", EmojiData.KAOMOJI, wide = true),
    )

    private fun typing(s: Screen, input: String) {
        engine.extras = { emoji.forWord(it) }
        val a = engine.analyze(input)
        s.strip.composition = a.display
        s.strip.items = a.candidates.take(40).map { it.text }
    }

    private lateinit var raster: Raster

    private fun render(dir: File, name: String, widthDp: Float, theme: Theme, s: Screen, density: Float = 2f, setup: Screen.Pressed.() -> Unit = {}) {
        s.dp = density
        val probe = BufferedImage(1, 1, BufferedImage.TYPE_INT_ARGB)
        val measure = AwtCanvas(probe.createGraphics(), raster)
        s.layout(measure, widthDp * density)
        val img = BufferedImage((widthDp * density).toInt(), s.height().toInt(), BufferedImage.TYPE_INT_ARGB)
        val g = img.createGraphics()
        val c = AwtCanvas(g, raster)
        s.layout(c, widthDp * density)
        val pressed = Screen.Pressed().apply(setup)
        s.draw(c, theme, pressed)
        g.dispose()
        ImageIO.write(img, "png", File(dir, "$name.png"))
    }

    @Test
    fun render() {
        val out = System.getProperty("knight.preview").orEmpty()
        Assume.assumeTrue("set -Dknight.preview=<dir> to draw previews", out.isNotEmpty())
        val dir = File(out).apply { mkdirs() }
        raster = Raster(File(dir, ".raster"))
        // First pass finds text that needs Chromium; the second draws it.
        renderAll(dir)
        if (raster.hasMissing) {
            raster.fetch(File(assets, "../../../..").canonicalFile)
            renderAll(dir)
        }
    }

    private fun renderAll(dir: File) {
        val tablet = 1280f

        screen(LayoutMode.STANDARD).let { s ->
            typing(s, "nihao")
            render(dir, "01-standard-typing-light", tablet, Theme.LIGHT, s) {
                s.keys.blocks.flatMap { b -> b.rows.flatMap { it.keys } }.firstOrNull { it.label == "o" }?.let { keys.add(it) }
            }
        }
        screen(LayoutMode.STANDARD).let { s ->
            typing(s, "womenjintianqugongyuan")
            render(dir, "02-standard-sentence-dark", tablet, Theme.DARK, s)
        }
        screen(LayoutMode.SPLIT).let { s -> render(dir, "03-split-idle-light", tablet, Theme.LIGHT, s) }
        screen(LayoutMode.TRAPEZOID).let { s ->
            typing(s, "mingtain")
            render(dir, "04-trapezoid-typo-light", tablet, Theme.LIGHT, s)
        }
        screen(LayoutMode.STANDARD, symbols = true).let { s -> render(dir, "05-symbols-light", tablet, Theme.LIGHT, s) }
        screen(LayoutMode.STANDARD).let { s ->
            s.body = Screen.Body.EMOJI
            s.emoji.selected = 1
            s.strip.activeTool = io.github.quberen.knight.ui.CandidateStrip.Tool.EMOJI
            render(dir, "06-emoji-light", tablet, Theme.LIGHT, s)
        }
        screen(LayoutMode.STANDARD).let { s ->
            typing(s, "shi")
            s.strip.expanded = true
            s.body = Screen.Body.CANDIDATES
            s.grid.items = engine.candidates("shi").take(120).map { it.text }
            render(dir, "07-all-candidates-light", tablet, Theme.LIGHT, s)
        }
        screen(LayoutMode.STANDARD, zh = false).let { s ->
            s.strip.items = english.suggest("keyb").map { it.text }
            render(dir, "08-portrait-english-light", 800f, Theme.LIGHT, s)
        }
        screen(LayoutMode.SPLIT).let { s ->
            s.keys.shiftState = 1
            typing(s, "kaixin")
            render(dir, "09-split-shift-dark", tablet, Theme.DARK, s)
        }
        screen(LayoutMode.STANDARD).let { s ->
            s.body = Screen.Body.EMOJI
            s.emoji.selected = 10
            s.strip.activeTool = io.github.quberen.knight.ui.CandidateStrip.Tool.EMOJI
            render(dir, "10-kaomoji-dark", tablet, Theme.DARK, s)
        }
    }
}
