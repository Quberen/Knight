package io.github.quberen.knight

import android.content.Context
import io.github.quberen.knight.pinyin.Fuzzy
import io.github.quberen.knight.pinyin.SpellingOptions
import io.github.quberen.knight.ui.LayoutMode

class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("knight", Context.MODE_PRIVATE)

    private fun int(key: String, def: Int) = sp.getInt(key, def)
    private fun bool(key: String, def: Boolean) = sp.getBoolean(key, def)
    private fun put(key: String, v: Int) = sp.edit().putInt(key, v).apply()
    private fun put(key: String, v: Boolean) = sp.edit().putBoolean(key, v).apply()

    var heightPercent: Int
        get() = int("height", 100)
        set(v) = put("height", v)

    var widthPercent: Int
        get() = int("width", 100)
        set(v) = put("width", v)

    /** Width of each half of the split layout, as % of the screen. */
    var splitPercent: Int
        get() = int("split", 34)
        set(v) = put("split", v)

    var layout: LayoutMode
        get() = LayoutMode.values().firstOrNull { it.name == sp.getString("layout", null) } ?: LayoutMode.STANDARD
        set(v) = sp.edit().putString("layout", v.name).apply()

    var digitRow: Boolean
        get() = bool("digit_row", true)
        set(v) = put("digit_row", v)

    var vibrate: Boolean
        get() = bool("vibrate", true)
        set(v) = put("vibrate", v)

    var chinese: Boolean
        get() = bool("chinese", true)
        set(v) = put("chinese", v)

    var fuzzy: Set<Fuzzy>
        get() = sp.getStringSet("fuzzy", emptySet())!!.mapNotNull { n -> Fuzzy.values().firstOrNull { it.name == n } }.toSet()
        set(v) = sp.edit().putStringSet("fuzzy", v.map { it.name }.toSet()).apply()

    var correctPinyin: Boolean
        get() = bool("correct_pinyin", true)
        set(v) = put("correct_pinyin", v)

    var emojiCandidates: Boolean
        get() = bool("emoji_candidates", true)
        set(v) = put("emoji_candidates", v)

    var englishSuggestions: Boolean
        get() = bool("english_suggestions", true)
        set(v) = put("english_suggestions", v)

    var autocorrectEnglish: Boolean
        get() = bool("autocorrect_english", true)
        set(v) = put("autocorrect_english", v)

    var autoCapitalize: Boolean
        get() = bool("auto_cap", true)
        set(v) = put("auto_cap", v)

    var doubleSpacePeriod: Boolean
        get() = bool("double_space", true)
        set(v) = put("double_space", v)

    var recentEmoji: List<String>
        get() = sp.getString("recent_emoji", "")!!.split('\u0001').filter { it.isNotEmpty() }
        set(v) = sp.edit().putString("recent_emoji", v.joinToString("\u0001")).apply()

    val spelling: SpellingOptions get() = SpellingOptions(fuzzy, correctPinyin)
}
