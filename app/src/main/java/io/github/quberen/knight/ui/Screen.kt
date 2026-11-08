package io.github.quberen.knight.ui

/**
 * Everything the input view shows: the strip on top, and below it the keys,
 * all candidates, or the emoji board. The Android view and the previews both use it.
 */
class Screen {
    enum class Body { KEYS, CANDIDATES, EMOJI }

    val strip = CandidateStrip()
    val keys = KeyboardScene()
    val grid = CandidateGrid()
    val emoji = EmojiBoard()
    var body = Body.KEYS

    var dp = 1f
        set(v) {
            field = v; strip.dp = v; keys.dp = v; grid.dp = v; emoji.dp = v
        }

    /** What a finger is holding down, for the pressed look. */
    class Pressed {
        val keys = HashSet<Key>()
        var strip: CandidateStrip.Hit? = null
        var grid = -1
        var emoji: EmojiBoard.Hit? = null
        fun clear() {
            keys.clear(); strip = null; grid = -1; emoji = null
        }
    }

    val bodyBox = Box()

    fun height(): Float = strip.height + keys.height()

    fun layout(c: Canvas2D, width: Float) {
        strip.layout(c, 0f, 0f, width)
        keys.layout(0f, strip.height, width)
        bodyBox.set(0f, strip.height, width, strip.height + keys.height())
        when (body) {
            Body.CANDIDATES -> grid.layout(c, bodyBox)
            Body.EMOJI -> emoji.layout(c, bodyBox)
            Body.KEYS -> Unit
        }
    }

    fun draw(c: Canvas2D, t: Theme, pressed: Pressed) {
        strip.draw(c, t, pressed.strip)
        when (body) {
            Body.KEYS -> keys.draw(c, t, pressed.keys)
            Body.CANDIDATES -> grid.draw(c, t, pressed.grid)
            Body.EMOJI -> emoji.draw(c, t, pressed.emoji)
        }
    }
}
