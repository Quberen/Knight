package io.github.quberen.knight.ui

import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.max

/** The strip above the keys: candidates while typing, tools otherwise. */
class CandidateStrip {
    enum class Tool(val label: String) { LAYOUT("布局"), EMOJI("表情"), CLIPBOARD("剪贴板"), SWITCH("切换输入法"), SETTINGS("设置"), HIDE("收起") }

    sealed class Hit {
        data class Item(val index: Int) : Hit()
        data class ToolHit(val tool: Tool) : Hit()
        object Expand : Hit()
    }

    var dp = 1f
    var composition = ""
    var items: List<String> = emptyList()
    var expanded = false
    var layoutIcon: Icon = Icons.LAYOUT_STANDARD
    var scrollX = 0f

    /** A tool whose panel is open, shown as selected. */
    var activeTool: Tool? = null

    val box = Box()
    val height: Float get() = 48f * dp
    val showsTools: Boolean get() = composition.isEmpty() && items.isEmpty()
    val maxScroll: Float get() = max(0f, contentWidth - listBox.width)
    val listArea: Box get() = listBox

    private val listBox = Box()
    private val compBox = Box()
    private val expandBox = Box()
    private val itemBoxes = ArrayList<Box>()
    private val toolBoxes = LinkedHashMap<Tool, Box>()
    private var contentWidth = 0f

    private val candidateSize get() = 20f * dp
    private val compositionSize get() = 15f * dp

    fun layout(c: Canvas2D, left: Float, top: Float, width: Float) {
        box.set(left, top, left + width, top + height)
        toolBoxes.clear()
        itemBoxes.clear()
        if (showsTools) {
            val w = 52f * dp
            var x = left + 8f * dp
            for (t in listOf(Tool.LAYOUT, Tool.EMOJI, Tool.CLIPBOARD)) {
                toolBoxes[t] = Box(x, top, x + w, top + height); x += w
            }
            x = left + width - 8f * dp
            for (t in listOf(Tool.HIDE, Tool.SETTINGS, Tool.SWITCH)) {
                toolBoxes[t] = Box(x - w, top, x, top + height); x -= w
            }
            return
        }
        if (composition.isNotEmpty()) {
            val w = (c.measure(composition, compositionSize) + 28f * dp).coerceAtMost(width * 0.4f)
            compBox.set(left, top, left + w, top + height)
        } else {
            compBox.set(left, top, left + 4f * dp, top + height)
        }
        val ew = if (items.size > 1) 52f * dp else 0f
        expandBox.set(left + width - ew, top, left + width, top + height)
        listBox.set(compBox.right, top, expandBox.left, top + height)
        var x = 0f
        // While all candidates are shown below, the strip only keeps the pinyin.
        for (s in if (expanded) emptyList() else items) {
            val w = max(c.measure(s, candidateSize) + 28f * dp, 48f * dp)
            itemBoxes.add(Box(x, top, x + w, top + height))
            x += w
        }
        contentWidth = x
        scrollX = scrollX.coerceIn(0f, maxScroll)
    }

    fun draw(c: Canvas2D, t: Theme, pressed: Hit?) {
        c.fillRect(box, t.background)
        if (showsTools) {
            for ((tool, b) in toolBoxes) {
                val on = tool == activeTool
                if (on || pressed == Hit.ToolHit(tool)) c.fillRoundRect(b.inset(4f * dp, 6f * dp), 10f * dp, t.chip)
                val icon = when (tool) {
                    Tool.LAYOUT -> layoutIcon
                    Tool.EMOJI -> Icons.EMOJI
                    Tool.CLIPBOARD -> Icons.CLIPBOARD
                    Tool.SWITCH -> Icons.SWITCH_IME
                    Tool.SETTINGS -> Icons.SETTINGS
                    Tool.HIDE -> Icons.HIDE_KEYBOARD
                }
                c.icon(icon, b.centerX, b.centerY, 25f * dp, if (on) t.ink else t.inkSoft)
            }
            return
        }
        if (composition.isNotEmpty()) {
            c.save()
            c.clip(compBox)
            c.textAt(composition, compBox.left + 14f * dp, compBox.centerY, compositionSize, t.ink, align = Align.LEFT)
            c.restore()
            c.line(compBox.right, top(12f), compBox.right, bottom(12f), dp, t.divider)
        }
        c.save()
        c.clip(listBox)
        c.translate(listBox.left - scrollX, 0f)
        for ((i, b) in itemBoxes.withIndex()) {
            val s = items[i]
            if (b.right < scrollX || b.left > scrollX + listBox.width) continue
            if (pressed == Hit.Item(i)) c.fillRoundRect(b.inset(2f * dp, 6f * dp), 10f * dp, t.chip)
            c.textIn(s, b, candidateSize, t.ink, bold = i == 0)
        }
        c.restore()
        if (items.size > 1) {
            c.icon(if (expanded) Icons.COLLAPSE else Icons.EXPAND, expandBox.centerX, expandBox.centerY, 26f * dp, t.inkSoft)
        }
    }

