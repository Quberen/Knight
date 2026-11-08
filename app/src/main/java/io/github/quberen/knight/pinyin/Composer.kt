package io.github.quberen.knight.pinyin

/**
 * The pinyin being typed right now. Picking a candidate that covers only part
 * of the input keeps the rest, so long sentences can be built piece by piece.
 */
class Composer(var engine: PinyinEngine?) {
    private class Pick(val text: String, val entries: List<Entry>, val input: String)

    private val raw = StringBuilder()
    private val picks = ArrayList<Pick>()

    var candidates: List<Candidate> = emptyList()
        private set

    /** When off (password or incognito fields), picks are not remembered. */
    var learning = true

    private var shownInput = ""

    val isActive: Boolean get() = raw.isNotEmpty() || picks.isNotEmpty()
    val input: String get() = raw.toString()

    /** Text to show above the keys: picked words plus the remaining pinyin. */
    val display: String
        get() = picks.joinToString("") { it.text } + shownInput

    fun type(c: Char) {
        if (raw.length >= MAX_INPUT) return
        if (c == '\'' && (raw.isEmpty() || raw.last() == '\'')) return
        raw.append(c)
        refresh()
    }

    /** Returns false when there was nothing to delete. */
    fun backspace(): Boolean {
        when {
            raw.isNotEmpty() -> raw.deleteCharAt(raw.length - 1)
            picks.isNotEmpty() -> raw.append(picks.removeAt(picks.size - 1).input)
            else -> return false
        }
        refresh()
        return true
    }

    /** Picks candidate [index]. Returns the text to commit when the input is used up. */
    fun select(index: Int): String? {
        val c = candidates.getOrNull(index) ?: return null
        val used = c.inputLength.coerceAtMost(raw.length)
        val consumed = raw.substring(0, used)
        raw.delete(0, used)
        while (raw.isNotEmpty() && raw[0] == '\'') raw.deleteCharAt(0)
        picks.add(Pick(c.text, c.entries, consumed))
        if (raw.isNotEmpty()) {
            refresh()
            return null
        }
        return finish()
    }

    /** Commits the first candidate, or the letters when there is none. */
    fun commitFirst(): String {
        while (raw.isNotEmpty()) {
            if (candidates.isEmpty()) return commitRaw()
            select(0)?.let { return it }
        }
        return finish()
    }

    /** Commits picked words plus the remaining letters as typed. */
    fun commitRaw(): String {
        val text = picks.joinToString("") { it.text } + raw.toString().replace("'", "")
        clear()
        return text
    }

    fun clear() {
        raw.setLength(0)
        picks.clear()
        candidates = emptyList()
        shownInput = ""
    }

    private fun finish(): String {
        val text = picks.joinToString("") { it.text }
        val entries = picks.flatMap { it.entries }
        if (learning && entries.isNotEmpty() && picks.all { it.entries.isNotEmpty() }) engine?.learn(entries)
        clear()
        return text
    }

    /** Recomputes candidates, e.g. after the engine or its options changed. */
    fun refresh() {
        val s = raw.toString()
        val e = engine
        when {
            s.isEmpty() || s.all { it == '\'' } -> {
                candidates = emptyList(); shownInput = s
            }
            e == null -> {
                candidates = listOf(Candidate(s.replace("'", ""), s.length, emptyList(), 0.0)); shownInput = s
            }
            else -> {
                val a = e.analyze(s)
                candidates = a.candidates; shownInput = a.display
            }
        }
    }

    companion object {
        const val MAX_INPUT = 48
    }
}
