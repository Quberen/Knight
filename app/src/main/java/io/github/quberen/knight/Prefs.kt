package io.github.quberen.knight

import android.content.Context

class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("knight", Context.MODE_PRIVATE)

    var heightPercent: Int
        get() = sp.getInt("height", 100)
        set(v) = sp.edit().putInt("height", v).apply()

    var widthPercent: Int
        get() = sp.getInt("width", 100)
        set(v) = sp.edit().putInt("width", v).apply()

    var digitRow: Boolean
        get() = sp.getBoolean("digit_row", true)
        set(v) = sp.edit().putBoolean("digit_row", v).apply()

    var vibrate: Boolean
        get() = sp.getBoolean("vibrate", true)
        set(v) = sp.edit().putBoolean("vibrate", v).apply()

    var chinese: Boolean
        get() = sp.getBoolean("chinese", true)
        set(v) = sp.edit().putBoolean("chinese", v).apply()
}
