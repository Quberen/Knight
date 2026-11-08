package io.github.quberen.knight.english

import io.github.quberen.knight.pinyin.Spellings
import java.io.BufferedReader
import java.io.Reader

/**
 * Common English words with frequencies, for suggestions and typo fixes.
 * Frequencies are Zipf values: log10 of uses per billion words (the ≈ 7.7).
 */
class EnglishDict {
    private val index = HashMap<String, Int>()
    private val shown = ArrayList<String>()
    private val zipf = ArrayList<Float>()
    private var sorted: Array<String> = emptyArray()
    private var sortedIds: IntArray = IntArray(0)

    val size: Int get() = shown.size

    fun load(reader: Reader) {
        BufferedReader(reader).useLines { lines ->
            for (line in lines) {
                val tab = line.indexOf('\t')
                if (tab <= 0) continue
                val word = line.substring(0, tab)
                val z = line.substring(tab + 1).toFloatOrNull() ?: continue
                val key = word.lowercase()
                if (index.containsKey(key)) continue
                index[key] = shown.size
                shown.add(word)
                zipf.add(z)
            }
        }
        val ids = (0 until shown.size).sortedBy { shown[it].lowercase() }
        sortedIds = ids.toIntArray()
        sorted = Array(ids.size) { shown[ids[it]].lowercase() }
    }

    fun isWord(w: String): Boolean = index.containsKey(w.lowercase())

    /** Zipf frequency, or 0 for unknown words. */
    fun frequency(w: String): Float = index[w.lowercase()]?.let { zipf[it] } ?: 0f

    class Suggestion(val text: String, val score: Double, val kind: Kind)

    enum class Kind { TYPED, COMPLETION, CORRECTION }

    /** Words starting with [prefix], most common first. */
    fun completions(prefix: String, limit: Int): List<Suggestion> {
        val p = prefix.lowercase()
        if (p.isEmpty()) return emptyList()
        var lo = sorted.binarySearch(p).let { if (it < 0) -it - 1 else it }
        val found = ArrayList<Int>()
        while (lo < sorted.size && sorted[lo].startsWith(p)) {
            if (sorted[lo] != p) found.add(sortedIds[lo])
            lo++
        }
        return found.sortedByDescending { zipf[it] }.take(limit).map {
            Suggestion(matchCase(prefix, shown[it]), zipf[it] - 0.6 - 0.15 * (shown[it].length - p.length), Kind.COMPLETION)
        }
    }

    /** Known words one or two small slips away from [typed], best first. */
    fun corrections(typed: String, limit: Int): List<Suggestion> {
        val w = typed.lowercase()
        val best = HashMap<String, Double>()
        fun consider(cand: String, cost: Double) {
            val id = index[cand] ?: return
            val s = zipf[id] - cost
            if ((best[cand] ?: Double.NEGATIVE_INFINITY) < s) best[cand] = s
        }
        val first = edits(w)
        for ((c, cost) in first) consider(c, cost)
        if (best.isEmpty() && w.length >= 5) {
            for ((c, cost) in first) for ((c2, cost2) in edits(c)) consider(c2, cost + cost2)
        }
        best.remove(w)
        return best.entries.sortedByDescending { it.value }.take(limit).map {
            Suggestion(matchCase(typed, shown[index[it.key]!!]), it.value, Kind.CORRECTION)
        }
    }

    /** Every string one edit from [w], with how unlikely that slip is. */
    private fun edits(w: String): List<Pair<String, Double>> {
        val out = ArrayList<Pair<String, Double>>()
        val n = w.length
        for (i in 0 until n) {
            // Extra letter typed.
            val dup = i > 0 && w[i] == w[i - 1]
            out.add(w.removeRange(i, i + 1) to if (dup) 1.0 else 2.0)
            // Wrong key.
            for (c in 'a'..'z') {
                if (c == w[i]) continue
                val near = Spellings.NEIGHBORS[w[i]]?.contains(c) == true
                out.add(w.substring(0, i) + c + w.substring(i + 1) to if (near) 1.0 else 2.5)
            }
            // Two letters swapped.
            if (i < n - 1 && w[i] != w[i + 1]) {
                out.add(w.substring(0, i) + w[i + 1] + w[i] + w.substring(i + 2) to 1.2)
            }
        }
        // Letter left out (a doubled letter typed once is the most common).
        for (i in 0..n) for (c in 'a'..'z') {
            val doubled = (i > 0 && w[i - 1] == c) || (i < n && w[i] == c)
            out.add(w.substring(0, i) + c + w.substring(i) to if (doubled) 1.0 else 1.7)
        }
        if ('\'' !in w) for (i in 1 until n) out.add(w.substring(0, i) + "'" + w.substring(i) to 0.8)
        return out
    }

