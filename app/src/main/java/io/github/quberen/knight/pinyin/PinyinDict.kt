package io.github.quberen.knight.pinyin

import java.io.BufferedReader
import java.io.Reader
import java.io.Writer
import kotlin.math.ln

/** One dictionary word with its syllables. */
class Entry(val word: String, val syllables: IntArray, val baseWeight: Long) {
    /** How many times the user picked this word. */
    var userCount: Int = 0
}

/**
 * Word list indexed by the first letter of each syllable, so both full pinyin
 * ("zhongguo") and abbreviations ("zg") find 中国 with one lookup.
 */
class PinyinDict {
    private val syllableNames = ArrayList<String>()
    private val syllableIds = HashMap<String, Int>()
    private val syllablePrefixes = HashSet<String>()
    private val byInitials = HashMap<String, ArrayList<Entry>>()
    private var totalWeight = 1.0
    private var lnTotal = 0.0

    var size: Int = 0
        private set

    val syllableCount: Int get() = syllableNames.size

    fun syllable(id: Int): String = syllableNames[id]
    fun syllableId(s: String): Int = syllableIds[s] ?: -1
    fun isSyllable(s: String): Boolean = syllableIds.containsKey(s)
    fun isSyllablePrefix(s: String): Boolean = syllablePrefixes.contains(s)

    /** Reads lines of `word<TAB>syllables<TAB>weight`. */
    fun load(reader: Reader) {
        BufferedReader(reader, 1 shl 16).useLines { lines ->
            for (line in lines) {
                val t1 = line.indexOf('\t')
                val t2 = line.indexOf('\t', t1 + 1)
                if (t1 <= 0 || t2 <= t1) continue
                val weight = line.substring(t2 + 1).trim().toLongOrNull() ?: continue
                add(line.substring(0, t1), line.substring(t1 + 1, t2).split(' '), weight, checkDuplicate = false)
            }
        }
        recomputeTotal()
    }

    private fun internSyllable(s: String): Int {
        syllableIds[s]?.let { return it }
        val id = syllableNames.size
        syllableNames.add(s)
        syllableIds[s] = id
        for (i in 1..s.length) syllablePrefixes.add(s.substring(0, i))
        return id
    }

    private fun initialsOf(ids: IntArray) = String(CharArray(ids.size) { syllableNames[ids[it]][0] })

    fun find(word: String, syllables: IntArray): Entry? =
        byInitials[initialsOf(syllables)]?.firstOrNull { it.word == word && it.syllables.contentEquals(syllables) }

    /** Adds a word, or returns the existing one. The bundled file has no duplicates, so loading skips the check. */
    fun add(word: String, pinyin: List<String>, weight: Long, checkDuplicate: Boolean = true): Entry {
        val ids = IntArray(pinyin.size) { internSyllable(pinyin[it]) }
        if (checkDuplicate) find(word, ids)?.let { return it }
        val e = Entry(word, ids, weight)
        byInitials.getOrPut(initialsOf(ids)) { ArrayList() }.add(e)
        totalWeight += weight
        size++
        return e
    }

    private fun recomputeTotal() {
        lnTotal = ln(totalWeight)
    }

    fun entriesForInitials(initials: String): List<Entry> = byInitials[initials] ?: emptyList()

    /** Log probability of a word, including what the user taught it. */
    fun score(e: Entry): Double {
        val bonus = if (e.userCount > 0) 2.0 + 2.0 * ln(1.0 + e.userCount) else 0.0
        return ln(e.baseWeight.toDouble().coerceAtLeast(1.0)) - lnTotal + bonus
    }

    fun pinyinOf(e: Entry): String = e.syllables.joinToString(" ") { syllableNames[it] }

    // ---- user data -------------------------------------------------------

    /** Writes every word the user has picked as `word<TAB>pinyin<TAB>count`. */
    fun saveUser(writer: Writer) {
        writer.buffered().use { w ->
            for (e in byInitials.values.flatten()) {
                if (e.userCount > 0) w.write("${e.word}\t${pinyinOf(e)}\t${e.userCount}\n")
            }
        }
    }

    fun loadUser(reader: Reader) {
        BufferedReader(reader).useLines { lines ->
            for (line in lines) {
                val p = line.split('\t')
                if (p.size != 3) continue
                val count = p[2].toIntOrNull() ?: continue
                val e = add(p[0], p[1].split(' '), USER_WORD_WEIGHT)
                e.userCount = count
            }
        }
        recomputeTotal()
    }

    fun clearUser() {
        for (list in byInitials.values) {
            val iter = list.iterator()
            while (iter.hasNext()) {
                val e = iter.next()
                if (e.userCount > 0 && e.baseWeight == USER_WORD_WEIGHT) {
                    iter.remove()
                    size--
                }
                e.userCount = 0
            }
        }
    }

    companion object {
        /** Base weight for phrases the user created; the user bonus does the rest. */
        const val USER_WORD_WEIGHT = 2000L
    }
}
