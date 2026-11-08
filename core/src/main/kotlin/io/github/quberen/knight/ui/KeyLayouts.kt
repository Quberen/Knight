package io.github.quberen.knight.ui

enum class KeyType {
    CHAR, SEPARATOR, BACKSPACE, ENTER, SHIFT, SPACE,
    TO_SYMBOLS, TO_LETTERS, SYMBOL_SET, LANG, LEFT, RIGHT, HIDE, EMOJI,
}

/** One key. [hint] is what a long press types. */
class Key(
    val label: String,
    val type: KeyType = KeyType.CHAR,
    val text: String = label,
    val width: Float = 1f,
    val hint: String? = null,
    /** Drawn in the function-key colour, e.g. the wing keys of the trapezoid layout. */
    val quiet: Boolean = false,
) {
    val box = Box()
    val isFunction: Boolean get() = type != KeyType.CHAR && type != KeyType.SEPARATOR && type != KeyType.SPACE
}

class Row(val keys: List<Key>, val height: Float = 1f)

enum class Side { FULL, LEFT, RIGHT }

/** A group of rows placed together: the whole keyboard, or one half of a split one. */
class Block(val side: Side, val rows: List<Row>)

enum class LayoutMode(val label: String) {
    STANDARD("标准"),

    /** Two mirrored halves at the screen edges, for thumbs on a tablet held in both hands. */
    SPLIT("对称"),

    /** Letter rows of 10, 9 and 7 equal keys centred under each other, function keys in the wings. */
    TRAPEZOID("倒梯形"),
}

object KeyLayouts {
    private const val DIGIT_ROW = 0.8f
    private const val DIGIT_HINTS = "!@#$%^&*()"

    private fun letters(s: String, w: Float = 1f) = s.map { Key(it.toString(), width = w) }
    private fun syms(list: List<String>, w: Float = 1f) = list.map { Key(it, width = w) }
    private fun fn(type: KeyType, w: Float, label: String = "") = Key(label, type, width = w)

    private fun digits(range: IntRange, w: Float) = Row(
        range.map { i -> Key("1234567890"[i].toString(), width = w, hint = DIGIT_HINTS[i].toString()) },
        DIGIT_ROW,
    )

    private fun punct(zh: Boolean): List<Key> = if (zh) {
        listOf(Key("，", hint = "！"), Key("。", hint = "？"), Key("？", hint = "："))
    } else {
        listOf(Key(",", hint = "!"), Key(".", hint = "?"), Key("?", hint = ";"))
    }

    private fun separator(zh: Boolean, w: Float = 1f, quiet: Boolean = false) =
        if (zh) Key("'", KeyType.SEPARATOR, width = w, hint = "、", quiet = quiet) else Key("'", width = w, hint = "\"", quiet = quiet)

    fun letters(mode: LayoutMode, zh: Boolean, digitRow: Boolean): List<Block> = when (mode) {
        LayoutMode.STANDARD -> listOf(Block(Side.FULL, buildList {
            if (digitRow) add(digits(0..9, 1.15f))
            add(Row(letters("qwertyuiop") + fn(KeyType.BACKSPACE, 1.5f)))
            add(Row(letters("asdfghjkl") + separator(zh) + fn(KeyType.ENTER, 1.5f)))
            add(Row(listOf(fn(KeyType.SHIFT, 1.25f)) + letters("zxcvbnm") + punct(zh).take(2) + fn(KeyType.SHIFT, 1.25f)))
            add(bottom(zh, 1.25f, 5f))
        }))

        LayoutMode.TRAPEZOID -> {
            val p = punct(zh)
            listOf(Block(Side.FULL, buildList {
                if (digitRow) add(digits(0..9, 1.2f))
                add(Row(listOf(fn(KeyType.TO_SYMBOLS, 1f, "123")) + letters("qwertyuiop") + fn(KeyType.BACKSPACE, 1f)))
                add(Row(listOf(fn(KeyType.SHIFT, 1.5f)) + letters("asdfghjkl") + fn(KeyType.ENTER, 1.5f)))
                add(Row(listOf(fn(KeyType.EMOJI, 1.25f), wing(p[0])) + letters("zxcvbnm") + wing(p[1]) + separator(zh, 1.25f, quiet = true)))
                add(Row(listOf(fn(KeyType.LANG, 1.5f), fn(KeyType.LEFT, 1f), Key("", KeyType.SPACE, " ", 7f), fn(KeyType.RIGHT, 1f), fn(KeyType.HIDE, 1.5f))))
            }))
        }

        LayoutMode.SPLIT -> {
            val p = punct(zh)
            listOf(
                Block(Side.LEFT, buildList {
                    if (digitRow) add(digits(0..4, 1.2f))
                    add(Row(listOf(fn(KeyType.TO_SYMBOLS, 1f, "123")) + letters("qwert")))
                    add(Row(listOf(fn(KeyType.SHIFT, 1f)) + letters("asdfg")))
                    add(Row(listOf(fn(KeyType.EMOJI, 1f)) + letters("zxcvb")))
                    add(Row(listOf(fn(KeyType.LANG, 1.25f), Key("", KeyType.SPACE, " ", 3.75f), fn(KeyType.LEFT, 1f))))
                }),
                Block(Side.RIGHT, buildList {
                    if (digitRow) add(digits(5..9, 1.2f))
                    add(Row(letters("yuiop") + fn(KeyType.BACKSPACE, 1f)))
                    add(Row(letters("hjkl") + separator(zh) + fn(KeyType.ENTER, 1f)))
                    add(Row(letters("nm") + p + fn(KeyType.SHIFT, 1f)))
                    add(Row(listOf(fn(KeyType.RIGHT, 1f), Key("", KeyType.SPACE, " ", 3.75f), fn(KeyType.HIDE, 1.25f))))
                }),
            )
        }
    }

