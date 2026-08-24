plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

// Version is injected by CI from the release tag (-PhikariVersionName=1.2.3).
// A local build with no properties keeps the defaults below. An explicitly
// supplied but unusable value fails the build rather than silently shipping 1.
val hikariVersionName = (findProperty("hikariVersionName") as String?)
    ?.takeIf { it.isNotBlank() } ?: "1.0"
val hikariVersionCode = (findProperty("hikariVersionCode") as String?)
    ?.takeIf { it.isNotBlank() }
    ?.let { raw ->
        raw.toIntOrNull()?.takeIf { it in 1..2_100_000_000 }
            ?: error("hikariVersionCode must be an integer in 1..2100000000, got \"$raw\"")
    } ?: 1

// Release signing is opt-in: the workflow exports these only when the keystore
// secrets exist, so an unsigned build stays possible without extra config.
val keystorePath: String? = System.getenv("HIKARI_KEYSTORE_PATH")?.takeIf { it.isNotBlank() }
val keystoreFile = keystorePath?.let(::file)?.takeIf { it.exists() }

android {
    namespace = "com.example.android_client"
    compileSdk {
        version = release(36) {
            minorApiLevel = 1
        }
    }

    defaultConfig {
        applicationId = "com.example.android_client"
        minSdk = 24
        targetSdk = 36
        versionCode = hikariVersionCode
        versionName = hikariVersionName
    }

    signingConfigs {
        if (keystoreFile != null) {
            create("release") {
                storeFile = keystoreFile
                storePassword = System.getenv("HIKARI_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("HIKARI_KEY_ALIAS")
                keyPassword = System.getenv("HIKARI_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        debug {
            buildConfigField("boolean", "INSECURE_TLS", "true")
        }
        release {
            buildConfigField("boolean", "INSECURE_TLS", "false")
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
            // Left unsigned when no keystore is supplied; the APK is then still
            // installable via `adb install` but not distributable through stores.
            signingConfig = signingConfigs.findByName("release")
        }
    }
    compileOptions {
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    buildFeatures {
        compose = true
        buildConfig = true
    }

    // Custom source layout: everything lives directly under app/src/ instead of
    // the conventional app/src/main/{java,res,AndroidManifest.xml}.
    sourceSets {
        getByName("main") {
            manifest.srcFile("src/AndroidManifest.xml")
            java.setSrcDirs(listOf("src"))
            kotlin.setSrcDirs(listOf("src"))
            res.setSrcDirs(listOf("src/res"))
        }
    }
}

dependencies {
    coreLibraryDesugaring(libs.desugar.jdk.libs)
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.material.icons.extended)
    implementation(libs.ktor.client.core)
    implementation(libs.ktor.client.cio)
    implementation(libs.ktor.client.content.negotiation)
    implementation(libs.ktor.serialization.kotlinx.json)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.androidx.datastore.preferences)
    implementation(libs.jaudiotagger)
    implementation(libs.androidx.exifinterface)
    implementation(libs.mp4parser)
    implementation(libs.zip4j)
    implementation("io.coil-kt.coil3:coil-compose:3.4.0")
    debugImplementation(libs.androidx.compose.ui.tooling)
}