package io.github.quberen.knight

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.provider.Settings
import android.util.TypedValue
import android.view.ViewGroup
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.CompoundButton
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import io.github.quberen.knight.ime.KnightIME

/** Setup steps and keyboard options, all built in code. */
class SettingsActivity : Activity() {
    private lateinit var prefs: Prefs
    private lateinit var status: TextView
    private val dp by lazy { resources.displayMetrics.density }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val p = (20 * dp).toInt()
            setPadding(p, p, p, p)
        }
        fun add(v: android.view.View) = col.addView(
            v, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                .apply { topMargin = (10 * dp).toInt() }
        )

        add(text("Knight 输入法", 26f))
        status = text("", 15f)
        add(status)
        add(button("① 在系统设置里启用 Knight") { startActivity(Intent(Settings.ACTION_INPUT_METHOD_SETTINGS)) })
        add(button("② 切换到 Knight") { imm().showInputMethodPicker() })
        add(EditText(this).apply { hint = "在这里试着打字" })

        add(text("键盘", 20f))
        add(slider("高度", 60, 140, prefs.heightPercent) { prefs.heightPercent = it })
        add(slider("宽度（平板上调窄更顺手）", 50, 100, prefs.widthPercent) { prefs.widthPercent = it })
        add(switch("显示数字行", prefs.digitRow) { prefs.digitRow = it })
        add(switch("按键振动", prefs.vibrate) { prefs.vibrate = it })

        add(text("词库", 20f))
        add(text("会记住你选过的词和自己组的词，下次排在前面。", 14f))
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
        add(text("词库来自雾凇拼音 (rime-ice)，GPL-3.0 许可。", 12f))

        setContentView(ScrollView(this).apply { addView(col) })
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

    private fun text(s: String, size: Float) = TextView(this).apply {
        text = s
        setTextSize(TypedValue.COMPLEX_UNIT_SP, size)
    }

    private fun button(label: String, onClick: () -> Unit) = Button(this).apply {
        text = label
        isAllCaps = false
        setOnClickListener { onClick() }
    }

    private fun switch(label: String, value: Boolean, onChange: (Boolean) -> Unit) = Switch(this).apply {
        text = label
        setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
        isChecked = value
        setOnCheckedChangeListener { _: CompoundButton, checked: Boolean -> onChange(checked) }
    }

    private fun slider(label: String, min: Int, max: Int, value: Int, onChange: (Int) -> Unit): LinearLayout {
        val title = text("$label：$value%", 16f)
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
            addView(title)
            addView(bar)
        }
    }
}
