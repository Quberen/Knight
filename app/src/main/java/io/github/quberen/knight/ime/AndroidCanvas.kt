package io.github.quberen.knight.ime

import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import io.github.quberen.knight.ui.Align
import io.github.quberen.knight.ui.Box
import io.github.quberen.knight.ui.Canvas2D
import io.github.quberen.knight.ui.Icon
import io.github.quberen.knight.ui.Layer
import io.github.quberen.knight.ui.PathSink

/** [Canvas2D] on an Android canvas. Measuring works without one. */
class AndroidCanvas : Canvas2D {
    var canvas: Canvas? = null

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeJoin = Paint.Join.ROUND
    }
    private val text = Paint(Paint.ANTI_ALIAS_FLAG or Paint.SUBPIXEL_TEXT_FLAG)
    private val medium: Typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    private val rect = RectF()
    private val paths = HashMap<Layer, Path>()

    private fun textPaint(size: Float, bold: Boolean): Paint {
        text.textSize = size
        text.typeface = if (bold) medium else Typeface.DEFAULT
        return text
    }

    override fun fillRect(b: Box, color: Int) {
        fill.color = color
        canvas?.drawRect(b.left, b.top, b.right, b.bottom, fill)
    }

    override fun fillRoundRect(b: Box, radius: Float, color: Int) {
        fill.color = color
        rect.set(b.left, b.top, b.right, b.bottom)
        canvas?.drawRoundRect(rect, radius, radius, fill)
    }

    override fun line(x1: Float, y1: Float, x2: Float, y2: Float, width: Float, color: Int) {
        stroke.color = color
        stroke.strokeWidth = width
        stroke.strokeCap = Paint.Cap.BUTT
        canvas?.drawLine(x1, y1, x2, y2, stroke)
    }

    override fun text(s: String, x: Float, baseline: Float, size: Float, color: Int, bold: Boolean, align: Align) {
        val p = textPaint(size, bold)
        p.color = color
        p.textAlign = when (align) {
            Align.LEFT -> Paint.Align.LEFT
            Align.CENTER -> Paint.Align.CENTER
            Align.RIGHT -> Paint.Align.RIGHT
        }
        canvas?.drawText(s, x, baseline, p)
    }

    override fun measure(s: String, size: Float, bold: Boolean): Float = textPaint(size, bold).measureText(s)

    override fun ascent(size: Float): Float = -textPaint(size, false).ascent()

    override fun descent(size: Float): Float = textPaint(size, false).descent()

    private fun pathOf(layer: Layer): Path = paths.getOrPut(layer) {
        val p = Path()
        if (layer.evenOdd) p.fillType = Path.FillType.EVEN_ODD
        layer.replay(object : PathSink {
            override fun moveTo(x: Float, y: Float) = p.moveTo(x, y)
            override fun lineTo(x: Float, y: Float) = p.lineTo(x, y)
            override fun cubicTo(x1: Float, y1: Float, x2: Float, y2: Float, x: Float, y: Float) = p.cubicTo(x1, y1, x2, y2, x, y)
            override fun close() = p.close()
        })
        p
    }

    override fun icon(icon: Icon, cx: Float, cy: Float, size: Float, color: Int) {
        val c = canvas ?: return
        c.save()
        c.translate(cx - size / 2, cy - size / 2)
        val s = size / 24f
        c.scale(s, s)
        for (layer in icon.layers) {
            val alpha = (Color.alpha(color) * layer.alpha).toInt().coerceIn(0, 255)
            if (layer.style == Layer.FILL) {
                fill.color = color
                fill.alpha = alpha
                c.drawPath(pathOf(layer), fill)
            } else {
                stroke.color = color
                stroke.alpha = alpha
                stroke.strokeWidth = layer.strokeWidth
                stroke.strokeCap = Paint.Cap.ROUND
                c.drawPath(pathOf(layer), stroke)
            }
        }
        c.restore()
    }

    override fun save() {
        canvas?.save()
    }

    override fun restore() {
        canvas?.restore()
    }

    override fun clip(b: Box) {
        canvas?.clipRect(b.left, b.top, b.right, b.bottom)
    }

    override fun translate(dx: Float, dy: Float) {
        canvas?.translate(dx, dy)
    }
}
