package io.github.quberen.knight.ime

import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.inputmethodservice.InputMethodService
import android.os.SystemClock
import android.text.InputType
import android.view.KeyEvent
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.FrameLayout
import android.widget.LinearLayout
import io.github.quberen.knight.DictStore
import io.github.quberen.knight.Prefs
import io.github.quberen.knight.SettingsActivity
import io.github.quberen.knight.pinyin.Composer

class KnightIME : InputMethodService(), KeyboardView.Listener, CandidateBar.Listener {

    private lateinit var prefs: Prefs
    private val composer = Composer(null)

    private var keyboard: KeyboardView? = null
    private var bar: CandidateBar? = null
    private var panel: CandidatePanel? = null

    private var chinese = true
    private var symbols = false
    private var zhSymbols = true
    private var lastShiftTap = 0L
    private var learnAllowed = true

    override fun onCreate() {
        super.onCreate()
        prefs = Prefs(this)
        chinese = prefs.chinese
        DictStore.load(this) { composer.engine = it }
    }

    override fun onCreateInputView(): View {
        val palette = Palette.of(this)
        val dp = resources.displayMetrics.density
        val kb = KeyboardView(this, this)
        val cb = CandidateBar(this, this)
        val cp = CandidatePanel(this) { pick(it) }
        keyboard = kb; bar = cb; panel = cp

        val keys = FrameLayout(this)
        keys.addView(kb, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        keys.addView(cp, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0))
        cp.visibility = View.GONE

        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(palette.background)
            addView(cb, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, (50 * dp).toInt()))
            addView(keys, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
    }

    override fun onEvaluateFullscreenMode(): Boolean = false

    override fun onStartInputView(info: EditorInfo, restarting: Boolean) {
        super.onStartInputView(info, restarting)
        composer.clear()
        val cls = info.inputType and InputType.TYPE_MASK_CLASS
        val variation = info.inputType and InputType.TYPE_MASK_VARIATION
        val isPassword = cls == InputType.TYPE_CLASS_TEXT && (
            variation == InputType.TYPE_TEXT_VARIATION_PASSWORD ||
                variation == InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD ||
                variation == InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD)
        val isAddress = cls == InputType.TYPE_CLASS_TEXT && (
            variation == InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS ||
                variation == InputType.TYPE_TEXT_VARIATION_URI ||
                variation == InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS)
        learnAllowed = !isPassword
        symbols = cls == InputType.TYPE_CLASS_NUMBER || cls == InputType.TYPE_CLASS_PHONE || cls == InputType.TYPE_CLASS_DATETIME
        chinese = if (isPassword || isAddress) false else prefs.chinese
        zhSymbols = chinese

        val palette = Palette.of(this)
        keyboard?.apply {
            this.palette = palette
            heightScale = prefs.heightPercent / 100f
            widthFraction = prefs.widthPercent / 100f
            vibrate = prefs.vibrate
            shiftState = 0
            enterLabel = enterLabelFor(info)
        }
        bar?.applyPalette(palette)
        showPanel(false)
        refreshKeys()
        refreshBar()
    }

    override fun onFinishInputView(finishingInput: Boolean) {
        super.onFinishInputView(finishingInput)
        composer.clear()
        refreshBar()
        DictStore.saveNow()
    }

    private fun enterLabelFor(info: EditorInfo): String {
        if (info.imeOptions and EditorInfo.IME_FLAG_NO_ENTER_ACTION != 0) return "↵"
        return when (info.imeOptions and EditorInfo.IME_MASK_ACTION) {
            EditorInfo.IME_ACTION_SEARCH -> "搜索"
            EditorInfo.IME_ACTION_SEND -> "发送"
            EditorInfo.IME_ACTION_GO -> "前往"
            EditorInfo.IME_ACTION_NEXT -> "下一项"
            EditorInfo.IME_ACTION_DONE -> "完成"
            else -> "↵"
        }
    }

    private fun refreshKeys() {
        val kb = keyboard ?: return
        kb.rows = if (symbols) Layouts.symbols(zhSymbols, chinese) else Layouts.letters(chinese, prefs.digitRow)
        kb.spaceLabel = if (chinese) "Knight · 中" else "Knight · EN"
    }

    private fun refreshBar() {
        val texts = composer.candidates.map { it.text }
        bar?.show(composer.display, texts)
        if (panel?.visibility == View.VISIBLE) {
            if (texts.isEmpty()) showPanel(false) else panel?.show(texts, Palette.of(this))
        }
    }

