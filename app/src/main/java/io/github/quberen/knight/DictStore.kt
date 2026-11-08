package io.github.quberen.knight

import android.content.Context
import android.graphics.Paint
import android.os.Handler
import android.os.Looper
import android.util.Log
import io.github.quberen.knight.emoji.EmojiData
import io.github.quberen.knight.english.EnglishDict
import io.github.quberen.knight.pinyin.PinyinDict
import io.github.quberen.knight.pinyin.PinyinEngine
import java.io.File
import java.io.StringWriter

/** Loads the word lists once per process and saves what the user teaches the pinyin engine. */
object DictStore {
    private const val TAG = "KnightDict"
    private const val USER_FILE = "user_words.txt"

    class Data(val pinyin: PinyinEngine, val english: EnglishDict, val emoji: EmojiData)

    private val main = Handler(Looper.getMainLooper())
    private val waiting = ArrayList<(Data) -> Unit>()
    private var loading = false

    var data: Data? = null
        private set

    /** Calls [onReady] on the main thread once everything is loaded. */
    fun load(context: Context, onReady: (Data) -> Unit) {
        data?.let { onReady(it); return }
        waiting.add(onReady)
        if (loading) return
        loading = true
        val app = context.applicationContext
        appContext = app
        Thread {
            val t = System.currentTimeMillis()
            val dict = PinyinDict()
            app.assets.open("dict.txt").reader().use { dict.load(it) }
            val user = File(app.filesDir, USER_FILE)
            if (user.exists()) {
                try {
                    user.reader().use { dict.loadUser(it) }
                } catch (e: Exception) {
                    Log.w(TAG, "user words unreadable", e)
                }
            }
            val english = EnglishDict()
            app.assets.open("english.txt").reader().use { english.load(it) }
            // Leave out emoji this device's font can't draw.
            val paint = Paint()
            val emoji = EmojiData()
            app.assets.open("emoji.txt").reader().use { r -> emoji.loadEmoji(r) { paint.hasGlyph(it) } }
            app.assets.open("emoji_words.txt").reader().use { emoji.loadWords(it) }
            Log.i(TAG, "loaded ${dict.size} pinyin words, ${english.size} English words in ${System.currentTimeMillis() - t} ms")
            val d = Data(PinyinEngine(dict), english, emoji)
            main.post {
                data = d
                loading = false
                waiting.forEach { it(d) }
                waiting.clear()
            }
        }.apply { priority = Thread.MAX_PRIORITY }.start()
    }

    private val saveTask = Runnable { saveNow() }
    private var appContext: Context? = null

    /** Saves a moment later, so quick typing does not write on every word. */
    fun scheduleSave() {
        main.removeCallbacks(saveTask)
        main.postDelayed(saveTask, 3000)
    }

    fun saveNow() {
        val ctx = appContext ?: return
        val dict = data?.pinyin?.dict ?: return
        // Read the words on the main thread, where they change; write in the background.
        val text = StringWriter().also { dict.saveUser(it) }.toString()
        Thread {
            val file = File(ctx.filesDir, USER_FILE)
            val tmp = File(ctx.filesDir, "$USER_FILE.tmp")
            tmp.writeText(text)
            if (!tmp.renameTo(file)) Log.w(TAG, "could not save user words")
        }.start()
    }

    fun clearUser(context: Context) {
        data?.pinyin?.dict?.clearUser()
        File(context.filesDir, USER_FILE).delete()
    }
}
