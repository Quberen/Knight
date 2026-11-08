package io.github.quberen.knight.ime

import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.res.Configuration
import android.inputmethodservice.InputMethodService
import android.os.SystemClock
import android.text.InputType
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import io.github.quberen.knight.DictStore
import io.github.quberen.knight.Prefs
import io.github.quberen.knight.SettingsActivity
import io.github.quberen.knight.emoji.EmojiData
import io.github.quberen.knight.pinyin.Composer
import io.github.quberen.knight.ui.CandidateStrip
import io.github.quberen.knight.ui.EmojiBoard
import io.github.quberen.knight.ui.Icons
import io.github.quberen.knight.ui.Key
import io.github.quberen.knight.ui.KeyLayouts
import io.github.quberen.knight.ui.KeyType
import io.github.quberen.knight.ui.LayoutMode
import io.github.quberen.knight.ui.Screen
import io.github.quberen.knight.ui.Theme

class KnightIME : InputMethodService(), KnightView.Listener {

    private lateinit var prefs: Prefs
    private val composer = Composer(null)
    private var data: DictStore.Data? = null
    private var view: KnightView? = null

    private var chinese = true
    private var symbols = false
    private var zhSymbols = true
    private var mode = LayoutMode.STANDARD
    private var lastShiftTap = 0L
    private var lastSpace = 0L

    /** Off in password and incognito fields. */
    private var learn = true

    /** English suggestions and fixes are allowed in this field. */
    private var suggest = true

    /** The English word right before the cursor. */
    private var englishWord = ""

    /** The last automatic fix (typed, fixed), undone by an immediate backspace. */
    private var undoFix: Pair<String, String>? = null

    override fun onCreate() {
        super.onCreate()
        prefs = Prefs(this)
        chinese = prefs.chinese
        mode = prefs.layout
        DictStore.load(this) { d ->
            data = d
            applyEngineSettings()
            composer.engine = d.pinyin
            if (composer.isActive) composer.refresh()
            refreshStrip()
        }
    }

    private fun applyEngineSettings() {
        val d = data ?: return
        d.pinyin.options = prefs.spelling
        d.pinyin.extras = if (prefs.emojiCandidates) { w -> d.emoji.forWord(w) } else null
        d.pinyin.englishWord = { w -> d.english.frequency(w) >= 4f }
    }

    override fun onCreateInputView(): View {
        val v = KnightView(this, this)
        view = v
        applyLook(v)
        refreshKeys()
        refreshStrip()
        return v
    }

    override fun onEvaluateFullscreenMode(): Boolean = false

    private fun applyLook(v: KnightView) {
        val night = (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
        v.theme = if (night) Theme.DARK else Theme.LIGHT
        v.vibrate = prefs.vibrate
        v.screen.keys.heightScale = prefs.heightPercent / 100f
        v.screen.keys.widthPercent = prefs.widthPercent
        v.screen.keys.splitPercent = prefs.splitPercent
    }

    override fun onStartInputView(info: EditorInfo, restarting: Boolean) {
        super.onStartInputView(info, restarting)
        composer.clear()
        undoFix = null
        mode = prefs.layout
        applyEngineSettings()

        val cls = info.inputType and InputType.TYPE_MASK_CLASS
        val variation = info.inputType and InputType.TYPE_MASK_VARIATION
        val text = cls == InputType.TYPE_CLASS_TEXT
        val password = (text && variation in PASSWORDS) ||
            (cls == InputType.TYPE_CLASS_NUMBER && variation == InputType.TYPE_NUMBER_VARIATION_PASSWORD)
        val address = text && variation in ADDRESSES
        learn = !password && (info.imeOptions and EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING) == 0
        composer.learning = learn
        suggest = text && !password && (info.inputType and InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS) == 0
        symbols = cls == InputType.TYPE_CLASS_NUMBER || cls == InputType.TYPE_CLASS_PHONE || cls == InputType.TYPE_CLASS_DATETIME
        chinese = if (password || address) false else prefs.chinese
        zhSymbols = chinese

        val v = view ?: return
        v.reset()
        applyLook(v)
        v.screen.body = Screen.Body.KEYS
        v.screen.strip.expanded = false
        v.screen.keys.shiftState = 0
        v.screen.keys.enterIcon = enterIcon(info)
        refreshKeys()
        englishWord = if (chinese) "" else readWord()
        updateAutoCap()
        refreshStrip()
    }

    override fun onFinishInputView(finishingInput: Boolean) {
        super.onFinishInputView(finishingInput)
        composer.clear()
        view?.reset()
        DictStore.saveNow()
    }

    override fun onUpdateSelection(oldSelStart: Int, oldSelEnd: Int, newSelStart: Int, newSelEnd: Int, candidatesStart: Int, candidatesEnd: Int) {
        super.onUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd)
        if (!chinese && view != null) {
            val w = readWord()
            if (w != englishWord) {
                englishWord = w
                refreshStrip()
            }
        }
    }