    private fun showPanel(show: Boolean) {
        val cp = panel ?: return
        val kb = keyboard ?: return
        if (show) {
            cp.layoutParams = cp.layoutParams.apply { height = kb.height }
            cp.show(composer.candidates.map { it.text }, Palette.of(this))
            cp.visibility = View.VISIBLE
            kb.visibility = View.INVISIBLE
        } else {
            cp.visibility = View.GONE
            kb.visibility = View.VISIBLE
        }
        bar?.expanded = show
    }

    // ---- typing ----------------------------------------------------------

    private fun commit(text: String) {
        if (text.isNotEmpty()) currentInputConnection?.commitText(text, 1)
    }

    /** Commits whatever pinyin is pending, using the first candidate. */
    private fun flushComposition() {
        if (!composer.isActive) return
        commit(composer.commitFirst())
        afterCommit()
    }

    private fun afterCommit() {
        if (learnAllowed) DictStore.scheduleSave(this)
        refreshBar()
    }

    private fun pick(index: Int) {
        composer.select(index)?.let { commit(it); afterCommit() }
        refreshBar()
    }

    override fun onCandidate(index: Int) = pick(index)

    override fun onExpand(expand: Boolean) = showPanel(expand)

    override fun onTool(tool: CandidateBar.Tool) {
        when (tool) {
            CandidateBar.Tool.SETTINGS -> startActivity(
                Intent(this, SettingsActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            )
            CandidateBar.Tool.PASTE -> {
                val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                cm.primaryClip?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(this)?.let { commit(it.toString()) }
            }
            CandidateBar.Tool.SWITCH ->
                (getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).showInputMethodPicker()
            CandidateBar.Tool.HIDE -> requestHideSelf(0)
        }
    }

    override fun onCursorMove(steps: Int) {
        flushComposition()
        val code = if (steps < 0) KeyEvent.KEYCODE_DPAD_LEFT else KeyEvent.KEYCODE_DPAD_RIGHT
        repeat(kotlin.math.abs(steps)) { sendDownUpKeyEvents(code) }
    }

    override fun onKey(key: Key, longPress: Boolean) {
        val kb = keyboard ?: return
        when (key.type) {
            KeyType.CHAR -> typeChar(if (longPress) key.hint ?: key.text else key.text)
            KeyType.SEPARATOR -> when {
                longPress -> { flushComposition(); commit("'") }
                composer.isActive -> { composer.type('\''); refreshBar() }
                else -> commit("、")
            }
            KeyType.BACKSPACE -> if (composer.isActive) {
                composer.backspace()
                refreshBar()
            } else {
                sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL)
            }
            KeyType.ENTER -> when {
                composer.isActive -> { commit(composer.commitRaw()); refreshBar() }
                else -> sendEnter()
            }
            KeyType.SPACE -> if (composer.isActive) pick(0) else commit(" ")
            KeyType.SHIFT -> {
                val now = SystemClock.uptimeMillis()
                kb.shiftState = when {
                    kb.shiftState == 0 -> 1
                    kb.shiftState == 1 && now - lastShiftTap < 350 -> 2
                    else -> 0
                }
                lastShiftTap = now
            }
            KeyType.LANG -> {
                if (composer.isActive) { commit(composer.commitRaw()); refreshBar() }
                chinese = !chinese
                zhSymbols = chinese
                prefs.chinese = chinese
                refreshKeys()
            }
            KeyType.TO_SYMBOLS -> { symbols = true; refreshKeys() }
            KeyType.TO_LETTERS -> { symbols = false; refreshKeys() }
            KeyType.SYMBOL_SET -> { zhSymbols = !zhSymbols; refreshKeys() }
            KeyType.LEFT -> onCursorMove(-1)
            KeyType.RIGHT -> onCursorMove(1)
            KeyType.HIDE -> requestHideSelf(0)
        }
    }

    private fun typeChar(text: String) {
        val kb = keyboard ?: return
        val letter = text.length == 1 && text[0] in 'a'..'z'
        if (letter && chinese && kb.shiftState == 0 && !symbols) {
            composer.type(text[0])
            refreshBar()
            return
        }
        flushComposition()
        commit(if (letter && kb.shiftState > 0) text.uppercase() else text)
        if (letter && kb.shiftState == 1) kb.shiftState = 0
    }

    private fun sendEnter() {
        val info = currentInputEditorInfo
        val action = info?.imeOptions?.and(EditorInfo.IME_MASK_ACTION) ?: EditorInfo.IME_ACTION_NONE
        val noAction = info == null || info.imeOptions and EditorInfo.IME_FLAG_NO_ENTER_ACTION != 0 ||
            action == EditorInfo.IME_ACTION_NONE || action == EditorInfo.IME_ACTION_UNSPECIFIED
        if (noAction) sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER)
        else currentInputConnection?.performEditorAction(action)
    }

    override fun onDestroy() {
        DictStore.saveNow()
        super.onDestroy()
    }
}
