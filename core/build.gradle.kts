import org.jetbrains.kotlin.gradle.dsl.JvmTarget

// Everything that doesn't need Android: the pinyin and English engines, emoji
// data, and the keyboard's layout and drawing. Tests and previews run here on a
// normal JVM, with Java2D available for drawing PNGs.
plugins {
    id("org.jetbrains.kotlin.jvm")
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }
}

dependencies {
    testImplementation("junit:junit:4.13.2")
}

// Previews: ./gradlew :core:test --tests '*PreviewTest' -Dknight.preview=/some/dir
tasks.test {
    systemProperty("knight.preview", System.getProperty("knight.preview") ?: "")
    systemProperty("java.awt.headless", "true")
}
