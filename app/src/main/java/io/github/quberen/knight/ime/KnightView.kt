package io.github.quberen.knight.ime

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.os.Handler
import android.os.Looper
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.VelocityTracker
import android.view.View
import android.view.ViewConfiguration
import android.widget.OverScroller
import io.github.quberen.knight.ui.CandidateStrip
import io.github.quberen.knight.ui.EmojiBoard
import io.github.quberen.knight.ui.Key
import io.github.quberen.knight.ui.KeyType
import io.github.quberen.knight.ui.Screen
import io.github.quberen.knight.ui.Theme
import kotlin.math.abs

/**
 * The whole input view, drawn by [Screen]. Handles fingers: several at once on
 * the keys, and dragging and flinging in the strip, the candidate grid and the emoji board.
 */
@SuppressLint("ViewConstructor")
class KnightView(context: Context, private val listener: Listener) : View(context) {

    interface Listener {
        /** [longPress] is true when the key's hint (or its long-press action) is wanted. */
        fun onKey(key: Key, longPress: Boolean)
        fun onCursorMove(steps: Int)
        fun onStrip(hit: CandidateStrip.Hit)
        fun onGridPick(index: Int)
        fun onEmoji(hit: EmojiBoard.Hit, repeat: Boolean)
    }

    val screen = Screen()
    var theme: Theme = Theme.LIGHT
    var vibrate = true

    private val canvas2d = AndroidCanvas()
    private val pressed = Screen.Pressed()
    private val handler = Handler(Looper.getMainLooper())
    private val slop = ViewConfiguration.get(context).scaledTouchSlop.toFloat()
    private val scroller = OverScroller(context)
    private var velocity: VelocityTracker? = null
    private val dp = resources.displayMetrics.density

    init {
        screen.dp = dp
    }

    /** Call after changing anything in [screen]. */
    fun update() {
        val h = screen.height().toInt()
        if (h != height) requestLayout()
        if (width > 0) screen.layout(canvas2d, width.toFloat())
        invalidate()
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        setMeasuredDimension(MeasureSpec.getSize(widthMeasureSpec), screen.height().toInt())
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        screen.layout(canvas2d, w.toFloat())
    }

    override fun onDraw(canvas: Canvas) {
        canvas2d.canvas = canvas
        screen.draw(canvas2d, theme, pressed)
        canvas2d.canvas = null
    }

    private fun feedback() {
        if (vibrate) performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
    }

    // ---- touch -------------------------------------------------------------

    private enum class Region { NONE, KEYS, STRIP, GRID, EMOJI }

    private var region = Region.NONE

    /** One finger on the keys. */
    private class Press(val key: Key, val downX: Float) {
        var lastX = downX
        var longFired = false
        var sliding = false
        var timer: Runnable? = null
    }

    private val presses = HashMap<Int, Press>()

