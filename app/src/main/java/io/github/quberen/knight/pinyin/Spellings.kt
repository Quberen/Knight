package io.github.quberen.knight.pinyin

/** Sound pairs some people don't tell apart. Each one can be switched on separately. */
enum class Fuzzy(val label: String) {
    Z_ZH("z = zh"), C_CH("c = ch"), S_SH("s = sh"),
    N_L("n = l"), F_H("f = h"), R_L("r = l"),
    AN_ANG("an = ang"), EN_ENG("en = eng"), IN_ING("in = ing"),
    IAN_IANG("ian = iang"), UAN_UANG("uan = uang"),
}

data class SpellingOptions(
    val fuzzy: Set<Fuzzy> = emptySet(),
    /** Fix typos such as "tain" -> tian, "hoa" -> hao, "zhogn" -> zhong. */
    val correct: Boolean = true,
)

/**
 * Every way a typed string can stand for a syllable, with a penalty in log
 * probability: 0 for an exact spelling, small for a fuzzy pair, larger for a typo.
 */
class Spellings(private val dict: PinyinDict, val options: SpellingOptions) {

    private val variants = HashMap<String, HashMap<Int, Double>>()
    private val prefixIds = HashMap<String, IntArray>()

    init {
        val byPrefix = HashMap<String, ArrayList<Int>>()
        for (id in 0 until dict.syllableCount) {
            val s = dict.syllable(id)
            for (i in 1..s.length) byPrefix.getOrPut(s.substring(0, i)) { ArrayList() }.add(id)
        }
        for ((k, v) in byPrefix) prefixIds[k] = v.toIntArray()

        for (id in 0 until dict.syllableCount) {
            val s = dict.syllable(id)
            for ((v, p) in fuzzyForms(s)) put(v, id, p)
            if (options.correct) {
                for ((v, p) in typoForms(s)) {
                    // A typo rule never takes over a spelling that is already a real syllable.
                    if (!dict.isSyllable(v)) put(v, id, p)
                }
            }
        }
    }

    private fun put(spelling: String, id: Int, penalty: Double) {
        val m = variants.getOrPut(spelling) { HashMap() }
        val old = m[id]
        if (old == null || penalty < old) m[id] = penalty
    }

    /** Syllables [s] can be on its own, with penalties. Includes the exact syllable. */
    fun whole(s: String): Map<Int, Double> {
        val exact = dict.syllableId(s)
        val v = variants[s]
        if (exact < 0) return v ?: emptyMap()
        if (v == null) return mapOf(exact to 0.0)
        return HashMap(v).apply { put(exact, 0.0) }
    }

    /** Syllables [s] could be the start of ("zh" -> zhong, zhang ...), with penalties. */
    fun prefix(s: String): Map<Int, Double> {
        val out = HashMap<Int, Double>()
        prefixIds[s]?.forEach { out[it] = 0.0 }
        for ((alt, p) in initialSwaps(s)) {
            prefixIds[alt]?.forEach { id -> if (!out.containsKey(id)) out[id] = p }
        }
        return out
    }

    fun isPrefix(s: String): Boolean = prefixIds.containsKey(s) || initialSwaps(s).any { prefixIds.containsKey(it.first) }

    fun hasWhole(s: String): Boolean = dict.isSyllable(s) || variants.containsKey(s)

    // ---- fuzzy pairs -------------------------------------------------------

    private fun splitInitial(s: String): Int = when {
        s.length >= 2 && s[1] == 'h' && s[0] in "zcs" -> 2
        s.isNotEmpty() && s[0] in INITIALS -> 1
        else -> 0
    }

    /** The same string with its initial swapped by an enabled fuzzy pair. */
    private fun initialSwaps(s: String): List<Pair<String, Double>> {
        val f = options.fuzzy
        if (f.isEmpty() || s.isEmpty()) return emptyList()
        val cut = splitInitial(s)
        val ini = s.substring(0, cut)
        val rest = s.substring(cut)
        val out = ArrayList<Pair<String, Double>>(2)
        fun pair(a: String, b: String, rule: Fuzzy) {
            if (rule !in f) return
            if (ini == a) out.add(b + rest to FUZZY_PENALTY)
            else if (ini == b) out.add(a + rest to FUZZY_PENALTY)
        }
        pair("z", "zh", Fuzzy.Z_ZH)
        pair("c", "ch", Fuzzy.C_CH)
        pair("s", "sh", Fuzzy.S_SH)
        pair("n", "l", Fuzzy.N_L)
        pair("f", "h", Fuzzy.F_H)
        pair("r", "l", Fuzzy.R_L)
        return out
    }

