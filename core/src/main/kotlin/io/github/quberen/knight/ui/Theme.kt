package io.github.quberen.knight.ui

/**
 * Keyboard colours. One dark slate ink, greys around it, and the ink itself
 * as the only accent, like the icon set.
 */
class Theme(
    val dark: Boolean,
    val background: Int,
    val key: Int,
    /** A 1dp edge under keys in the light theme; 0 for none. */
    val keyShadow: Int,
    val functionKey: Int,
    val pressed: Int,
    val functionPressed: Int,
    val ink: Int,
    val inkSoft: Int,
    val accent: Int,
    val onAccent: Int,
    val divider: Int,
    /** Background of a selected tab or a tapped strip button. */
    val chip: Int,
) {
    companion object {
        private fun c(v: Long) = v.toInt()

        val LIGHT = Theme(
            dark = false,
            background = c(0xFFE8EAEE),
            key = c(0xFFFFFFFF),
            keyShadow = c(0xFFC3C8D0),
            functionKey = c(0xFFD3D8DF),
            pressed = c(0xFFD8DCE2),
            functionPressed = c(0xFFBCC2CB),
            ink = c(0xFF2B3240),
            inkSoft = c(0xFF7D8594),
            accent = c(0xFF2B3240),
            onAccent = c(0xFFFFFFFF),
            divider = c(0xFFD2D6DD),
            chip = c(0xFFFFFFFF),
        )

        val DARK = Theme(
            dark = true,
            background = c(0xFF17191D),
            key = c(0xFF2E3238),
            keyShadow = 0,
            functionKey = c(0xFF24272C),
            pressed = c(0xFF41464E),
            functionPressed = c(0xFF383C43),
            ink = c(0xFFE7E9ED),
            inkSoft = c(0xFF8E96A3),
            accent = c(0xFFE7E9ED),
            onAccent = c(0xFF17191D),
            divider = c(0xFF2C3036),
            chip = c(0xFF2E3238),
        )
    }
}
