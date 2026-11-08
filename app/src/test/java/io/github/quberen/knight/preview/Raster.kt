package io.github.quberen.knight.preview

import java.awt.image.BufferedImage
import java.io.File
import java.security.MessageDigest
import javax.imageio.ImageIO

/**
 * Pictures of text that Java2D can't draw (colour emoji, rare symbols), made by
 * tools/preview/raster.mjs with Chromium. Missing ones are collected during a
 * first drawing pass, then made all at once.
 */
class Raster(private val dir: File) {
    class Entry(val image: BufferedImage, val width: Float, val ascent: Float)

    private val index = HashMap<String, Pair<Float, Float>>()
    private val images = HashMap<String, BufferedImage>()
    private val missing = LinkedHashMap<String, Triple<String, Float, String>>()

    init {
        load()
    }

    private fun load() {
        val f = File(dir, "index.json")
        if (!f.exists()) return
        // Small fixed format: {"key":{"width":1.0,"ascent":2},...}
        Regex("\"([0-9a-f]+)\":\\{\"width\":([0-9.eE-]+),\"ascent\":([0-9.eE-]+)\\}").findAll(f.readText()).forEach {
            index[it.groupValues[1]] = it.groupValues[2].toFloat() to it.groupValues[3].toFloat()
        }
    }

    private fun key(text: String, size: Float, color: String): String {
        val d = MessageDigest.getInstance("SHA-1").digest("$text|${"%.1f".format(size)}|$color".toByteArray())
        return d.joinToString("") { "%02x".format(it) }.take(20)
    }

    private fun hex(color: Int) = "#%06X".format(color and 0xFFFFFF)

    fun get(text: String, size: Float, color: Int): Entry? {
        val k = key(text, size, hex(color))
        val meta = index[k]
        if (meta == null) {
            missing[k] = Triple(text, size, hex(color))
            return null
        }
        val img = images.getOrPut(k) { ImageIO.read(File(dir, "$k.png")) }
        return Entry(img, meta.first, meta.second)
    }

    /** Width without caring about colour: any colour drawn before will do. */
    fun width(text: String, size: Float): Float? = get(text, size, 0x2B3240)?.width

    val hasMissing: Boolean get() = missing.isNotEmpty()

    /** Draws every missing string with Chromium. */
    fun fetch(repoRoot: File) {
        if (missing.isEmpty()) return
        dir.mkdirs()
        val req = File(dir, "requests.json")
        req.writeText(missing.entries.joinToString(",", "[", "]") { (k, v) ->
            "{\"key\":\"$k\",\"text\":${json(v.first)},\"size\":${v.second},\"color\":\"${v.third}\"}"
        })
        val p = ProcessBuilder("node", File(repoRoot, "tools/preview/raster.mjs").path, req.path, dir.path)
            .redirectErrorStream(true).start()
        val log = p.inputStream.bufferedReader().readText()
        check(p.waitFor() == 0) { "raster.mjs failed: $log" }
        missing.clear()
        load()
    }

    private fun json(s: String): String = buildString {
        append('"')
        for (ch in s) when (ch) {
            '"' -> append("\\\"")
            '\\' -> append("\\\\")
            else -> if (ch.code < 0x20) append("\\u%04x".format(ch.code)) else append(ch)
        }
        append('"')
    }
}