    private fun top(inset: Float) = box.top + inset * dp
    private fun bottom(inset: Float) = box.bottom - inset * dp

    fun hit(x: Float, y: Float): Hit? {
        if (!box.contains(x, y)) return null
        if (showsTools) return toolBoxes.entries.firstOrNull { it.value.contains(x, y) }?.let { Hit.ToolHit(it.key) }
        if (expandBox.width > 0 && expandBox.contains(x, y)) return Hit.Expand
        if (!listBox.contains(x, y)) return null
        val cx = x - listBox.left + scrollX
        val i = itemBoxes.indexOfFirst { cx >= it.left && cx < it.right }
        return if (i >= 0) Hit.Item(i) else null
    }
}

/** All candidates in a grid over the keys, for picking past the first few. */
class CandidateGrid {
    var dp = 1f
    var items: List<String> = emptyList()
    var scrollY = 0f
    val box = Box()
    val maxScroll: Float get() = max(0f, contentHeight - box.height)

    private val cells = ArrayList<Box>()
    private var contentHeight = 0f
    private val textSize get() = 20f * dp

    fun layout(c: Canvas2D, area: Box) {
        box.set(area.left, area.top, area.right, area.bottom)
        cells.clear()
        val pad = 6f * dp
        val gap = 6f * dp
        val usable = box.width - 2 * pad
        val cols = max(1, floor((usable + gap) / (72f * dp + gap)).toInt())
        val colW = (usable + gap) / cols
        val cellH = 52f * dp
        var col = 0
        var y = pad
        for (s in items) {
            val span = ceil((c.measure(s, textSize) + 28f * dp + gap) / colW).toInt().coerceIn(1, cols)
            if (col + span > cols) {
                col = 0; y += cellH + gap
            }
            val x = pad + col * colW
            cells.add(Box(x, y, x + span * colW - gap, y + cellH))
            col += span
        }
        contentHeight = if (items.isEmpty()) 0f else y + cellH + pad
        scrollY = scrollY.coerceIn(0f, maxScroll)
    }

    fun draw(c: Canvas2D, t: Theme, pressed: Int) {
        c.fillRect(box, t.background)
        c.save()
        c.clip(box)
        c.translate(box.left, box.top - scrollY)
        for ((i, s) in items.withIndex()) {
            val b = cells[i]
            if (b.bottom < scrollY || b.top > scrollY + box.height) continue
            if (t.keyShadow != 0) c.fillRoundRect(b.offset(0f, dp), 7f * dp, t.keyShadow)
            c.fillRoundRect(b, 7f * dp, if (i == pressed) t.pressed else t.key)
            c.textIn(s, b, textSize, t.ink, bold = i == 0)
        }
        c.restore()
    }

    fun hit(x: Float, y: Float): Int {
        if (!box.contains(x, y)) return -1
        val cx = x - box.left
        val cy = y - box.top + scrollY
        return cells.indexOfFirst { it.contains(cx, cy) }
    }
}

/** Emoji and kaomoji by category, with a small bar to get back to the letters. */
class EmojiBoard {
    class Category(val icon: Icon, val name: String, var items: List<String>, val wide: Boolean = false)

    sealed class Hit {
        data class Tab(val index: Int) : Hit()
        data class Item(val index: Int) : Hit()
        object Letters : Hit()
        object Space : Hit()
        object Delete : Hit()
    }

    var dp = 1f
    var categories: List<Category> = emptyList()
    var selected = 0
    var scrollY = 0f
    var lettersLabel = "拼音"
    val box = Box()
    val maxScroll: Float get() = max(0f, contentHeight - gridBox.height)
    val gridArea: Box get() = gridBox

    private val tabBoxes = ArrayList<Box>()
    private val gridBox = Box()
    private val cells = ArrayList<Box>()
    private val lettersBox = Box()
    private val spaceBox = Box()
    private val deleteBox = Box()
    private var contentHeight = 0f

    val current: Category? get() = categories.getOrNull(selected)

