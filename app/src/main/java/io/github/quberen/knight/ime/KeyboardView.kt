package io.github.quberen.knight.ime

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Typeface
import android.os.Handler
import android.os.Looper
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import kotlin.math.abs

/** Draws the keys itself, so sizes and behavior are fully ours. */
@SuppressLint("ViewConstructor")
class KeyboardView(context: Context, private val listener: Listener) : View(context) {

    interface Listener {
        /** [longPress] is true when the key's hint should be typed instead. */
        fun onKey(key: Key, longPress: Boolean)
        fun onCursorMove(steps: Int)
    }

    var palette: Palette = Palette.of(context)
        set(v) { field = v; invalidate() }

    var rows: List<Row> = emptyList()
        set(v) { field = v; requestLayout(); layoutKeys(); invalidate() }

    /** 0 = off, 1 = next letter only, 2 = caps lock. */
    var shiftState = 0
        set(v) { field = v; invalidate() }

    var enterLabel = "↵"
        set(v) { field = v; invalidate() }

    var spaceLabel = ""
        set(v) { field = v; invalidate() }

    var heightScale = 1f
        set(v) { field = v; requestLayout() }

    var widthFraction = 1f
        set(v) { field = v; layoutKeys(); invalidate() }

    var vibrate = true

    private val dp = resources.displayMetrics.density
    private val rowHeight get() = 56 * dp * heightScale
    private val gap = 5 * dp
    private val pad = 4 * dp

    private val keyPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textAlign = Paint.Align.CENTER }
    private val hintPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textAlign = Paint.Align.RIGHT }

    private class Press(val key: Key, val downX: Float) {
        var lastX = downX
        var longFired = false
        var sliding = false
        var repeat: Runnable? = null
    }

    private val presses = HashMap<Int, Press>()
    private val handler = Handler(Looper.getMainLooper())

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val w = MeasureSpec.getSize(widthMeasureSpec)
        val h = rows.sumOf { (it.height * rowHeight).toDouble() }.toFloat() + pad * 2
        setMeasuredDimension(w, h.toInt())
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) = layoutKeys()

    private fun layoutKeys() {
        if (width == 0) return
        val contentWidth = width * widthFraction - pad * 2
        val left = (width - contentWidth) / 2
        var top = pad
        for (row in rows) {
            val h = row.height * rowHeight
            val unit = contentWidth / row.keys.sumOf { it.width.toDouble() }.toFloat()
            var x = left
            for (k in row.keys) {
                val w = k.width * unit
                k.rect.set(x + gap / 2, top + gap / 2, x + w - gap / 2, top + h - gap / 2)
                x += w
            }
            top += h
        }
    }

    override fun onDraw(canvas: Canvas) {
        canvas.drawColor(palette.background)
        val radius = 7 * dp
        val pressed = presses.values.map { it.key }.toSet()
        for (row in rows) for (k in row.keys) {
            val active = (k.type == KeyType.SHIFT && shiftState > 0) || k.type == KeyType.ENTER
            keyPaint.color = when {
                k in pressed -> palette.pressed
                active -> palette.accent
                k.isFunction -> palette.functionKey
                else -> palette.key
            }
            canvas.drawRoundRect(k.rect, radius, radius, keyPaint)

            val label = labelOf(k)
            textPaint.color = if (active && k !in pressed) palette.onAccent else palette.text
            val big = k.type == KeyType.CHAR && label.length == 1
            textPaint.textSize = (if (big) 22f else 16f) * dp * heightScale.coerceIn(0.8f, 1.2f)
            textPaint.typeface = if (big) Typeface.DEFAULT else Typeface.DEFAULT_BOLD
            val y = k.rect.centerY() - (textPaint.descent() + textPaint.ascent()) / 2
            canvas.drawText(label, k.rect.centerX(), y, textPaint)

            k.hint?.let {
                hintPaint.color = palette.hint
                hintPaint.textSize = 11 * dp * heightScale.coerceIn(0.8f, 1.2f)
                canvas.drawText(it, k.rect.right - 5 * dp, k.rect.top - hintPaint.ascent() + 3 * dp, hintPaint)
            }
        }
    }

    private fun labelOf(k: Key): String = when (k.type) {
        KeyType.CHAR -> if (shiftState > 0 && k.label.length == 1 && k.label[0] in 'a'..'z') k.label.uppercase() else k.label
        KeyType.SHIFT -> if (shiftState == 2) "⇪" else "⇧"
        KeyType.ENTER -> enterLabel
        KeyType.SPACE -> spaceLabel
        else -> k.label
    }

    private fun findKey(x: Float, y: Float): Key? {
        var top = pad
        for (row in rows) {
            val h = row.height * rowHeight
            if (y < top + h || row === rows.last()) {
                return row.keys.minByOrNull { k ->
                    if (x < k.rect.left) k.rect.left - x else if (x > k.rect.right) x - k.rect.right else 0f
                }
            }
            top += h
        }
        return null
    }

    private fun feedback() {
        if (vibrate) performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
    }

    private fun fire(p: Press) {
        if (!p.longFired && !p.sliding && p.key.type != KeyType.BACKSPACE) listener.onKey(p.key, false)
    }

    private fun startLongPress(p: Press) {
        val r = object : Runnable {
            override fun run() {
                when {
                    p.key.type == KeyType.BACKSPACE -> {
                        p.longFired = true
                        listener.onKey(p.key, false)
                        handler.postDelayed(this, 50)
                    }
                    p.key.hint != null -> {
                        p.longFired = true
                        feedback()
                        listener.onKey(p.key, true)
                    }
                }
            }
        }
        p.repeat = r
        handler.postDelayed(r, 400)
    }

    private fun release(p: Press) {
        p.repeat?.let { handler.removeCallbacks(it) }
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                // Fast typing: a new finger finishes the key still held by the previous one.
                for ((id, p) in presses.toList()) {
                    if (p.key.type == KeyType.CHAR || p.key.type == KeyType.SEPARATOR) {
                        release(p); fire(p); presses.remove(id)
                    }
                }
                val i = e.actionIndex
                val key = findKey(e.getX(i), e.getY(i)) ?: return true
                val p = Press(key, e.getX(i))
                presses[e.getPointerId(i)] = p
                feedback()
                if (key.type == KeyType.BACKSPACE) listener.onKey(key, false)
                startLongPress(p)
                invalidate()
            }
            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until e.pointerCount) {
                    val p = presses[e.getPointerId(i)] ?: continue
                    if (p.key.type != KeyType.SPACE) continue
                    // Slide on the space bar to move the cursor.
                    val x = e.getX(i)
                    if (!p.sliding && abs(x - p.downX) > 18 * dp) {
                        p.sliding = true
                        release(p)
                        p.lastX = x
                    }
                    if (p.sliding) {
                        val step = 14 * dp
                        val steps = ((x - p.lastX) / step).toInt()
                        if (steps != 0) {
                            listener.onCursorMove(steps)
                            p.lastX += steps * step
                        }
                    }
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                val p = presses.remove(e.getPointerId(e.actionIndex))
                if (p != null) {
                    release(p); fire(p)
                }
                invalidate()
            }
            MotionEvent.ACTION_CANCEL -> {
                presses.values.forEach(::release)
                presses.clear()
                invalidate()
            }
        }
        return true
    }
}