    private fun fuzzyForms(s: String): List<Pair<String, Double>> {
        val f = options.fuzzy
        if (f.isEmpty()) return emptyList()
        val cut = splitInitial(s)
        val ini = s.substring(0, cut)
        val fin = s.substring(cut)
        val heads = arrayListOf(ini to 0.0)
        for ((alt, p) in initialSwaps(s)) heads.add(alt.substring(0, alt.length - fin.length) to p)
        val tails = arrayListOf(fin to 0.0)
        fun pair(a: String, b: String, rule: Fuzzy) {
            if (rule !in f) return
            if (fin == a) tails.add(b to FUZZY_PENALTY) else if (fin == b) tails.add(a to FUZZY_PENALTY)
        }
        pair("an", "ang", Fuzzy.AN_ANG)
        pair("en", "eng", Fuzzy.EN_ENG)
        pair("in", "ing", Fuzzy.IN_ING)
        pair("ian", "iang", Fuzzy.IAN_IANG)
        pair("uan", "uang", Fuzzy.UAN_UANG)
        val out = ArrayList<Pair<String, Double>>()
        for ((h, hp) in heads) for ((t, tp) in tails) {
            if (hp + tp > 0) out.add(h + t to hp + tp)
        }
        return out
    }

    // ---- typos -------------------------------------------------------------

    private fun typoForms(s: String): List<Pair<String, Double>> {
        val out = ArrayList<Pair<String, Double>>()
        val n = s.length
        val cut = splitInitial(s)
        val ini = s.substring(0, cut)
        val fin = s.substring(cut)

        // Other accepted ways of writing the same sound.
        if (fin == "un" && ini !in listOf("j", "q", "x", "y")) out.add(ini + "uen" to SPELLING_PENALTY)
        if (fin == "ui") out.add(ini + "uei" to SPELLING_PENALTY)
        if (fin == "iu") out.add(ini + "iou" to SPELLING_PENALTY)
        if (ini in listOf("j", "q", "x", "y") && fin.startsWith("u")) out.add(ini + "v" + fin.substring(1) to SPELLING_PENALTY)
        if (ini in listOf("l", "n") && fin == "ve") out.add(ini + "ue" to SPELLING_PENALTY)

        // Slips that happen all the time.
        if (s.endsWith("ng")) out.add(s.dropLast(2) + "gn" to COMMON_PENALTY)
        if (s.endsWith("ao")) out.add(s.dropLast(2) + "oa" to COMMON_PENALTY)
        for (end in listOf("iao", "ian", "iang", "uan", "uang", "uai")) {
            if (s.endsWith(end) && n > end.length) {
                val head = s.dropLast(end.length)
                out.add(head + end[1] + end[0] + end.substring(2) to COMMON_PENALTY)
            }
        }

        if (n >= 2) {
            // Two letters swapped.
            for (i in 0 until n - 1) {
                if (s[i] == s[i + 1]) continue
                val c = s.toCharArray()
                c[i] = s[i + 1]; c[i + 1] = s[i]
                addIfLong(out, String(c), SWAP_PENALTY)
            }
            // A neighbouring key hit instead.
            for (i in 0 until n) {
                for (k in NEIGHBORS[s[i]] ?: "") {
                    addIfLong(out, s.substring(0, i) + k + s.substring(i + 1), NEIGHBOR_PENALTY)
                }
            }
            // A key pressed twice.
            for (i in 0 until n) addIfLong(out, s.substring(0, i + 1) + s[i] + s.substring(i + 1), DOUBLE_PENALTY)
        }
        // A letter left out (never the first one).
        if (n >= 4) for (i in 1 until n) addIfLong(out, s.removeRange(i, i + 1), MISSING_PENALTY)
        return out
    }

    /**
     * Two-letter typo forms are only used when they can't be the start of a
     * syllable, so abbreviations like "zh" or "sh" keep their meaning.
     */
    private fun addIfLong(out: MutableList<Pair<String, Double>>, v: String, p: Double) {
        if (v.length >= 3 || (v.length == 2 && !prefixIds.containsKey(v))) out.add(v to p)
    }

    companion object {
        const val INITIALS = "bpmfdtnlgkhjqxrzcsyw"
        const val FUZZY_PENALTY = 1.1
        const val SPELLING_PENALTY = 0.2
        const val COMMON_PENALTY = 1.2
        const val DOUBLE_PENALTY = 1.5
        const val SWAP_PENALTY = 2.0
        const val NEIGHBOR_PENALTY = 2.5
        const val MISSING_PENALTY = 2.5

        /** Keys next to each other on a QWERTY layout. */
        val NEIGHBORS: Map<Char, String> = mapOf(
            'q' to "wa", 'w' to "qeas", 'e' to "wrsd", 'r' to "etdf", 't' to "ryfg",
            'y' to "tugh", 'u' to "yihj", 'i' to "uojk", 'o' to "ipkl", 'p' to "ol",
            'a' to "qwsz", 's' to "adwezx", 'd' to "sferxc", 'f' to "dgrtcv", 'g' to "fhtyvb",
            'h' to "gjyubn", 'j' to "hkuinm", 'k' to "jliom", 'l' to "kop",
            'z' to "asx", 'x' to "zcsd", 'c' to "xvdf", 'v' to "cbfg", 'b' to "vngh",
            'n' to "bmhj", 'm' to "njk",
        )
    }
}
