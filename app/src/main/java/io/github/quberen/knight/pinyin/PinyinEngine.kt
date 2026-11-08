package io.github.quberen.knight.pinyin

/** A piece of the typed letters: a whole syllable ("zhong") or the start of one ("zh", "g"). */
data class Token(val text: String, val full: Boolean, val end: Int)

class Candidate(
    val text: String,
    /** How many characters of the typed input this candidate uses up. */
    val inputLength: Int,
    /** Dictionary words that make up this candidate, empty for raw letters. */
    val entries: List<Entry>,
    val score: Double,
)

class PinyinEngine(val dict: PinyinDict) {

    private class Seg(val cost: Double, val tokens: List<Token>)

    /** Splits input into syllables. Returns the cheapest few ways, best first. */
    fun segment(input: String): List<List<Token>> = segs(input).map { it.tokens }

    private fun segs(input: String): List<Seg> {
        val memo = arrayOfNulls<List<Seg>>(input.length + 1)
        memo[input.length] = listOf(Seg(0.0, emptyList()))
        for (i in input.length - 1 downTo 0) {
            if (input[i] == '\'') {
                memo[i] = memo[i + 1]
                continue
            }
            val options = ArrayList<Seg>()
            var limit = i
            while (limit < input.length && input[limit] != '\'' && limit - i < MAX_SYLLABLE) limit++
            for (end in i + 1..limit) {
                val s = input.substring(i, end)
                val (cost, full) = when {
                    dict.isSyllable(s) -> 1.0 to true
                    dict.isSyllablePrefix(s) -> 1.6 to false
                    end == i + 1 -> 10.0 to false
                    else -> continue
                }
                for (rest in memo[end]!!) {
                    options.add(Seg(cost + rest.cost, listOf(Token(s, full, end)) + rest.tokens))
                }
            }
            options.sortBy { it.cost }
            memo[i] = options.take(MAX_SEGS)
        }
        return memo[0]!!
    }

    private class Match(val entry: Entry, val score: Double)

    /** Words whose syllables match tokens[from, to). */
    private fun match(
        tokens: List<Token>, from: Int, to: Int, cache: HashMap<Int, List<Match>>,
    ): List<Match> = cache.getOrPut(from * 64 + to) {
        val initials = String(CharArray(to - from) { tokens[from + it].text[0] })
        val result = ArrayList<Match>()
        val last = tokens.size - 1
        for (e in dict.entriesForInitials(initials)) {
            var penalty = 0.0
            var ok = true
            for (k in 0 until to - from) {
                val tok = tokens[from + k]
                val syl = dict.syllable(e.syllables[k])
                if (tok.full) {
                    if (syl == tok.text) continue
                    // The last syllable may still be growing: "zhan" can become "zhang".
                    if (from + k == last && syl.startsWith(tok.text)) {
                        penalty += EXTEND_PENALTY
                        continue
                    }
                    ok = false
                    break
                } else if (!syl.startsWith(tok.text)) {
                    ok = false
                    break
                }
            }
            if (ok) result.add(Match(e, dict.score(e) - penalty))
        }
        result.sortByDescending { it.score }
        result
    }

    /** Best whole-sentence guess built from several words, or null. */
    private fun sentence(tokens: List<Token>, cache: HashMap<Int, List<Match>>): Candidate? {
        val n = tokens.size
        if (n < 2) return null
        val best = DoubleArray(n + 1) { Double.NEGATIVE_INFINITY }
        val back = IntArray(n + 1)
        val word = arrayOfNulls<Match>(n + 1)
        best[0] = 0.0
        for (j in 1..n) {
            for (i in maxOf(0, j - MAX_WORD) until j) {
                if (best[i] == Double.NEGATIVE_INFINITY) continue
                val m = match(tokens, i, j, cache).firstOrNull() ?: continue
                val s = best[i] + m.score - WORD_PENALTY
                if (s > best[j]) {
                    best[j] = s; back[j] = i; word[j] = m
                }
            }
        }
        if (best[n] == Double.NEGATIVE_INFINITY) return null
        val parts = ArrayList<Entry>()
        var j = n
        while (j > 0) {
            parts.add(word[j]!!.entry); j = back[j]
        }
        if (parts.size < 2) return null
        parts.reverse()
        return Candidate(parts.joinToString("") { it.word }, tokens.last().end, parts, best[n])
    }

    fun candidates(input: String, limit: Int = 300): List<Candidate> {
        if (input.isEmpty()) return emptyList()
        val segs = segs(input)
        val out = LinkedHashMap<String, Candidate>()
        val primary = segs.first()
        val tokens = primary.tokens
        val n = tokens.size
        val cache = HashMap<Int, List<Match>>()

        sentence(tokens, cache)?.let { out[it.text] = it }

        // Words covering the whole input, also from other close splits ("xian" -> 西安).
        val whole = ArrayList<Candidate>()
        for (m in match(tokens, 0, n, cache)) whole.add(Candidate(m.entry.word, input.length, listOf(m.entry), m.score))
        val top = whole.firstOrNull()?.score ?: Double.NEGATIVE_INFINITY
        for (alt in segs.drop(1)) {
            if (alt.cost > primary.cost + 1.01) continue
            val altCache = HashMap<Int, List<Match>>()
            for (m in match(alt.tokens, 0, alt.tokens.size, altCache)) {
                if (m.score < top - ALT_RANGE) break
                whole.add(Candidate(m.entry.word, input.length, listOf(m.entry), m.score))
            }
        }
        whole.sortByDescending { it.score }
        for (c in whole.take(if (n == 1) 200 else 60)) out.putIfAbsent(c.text, c)

        // Words covering only the start; the rest stays typed.
        for (k in minOf(n - 1, MAX_WORD) downTo 1) {
            val used = tokens[k - 1].end
            for (m in match(tokens, 0, k, cache).take(if (k == 1) 200 else 30)) {
                out.putIfAbsent(m.entry.word, Candidate(m.entry.word, used, listOf(m.entry), m.score))
            }
            if (out.size >= limit) break
        }

        val raw = input.replace("'", "")
        out.putIfAbsent(raw, Candidate(raw, input.length, emptyList(), Double.NEGATIVE_INFINITY))
        return out.values.take(limit)
    }

    /** Records that the user picked [parts]; several parts also become one new phrase. */
    fun learn(parts: List<Entry>) {
        for (e in parts) e.userCount++
        if (parts.size < 2) return
        val syllables = parts.flatMap { e -> e.syllables.map { dict.syllable(it) } }
        if (syllables.size > MAX_WORD) return
        val word = parts.joinToString("") { it.word }
        val e = dict.add(word, syllables, PinyinDict.USER_WORD_WEIGHT)
        e.userCount++
    }

    /** The input with syllable breaks shown, e.g. "zhong'guo". */
    fun display(input: String): String {
        if (input.isEmpty()) return ""
        return segs(input).first().tokens.joinToString("'") { it.text }
    }

    companion object {
        const val MAX_SYLLABLE = 6
        const val MAX_WORD = 8
        const val MAX_SEGS = 4
        const val EXTEND_PENALTY = 2.5
        const val WORD_PENALTY = 1.0
        const val ALT_RANGE = 4.0
    }
}
