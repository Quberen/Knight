package io.github.quberen.knight.ui

/** A drawing made of layers on a 24×24 grid. The shapes are SVG files in the icons folder. */
class Icon(val name: String, val layers: Array<Layer>)

/**
 * One filled or stroked path. [ops] holds commands: 0 x y = move, 1 x y = line,
 * 2 x1 y1 x2 y2 x y = cubic curve, 3 = close.
 */
class Layer(val style: Int, val alpha: Float, val strokeWidth: Float, val evenOdd: Boolean, val ops: FloatArray) {
    companion object {
        const val FILL = 0
        const val STROKE = 1
    }

    /** Feeds the path to a platform path builder. */
    fun replay(sink: PathSink) {
        var i = 0
        while (i < ops.size) {
            when (ops[i].toInt()) {
                0 -> { sink.moveTo(ops[i + 1], ops[i + 2]); i += 3 }
                1 -> { sink.lineTo(ops[i + 1], ops[i + 2]); i += 3 }
                2 -> { sink.cubicTo(ops[i + 1], ops[i + 2], ops[i + 3], ops[i + 4], ops[i + 5], ops[i + 6]); i += 7 }
                else -> { sink.close(); i += 1 }
            }
        }
    }
}

interface PathSink {
    fun moveTo(x: Float, y: Float)
    fun lineTo(x: Float, y: Float)
    fun cubicTo(x1: Float, y1: Float, x2: Float, y2: Float, x: Float, y: Float)
    fun close()
}

class Box(var left: Float = 0f, var top: Float = 0f, var right: Float = 0f, var bottom: Float = 0f) {
    val width: Float get() = right - left
    val height: Float get() = bottom - top
    val centerX: Float get() = (left + right) / 2
    val centerY: Float get() = (top + bottom) / 2

    fun set(l: Float, t: Float, r: Float, b: Float): Box {
        left = l; top = t; right = r; bottom = b
        return this
    }

    fun contains(x: Float, y: Float) = x >= left && x < right && y >= top && y < bottom

    fun inset(dx: Float, dy: Float) = Box(left + dx, top + dy, right - dx, bottom - dy)

    fun offset(dx: Float, dy: Float) = Box(left + dx, top + dy, right + dx, bottom + dy)
}

enum class Align { LEFT, CENTER, RIGHT }

/** The few drawing calls the keyboard needs, so it can draw on Android and in previews alike. */
interface Canvas2D {
    fun fillRect(b: Box, color: Int)
    fun fillRoundRect(b: Box, radius: Float, color: Int)
    fun line(x1: Float, y1: Float, x2: Float, y2: Float, width: Float, color: Int)
    fun text(s: String, x: Float, baseline: Float, size: Float, color: Int, bold: Boolean = false, align: Align = Align.CENTER)
    fun measure(s: String, size: Float, bold: Boolean = false): Float

    /** Distance from the baseline up to the top of the letters, positive. */
    fun ascent(size: Float): Float

    /** Distance from the baseline down to the bottom of the letters, positive. */
    fun descent(size: Float): Float

    /** Draws [icon] in a [size]×[size] square centred on (cx, cy). */
    fun icon(icon: Icon, cx: Float, cy: Float, size: Float, color: Int)

    fun save()
    fun restore()
    fun clip(b: Box)
    fun translate(dx: Float, dy: Float)
}

/** Text centred in [box] both ways. */
fun Canvas2D.textIn(s: String, box: Box, size: Float, color: Int, bold: Boolean = false) {
    text(s, box.centerX, box.centerY + (ascent(size) - descent(size)) / 2, size, color, bold)
}

fun Canvas2D.textAt(s: String, x: Float, centerY: Float, size: Float, color: Int, bold: Boolean = false, align: Align) {
    text(s, x, centerY + (ascent(size) - descent(size)) / 2, size, color, bold, align)
}

/** Colour with a new alpha (0–1). */
fun Int.withAlpha(a: Float): Int = ((((this ushr 24) and 0xFF) * a).toInt().coerceIn(0, 255) shl 24) or (this and 0xFFFFFF)
