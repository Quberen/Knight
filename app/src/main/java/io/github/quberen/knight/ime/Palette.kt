package io.github.quberen.knight.ime

import android.content.Context
import android.content.res.Configuration
import android.graphics.Color

/** Keyboard colors; follows the system light/dark setting. */
class Palette(
    val background: Int,
    val key: Int,
    val functionKey: Int,
    val pressed: Int,
    val text: Int,
    val hint: Int,
    val accent: Int,
    val onAccent: Int,
) {
    companion object {
        fun of(context: Context): Palette {
            val night = (context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
                Configuration.UI_MODE_NIGHT_YES
            return if (night) {
                Palette(
                    background = Color.parseColor("#1B1C1F"),
                    key = Color.parseColor("#3A3C41"),
                    functionKey = Color.parseColor("#2A2C30"),
                    pressed = Color.parseColor("#55585E"),
                    text = Color.parseColor("#ECEDEF"),
                    hint = Color.parseColor("#9A9DA3"),
                    accent = Color.parseColor("#5B9BF8"),
                    onAccent = Color.WHITE,
                )
            } else {
                Palette(
                    background = Color.parseColor("#E6E9ED"),
                    key = Color.WHITE,
                    functionKey = Color.parseColor("#CDD2D9"),
                    pressed = Color.parseColor("#B7BEC7"),
                    text = Color.parseColor("#1F2328"),
                    hint = Color.parseColor("#7A818A"),
                    accent = Color.parseColor("#2F6FEB"),
                    onAccent = Color.WHITE,
                )
            }
        }
    }
}