    /** The finger in the strip, grid or emoji board. */
    private var downX = 0f
    private var downY = 0f
    private var startScroll = 0f
    private var dragging = false
    private var emojiTimer: Runnable? = null
    private var emojiRepeating = false

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (e.actionMasked == MotionEvent.ACTION_DOWN) {
            scroller.forceFinished(true)
            region = when {
                e.y < screen.strip.height -> Region.STRIP
                screen.body == Screen.Body.KEYS -> Region.KEYS
                screen.body == Screen.Body.CANDIDATES -> Region.GRID
                else -> Region.EMOJI
            }
        }
        if (region == Region.KEYS) return keysTouch(e)
        return panelTouch(e)
    }

    private fun keysTouch(e: MotionEvent): Boolean {
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                // Fast typing: a new finger finishes the letter still held by the last one.
                for ((id, p) in presses.toList()) {
                    if (p.key.type == KeyType.CHAR || p.key.type == KeyType.SEPARATOR) {
                        stopTimer(p); fire(p); presses.remove(id); pressed.keys.remove(p.key)
                    }
                }
                val i = e.actionIndex
                val key = screen.keys.keyAt(e.getX(i), e.getY(i)) ?: return true
                val p = Press(key, e.getX(i))
                presses[e.getPointerId(i)] = p
                pressed.keys.add(key)
                feedback()
                if (key.type == KeyType.BACKSPACE) listener.onKey(key, false)
                startTimer(p)
                invalidate()
            }
            MotionEvent.ACTION_MOVE -> {
                for (i in 0 until e.pointerCount) {
                    val p = presses[e.getPointerId(i)] ?: continue
                    if (p.key.type != KeyType.SPACE) continue
                    // Slide along the space bar to move the cursor.
                    val x = e.getX(i)
                    if (!p.sliding && abs(x - p.downX) > 16 * dp) {
                        p.sliding = true
                        stopTimer(p)
                        p.lastX = x
                    }
                    if (p.sliding) {
                        val step = 12 * dp
                        val steps = ((x - p.lastX) / step).toInt()
                        if (steps != 0) {
                            listener.onCursorMove(steps)
                            p.lastX += steps * step
                        }
                    }
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                presses.remove(e.getPointerId(e.actionIndex))?.let { p ->
                    stopTimer(p); fire(p); pressed.keys.remove(p.key)
                }
                invalidate()
            }
            MotionEvent.ACTION_CANCEL -> {
                presses.values.forEach(::stopTimer)
                presses.clear()
                pressed.keys.clear()
                invalidate()
            }
        }
        return true
    }

    private fun fire(p: Press) {
        if (!p.longFired && !p.sliding && p.key.type != KeyType.BACKSPACE) listener.onKey(p.key, false)
    }

    private fun startTimer(p: Press) {
        val r = object : Runnable {
            override fun run() {
                when {
                    p.key.type == KeyType.BACKSPACE -> {
                        p.longFired = true
                        listener.onKey(p.key, false)
                        handler.postDelayed(this, 50)
                    }
                    p.key.type == KeyType.SPACE -> Unit
                    p.key.hint != null || p.key.type == KeyType.LANG -> {
                        p.longFired = true
                        feedback()
                        listener.onKey(p.key, true)
                    }
                }
            }
        }
        p.timer = r
        handler.postDelayed(r, 400)
    }

    private fun stopTimer(p: Press) {
        p.timer?.let { handler.removeCallbacks(it) }
    }

    private fun panelTouch(e: MotionEvent): Boolean {
        val vt = velocity ?: VelocityTracker.obtain().also { velocity = it }
        vt.addMovement(e)
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                downX = e.x; downY = e.y; dragging = false
                when (region) {
                    Region.STRIP -> {
                        startScroll = screen.strip.scrollX
                        pressed.strip = screen.strip.hit(e.x, e.y)
                    }
                    Region.GRID -> {
                        startScroll = screen.grid.scrollY
                        pressed.grid = screen.grid.hit(e.x, e.y)
                    }
                    Region.EMOJI -> {
                        startScroll = screen.emoji.scrollY
                        val hit = screen.emoji.hit(e.x, e.y)
                        pressed.emoji = hit
                        if (hit == EmojiBoard.Hit.Delete) startDeleteRepeat()
                    }
                    else -> Unit
                }
                if (pressed.strip != null || pressed.grid >= 0 || pressed.emoji != null) feedback()
                invalidate()
            }
            MotionEvent.ACTION_MOVE -> {
                val dx = e.x - downX
                val dy = e.y - downY
                when (region) {
                    Region.STRIP -> if (dragging || abs(dx) > slop) {
                        if (screen.strip.maxScroll > 0) {
                            dragging = true
                            pressed.strip = null
                            screen.strip.scrollX = (startScroll - dx).coerceIn(0f, screen.strip.maxScroll)
                        }
                    }
                    Region.GRID -> if (dragging || abs(dy) > slop) {
                        dragging = true
                        pressed.grid = -1
                        screen.grid.scrollY = (startScroll - dy).coerceIn(0f, screen.grid.maxScroll)
                    }
                    Region.EMOJI -> if ((dragging || abs(dy) > slop) && pressed.emoji !is EmojiBoard.Hit.Tab &&
                        pressed.emoji != EmojiBoard.Hit.Delete && screen.emoji.gridArea.contains(downX, downY)
                    ) {
                        dragging = true
                        pressed.emoji = null
                        screen.emoji.scrollY = (startScroll - dy).coerceIn(0f, screen.emoji.maxScroll)
                    }
                    else -> Unit
                }
                invalidate()
            }
            MotionEvent.ACTION_UP -> {
                stopDeleteRepeat()
                if (dragging) {
                    vt.computeCurrentVelocity(1000)
                    fling(vt.xVelocity, vt.yVelocity)
                } else {
                    when (region) {
                        Region.STRIP -> pressed.strip?.let { listener.onStrip(it) }
                        Region.GRID -> pressed.grid.takeIf { it >= 0 }?.let { listener.onGridPick(it) }
                        Region.EMOJI -> pressed.emoji?.let { if (!(it == EmojiBoard.Hit.Delete && emojiRepeating)) listener.onEmoji(it, false) }
                        else -> Unit
                    }
                }
                endPanelTouch()
            }
            MotionEvent.ACTION_CANCEL -> {
                stopDeleteRepeat()
                endPanelTouch()
            }
        }
        return true
    }

    private fun endPanelTouch() {
        pressed.strip = null
        pressed.grid = -1
        pressed.emoji = null
        velocity?.recycle()
        velocity = null
        invalidate()
    }

    private fun startDeleteRepeat() {
        emojiRepeating = false
        val r = object : Runnable {
            override fun run() {
                emojiRepeating = true
                listener.onEmoji(EmojiBoard.Hit.Delete, true)
                handler.postDelayed(this, 50)
            }
        }
        emojiTimer = r
        handler.postDelayed(r, 400)
    }

    private fun stopDeleteRepeat() {
        emojiTimer?.let { handler.removeCallbacks(it) }
        emojiTimer = null
    }

    private fun fling(vx: Float, vy: Float) {
        when (region) {
            Region.STRIP -> scroller.fling(screen.strip.scrollX.toInt(), 0, -vx.toInt(), 0, 0, screen.strip.maxScroll.toInt(), 0, 0)
            Region.GRID -> scroller.fling(0, screen.grid.scrollY.toInt(), 0, -vy.toInt(), 0, 0, 0, screen.grid.maxScroll.toInt())
            Region.EMOJI -> scroller.fling(0, screen.emoji.scrollY.toInt(), 0, -vy.toInt(), 0, 0, 0, screen.emoji.maxScroll.toInt())
            else -> return
        }
        flingRegion = region
        postInvalidateOnAnimation()
    }

    private var flingRegion = Region.NONE

    override fun computeScroll() {
        if (!scroller.computeScrollOffset()) return
        when (flingRegion) {
            Region.STRIP -> screen.strip.scrollX = scroller.currX.toFloat()
            Region.GRID -> screen.grid.scrollY = scroller.currY.toFloat()
            Region.EMOJI -> screen.emoji.scrollY = scroller.currY.toFloat()
            else -> return
        }
        postInvalidateOnAnimation()
    }

    /** Stops timers when the keyboard goes away. */
    fun reset() {
        presses.values.forEach(::stopTimer)
        presses.clear()
        stopDeleteRepeat()
        scroller.forceFinished(true)
        pressed.clear()
    }
}
