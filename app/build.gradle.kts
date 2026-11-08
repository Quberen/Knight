plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// CI run number, so every build installs over the last one.
val build = System.getenv("GITHUB_RUN_NUMBER")?.toIntOrNull() ?: 1

android {
    namespace = "io.github.quberen.knight"
    compileSdk = 35

    defaultConfig {
        applicationId = "io.github.quberen.knight"
        minSdk = 24
        targetSdk = 35
        versionCode = build
        versionName = "0.1.$build"
    }

    // One fixed key in the repo, so updates install without uninstalling first.
    // Fine for personal builds; use a private key before publishing anywhere.
    signingConfigs {
        create("shared") {
            storeFile = rootProject.file("keystore/knight.keystore")
            storePassword = "knight-ime"
            keyAlias = "knight"
            keyPassword = "knight-ime"
        }
    }

    buildTypes {
        debug {
            signingConfig = signingConfigs.getByName("shared")
        }
        release {
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
            signingConfig = signingConfigs.getByName("shared")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation(project(":core"))
}
