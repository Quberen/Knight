package io.github.quberen.knight.pinyin

/** A piece of the typed letters, from [start] up to [end] in the input. */
class Token(val text: String, val start: Int, val end: Int, val exact: Boolean)

class Candidate(
    val text: String,
    /** How many characters of the typed input this candidate uses up. */
    val inputLength: Int,
    /** Dictionary words that make up this candidate, empty for raw letters or emoji. */
    val entries: List<Entry>,
    val score: Double,
    /** True when a typo had to be fixed to get here. */
    val corrected: Boolean = false,
)

/** Candidates for the typed input, plus the pinyin to show ("zhong'guo", typos fixed). */
class Analysis(val candidates: List<Candidate>, val display: String) {
    companion object {
        val EMPTY = Analysis(emptyList(), "")
    }
}

/**
 * Turns typed letters into Chinese. It tries a few ways of splitting the input
 * into syllables (exact, abbreviated, fuzzy or corrected), scores each with word
 * frequencies, and lists what fits best first.
 */
class PinyinEngine(val dict: PinyinDict, options: SpellingOptions = SpellingOptions()) {

    var options: SpellingOptions = options
        set(v) {
            if (v != field) {
                field = v
                spellings = Spellings(dict, v)
            }
        }

    private var spellings = Spellings(dict, options)

    /** Extra candidates to show right after a word, such as emoji for 开心. */
    var extras: ((String) -> List<String>)? = null

    /** Whether typed letters are a common English word, so they can be offered early. */
    var englishWord: ((String) -> Boolean)? = null

    private class Seg(val cost: Double, val tokens: List<Token>)

    /** [typo] is set when a syllable had to be corrected, not just completed. */
    private class Match(val entry: Entry, val score: Double, val typo: Boolean)

    private class Path(val score: Double, val words: List<Match>, val starts: IntArray) {
        val text: String get() = words.joinToString("") { it.entry.word }
        val typo: Boolean get() = words.any { it.typo }
    }

    /** Cheapest few ways to split [input] into syllables. */
    private fun segs(input: String): List<Seg> {
        val len = input.length
        val memo = arrayOfNulls<List<Seg>>(len + 1)
        memo[len] = listOf(Seg(0.0, emptyList()))
        for (i in len - 1 downTo 0) {
            if (input[i] == '\'') {
                memo[i] = memo[i + 1]
                continue
            }
            var limit = i
            while (limit < len && input[limit] != '\'' && limit - i < MAX_SYLLABLE) limit++
            val options = ArrayList<Seg>()
            for (end in i + 1..limit) {
                val s = input.substring(i, end)
                val exact = dict.isSyllable(s)
                val cost = when {
                    exact -> 1.0
                    spellings.isPrefix(s) -> 1.6
                    spellings.hasWhole(s) -> 1.0 + 0.4 * (spellings.whole(s).values.minOrNull() ?: 3.0)
                    end == i + 1 -> 10.0
                    else -> continue
                }
                val token = Token(s, i, end, exact)
                for (rest in memo[end]!!) {
                    val tokens = ArrayList<Token>(rest.tokens.size + 1)
                    tokens.add(token)
                    tokens.addAll(rest.tokens)
                    options.add(Seg(cost + rest.cost, tokens))
                }
            }
            options.sortBy { it.cost }
            memo[i] = options.take(MAX_SEGS)
        }
        return memo[0]!!
    }