    fun layout(c: Canvas2D, area: Box) {
        box.set(area.left, area.top, area.right, area.bottom)
        val pad = 6f * dp
        val gap = 6f * dp
        // Tabs.
        tabBoxes.clear()
        val tabH = 44f * dp
        val tabW = minOf(60f * dp, (box.width - 2 * pad) / max(1, categories.size))
        var x = box.centerX - tabW * categories.size / 2
        for (i in categories.indices) {
            tabBoxes.add(Box(x, box.top, x + tabW, box.top + tabH)); x += tabW
        }
        // Bottom bar, shaped like the keyboard's bottom row.
        val barH = 50f * dp
        val barTop = box.bottom - barH
        val unit = (box.width - 2 * pad) / 10f
        lettersBox.set(box.left + pad + gap / 2, barTop + gap / 2, box.left + pad + 1.5f * unit - gap / 2, box.bottom - gap)
        deleteBox.set(box.right - pad - 1.5f * unit + gap / 2, barTop + gap / 2, box.right - pad - gap / 2, box.bottom - gap)
        spaceBox.set(lettersBox.right + gap, barTop + gap / 2, deleteBox.left - gap, box.bottom - gap)
        gridBox.set(box.left, box.top + tabH, box.right, barTop)

        // Grid, in coordinates relative to the grid's top-left corner.
        cells.clear()
        val cat = current ?: return
        val usable = gridBox.width - 2 * pad
        if (!cat.wide) {
            val cols = max(1, floor(usable / (50f * dp)).toInt())
            val cw = usable / cols
            for ((i, _) in cat.items.withIndex()) {
                val r = i / cols
                val col = i % cols
                cells.add(Box(pad + col * cw, r * cw, pad + (col + 1) * cw, (r + 1) * cw))
            }
            contentHeight = ceil(cat.items.size / cols.toFloat()) * cw + pad
        } else {
            val cols = max(1, floor((usable + gap) / (118f * dp + gap)).toInt())
            val colW = (usable + gap) / cols
            val cellH = 48f * dp
            var col = 0
            var y = 0f
            for (s in cat.items) {
                val span = ceil((c.measure(s, 16f * dp) + 24f * dp + gap) / colW).toInt().coerceIn(1, cols)
                if (col + span > cols) {
                    col = 0; y += cellH + gap
                }
                cells.add(Box(pad + col * colW, y, pad + (col + span) * colW - gap, y + cellH))
                col += span
            }
            contentHeight = if (cat.items.isEmpty()) 0f else y + cellH + pad
        }
        scrollY = scrollY.coerceIn(0f, maxScroll)
    }

    fun draw(c: Canvas2D, t: Theme, pressed: Hit?) {
        c.fillRect(box, t.background)
        for ((i, b) in tabBoxes.withIndex()) {
            val on = i == selected
            if (on) c.fillRoundRect(b.inset(6f * dp, 5f * dp), 10f * dp, t.chip)
            c.icon(categories[i].icon, b.centerX, b.centerY, 24f * dp, if (on) t.ink else t.inkSoft)
        }
        c.line(box.left, gridBox.top, box.right, gridBox.top, dp, t.divider)

        val cat = current
        c.save()
        c.clip(gridBox)
        c.translate(gridBox.left, gridBox.top - scrollY)
        if (cat != null) {
            if (cat.items.isEmpty()) {
                c.textIn("还没有用过的表情", Box(0f, 0f, gridBox.width, gridBox.height), 14f * dp, t.inkSoft)
            }
            for ((i, s) in cat.items.withIndex()) {
                val b = cells[i]
                if (b.bottom < scrollY || b.top > scrollY + gridBox.height) continue
                val down = pressed == Hit.Item(i)
                if (cat.wide) {
                    c.fillRoundRect(b, 7f * dp, if (down) t.pressed else t.key)
                    c.textIn(s, b, 16f * dp, t.ink)
                } else {
                    if (down) c.fillRoundRect(b.inset(3f * dp, 3f * dp), 10f * dp, t.chip)
                    c.textIn(s, b, 28f * dp, t.ink)
                }
            }
        }
        c.restore()

        fun key(b: Box, down: Boolean, bg: Int) {
            if (t.keyShadow != 0) c.fillRoundRect(b.offset(0f, dp), 7f * dp, t.keyShadow)
            c.fillRoundRect(b, 7f * dp, if (down) t.functionPressed else bg)
        }
        key(lettersBox, pressed == Hit.Letters, t.functionKey)
        c.textIn(lettersLabel, lettersBox, 15f * dp, t.ink)
        key(spaceBox, pressed == Hit.Space, t.key)
        c.textIn(categories.getOrNull(selected)?.name ?: "", spaceBox, 13f * dp, t.inkSoft)
        key(deleteBox, pressed == Hit.Delete, t.functionKey)
        c.icon(Icons.BACKSPACE, deleteBox.centerX, deleteBox.centerY, 25f * dp, t.ink)
    }

    fun hit(x: Float, y: Float): Hit? {
        if (!box.contains(x, y)) return null
        tabBoxes.indexOfFirst { it.contains(x, y) }.let { if (it >= 0) return Hit.Tab(it) }
        if (lettersBox.inset(-3f * dp, -3f * dp).contains(x, y)) return Hit.Letters
        if (deleteBox.inset(-3f * dp, -3f * dp).contains(x, y)) return Hit.Delete
        if (spaceBox.inset(-3f * dp, -3f * dp).contains(x, y)) return Hit.Space
        if (!gridBox.contains(x, y)) return null
        val gx = x - gridBox.left
        val gy = y - gridBox.top + scrollY
        val i = cells.indexOfFirst { it.contains(gx, gy) }
        return if (i >= 0) Hit.Item(i) else null
    }
}
