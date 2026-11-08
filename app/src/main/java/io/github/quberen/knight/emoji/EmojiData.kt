package io.github.quberen.knight.emoji

import java.io.BufferedReader
import java.io.Reader

/** Emoji by category, their Chinese names, and which emoji go with which words. */
class EmojiData {
    /** Unicode group number to emoji, in Unicode order. */
    val groups = LinkedHashMap<Int, ArrayList<String>>()
    val names = HashMap<String, String>()
    private val byWord = HashMap<String, List<String>>()

    fun loadEmoji(reader: Reader, supported: (String) -> Boolean = { true }) {
        BufferedReader(reader).useLines { lines ->
            for (line in lines) {
                val p = line.split('\t')
                if (p.size < 2) continue
                val group = p[0].toIntOrNull() ?: continue
                if (!supported(p[1])) continue
                groups.getOrPut(group) { ArrayList() }.add(p[1])
                if (p.size > 2) names[p[1]] = p[2]
            }
        }
    }

    fun loadWords(reader: Reader) {
        BufferedReader(reader).useLines { lines ->
            for (line in lines) {
                val tab = line.indexOf('\t')
                if (tab <= 0) continue
                val list = line.substring(tab + 1).split(' ').filter { names.containsKey(it) }
                if (list.isNotEmpty()) byWord[line.substring(0, tab)] = list
            }
        }
    }

    /** Emoji that go with a Chinese word, e.g. 开心 -> 😄. */
    fun forWord(word: String): List<String> = byWord[word] ?: emptyList()

    companion object {
        const val SMILEYS = 0
        const val PEOPLE = 1
        const val NATURE = 3
        const val FOOD = 4
        const val TRAVEL = 5
        const val ACTIVITIES = 6
        const val OBJECTS = 7
        const val SYMBOLS = 8
        const val FLAGS = 9

        /** Text faces popular in Chinese chats. */
        val KAOMOJI = listOf(
            "(＾▽＾)", "(*^▽^*)", "o(*￣▽￣*)o", "(๑•̀ㅂ•́)و✧", "( •̀ ω •́ )✧", "(≖ᴗ≖)✧", "٩(๑❛ᴗ❛๑)۶",
            "ヽ(✿ﾟ▽ﾟ)ノ", "＼(^o^)／", "(ノ゜▽゜)ノ", "♪(^∇^*)", "(〃'▽'〃)", "(✪ω✪)", "(｡･ω･｡)",
            "(=・ω・=)", "(ฅ´ω`ฅ)", "(｀・ω・´)", "(ง •_•)ง", "(^_−)☆", "(｡♥‿♥｡)", "(　ﾟ∀ﾟ) ﾉ♡",
            "(づ｡◕‿‿◕｡)づ", "(っ´Ι`)っ", "(*/ω＼*)", "(〃▽〃)", "( ˘•ω•˘ )", "(๑¯∀¯๑)", "（￣︶￣）↗",
            "(￣▽￣)~*", "(￣﹃￣)", "(*￣3￣)╭", "ψ(｀∇´)ψ", "_(:з」∠)_", "(～﹃～)~zZ", "ヾ(•ω•`)o",
            "(⊙_⊙)?", "(O_O)", "Σ(っ °Д °;)っ", "(°ー°〃)", "(・_・;)", "(ーー;)", "(¬_¬)", "(눈_눈)",
            "(ಥ﹏ಥ)", "(T_T)", "QAQ", "(´；ω；`)", "(｡•́︿•̀｡)", "(；′⌒`)", "(；´д｀)ゞ",
            "(｀へ´)", "(╬▔皿▔)╯", "(╯°□°）╯︵ ┻━┻", "┬─┬ノ( º _ ºノ)", "ε=ε=ε=┏(゜ロ゜;)┛",
        )
    }
}