    private fun enterIcon(info: EditorInfo) =
        if (info.imeOptions and EditorInfo.IME_FLAG_NO_ENTER_ACTION != 0) Icons.RETURN
        else when (info.imeOptions and EditorInfo.IME_MASK_ACTION) {
            EditorInfo.IME_ACTION_SEARCH -> Icons.SEARCH
            EditorInfo.IME_ACTION_SEND -> Icons.SEND
            EditorInfo.IME_ACTION_GO -> Icons.GO
            EditorInfo.IME_ACTION_NEXT -> Icons.NEXT
            EditorInfo.IME_ACTION_DONE -> Icons.DONE
            else -> Icons.RETURN
        }

    // ---- what the view shows ------------------------------------------------

    private fun refreshKeys() {
        val v = view ?: return
        val k = v.screen.keys
        k.blocks = if (symbols) KeyLayouts.symbols(mode, zhSymbols, chinese) else KeyLayouts.letters(mode, chinese, prefs.digitRow)
        k.chinese = chinese
        k.spaceLabel = if (chinese) "拼音" else "English"
        v.screen.strip.layoutIcon = when (mode) {
            LayoutMode.STANDARD -> Icons.LAYOUT_STANDARD
            LayoutMode.SPLIT -> Icons.LAYOUT_SPLIT
            LayoutMode.TRAPEZOID -> Icons.LAYOUT_TRAPEZOID
        }
        v.screen.emoji.lettersLabel = if (chinese) "拼音" else "ABC"
        v.update()
    }

    private fun refreshStrip() {
        val v = view ?: return
        val s = v.screen
        if (chinese && composer.isActive) {
            s.strip.composition = composer.display
            s.strip.items = composer.candidates.take(STRIP_LIMIT).map { it.text }
            if (s.body == Screen.Body.CANDIDATES) s.grid.items = composer.candidates.map { it.text }
        } else {
            s.strip.composition = ""
            s.strip.items = englishSuggestions()
            if (s.body == Screen.Body.CANDIDATES) s.body = Screen.Body.KEYS
        }
        if (s.body != Screen.Body.CANDIDATES) s.strip.expanded = false
        s.strip.activeTool = if (s.body == Screen.Body.EMOJI) CandidateStrip.Tool.EMOJI else null
        s.strip.scrollX = 0f
        v.update()
    }

    private fun englishSuggestions(): List<String> {
        if (chinese || symbols || !suggest || !prefs.englishSuggestions) return emptyList()
        val d = data ?: return emptyList()
        if (englishWord.isEmpty()) return emptyList()
        return d.english.suggest(englishWord).map { it.text }
    }

    // ---- editing helpers ------------------------------------------------------

    private fun commit(text: String) {
        if (text.isNotEmpty()) currentInputConnection?.commitText(text, 1)
    }

    private fun isWordChar(c: Char) = c in 'a'..'z' || c in 'A'..'Z' || c == '\''

    /** The English word right before the cursor, or "" when the cursor is inside a word. */
    private fun readWord(): String {
        val ic = currentInputConnection ?: return ""
        val before = ic.getTextBeforeCursor(48, 0)?.toString() ?: return ""
        var i = before.length
        while (i > 0 && isWordChar(before[i - 1])) i--
        val after = ic.getTextAfterCursor(1, 0)
        if (!after.isNullOrEmpty() && isWordChar(after[0])) return ""
        return before.substring(i).trimStart('\'')
    }

