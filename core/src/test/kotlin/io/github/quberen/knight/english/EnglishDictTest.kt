package io.github.quberen.knight.english

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.BeforeClass
import org.junit.Test
import java.io.File

class EnglishDictTest {
    companion object {
        lateinit var dict: EnglishDict

        @BeforeClass
        @JvmStatic
        fun load() {
            val file = listOf("../app/src/main/assets/english.txt", "app/src/main/assets/english.txt")
                .map(::File).first { it.exists() }
            dict = EnglishDict()
            file.reader().use { dict.load(it) }
        }
    }

    @Test fun fixesCommonTypos() {
        assertEquals("the", dict.autocorrect("teh"))
        assertEquals("receive", dict.autocorrect("recieve"))
        assertEquals("keyboard", dict.autocorrect("keybaord"))
        assertEquals("because", dict.autocorrect("becuase"))
        assertEquals("tomorrow", dict.autocorrect("tommorow"))
    }

    @Test fun fixesNeighbourKeys() = assertEquals("hello", dict.autocorrect("hrllo"))

    @Test fun addsApostrophes() {
        assertEquals("don't", dict.autocorrect("dont"))
        assertEquals("I'm", dict.autocorrect("im"))
        assertEquals("I", dict.autocorrect("i"))
    }

    @Test fun keepsCase() {
        assertEquals("The", dict.autocorrect("Teh"))
        assertEquals("Monday", dict.autocorrect("monday"))
    }

    @Test fun leavesRealWordsAlone() {
        for (w in listOf("hello", "tablet", "okay", "wifi", "lol", "form", "from", "Hello", "iPad", "abc123")) {
            assertNull(w, dict.autocorrect(w))
        }
    }

    @Test fun suggestsCompletions() {
        val s = dict.suggest("keyb").map { it.text }
        assertTrue(s.toString(), s.contains("keyboard"))
        assertEquals("keyb", s[0])
    }

    @Test fun correctionComesFirst() {
        val s = dict.suggest("teh").map { it.text }
        assertEquals("the", s[0])
        assertTrue(s.contains("teh"))
    }
}