    /** Words to offer for [typed]: what space will commit first, then the rest. */
    fun suggest(typed: String, limit: Int = 12): List<Suggestion> {
        if (typed.isEmpty()) return emptyList()
        val out = LinkedHashMap<String, Suggestion>()
        autocorrect(typed)?.let { out[it] = Suggestion(it, 99.0, Kind.CORRECTION) }
        out.putIfAbsent(typed, Suggestion(typed, frequency(typed).toDouble(), Kind.TYPED))
        val rest = (completions(typed, limit) + corrections(typed, limit)).sortedByDescending { it.score }
        for (s in rest) {
            if (out.size >= limit) break
            out.putIfAbsent(s.text, s)
        }
        return out.values.toList()
    }

    /** The word space should turn [typed] into, or null to keep it. */
    fun autocorrect(typed: String): String? {
        val lower = typed.lowercase()
        if (typed == lower) CONTRACTIONS[lower]?.let { return it }
        if (typed.length < 2 || typed.any { !it.isLetter() && it != '\'' }) return null
        // Mixed case like "iPad" is deliberate.
        if (typed.drop(1).any { it.isUpperCase() } && !typed.all { it.isUpperCase() }) return null
        CONTRACTIONS[lower]?.let { return matchCase(typed, it) }
        val id = index[lower]
        if (id != null) {
            // Known word; only fix case for names ("monday" -> Monday).
            val s = shown[id]
            val capitalised = s == lower.replaceFirstChar { it.uppercaseChar() }
            return if (capitalised && s != lower && typed == lower) s else null
        }
        val c = corrections(typed, 2)
        val top = c.firstOrNull() ?: return null
        if (top.score < 1.8) return null
        // Probably a word still being typed ("keyb" -> keyboard), not a typo.
        val done = completions(typed, 1).firstOrNull()
        if (done != null && frequency(done.text) >= frequency(top.text) - 1.5) return null
        if (c.size > 1 && c[1].score > top.score - 0.3 && typed.length <= 3) return null
        return top.text
    }

    private fun matchCase(typed: String, word: String): String = when {
        typed.length > 1 && typed.all { it.isUpperCase() } -> word.uppercase()
        typed.first().isUpperCase() -> word.replaceFirstChar { it.uppercaseChar() }
        else -> word
    }

    companion object {
        val CONTRACTIONS = mapOf(
            "dont" to "don't", "im" to "I'm", "ive" to "I've", "youre" to "you're",
            "theyre" to "they're", "didnt" to "didn't", "doesnt" to "doesn't", "isnt" to "isn't",
            "wasnt" to "wasn't", "arent" to "aren't", "couldnt" to "couldn't", "shouldnt" to "shouldn't",
            "wouldnt" to "wouldn't", "havent" to "haven't", "hasnt" to "hasn't", "hadnt" to "hadn't",
            "werent" to "weren't", "thats" to "that's", "whats" to "what's", "theres" to "there's",
            "heres" to "here's", "wheres" to "where's", "whos" to "who's", "hows" to "how's",
            "youve" to "you've", "weve" to "we've", "theyve" to "they've", "youll" to "you'll",
            "theyll" to "they'll", "itll" to "it'll", "shes" to "she's", "wont" to "won't",
            "cant" to "can't", "aint" to "ain't", "yall" to "y'all", "i" to "I",
        )
    }
}
