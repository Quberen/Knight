package io.github.quberen.knight.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

/** Design rules for the layouts, checked for every mode and language. */
class LayoutTest {
    private fun all(): List<Pair<String, List<Block>>> = buildList {
        for (m in LayoutMode.values()) for (zh in listOf(true, false)) {
            for (digits in listOf(true, false)) add("$m letters zh=$zh digits=$digits" to KeyLayouts.letters(m, zh, digits))
            for (zs in listOf(true, false)) add("$m symbols zh=$zh zhSymbols=$zs" to KeyLayouts.symbols(m, zs, zh))
        }
    }

    @Test fun rowsInABlockAreEquallyWide() {
        for ((name, blocks) in all()) for (b in blocks) {
            val widths = b.rows.map { r -> r.keys.sumOf { it.width.toDouble() } }
            for (w in widths) assertEquals("$name ${b.side}: $widths", widths[0], w, 0.001)
        }
    }

    @Test fun splitHalvesMirrorEachOther() {
        for ((name, blocks) in all()) {
            val l = blocks.firstOrNull { it.side == Side.LEFT } ?: continue
            val r = blocks.first { it.side == Side.RIGHT }
            assertEquals(name, l.rows.map { it.height }, r.rows.map { it.height })
            assertEquals(name, l.rows.first().keys.sumOf { it.width.toDouble() }, r.rows.first().keys.sumOf { it.width.toDouble() }, 0.001)
        }
    }

    @Test fun trapezoidLettersShrinkTenNineSeven() {
        val rows = KeyLayouts.letters(LayoutMode.TRAPEZOID, true, false).single().rows
        val letterRows = rows.map { r -> r.keys.filter { it.type == KeyType.CHAR && it.label[0] in 'a'..'z' } }.filter { it.isNotEmpty() }
        assertEquals(listOf(10, 9, 7), letterRows.map { it.size })
        for (r in rows.take(3)) {
            // Wings on both sides are equally wide, so the letters sit centred.
            val first = r.keys.indexOfFirst { it.type == KeyType.CHAR && it.label[0] in 'a'..'z' }
            val last = r.keys.indexOfLast { it.type == KeyType.CHAR && it.label[0] in 'a'..'z' }
            val left = r.keys.take(first).sumOf { it.width.toDouble() }
            val right = r.keys.drop(last + 1).sumOf { it.width.toDouble() }
            assertEquals(left, right, 0.001)
        }
    }

    @Test fun everyLayoutCanDeleteAndEnter() {
        for ((name, blocks) in all()) {
            val types = blocks.flatMap { b -> b.rows.flatMap { r -> r.keys.map { it.type } } }
            assertTrue(name, KeyType.BACKSPACE in types && KeyType.ENTER in types && KeyType.SPACE in types)
        }
    }

    @Test fun eachKeyIsHitAtItsCentre() {
        for ((name, blocks) in all()) {
            val scene = KeyboardScene().apply { dp = 2f; this.blocks = blocks }
            scene.layout(0f, 0f, 2560f)
            for (b in blocks) for (r in b.rows) for (k in r.keys) {
                assertSame(name, k, scene.keyAt(k.box.centerX, k.box.centerY))
            }
            if (blocks.size == 2) assertNull("$name gap", scene.keyAt(1280f, scene.box.centerY))
        }
    }
}