    private fun replaceWord(word: String, replacement: String, then: String = "") {
        val ic = currentInputConnection ?: return
        ic.beginBatchEdit()
        ic.deleteSurroundingText(word.length, 0)
        ic.commitText(replacement + then, 1)
        ic.endBatchEdit()
    }

    /** Commits pending pinyin with its first candidate. */
    private fun flush() {
        if (!composer.isActive) return
        commit(composer.commitFirst())
        if (learn) DictStore.scheduleSave()
        refreshStrip()
    }

    private fun afterEnglishInput() {
        if (chinese) return
        englishWord = readWord()
        updateAutoCap()
        refreshStrip()
    }

    /** Turns shift on at the start of a sentence, where the field asks for it. */
    private fun updateAutoCap() {
        val k = view?.screen?.keys ?: return
        if (chinese || symbols || !prefs.autoCapitalize || k.shiftState == 2) return
        val type = currentInputEditorInfo?.inputType ?: return
        val caps = currentInputConnection?.getCursorCapsMode(type) ?: 0
        val want = if (caps != 0) 1 else 0
        if (k.shiftState != want) {
            k.shiftState = want
            view?.update()
        }
    }

    private fun imm() = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager

    // ---- keys -------------------------------------------------------------------

    override fun onKey(key: Key, longPress: Boolean) {
        if (key.type != KeyType.BACKSPACE) undoFix = null
        when (key.type) {
            KeyType.CHAR -> typeChar(if (longPress) key.hint ?: key.text else key.text)
            KeyType.SEPARATOR -> when {
                longPress -> { flush(); commit("'") }
                composer.isActive -> { composer.type('\''); refreshStrip() }
                else -> commit("、")
            }
            KeyType.BACKSPACE -> backspace()
            KeyType.ENTER -> if (composer.isActive) {
                commit(composer.commitRaw())
                refreshStrip()
            } else {
                sendEnter()
                afterEnglishInput()
            }
            KeyType.SPACE -> space()
            KeyType.SHIFT -> shift()
            KeyType.LANG -> if (longPress) imm().showInputMethodPicker() else toggleLanguage()
            KeyType.TO_SYMBOLS -> { symbols = true; refreshKeys() }
            KeyType.TO_LETTERS -> { symbols = false; refreshKeys(); updateAutoCap() }
            KeyType.SYMBOL_SET -> { zhSymbols = !zhSymbols; refreshKeys() }
            KeyType.EMOJI -> openEmoji()
            KeyType.LEFT -> onCursorMove(-1)
            KeyType.RIGHT -> onCursorMove(1)
            KeyType.HIDE -> requestHideSelf(0)
        }
    }

    private fun typeChar(text: String) {
        val v = view ?: return
        val k = v.screen.keys
        val letter = text.length == 1 && text[0] in 'a'..'z'
        if (letter && chinese && !symbols && k.shiftState == 0) {
            composer.type(text[0])
            refreshStrip()
            return
        }
        flush()
        val out = if (letter && k.shiftState > 0) text.uppercase() else text
        if (!chinese && suggest && prefs.autocorrectEnglish && out.length == 1 && out[0] in ".,!?;:") autocorrectWord()
        commit(out)
        if (letter && k.shiftState == 1) {
            k.shiftState = 0
            v.update()
        }
        afterEnglishInput()
    }

