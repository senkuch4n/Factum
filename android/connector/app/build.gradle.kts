// Factum Connector — módulo app (GNU GPL-3.0-or-later).
// Sin dependencias (ni AndroidX ni coroutines): solo el SDK de Android y el stdlib de Kotlin.
plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
}

// Versión desde propiedades de Gradle (el CI las pasa desde el tag). Defaults de desarrollo: 0.0.0-dev / 1.
val connectorVersionName: String = (project.findProperty("connectorVersionName") as String?) ?: "0.0.0-dev"
val connectorVersionCode: Int = ((project.findProperty("connectorVersionCode") as String?) ?: "1").toInt()

android {
    namespace = "app.factum.connector"
    compileSdk = 35

    defaultConfig {
        applicationId = "app.factum.connector"   // fijo después del primer release (D-T5)
        minSdk = 23
        targetSdk = 35
        versionCode = connectorVersionCode
        versionName = connectorVersionName
    }

    // Firma de release desde variables de entorno. Sin ellas, assembleRelease sale SIN firmar
    // (como en los PR): la clave la custodia el dueño del producto fuera del repo (U1).
    val keystorePath: String? = System.getenv("CONNECTOR_KEYSTORE")
    signingConfigs {
        create("release") {
            if (!keystorePath.isNullOrBlank()) {
                storeFile = file(keystorePath)
                storePassword = System.getenv("CONNECTOR_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("CONNECTOR_KEY_ALIAS")
                keyPassword = System.getenv("CONNECTOR_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
            signingConfig = if (!keystorePath.isNullOrBlank()) signingConfigs.getByName("release") else null
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    lint {
        // El build de CI corre :app:lintRelease; no queremos que un aviso informativo rompa el PR,
        // pero sí los errores reales. MissingApplicationIcon se ignora adrede (no hay launcher).
        warningsAsErrors = false
        abortOnError = true
        disable += "MissingApplicationIcon"
    }
}

dependencies {
    // Intencionalmente vacío.
}
