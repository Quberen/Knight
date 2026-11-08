package io.github.quberen.knight

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import io.github.quberen.knight.pinyin.PinyinDict
import io.github.quberen.knight.pinyin.PinyinEngine
import java.io.File
import java.io.StringWriter

/** Loads the dictionary once per process and saves what the user teaches it. */
object DictStore {
    private const val TAG = "KnightDict"
    private const val USER_FILE = "user_words.txt"

    private val main = Handler(Looper.getMainLooper())
    private val waiting = ArrayList<(PinyinEngine) -> Unit>()
    private var loading = false

    var engine: PinyinEngine? = null
        private set

    /** Calls [onReady] on the main thread once the dictionary is loaded. */
    fun load(context: Context, onReady: (PinyinEngine) -> Unit) {
        engine?.let { onReady(it); return }
        waiting.add(onReady)
        if (loading) return
        loading = true
        val app = context.applicationContext
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
            Log.i(TAG, "loaded ${dict.size} words in ${System.currentTimeMillis() - t} ms")
            val e = PinyinEngine(dict)
            main.post {
                engine = e
                loading = false
                waiting.forEach { it(e) }
                waiting.clear()
            }
        }.apply { priority = Thread.MAX_PRIORITY }.start()
    }

    private val saveTask = Runnable { saveNow() }
    private var appContext: Context? = null

    /** Saves a moment later, so quick typing does not write on every word. */
    fun scheduleSave(context: Context) {
        appContext = context.applicationContext
        main.removeCallbacks(saveTask)
        main.postDelayed(saveTask, 3000)
    }

    fun saveNow() {
        val ctx = appContext ?: return
        val dict = engine?.dict ?: return
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
        engine?.dict?.clearUser()
        File(context.filesDir, USER_FILE).delete()
    }
}