    private fun backspace() {
        if (composer.isActive) {
            composer.backspace()
            refreshStrip()
            return
        }
        val ic = currentInputConnection ?: return
        undoFix?.let { (typed, fixed) ->
            undoFix = null
            if (ic.getTextBeforeCursor(fixed.length + 1, 0)?.toString() == "$fixed ") {
                ic.beginBatchEdit()
                ic.deleteSurroundingText(fixed.length + 1, 0)
                ic.commitText(typed, 1)
                ic.endBatchEdit()
                afterEnglishInput()
                return
            }
        }
        sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL)
        afterEnglishInput()
    }

    private fun space() {
        if (chinese && composer.isActive) {
            pick(0)
            return
        }
        val ic = currentInputConnection ?: return
        if (!chinese) {
            val now = SystemClock.uptimeMillis()
            if (prefs.doubleSpacePeriod && now - lastSpace < 500) {
                val before = ic.getTextBeforeCursor(2, 0)?.toString().orEmpty()
                if (before.length == 2 && before[1] == ' ' && before[0].isLetterOrDigit()) {
                    ic.beginBatchEdit()
                    ic.deleteSurroundingText(1, 0)
                    ic.commitText(". ", 1)
                    ic.endBatchEdit()
                    lastSpace = 0
                    afterEnglishInput()
                    return
                }
            }
            lastSpace = now
            if (suggest && prefs.autocorrectEnglish) autocorrectWord()
        }
        commit(" ")
        afterEnglishInput()
    }

    /** Fixes the word before the cursor if it looks like a typo. */
    private fun autocorrectWord() {
        val d = data ?: return
        val word = readWord()
        if (word.isEmpty()) return
        val fix = d.english.autocorrect(word) ?: return
        if (fix == word) return
        replaceWord(word, fix)
        undoFix = word to fix
    }

    private fun shift() {
        val v = view ?: return
        val k = v.screen.keys
        val now = SystemClock.uptimeMillis()
        k.shiftState = when {
            k.shiftState == 0 -> 1
            k.shiftState == 1 && now - lastShiftTap < 350 -> 2
            else -> 0
        }
        lastShiftTap = now
        v.update()
    }

    private fun toggleLanguage() {
        if (composer.isActive) {
            commit(composer.commitRaw())
        }
        chinese = !chinese
        prefs.chinese = chinese
        zhSymbols = chinese
        view?.screen?.keys?.shiftState = 0
        refreshKeys()
        englishWord = if (chinese) "" else readWord()
        updateAutoCap()
        refreshStrip()
    }

    private fun sendEnter() {
        val info = currentInputEditorInfo
        val action = info?.imeOptions?.and(EditorInfo.IME_MASK_ACTION) ?: EditorInfo.IME_ACTION_NONE
        val plain = info == null || info.imeOptions and EditorInfo.IME_FLAG_NO_ENTER_ACTION != 0 ||
            action == EditorInfo.IME_ACTION_NONE || action == EditorInfo.IME_ACTION_UNSPECIFIED
        if (plain) sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER) else currentInputConnection?.performEditorAction(action)
    }

    override fun onCursorMove(steps: Int) {
        flush()
        val code = if (steps < 0) KeyEvent.KEYCODE_DPAD_LEFT else KeyEvent.KEYCODE_DPAD_RIGHT
        repeat(kotlin.math.abs(steps)) { sendDownUpKeyEvents(code) }
        afterEnglishInput()
    }

    // ---- candidates -----------------------------------------------------------------

    private fun pick(index: Int) {
        val text = composer.select(index)
        if (text != null) {
            commit(text)
            if (learn) DictStore.scheduleSave()
        }
        refreshStrip()
    }

    override fun onStrip(hit: CandidateStrip.Hit) {
        val v = view ?: return
        when (hit) {
            is CandidateStrip.Hit.Item -> if (chinese && composer.isActive) {
                pick(hit.index)
            } else {
                val word = englishWord
                val chosen = v.screen.strip.items.getOrNull(hit.index) ?: return
                replaceWord(word, chosen, " ")
                undoFix = null
                afterEnglishInput()
            }
            CandidateStrip.Hit.Expand -> {
                if (v.screen.body == Screen.Body.CANDIDATES) {
                    v.screen.body = Screen.Body.KEYS
                } else if (composer.isActive) {
                    v.screen.body = Screen.Body.CANDIDATES
                    v.screen.strip.expanded = true
                    v.screen.grid.scrollY = 0f
                }
                refreshStrip()
            }
            is CandidateStrip.Hit.ToolHit -> when (hit.tool) {
                CandidateStrip.Tool.LAYOUT -> {
                    mode = LayoutMode.values()[(mode.ordinal + 1) % LayoutMode.values().size]
                    prefs.layout = mode
                    refreshKeys()
                }
                CandidateStrip.Tool.EMOJI -> if (v.screen.body == Screen.Body.EMOJI) {
                    v.screen.body = Screen.Body.KEYS
                    refreshStrip()
                } else {
                    openEmoji()
                }
                CandidateStrip.Tool.CLIPBOARD -> {
                    val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                    cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.let { commit(it.toString()) }
                    afterEnglishInput()
                }
                CandidateStrip.Tool.SWITCH -> imm().showInputMethodPicker()
                CandidateStrip.Tool.SETTINGS -> startActivity(
                    Intent(this, SettingsActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                )
                CandidateStrip.Tool.HIDE -> requestHideSelf(0)
            }
        }
    }

    override fun onGridPick(index: Int) = pick(index)

    // ---- emoji ------------------------------------------------------------------------

    private fun openEmoji() {
        val v = view ?: return
        flush()
        val e = v.screen.emoji
        e.categories = emojiCategories()
        if (e.selected !in e.categories.indices || (e.selected == 0 && e.categories[0].items.isEmpty())) {
            e.selected = if (e.categories[0].items.isEmpty()) 1 else 0
        }
        e.scrollY = 0f
        v.screen.body = Screen.Body.EMOJI
        refreshStrip()
    }

    private fun emojiCategories(): List<EmojiBoard.Category> {
        val g = data?.emoji?.groups.orEmpty()
        fun group(n: Int) = g[n].orEmpty()
        return listOf(
            EmojiBoard.Category(Icons.RECENT, "最近", prefs.recentEmoji),
            EmojiBoard.Category(Icons.EMOJI, "笑脸", group(EmojiData.SMILEYS)),
            EmojiBoard.Category(Icons.PERSON, "人物", group(EmojiData.PEOPLE)),
            EmojiBoard.Category(Icons.LEAF, "自然", group(EmojiData.NATURE)),
            EmojiBoard.Category(Icons.CUP, "食物", group(EmojiData.FOOD)),
            EmojiBoard.Category(Icons.CAR, "出行", group(EmojiData.TRAVEL)),
            EmojiBoard.Category(Icons.BALL, "活动", group(EmojiData.ACTIVITIES)),
            EmojiBoard.Category(Icons.BULB, "物品", group(EmojiData.OBJECTS)),
            EmojiBoard.Category(Icons.HEART, "符号", group(EmojiData.SYMBOLS)),
            EmojiBoard.Category(Icons.FLAG, "旗帜", group(EmojiData.FLAGS)),
            EmojiBoard.Category(Icons.KAOMOJI, "颜文字", EmojiData.KAOMOJI, wide = true),
        )
    }

    override fun onEmoji(hit: EmojiBoard.Hit, repeat: Boolean) {
        val v = view ?: return
        val e = v.screen.emoji
        when (hit) {
            is EmojiBoard.Hit.Tab -> {
                e.selected = hit.index
                e.scrollY = 0f
                v.update()
            }
            is EmojiBoard.Hit.Item -> {
                val text = e.current?.items?.getOrNull(hit.index) ?: return
                commit(text)
                if (learn) prefs.recentEmoji = (listOf(text) + prefs.recentEmoji.filter { it != text }).take(RECENT_LIMIT)
            }
            EmojiBoard.Hit.Letters -> {
                v.screen.body = Screen.Body.KEYS
                refreshStrip()
            }
            EmojiBoard.Hit.Space -> commit(" ")
            EmojiBoard.Hit.Delete -> sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL)
        }
    }

    override fun onDestroy() {
        DictStore.saveNow()
        super.onDestroy()
    }

    companion object {
        private const val STRIP_LIMIT = 60
        private const val RECENT_LIMIT = 48
        private val PASSWORDS = setOf(
            InputType.TYPE_TEXT_VARIATION_PASSWORD,
            InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD,
            InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD,
        )
        private val ADDRESSES = setOf(
            InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS,
            InputType.TYPE_TEXT_VARIATION_URI,
            InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS,
        )
    }
}
