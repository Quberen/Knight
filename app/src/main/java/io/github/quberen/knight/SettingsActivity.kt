package io.github.quberen.knight

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.content.res.Configuration
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.provider.Settings
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.EditText
import android.widget.GridLayout
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import io.github.quberen.knight.ime.KnightIME
import io.github.quberen.knight.pinyin.Fuzzy
import io.github.quberen.knight.ui.LayoutMode
import io.github.quberen.knight.ui.Theme

/** Setup steps and options, built in code to keep the app small. */
class SettingsActivity : Activity() {
    private lateinit var prefs: Prefs
    private lateinit var status: TextView
    private val dp by lazy { resources.displayMetrics.density }
    private val look by lazy {
        val night = (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
        if (night) Theme.DARK else Theme.LIGHT
    }
    private val fuzzySwitches = HashMap<Fuzzy, Switch>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val p = (20 * dp).toInt()
            setPadding(p, p, p, (40 * dp).toInt())
        }
        fun add(v: View, top: Int = 8) = col.addView(
            v, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { topMargin = (top * dp).toInt() }
        )

        add(text("Knight 输入法", 26f, bold = true), 0)
        status = text("", 15f, soft = true)
        add(status, 4)

        add(header("开始使用"), 24)
        add(button("① 在系统设置里启用 Knight") { startActivity(Intent(Settings.ACTION_INPUT_METHOD_SETTINGS)) })
        add(button("② 切换到 Knight") { imm().showInputMethodPicker() })
        add(EditText(this).apply { hint = "在这里试着打字" })

        add(header("键盘布局"), 24)
        add(note("也可以点候选栏最左边的布局图标，三种布局轮流切换。"), 2)
        val group = RadioGroup(this)
        val descriptions = mapOf(
            LayoutMode.STANDARD to "全宽，和电脑键盘一样",
            LayoutMode.SPLIT to "左右分开、对称排列，双手拿着平板用拇指打字",
            LayoutMode.TRAPEZOID to "字母按 10 / 9 / 7 居中排成倒梯形，功能键放在两侧",
        )
        for (m in LayoutMode.values()) {
            group.addView(RadioButton(this).apply {
                id = View.generateViewId()
                text = "${m.label}  ·  ${descriptions[m]}"
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
                isChecked = prefs.layout == m
                setPadding(paddingLeft, (6 * dp).toInt(), paddingRight, (6 * dp).toInt())
                setOnCheckedChangeListener { _, checked -> if (checked) prefs.layout = m }
            })
        }
        add(group)

        add(header("尺寸"), 24)
        add(slider("键盘高度", 60, 140, prefs.heightPercent) { prefs.heightPercent = it })
        add(slider("键盘宽度（标准和倒梯形）", 50, 100, prefs.widthPercent) { prefs.widthPercent = it })
        add(slider("对称布局每一侧的宽度", 26, 46, prefs.splitPercent) { prefs.splitPercent = it })

        add(header("拼音"), 24)
        add(switch("自动纠错", "打错的拼音自动改正：tain→tian、zhogn→zhong、nihoa→nihao，按错旁边的键、漏打或多打一个字母也能认出来。", prefs.correctPinyin) {
            prefs.correctPinyin = it
        })
        add(text("模糊音", 16f, bold = true), 16)
        add(note("分不清的音可以打开。打对的拼音仍然排在前面，模糊匹配的词要常用得多才会排到第一。"), 2)
        val grid = GridLayout(this).apply { columnCount = 2 }
        val on = prefs.fuzzy
        for (f in Fuzzy.values()) {
            val s = Switch(this).apply {
                text = f.label
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
                isChecked = f in on
                setPadding((4 * dp).toInt(), (6 * dp).toInt(), (16 * dp).toInt(), (6 * dp).toInt())
                setOnCheckedChangeListener { _, checked ->
                    prefs.fuzzy = if (checked) prefs.fuzzy + f else prefs.fuzzy - f
                }
            }
            fuzzySwitches[f] = s
            grid.addView(s, GridLayout.LayoutParams().apply {
                width = 0
                columnSpec = GridLayout.spec(GridLayout.UNDEFINED, 1f)
            })
        }
        add(grid)
        val quick = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        quick.addView(button("南方口音常用") {
            setFuzzy(setOf(Fuzzy.Z_ZH, Fuzzy.C_CH, Fuzzy.S_SH, Fuzzy.N_L, Fuzzy.IN_ING, Fuzzy.EN_ENG))
        }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        quick.addView(button("全部关闭") { setFuzzy(emptySet()) },
            LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply { marginStart = (8 * dp).toInt() })
        add(quick)
        add(switch("候选里显示表情", "打出「开心」时，后面跟着 😄。", prefs.emojiCandidates) { prefs.emojiCandidates = it }, 12)

        add(header("英文"), 24)
        add(switch("单词建议", "打英文时在候选栏显示补全和改正。", prefs.englishSuggestions) { prefs.englishSuggestions = it })
        add(switch("空格时自动纠正", "teh → the、dont → don't。改错了马上按删除就能撤销。", prefs.autocorrectEnglish) { prefs.autocorrectEnglish = it })
        add(switch("句首自动大写", null, prefs.autoCapitalize) { prefs.autoCapitalize = it })
        add(switch("双击空格输入句号", null, prefs.doubleSpacePeriod) { prefs.doubleSpacePeriod = it })

        add(header("其他"), 24)
        add(switch("显示数字行", null, prefs.digitRow) { prefs.digitRow = it })
        add(switch("按键振动", null, prefs.vibrate) { prefs.vibrate = it })

        add(header("词库"), 24)
        add(note("会记住你选过的词和自己组的词，下次排在前面。只保存在这台设备上。"), 2)
        add(button("清除学到的词") {
            AlertDialog.Builder(this)
                .setMessage("清除所有学到的词？")
                .setPositiveButton("清除") { _, _ ->
                    DictStore.clearUser(this)
                    Toast.makeText(this, "已清除", Toast.LENGTH_SHORT).show()
                }
                .setNegativeButton("取消", null)
                .show()
        })

        add(header("关于"), 24)
        add(note("拼音词库来自雾凇拼音 rime-ice（GPL-3.0），表情数据来自 emojibase（MIT），英文词频来自 wordfreq（CC BY-SA 4.0）。本应用按 GPL-3.0 发布，不联网，不收集任何数据。"), 2)

        setContentView(ScrollView(this).apply {
            setBackgroundColor(look.background)
            addView(col)
        })
    }

