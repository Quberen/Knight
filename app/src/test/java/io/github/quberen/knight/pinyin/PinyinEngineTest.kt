package io.github.quberen.knight.pinyin

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.BeforeClass
import org.junit.Test
import java.io.File
import java.io.StringReader
import java.io.StringWriter

class PinyinEngineTest {
    companion object {
        lateinit var engine: PinyinEngine

        @BeforeClass
        @JvmStatic
        fun load() {
            val file = listOf("src/main/assets/dict.txt", "app/src/main/assets/dict.txt")
                .map(::File).first { it.exists() }
            val dict = PinyinDict()
            file.reader().use { dict.load(it) }
            engine = PinyinEngine(dict)
        }
    }

    private fun top(input: String, n: Int = 5) = engine.candidates(input).take(n).map { it.text }

    @Test fun fullPinyin() = assertEquals("中国", top("zhongguo")[0])

    @Test fun abbreviation() = assertTrue(top("zg", 10).toString(), top("zg", 10).contains("中国"))

    @Test fun mixedAbbreviation() = assertTrue(top("nih").toString(), top("nih").contains("你好"))

    @Test fun sentence() = assertEquals("我们今天去公园", top("womenjintianqugongyuan")[0])

    @Test fun ambiguousSplit() {
        val all = engine.candidates("fangan").map { it.text }
        assertEquals("方案", all[0])
        assertTrue(all.contains("反感"))
    }

    @Test fun apostropheSplit() = assertEquals("西安", top("xi'an")[0])

    @Test fun xianAlsoOffersXiAn() = assertTrue(engine.candidates("xian").take(40).any { it.text == "西安" })

    @Test fun lv() = assertEquals("绿色", top("lvse")[0])

    @Test fun display() = assertEquals("zhong'guo'ren", engine.display("zhongguoren"))

    @Test fun pickPieceByPiece() {
        val c = Composer(engine)
        "zhongguoren".forEach(c::type)
        val idx = c.candidates.indexOfFirst { it.text == "中国" }
        assertTrue(idx >= 0)
        assertEquals(null, c.select(idx))
        assertEquals("中国ren", c.display)
        val ren = c.candidates.indexOfFirst { it.text == "人" }
        assertEquals("中国人", c.select(ren))
        assertTrue(!c.isActive)
    }

    @Test fun backspaceUndoesPick() {
        val c = Composer(engine)
        "nihao".forEach(c::type)
        c.select(c.candidates.indexOfFirst { it.text == "你" })
        assertEquals("你hao", c.display)
        repeat(3) { c.backspace() }
        assertEquals("你", c.display)
        c.backspace()
        assertEquals("ni", c.display)
    }

    @Test fun learnsNewPhrase() {
        val dict = PinyinDict()
        dict.load(StringReader("库\tku\t100\n苦\tku\t5000\n奈\tnai\t100\n耐\tnai\t5000\n"))
        val e = PinyinEngine(dict)
        assertEquals("苦耐", e.candidates("kunai")[0].text)
        val c = Composer(e)
        "kunai".forEach(c::type)
        c.select(c.candidates.indexOfFirst { it.text == "库" })
        assertEquals("库奈", c.select(c.candidates.indexOfFirst { it.text == "奈" }))
        assertEquals("库奈", e.candidates("kunai")[0].text)

        val saved = StringWriter().also { dict.saveUser(it) }.toString()
        val again = PinyinDict()
        again.load(StringReader("库\tku\t100\n苦\tku\t5000\n奈\tnai\t100\n耐\tnai\t5000\n"))
        again.loadUser(StringReader(saved))
        assertEquals("库奈", PinyinEngine(again).candidates("kunai")[0].text)
    }

    @Test fun unknownLettersStillCommit() {
        val c = Composer(engine)
        "iphone".forEach(c::type)
        assertTrue(c.candidates.any { it.text == "iphone" })
    }

    @Test fun speed() {
        val t = System.nanoTime()
        repeat(20) { engine.candidates("womenjintianqugongyuanwanba") }
        val ms = (System.nanoTime() - t) / 1_000_000 / 20
        assertTrue("took $ms ms", ms < 150)
    }
}