    private fun wing(k: Key) = Key(k.label, k.type, k.text, 1.25f, k.hint, quiet = true)

    private fun bottom(zh: Boolean, side: Float, space: Float) = Row(
        listOf(
            fn(KeyType.TO_SYMBOLS, side, "123"),
            fn(KeyType.LANG, 1.25f),
            fn(KeyType.EMOJI, 1f),
            Key("", KeyType.SPACE, " ", space),
            fn(KeyType.LEFT, 1f),
            fn(KeyType.RIGHT, 1f),
            fn(KeyType.HIDE, 1f),
        )
    )

    private val ZH_SYMBOLS = listOf(
        listOf("！", "@", "#", "￥", "%", "……", "&", "×", "（", "）"),
        listOf("—", "、", "＝", "＋", "【", "】", "「", "」", "·", "～"),
        listOf("；", "：", "“", "”", "‘", "’", "《", "》", "，", "。"),
    )
    private val EN_SYMBOLS = listOf(
        "!@#$%^&*()".map { it.toString() },
        "-_=+[]{}\\|".map { it.toString() },
        ";:'\",./<>~".map { it.toString() },
    )

    fun symbols(mode: LayoutMode, zhSymbols: Boolean, zh: Boolean): List<Block> {
        val s = if (zhSymbols) ZH_SYMBOLS else EN_SYMBOLS
        val setLabel = if (zhSymbols) "英文" else "中文"
        val back = if (zh) "拼音" else "ABC"
        if (mode != LayoutMode.SPLIT) {
            return listOf(Block(Side.FULL, listOf(
                digits(0..9, 1.15f),
                Row(syms(s[0]) + fn(KeyType.BACKSPACE, 1.5f)),
                Row(syms(s[1]) + fn(KeyType.ENTER, 1.5f)),
                Row(listOf(fn(KeyType.SYMBOL_SET, 1.5f, setLabel)) + syms(s[2])),
                Row(listOf(fn(KeyType.TO_LETTERS, 1.25f, back), fn(KeyType.LANG, 1.25f), fn(KeyType.EMOJI, 1f),
                    Key("", KeyType.SPACE, " ", 5f), fn(KeyType.LEFT, 1f), fn(KeyType.RIGHT, 1f), fn(KeyType.HIDE, 1f))),
            )))
        }
        return listOf(
            Block(Side.LEFT, listOf(
                digits(0..4, 1.2f),
                Row(syms(s[0].take(5), 1.2f)),
                Row(syms(s[1].take(5), 1.2f)),
                Row(listOf(fn(KeyType.SYMBOL_SET, 1f, setLabel)) + syms(s[2].take(5))),
                Row(listOf(fn(KeyType.TO_LETTERS, 1.25f, back), Key("", KeyType.SPACE, " ", 3.75f), fn(KeyType.LEFT, 1f))),
            )),
            Block(Side.RIGHT, listOf(
                digits(5..9, 1.2f),
                Row(syms(s[0].drop(5)) + fn(KeyType.BACKSPACE, 1f)),
                Row(syms(s[1].drop(5)) + fn(KeyType.ENTER, 1f)),
                Row(syms(s[2].drop(5), 1.2f)),
                Row(listOf(fn(KeyType.RIGHT, 1f), Key("", KeyType.SPACE, " ", 3.75f), fn(KeyType.HIDE, 1.25f))),
            )),
        )
    }
}