    /** Per-input caches, so the splits share work. */
    private inner class Ctx(val input: String) {
        val n = dict.syllableCount
        private val altCache = HashMap<String, DoubleArray>()
        private val letterCache = HashMap<String, CharArray>()
        private val bestCache = HashMap<String, Match?>()
        private val allCache = HashMap<String, List<Match>>()

        private fun last(t: Token) = t.end == input.length
        private fun key(t: Token) = if (last(t)) t.text + "$" else t.text

        /** For each syllable id: the penalty for reading token [t] as it, or NaN. */
        fun alts(t: Token): DoubleArray = altCache.getOrPut(key(t)) {
            val a = DoubleArray(n) { Double.NaN }
            fun put(id: Int, p: Double) {
                if (id < n && (a[id].isNaN() || p < a[id])) a[id] = p
            }
            for ((id, p) in spellings.whole(t.text)) put(id, p)
            if (!t.exact) {
                for ((id, p) in spellings.prefix(t.text)) put(id, p)
            } else if (last(t)) {
                // The last syllable may still be growing: "zhan" can become "zhang".
                for ((id, p) in spellings.prefix(t.text)) put(id, p + EXTEND_PENALTY)
            }
            a
        }

        /** First letters of the syllables token [t] can stand for, cheapest first. */
        fun letters(t: Token, cheapOnly: Boolean): CharArray =
            letterCache.getOrPut(key(t) + if (cheapOnly) "!" else "") {
                val a = alts(t)
                val pairs = ArrayList<Pair<Char, Double>>()
                for (id in 0 until n) if (!a[id].isNaN() && (!cheapOnly || a[id] <= 1.2)) {
                    pairs.add(dict.syllable(id)[0] to a[id])
                }
                pairs.sortBy { it.second }
                pairs.map { it.first }.distinct().toCharArray()
            }

        private fun spanKey(tokens: List<Token>, from: Int, to: Int): String {
            val sb = StringBuilder()
            for (k in from until to) sb.append(tokens[k].start).append('-').append(tokens[k].end).append(',')
            return sb.toString()
        }

        private fun scan(tokens: List<Token>, from: Int, to: Int, keepAll: Boolean): List<Match> {
            val len = to - from
            val alts = Array(len) { alts(tokens[from + it]) }
            val texts = Array(len) { tokens[from + it].text }
            var letters = Array(len) { letters(tokens[from + it], false) }
            var combos = letters.fold(1L) { acc, l -> acc * l.size }
            if (combos > MAX_COMBOS) {
                letters = Array(len) { letters(tokens[from + it], true) }
                combos = letters.fold(1L) { acc, l -> acc * l.size }
            }
            if (combos == 0L || combos > MAX_COMBOS) return emptyList()

            val result = ArrayList<Match>()
            var best: Match? = null
            val idx = IntArray(len)
            val initials = CharArray(len)
            while (true) {
                for (k in 0 until len) initials[k] = letters[k][idx[k]]
                for (e in dict.entriesForInitials(String(initials))) {
                    var penalty = 0.0
                    var typo = false
                    var ok = true
                    for (k in 0 until len) {
                        val id = e.syllables[k]
                        val p = if (id < n) alts[k][id] else Double.NaN
                        if (p.isNaN()) {
                            ok = false
                            break
                        }
                        penalty += p
                        if (p >= CORRECTED && !dict.syllable(id).startsWith(texts[k])) typo = true
                    }
                    if (!ok) continue
                    val m = Match(e, dict.score(e) - penalty, typo)
                    if (keepAll) result.add(m) else if (best == null || m.score > best.score) best = m
                }
                // Next combination of first letters.
                var k = len - 1
                while (k >= 0) {
                    idx[k]++
                    if (idx[k] < letters[k].size) break
                    idx[k] = 0
                    k--
                }
                if (k < 0) break
            }
            if (!keepAll) return listOfNotNull(best)
            result.sortByDescending { it.score }
            return result
        }

        fun bestMatch(tokens: List<Token>, from: Int, to: Int): Match? =
            bestCache.getOrPut(spanKey(tokens, from, to)) { scan(tokens, from, to, false).firstOrNull() }

        fun allMatches(tokens: List<Token>, from: Int, to: Int): List<Match> =
            allCache.getOrPut(spanKey(tokens, from, to)) { scan(tokens, from, to, true) }

        /** Best way to cover all tokens with words. */
        fun sentence(tokens: List<Token>): Path? {
            val m = tokens.size
            val best = DoubleArray(m + 1) { Double.NEGATIVE_INFINITY }
            val back = IntArray(m + 1)
            val word = arrayOfNulls<Match>(m + 1)
            best[0] = 0.0
            for (j in 1..m) {
                for (i in maxOf(0, j - MAX_WORD) until j) {
                    if (best[i] == Double.NEGATIVE_INFINITY) continue
                    val w = bestMatch(tokens, i, j) ?: continue
                    val s = best[i] + w.score - WORD_PENALTY
                    if (s > best[j]) {
                        best[j] = s; back[j] = i; word[j] = w
                    }
                }
            }
            if (best[m] == Double.NEGATIVE_INFINITY) return null
            val words = ArrayList<Match>()
            val starts = ArrayList<Int>()
            var j = m
            while (j > 0) {
                words.add(word[j]!!); starts.add(back[j]); j = back[j]
            }
            words.reverse(); starts.reverse()
            return Path(best[m], words, starts.toIntArray())
        }
    }

