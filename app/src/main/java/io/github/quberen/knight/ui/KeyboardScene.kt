package io.github.quberen.knight.ui

/** Places the keys of the current layout, draws them, and finds the key under a finger. */
class KeyboardScene {
    var blocks: List<Block> = emptyList()

    /** 0 = off, 1 = next letter only, 2 = caps lock. */
    var shiftState = 0
    var chinese = true
    var enterIcon: Icon? = Icons.RETURN
    var enterLabel: String? = null
    var spaceLabel = ""

    var dp = 1f
    var heightScale = 1f

    /** Width of the standard and trapezoid keyboards, as % of the screen. */
    var widthPercent = 100

    /** Width of each half of the split keyboard, as % of the screen. */
    var splitPercent = 34

    val box = Box()

    private val rowHeight get() = 54f * dp * heightScale
    private val gap get() = 6f * dp
    private val padX get() = 6f * dp
    private val padTop get() = 4f * dp
    private val padBottom get() = 6f * dp
    private val radius get() = 7f * dp
    private val textScale get() = heightScale.coerceIn(0.85f, 1.15f)

    fun height(): Float {
        val rows = blocks.maxOfOrNull { b -> b.rows.sumOf { it.height.toDouble() } }?.toFloat() ?: 0f
        return padTop + rows * rowHeight + padBottom
    }

    fun layout(left: Float, top: Float, width: Float) {
        box.set(left, top, left + width, top + height())
        val usable = width - 2 * padX
        for (block in blocks) {
            val bw = when (block.side) {
                Side.FULL -> usable * (widthPercent / 100f).coerceIn(0.4f, 1f)
                else -> (width * splitPercent / 100f).coerceAtMost(usable / 2 - gap)
            }
            val bx = when (block.side) {
                Side.FULL -> left + padX + (usable - bw) / 2
                Side.LEFT -> left + padX
                Side.RIGHT -> left + width - padX - bw
            }
            var y = top + padTop
            for (row in block.rows) {
                val h = row.height * rowHeight
                val unit = bw / row.keys.sumOf { it.width.toDouble() }.toFloat()
                var x = bx
                for (k in row.keys) {
                    val w = k.width * unit
                    k.box.set(x + gap / 2, y + gap / 2, x + w - gap / 2, y + h - gap / 2)
                    x += w
                }
                y += h
            }
        }
    }

    /**
     * Draws a key label centred. Full-width Chinese punctuation sits in a corner
     * of its square in most fonts, so it is moved back to the middle.
     */
    private fun glyph(c: Canvas2D, s: String, b: Box, size: Float, color: Int) {
        val (dx, dy) = when (s) {
            "，", "。", "、", "．" -> 0.24f to -0.26f
            "：", "；", "！", "？" -> 0.24f to 0f
            "「", "『" -> 0.22f to 0.22f
            "」", "』" -> -0.22f to -0.22f
            else -> 0f to 0f
        }
        c.text(s, b.centerX + dx * size, b.centerY + (c.ascent(size) - c.descent(size)) / 2 + dy * size, size, color)
    }

    /** The key under (x, y). Touches in the gap of a split keyboard hit nothing. */
    fun keyAt(x: Float, y: Float): Key? {
        if (!box.contains(x, y)) return null
        var best: Key? = null
        var bestScore = Float.MAX_VALUE
        for (block in blocks) for (row in block.rows) for (k in row.keys) {
            val dx = if (x < k.box.left) k.box.left - x else if (x > k.box.right) x - k.box.right else 0f
            val dy = if (y < k.box.top) k.box.top - y else if (y > k.box.bottom) y - k.box.bottom else 0f
            val score = dx + dy * 2
            if (score < bestScore) {
                bestScore = score; best = k
            }
        }
        return if (bestScore <= gap * 2) best else null
    }

    fun draw(c: Canvas2D, t: Theme, pressed: Collection<Key>) {
        c.fillRect(box, t.background)
        for (block in blocks) for (row in block.rows) for (k in row.keys) drawKey(c, t, k, k in pressed)
    }

    private fun drawKey(c: Canvas2D, t: Theme, k: Key, down: Boolean) {
        val enter = k.type == KeyType.ENTER
        val bg = when {
            enter && down -> t.accent.withAlpha(0.78f)
            enter -> t.accent
            down && (k.isFunction || k.quiet) -> t.functionPressed
            down -> t.pressed
            k.isFunction || k.quiet -> t.functionKey
            else -> t.key
        }
        if (t.keyShadow != 0) c.fillRoundRect(k.box.offset(0f, dp), radius, t.keyShadow)
        c.fillRoundRect(k.box, radius, bg)

        val ink = if (enter) t.onAccent else t.ink
        val iconSize = 25f * dp * textScale
        val b = k.box
        when (k.type) {
            KeyType.CHAR, KeyType.SEPARATOR -> {
                val label = if (shiftState > 0 && k.label.length == 1 && k.label[0] in 'a'..'z') k.label.uppercase() else k.label
                val size = when {
                    label.length > 1 -> 16f
                    b.height < rowHeight * 0.85f -> 19f
                    else -> 22f
                } * dp * textScale
                glyph(c, label, b, size, t.ink)
                k.hint?.let {
                    val hs = 10.5f * dp * textScale
                    c.text(it, b.right - 6f * dp, b.top + 5f * dp + c.ascent(hs), hs, t.inkSoft, align = Align.RIGHT)
                }
            }
            KeyType.SPACE -> if (spaceLabel.isNotEmpty()) c.textIn(spaceLabel, b, 13f * dp * textScale, t.inkSoft)
            KeyType.SHIFT -> c.icon(
                when (shiftState) {
                    0 -> Icons.SHIFT
                    1 -> Icons.SHIFT_ON
                    else -> Icons.CAPS_LOCK
                }, b.centerX, b.centerY, iconSize, t.ink,
            )
            KeyType.BACKSPACE -> c.icon(Icons.BACKSPACE, b.centerX, b.centerY, iconSize, t.ink)
            KeyType.ENTER -> {
                val label = enterLabel
                val icon = enterIcon
                if (icon != null) c.icon(icon, b.centerX, b.centerY, iconSize, ink)
                else if (label != null) c.textIn(label, b, 15f * dp * textScale, ink)
            }
            KeyType.EMOJI -> c.icon(Icons.EMOJI, b.centerX, b.centerY, iconSize, t.ink)
            KeyType.LEFT -> c.icon(Icons.ARROW_LEFT, b.centerX, b.centerY, iconSize, t.ink)
            KeyType.RIGHT -> c.icon(Icons.ARROW_RIGHT, b.centerX, b.centerY, iconSize, t.ink)
            KeyType.HIDE -> c.icon(Icons.HIDE_KEYBOARD, b.centerX, b.centerY, iconSize, t.ink)
            KeyType.LANG -> {
                // The active language large, the other one small beside it.
                val big = 17f * dp * textScale
                val small = 11f * dp * textScale
                val (on, off) = if (chinese) "中" to "英" else "英" to "中"
                val wBig = c.measure(on, big)
                val wSmall = c.measure(off, small)
                val x0 = b.centerX - (wBig + 2f * dp + wSmall) / 2
                val base = b.centerY + (c.ascent(big) - c.descent(big)) / 2
                c.text(on, x0, base, big, t.ink, align = Align.LEFT)
                c.text(off, x0 + wBig + 2f * dp, base, small, t.inkSoft, align = Align.LEFT)
            }
            KeyType.TO_SYMBOLS, KeyType.TO_LETTERS, KeyType.SYMBOL_SET -> c.textIn(k.label, b, 15f * dp * textScale, t.ink)
        }
    }
}
