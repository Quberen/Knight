package io.github.quberen.knight.ime

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

/**
 * The strip above the keys. While typing pinyin it shows candidates;
 * otherwise it shows a few tools.
 */
@SuppressLint("ViewConstructor")
class CandidateBar(context: Context, private val listener: Listener) : LinearLayout(context) {

    interface Listener {
        fun onCandidate(index: Int)
        fun onTool(tool: Tool)
        fun onExpand(expand: Boolean)
    }

    enum class Tool(val label: String) { SETTINGS("⚙ 设置"), PASTE("📋 粘贴"), SWITCH("🌐 切换输入法"), HIDE("⌄ 收起") }

    private val dp = resources.displayMetrics.density
    private var palette = Palette.of(context)

    private val composition = TextView(context).apply {
        setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
        gravity = Gravity.CENTER_VERTICAL
        setPadding((12 * dp).toInt(), 0, (8 * dp).toInt(), 0)
        maxWidth = (260 * dp).toInt()
        isSingleLine = true
        ellipsize = TextUtils.TruncateAt.START
    }
    private val list = LinearLayout(context).apply { orientation = HORIZONTAL }
    private val scroll = HorizontalScrollView(context).apply {
        isHorizontalScrollBarEnabled = false
        addView(list, ViewGroup.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.MATCH_PARENT))
    }
    private val tools = LinearLayout(context).apply { orientation = HORIZONTAL }
    private val expand = TextView(context).apply {
        text = "▾"
        gravity = Gravity.CENTER
        setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
        setOnClickListener { expanded = !expanded; listener.onExpand(expanded) }
    }

    var expanded = false
        set(v) { field = v; expand.text = if (v) "▴" else "▾" }

    init {
        orientation = HORIZONTAL
        addView(composition, LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.MATCH_PARENT))
        addView(scroll, LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f))
        addView(tools, LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f))
        addView(expand, LayoutParams((52 * dp).toInt(), ViewGroup.LayoutParams.MATCH_PARENT))
        for (t in Tool.values()) {
            tools.addView(button(t.label, 15f) { listener.onTool(t) }, LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f))
        }
        applyPalette(palette)
        show("", emptyList())
    }

    fun applyPalette(p: Palette) {
        palette = p
        setBackgroundColor(p.background)
        composition.setTextColor(p.accent)
        expand.setTextColor(p.text)
        for (i in 0 until tools.childCount) (tools.getChildAt(i) as TextView).setTextColor(p.text)
    }

    private fun button(label: String, size: Float, onClick: () -> Unit) = TextView(context).apply {
        text = label
        gravity = Gravity.CENTER
        setTextSize(TypedValue.COMPLEX_UNIT_SP, size)
        setTextColor(palette.text)
        setPadding((14 * dp).toInt(), 0, (14 * dp).toInt(), 0)
        setOnClickListener { onClick() }
    }

    fun show(compositionText: String, candidates: List<String>) {
        val typing = compositionText.isNotEmpty()
        composition.text = compositionText
        composition.visibility = if (typing) View.VISIBLE else View.GONE
        scroll.visibility = if (typing) View.VISIBLE else View.GONE
        expand.visibility = if (typing && candidates.size > 1) View.VISIBLE else View.GONE
        tools.visibility = if (typing) View.GONE else View.VISIBLE
        list.removeAllViews()
        candidates.take(BAR_LIMIT).forEachIndexed { i, text ->
            val b = button(text, 20f) { listener.onCandidate(i) }
            if (i == 0) b.setTextColor(palette.accent)
            list.addView(b, LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.MATCH_PARENT))
        }
        scroll.scrollTo(0, 0)
        if (!typing) expanded = false
    }

    companion object {
        const val BAR_LIMIT = 40
    }
}

/** All candidates in a grid, shown over the keys when the bar is expanded. */
@SuppressLint("ViewConstructor")
class CandidatePanel(context: Context, private val onPick: (Int) -> Unit) : ScrollView(context) {
    private val dp = resources.displayMetrics.density
    private val flow = FlowLayout(context, (6 * dp).toInt())

    init {
        addView(flow, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        val p = (8 * dp).toInt()
        flow.setPadding(p, p, p, p)
    }

    fun show(candidates: List<String>, palette: Palette) {
        setBackgroundColor(palette.background)
        flow.removeAllViews()
        candidates.forEachIndexed { i, text ->
            flow.addView(TextView(context).apply {
                this.text = text
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
                setTextColor(palette.text)
                typeface = Typeface.DEFAULT
                minWidth = (56 * dp).toInt()
                minHeight = (48 * dp).toInt()
                gravity = Gravity.CENTER
                setPadding((12 * dp).toInt(), 0, (12 * dp).toInt(), 0)
                background = GradientDrawable().apply { setColor(palette.key); cornerRadius = 7 * dp }
                setOnClickListener { onPick(i) }
            })
        }
        scrollTo(0, 0)
    }
}

/** Lays children left to right, wrapping to new lines. */
class FlowLayout(context: Context, private val spacing: Int) : ViewGroup(context) {
    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val maxWidth = MeasureSpec.getSize(widthMeasureSpec) - paddingLeft - paddingRight
        var x = 0
        var y = 0
        var lineHeight = 0
        for (i in 0 until childCount) {
            val c = getChildAt(i)
            c.measure(MeasureSpec.makeMeasureSpec(maxWidth, MeasureSpec.AT_MOST), MeasureSpec.makeMeasureSpec(0, MeasureSpec.UNSPECIFIED))
            if (x > 0 && x + c.measuredWidth > maxWidth) {
                x = 0; y += lineHeight + spacing; lineHeight = 0
            }
            x += c.measuredWidth + spacing
            lineHeight = maxOf(lineHeight, c.measuredHeight)
        }
        setMeasuredDimension(MeasureSpec.getSize(widthMeasureSpec), y + lineHeight + paddingTop + paddingBottom)
    }

    override fun onLayout(changed: Boolean, l: Int, t: Int, r: Int, b: Int) {
        val maxWidth = r - l - paddingLeft - paddingRight
        var x = 0
        var y = 0
        var lineHeight = 0
        for (i in 0 until childCount) {
            val c = getChildAt(i)
            if (x > 0 && x + c.measuredWidth > maxWidth) {
                x = 0; y += lineHeight + spacing; lineHeight = 0
            }
            c.layout(paddingLeft + x, paddingTop + y, paddingLeft + x + c.measuredWidth, paddingTop + y + c.measuredHeight)
            x += c.measuredWidth + spacing
            lineHeight = maxOf(lineHeight, c.measuredHeight)
        }
    }
}