    private fun setFuzzy(set: Set<Fuzzy>) {
        prefs.fuzzy = set
        for ((f, s) in fuzzySwitches) s.isChecked = f in set
    }

    override fun onResume() {
        super.onResume()
        refreshStatus()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) refreshStatus()
    }

    private fun imm() = getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager

    private fun refreshStatus() {
        val enabled = imm().enabledInputMethodList.any { it.packageName == packageName }
        val current = Settings.Secure.getString(contentResolver, Settings.Secure.DEFAULT_INPUT_METHOD) ?: ""
        val selected = current.startsWith("$packageName/") && current.endsWith(KnightIME::class.java.simpleName)
        status.text = when {
            selected -> "✅ 已启用并正在使用"
            enabled -> "已启用，请点 ② 切换过来"
            else -> "还没启用，请点 ①"
        }
    }

    private fun text(s: String, size: Float, bold: Boolean = false, soft: Boolean = false) = TextView(this).apply {
        text = s
        setTextSize(TypedValue.COMPLEX_UNIT_SP, size)
        setTextColor(if (soft) look.inkSoft else look.ink)
        if (bold) typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    }

    private fun header(s: String) = text(s, 13f, bold = true, soft = true).apply { letterSpacing = 0.05f }

    private fun note(s: String) = text(s, 13f, soft = true).apply { setLineSpacing(0f, 1.2f) }

    private fun button(label: String, onClick: () -> Unit) = Button(this).apply {
        text = label
        isAllCaps = false
        setTextColor(look.ink)
        background = GradientDrawable().apply {
            setColor(look.key)
            cornerRadius = 10 * dp
            setStroke((1 * dp).toInt(), look.divider)
        }
        minHeight = (48 * dp).toInt()
        gravity = Gravity.CENTER
        setOnClickListener { onClick() }
    }

    private fun switch(label: String, detail: String?, value: Boolean, onChange: (Boolean) -> Unit): View {
        val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        box.addView(Switch(this).apply {
            text = label
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            setTextColor(look.ink)
            isChecked = value
            minHeight = (44 * dp).toInt()
            setOnCheckedChangeListener { _, checked -> onChange(checked) }
        })
        if (detail != null) box.addView(note(detail).apply { setPadding(0, 0, (56 * dp).toInt(), (4 * dp).toInt()) })
        return box
    }

    private fun slider(label: String, min: Int, max: Int, value: Int, onChange: (Int) -> Unit): LinearLayout {
        val title = text("$label：$value%", 15f)
        val bar = SeekBar(this).apply {
            this.max = max - min
            progress = value - min
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(s: SeekBar, p: Int, fromUser: Boolean) {
                    title.text = "$label：${p + min}%"
                    if (fromUser) onChange(p + min)
                }

                override fun onStartTrackingTouch(s: SeekBar) {}
                override fun onStopTrackingTouch(s: SeekBar) {}
            })
        }
        return LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, (6 * dp).toInt(), 0, (6 * dp).toInt())
            addView(title)
            addView(bar)
        }
    }
}
