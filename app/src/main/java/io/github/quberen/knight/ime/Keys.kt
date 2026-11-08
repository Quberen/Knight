package io.github.quberen.knight.ime

import android.graphics.RectF

enum class KeyType {
    CHAR, SEPARATOR, BACKSPACE, ENTER, SHIFT, SPACE,
    TO_SYMBOLS, TO_LETTERS, SYMBOL_SET, LANG, LEFT, RIGHT, HIDE,
}

/** One key. [hint] is what a long press types. */
class Key(
    val label: String,
    val type: KeyType = KeyType.CHAR,
    val text: String = label,
    val width: Float = 1f,
    val hint: String? = null,
) {
    val rect = RectF()
    val isFunction: Boolean get() = type != KeyType.CHAR && type != KeyType.SEPARATOR && type != KeyType.SPACE
}

class Row(val keys: List<Key>, val height: Float = 1f)

/** Every row adds up to 11.5 key widths. */
object Layouts {
    private fun chars(s: String, width: Float = 1f) = s.map { Key(it.toString(), width = width) }
    private fun list(vararg labels: String) = labels.map { Key(it) }

    private fun digits() = Row(chars("1234567890", 1.15f), 0.8f)

    private fun bottom(first: Key, zh: Boolean) = Row(
        listOf(
            first,
            Key(if (zh) "中" else "EN", KeyType.LANG, width = 1.25f),
            Key("", KeyType.SPACE, text = " ", width = 5.75f),
            Key("◀", KeyType.LEFT),
            Key("▶", KeyType.RIGHT),
            Key("⌄", KeyType.HIDE, width = 1.25f),
        )
    )

    fun letters(zh: Boolean, digitRow: Boolean): List<Row> {
        val rows = ArrayList<Row>()
        if (digitRow) rows.add(digits())
        rows.add(Row(chars("qwertyuiop") + Key("⌫", KeyType.BACKSPACE, width = 1.5f)))
        val sep = if (zh) Key("'", KeyType.SEPARATOR, hint = "、") else Key("'", hint = "\"")
        rows.add(Row(chars("asdfghjkl") + sep + Key("↵", KeyType.ENTER, width = 1.5f)))
        val punct = if (zh) {
            listOf(Key("，", hint = "！"), Key("。", hint = "…"), Key("？", hint = "："))
        } else {
            listOf(Key(",", hint = "!"), Key(".", hint = ":"), Key("?", hint = ";"))
        }
        rows.add(
            Row(
                listOf(Key("⇧", KeyType.SHIFT, width = 1.25f)) + chars("zxcvbnm") + punct +
                    Key("⇧", KeyType.SHIFT, width = 1.25f)
            )
        )
        rows.add(bottom(Key("123", KeyType.TO_SYMBOLS, width = 1.25f), zh))
        return rows
    }

    fun symbols(zhSymbols: Boolean, zh: Boolean): List<Row> {
        val rows = ArrayList<Row>()
        rows.add(digits())
        if (zhSymbols) {
            rows.add(Row(list("！", "@", "#", "￥", "%", "……", "&", "×", "（", "）") + Key("⌫", KeyType.BACKSPACE, width = 1.5f)))
            rows.add(Row(list("—", "、", "＝", "＋", "【", "】", "「", "」", "·", "～") + Key("↵", KeyType.ENTER, width = 1.5f)))
            rows.add(Row(listOf(Key("英文", KeyType.SYMBOL_SET, width = 1.5f)) + list("；", "：", "“", "”", "‘", "’", "《", "》", "，", "。")))
        } else {
            rows.add(Row(chars("!@#$%^&*()") + Key("⌫", KeyType.BACKSPACE, width = 1.5f)))
            rows.add(Row(chars("-_=+[]{}\\|") + Key("↵", KeyType.ENTER, width = 1.5f)))
            rows.add(Row(listOf(Key("中文", KeyType.SYMBOL_SET, width = 1.5f)) + chars(";:'\",./<>~")))
        }
        rows.add(bottom(Key("ABC", KeyType.TO_LETTERS, width = 1.25f), zh))
        return rows
    }
}
