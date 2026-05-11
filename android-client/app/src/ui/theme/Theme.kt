package com.example.android_client.ui.theme

import android.app.Activity
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

// ════ Theme variants ══════════════════════════════════════
enum class HikariTheme(val displayName: String) {
    Wisteria("Wisteria"),
    GoldenLeaf("Golden Leaf"),
    Sakura("Sakura");

    companion object {
        fun fromName(name: String): HikariTheme =
            entries.firstOrNull { it.name == name } ?: Wisteria
    }
}

// ──── Wisteria (purple / lavender) ────────────────────
private val WisteriaLightScheme = lightColorScheme(
    primary = Wisteria,
    onPrimary = WashiIvory,
    primaryContainer = WisteriaPale,
    onPrimaryContainer = WisteriaDark,
    secondary = GoldLeaf,
    onSecondary = WashiText,
    secondaryContainer = GoldPale,
    onSecondaryContainer = GoldDeep,
    tertiary = WisteriaMuted,
    onTertiary = WashiIvory,
    background = WashiCream,
    onBackground = WashiText,
    surface = WashiIvory,
    onSurface = WashiText,
    surfaceVariant = WashiEdge,
    onSurfaceVariant = WashiTextSoft,
    outline = WisteriaMuted,
    outlineVariant = WashiEdge,
)

private val WisteriaDarkScheme = darkColorScheme(
    primary = WisteriaLight,
    onPrimary = NightPaper,
    primaryContainer = WisteriaDark,
    onPrimaryContainer = WisteriaPale,
    secondary = GoldShimmer,
    onSecondary = NightPaper,
    secondaryContainer = GoldDeep,
    onSecondaryContainer = GoldShimmer,
    tertiary = WisteriaMuted,
    onTertiary = NightPaper,
    background = NightPaper,
    onBackground = NightText,
    surface = NightSurface,
    onSurface = NightText,
    surfaceVariant = NightBorder,
    onSurfaceVariant = NightTextSoft,
    outline = NightBorder,
    outlineVariant = NightBorder,
)

// ──── Golden Leaf (gold / amber) ──────────────────────
private val GoldenLeafLight = lightColorScheme(
    primary = GoldPrimary,
    onPrimary = WashiIvory,
    primaryContainer = GoldPale,
    onPrimaryContainer = GoldDeep,
    secondary = Wisteria,
    onSecondary = WashiIvory,
    secondaryContainer = WisteriaPale,
    onSecondaryContainer = WisteriaDark,
    tertiary = GoldAmber,
    onTertiary = WashiIvory,
    background = WashiCream,
    onBackground = WashiText,
    surface = WashiIvory,
    onSurface = WashiText,
    surfaceVariant = WashiEdge,
    onSurfaceVariant = WashiTextSoft,
    outline = GoldLight,
    outlineVariant = WashiEdge,
)

private val GoldenLeafDark = darkColorScheme(
    primary = GoldShimmer,
    onPrimary = NightPaper,
    primaryContainer = GoldDeep,
    onPrimaryContainer = GoldShimmer,
    secondary = WisteriaLight,
    onSecondary = NightPaper,
    secondaryContainer = WisteriaDark,
    onSecondaryContainer = WisteriaPale,
    tertiary = GoldAmber,
    onTertiary = NightPaper,
    background = NightPaper,
    onBackground = NightText,
    surface = NightSurface,
    onSurface = NightText,
    surfaceVariant = NightBorder,
    onSurfaceVariant = NightTextSoft,
    outline = NightBorder,
    outlineVariant = NightBorder,
)

// ──── Sakura (pink / rose) ───────────────────────────
private val SakuraLightScheme = lightColorScheme(
    primary = SakuraPink,
    onPrimary = WashiIvory,
    primaryContainer = SakuraPale,
    onPrimaryContainer = SakuraDark,
    secondary = GoldLeaf,
    onSecondary = WashiText,
    secondaryContainer = GoldPale,
    onSecondaryContainer = GoldDeep,
    tertiary = SakuraMuted,
    onTertiary = WashiIvory,
    background = WashiCream,
    onBackground = WashiText,
    surface = WashiIvory,
    onSurface = WashiText,
    surfaceVariant = WashiEdge,
    onSurfaceVariant = WashiTextSoft,
    outline = SakuraMuted,
    outlineVariant = WashiEdge,
)

private val SakuraDarkScheme = darkColorScheme(
    primary = SakuraLight,
    onPrimary = NightPaper,
    primaryContainer = SakuraDark,
    onPrimaryContainer = SakuraPale,
    secondary = GoldShimmer,
    onSecondary = NightPaper,
    secondaryContainer = GoldDeep,
    onSecondaryContainer = GoldShimmer,
    tertiary = SakuraMuted,
    onTertiary = NightPaper,
    background = NightPaper,
    onBackground = NightText,
    surface = NightSurface,
    onSurface = NightText,
    surfaceVariant = NightBorder,
    onSurfaceVariant = NightTextSoft,
    outline = NightBorder,
    outlineVariant = NightBorder,
)

// ════ Resolve pair ════════════════════════════════════════
private fun resolveScheme(theme: HikariTheme, dark: Boolean): ColorScheme = when (theme) {
    HikariTheme.Wisteria   -> if (dark) WisteriaDarkScheme  else WisteriaLightScheme
    HikariTheme.GoldenLeaf -> if (dark) GoldenLeafDark      else GoldenLeafLight
    HikariTheme.Sakura     -> if (dark) SakuraDarkScheme    else SakuraLightScheme
}

@Composable
fun AndroidclientTheme(
    hikariTheme: HikariTheme = HikariTheme.Sakura,
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    val colorScheme = resolveScheme(hikariTheme, darkTheme)

    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as Activity).window
            WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = !darkTheme
            WindowCompat.getInsetsController(window, view).isAppearanceLightNavigationBars = !darkTheme
        }
    }

    MaterialTheme(
        colorScheme = colorScheme,
        typography = PaperTypography,
        shapes = PaperShapes,
        content = content
    )
}