    fun candidates(input: String, limit: Int = 300): List<Candidate> = analyze(input, limit).candidates

    fun analyze(input: String, limit: Int = 300): Analysis {
        if (input.isEmpty() || input.all { it == '\'' }) return Analysis.EMPTY
        val ctx = Ctx(input)
        val segs = segs(input)
        val cheapest = segs.first().cost
        val considered = segs.filter { it.cost <= cheapest + SEG_RANGE }.take(MAX_EVAL)

        // The split whose best sentence scores highest leads.
        var primary = considered.first()
        var path = ctx.sentence(primary.tokens)
        for (seg in considered.drop(1)) {
            val p = ctx.sentence(seg.tokens) ?: continue
            if (path == null || p.score > path.score) {
                primary = seg; path = p
            }
        }
        val tokens = primary.tokens
        val n = tokens.size

        // Words that use up the whole input, from every split considered.
        val whole = ArrayList<Candidate>()
        for (seg in considered) {
            val cap = if (seg.tokens.size == 1) 200 else 60
            for (m in ctx.allMatches(seg.tokens, 0, seg.tokens.size).take(cap)) {
                whole.add(Candidate(m.entry.word, input.length, listOf(m.entry), m.score - WORD_PENALTY, m.typo))
            }
        }
        whole.sortByDescending { it.score }

        val ordered = ArrayList<Candidate>()
        val p = path
        if (p != null && p.words.size >= 2) {
            val sentence = Candidate(p.text, input.length, p.words.map { it.entry }, p.score, p.typo)
            ordered.add(sentence)
            if (whole.isNotEmpty() && whole[0].score > sentence.score) ordered.add(0, whole[0])
        }
        ordered.addAll(whole)

        // Words for just the start; the rest stays typed.
        for (k in minOf(n - 1, MAX_WORD) downTo 1) {
            val used = tokens[k - 1].end
            for (m in ctx.allMatches(tokens, 0, k).take(if (k == 1) 200 else 30)) {
                ordered.add(Candidate(m.entry.word, used, listOf(m.entry), m.score, m.typo))
            }
            if (ordered.size >= limit) break
        }

        val out = LinkedHashMap<String, Candidate>()
        for (c in ordered) out.putIfAbsent(c.text, c)
        val list = ArrayList(out.values)

        // The letters as typed: early when they are an English word, else last.
        val raw = input.replace("'", "")
        if (!out.containsKey(raw)) {
            val rawCandidate = Candidate(raw, input.length, emptyList(), Double.NEGATIVE_INFINITY)
            if (raw.length >= 2 && englishWord?.invoke(raw) == true) list.add(minOf(2, list.size), rawCandidate)
            else list.add(rawCandidate)
        }

        return Analysis(withExtras(list).take(limit), display(ctx, tokens, path))
    }

    /** Puts emoji and the like right after the words they belong to. */
    private fun withExtras(list: List<Candidate>): List<Candidate> {
        val ex = extras ?: return list
        val out = ArrayList<Candidate>(list.size + 6)
        val seen = HashSet<String>()
        list.forEach { seen.add(it.text) }
        var added = 0
        for ((i, c) in list.withIndex()) {
            out.add(c)
            if (added >= 3 || i > 40 || c.entries.isEmpty()) continue
            for (e in ex(c.text).take(2)) {
                if (seen.add(e)) {
                    out.add(Candidate(e, c.inputLength, emptyList(), c.score))
                    added++
                }
            }
        }
        return out
    }

    /** The typed syllables joined by ', with fuzzy or corrected ones spelled out. */
    private fun display(ctx: Ctx, tokens: List<Token>, path: Path?): String {
        val shown = Array(tokens.size) { tokens[it].text }
        if (path != null) {
            for ((w, start) in path.words.zip(path.starts.toList())) {
                for (k in w.entry.syllables.indices) {
                    val t = tokens.getOrNull(start + k) ?: break
                    val syl = dict.syllable(w.entry.syllables[k])
                    if (!syl.startsWith(t.text)) shown[start + k] = syl
                }
            }
        }
        return shown.joinToString("'")
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

    companion object {
        const val MAX_SYLLABLE = 7
        const val MAX_WORD = 8
        const val MAX_SEGS = 6
        const val MAX_EVAL = 4
        const val SEG_RANGE = 1.3
        const val MAX_COMBOS = 48L
        const val EXTEND_PENALTY = 2.5
        const val WORD_PENALTY = 1.0
        const val CORRECTED = 1.15
    }
}
