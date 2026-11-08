package io.github.quberen.knight.emoji

import io.github.quberen.knight.pinyin.PinyinDict
import io.github.quberen.knight.pinyin.PinyinEngine
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.io.StringReader

class EmojiDataTest {
    private val assets = listOf("src/main/assets", "app/src/main/assets").map(::File).first { it.exists() }

    private fun load(supported: (String) -> Boolean = { true }) = EmojiData().also { d ->
        File(assets, "emoji.txt").reader().use { d.loadEmoji(it, supported) }
        File(assets, "emoji_words.txt").reader().use { d.loadWords(it) }
    }

    @Test fun groupsAreFilled() {
        val d = load()
        for (g in listOf(EmojiData.SMILEYS, EmojiData.PEOPLE, EmojiData.NATURE, EmojiData.FOOD, EmojiData.TRAVEL,
            EmojiData.ACTIVITIES, EmojiData.OBJECTS, EmojiData.SYMBOLS, EmojiData.FLAGS)) {
            assertTrue("group $g", d.groups[g].orEmpty().size > 20)
        }
        assertEquals("😀", d.groups[EmojiData.SMILEYS]!!.first())
    }

    @Test fun wordsMapToEmoji() {
        val d = load()
        assertTrue(d.forWord("开心").contains("😄"))
        assertTrue(d.forWord("这个词没有表情").isEmpty())
    }

    @Test fun unsupportedEmojiAreLeftOut() {
        val d = load { it != "😄" }
        assertTrue("😄" !in d.groups[EmojiData.SMILEYS]!!)
        assertTrue("😄" !in d.forWord("开心"))
    }

    @Test fun emojiFollowTheirWordInCandidates() {
        val dict = PinyinDict()
        dict.load(StringReader("开心\tkai xin\t1000\n开\tkai\t500\n"))
        val e = PinyinEngine(dict)
        val emoji = load()
        e.extras = { emoji.forWord(it) }
        val list = e.candidates("kaixin").map { it.text }
        assertEquals("开心", list[0])
        assertEquals("😄", list[1])
    }
}
