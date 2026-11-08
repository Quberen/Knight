package io.github.quberen.knight.preview

import io.github.quberen.knight.ui.Align
import io.github.quberen.knight.ui.Box
import io.github.quberen.knight.ui.Canvas2D
import io.github.quberen.knight.ui.Icon
import io.github.quberen.knight.ui.Layer
import io.github.quberen.knight.ui.PathSink
import java.awt.BasicStroke
import java.awt.Color
import java.awt.Font
import java.awt.Graphics2D
import java.awt.RenderingHints
import java.awt.geom.AffineTransform
import java.awt.geom.Ellipse2D
import java.awt.geom.Line2D
import java.awt.geom.Path2D
import java.awt.geom.Rectangle2D
import java.awt.geom.RoundRectangle2D
import java.awt.Shape

/**
 * Draws the keyboard with Java2D, for previews on a computer. Latin text uses
 * Inter and Chinese uses WenQuanYi. Emoji and symbols Java2D can't draw come
 * from [raster] (Chromium); without it they show as grey dots.
 */
class AwtCanvas(private val g: Graphics2D, private val raster: Raster? = null) : Canvas2D {
    private val latin = Font("Inter", Font.PLAIN, 12)
    private val cjk = Font("WenQuanYi Zen Hei", Font.PLAIN, 12)
    private val states = ArrayList<Pair<AffineTransform, Shape?>>()
    private val paths = HashMap<Layer, Path2D.Float>()

    init {
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON)
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON)
        g.setRenderingHint(RenderingHints.KEY_FRACTIONALMETRICS, RenderingHints.VALUE_FRACTIONALMETRICS_ON)
        g.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL, RenderingHints.VALUE_STROKE_PURE)
    }

    private fun color(c: Int) = Color(c, true)

    private fun font(s: String, size: Float, bold: Boolean): Font {
        val base = if (s.any { it.code > 0x2E80 }) cjk else latin
        return base.deriveFont(if (bold) Font.BOLD else Font.PLAIN, size)
    }

    private fun isEmoji(s: String): Boolean = s.codePoints().anyMatch { it >= 0x1F000 || it in 0x2600..0x27BF || it == 0xFE0F }

    override fun fillRect(b: Box, color: Int) {
        g.color = color(color)
        g.fill(Rectangle2D.Float(b.left, b.top, b.width, b.height))
    }

    override fun fillRoundRect(b: Box, radius: Float, color: Int) {
        g.color = color(color)
        g.fill(RoundRectangle2D.Float(b.left, b.top, b.width, b.height, radius * 2, radius * 2))
    }

    override fun line(x1: Float, y1: Float, x2: Float, y2: Float, width: Float, color: Int) {
        g.color = color(color)
        g.stroke = BasicStroke(width)
        g.draw(Line2D.Float(x1, y1, x2, y2))
    }

    override fun text(s: String, x: Float, baseline: Float, size: Float, color: Int, bold: Boolean, align: Align) {
        val w = measure(s, size, bold)
        val left = when (align) {
            Align.LEFT -> x
            Align.CENTER -> x - w / 2
            Align.RIGHT -> x - w
        }
        if (raster != null && needsRaster(s, size, bold)) {
            val e = raster.get(s, size, color) ?: return
            val l = when (align) {
                Align.LEFT -> x
                Align.CENTER -> x - e.width / 2
                Align.RIGHT -> x - e.width
            }
            g.drawImage(e.image, Math.round(l), Math.round(baseline - e.ascent), null)
            return
        }
        if (isEmoji(s)) {
            g.color = color(color).let { Color(it.red, it.green, it.blue, 70) }
            val d = size * 0.9f
            g.fill(Ellipse2D.Float(left + (w - d) / 2, baseline - size * 0.8f, d, d))
            return
        }
        g.font = font(s, size, bold)
        g.color = color(color)
        g.drawString(s, left, baseline)
    }

    private fun needsRaster(s: String, size: Float, bold: Boolean) =
        isEmoji(s) || font(s, size, bold).canDisplayUpTo(s) != -1

    override fun measure(s: String, size: Float, bold: Boolean): Float {
        if (raster != null && needsRaster(s, size, bold)) return raster.width(s, size) ?: (size * 0.6f * s.length)
        if (isEmoji(s)) return size * s.codePointCount(0, s.length).coerceAtMost(1)
        val f = font(s, size, bold)
        return f.getStringBounds(s, g.fontRenderContext).width.toFloat()
    }

    override fun ascent(size: Float): Float = cjk.deriveFont(size).getLineMetrics("中", g.fontRenderContext).ascent * 0.88f

    override fun descent(size: Float): Float = cjk.deriveFont(size).getLineMetrics("中", g.fontRenderContext).descent * 0.9f

    private fun pathOf(layer: Layer): Path2D.Float = paths.getOrPut(layer) {
        val p = Path2D.Float(if (layer.evenOdd) Path2D.WIND_EVEN_ODD else Path2D.WIND_NON_ZERO)
        layer.replay(object : PathSink {
            override fun moveTo(x: Float, y: Float) = p.moveTo(x, y)
            override fun lineTo(x: Float, y: Float) = p.lineTo(x, y)
            override fun cubicTo(x1: Float, y1: Float, x2: Float, y2: Float, x: Float, y: Float) = p.curveTo(x1, y1, x2, y2, x, y)
            override fun close() = p.closePath()
        })
        p
    }

    override fun icon(icon: Icon, cx: Float, cy: Float, size: Float, color: Int) {
        val saved = g.transform
        g.translate((cx - size / 2).toDouble(), (cy - size / 2).toDouble())
        g.scale(size / 24.0, size / 24.0)
        for (layer in icon.layers) {
            val c = color(color)
            g.color = Color(c.red, c.green, c.blue, (c.alpha * layer.alpha).toInt().coerceIn(0, 255))
            val p = pathOf(layer)
            if (layer.style == Layer.FILL) {
                g.fill(p)
            } else {
                g.stroke = BasicStroke(layer.strokeWidth, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND)
                g.draw(p)
            }
        }
        g.transform = saved
    }

    override fun save() {
        states.add(g.transform to g.clip)
    }

    override fun restore() {
        val (t, c) = states.removeAt(states.size - 1)
        g.transform = t
        g.clip = c
    }

    override fun clip(b: Box) = g.clip(Rectangle2D.Float(b.left, b.top, b.width, b.height))

    override fun translate(dx: Float, dy: Float) = g.translate(dx.toDouble(), dy.toDouble())
}
