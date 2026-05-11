package com.example.android_client.ui.theme

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.DarkMode
import androidx.compose.material.icons.filled.LightMode
import androidx.compose.material.icons.filled.NightsStay
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.filled.WbSunny
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.graphics.painter.Painter
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.rememberVectorPainter
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.android.awaitFrame
import kotlinx.coroutines.isActive
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt
import kotlin.random.Random

// =======================================================
//  Celestial icon shapes
// =======================================================

private enum class CelestialShape {
    STAR, SPARKLE, SUN_WAVY, SUN_STRAIGHT, MOON_CRESCENT, MOON_STARRY,
}

// =======================================================
//  Mutable state holder for each celestial body.
//  ALL positions (x, y) and velocities (vx, vy) are in
//  PIXELS.  Positions are set lazily once screen size is
//  known via placeBodies().
// =======================================================

private class CelestialBody(
    var x: Float = 0f,
    var y: Float = 0f,
    val sizeDp: Float,
    val shape: CelestialShape,
    val baseAlpha: Float,
    var phase: Float,
    val twinkleSpeed: Float,
    val driftDpSec: Float = 0f,
    val driftAngle: Float = 0f,
    var vx: Float = 0f,
    var vy: Float = 0f,
    var rotation: Float = 0f,
    val rotationSpeed: Float = 0f,
    val color: Color,
    val hasGlow: Boolean = false,
)

/**
 * Padded envelope radius in pixels.  Used everywhere:
 * wall bounce, body-body collision, and draw clamping.
 *
 *   = sizeDp * density * 0.75  =  (icon half-size) * 1.5
 *
 * Covers the rotation diagonal (sqrt2 ~ 1.42) plus breathing room.
 */
private fun CelestialBody.envelope(density: Float): Float =
    sizeDp * density * 0.75f

private const val TAU = (2 * PI).toFloat()

// =======================================================
//  Factory  -  creates bodies WITHOUT positions
// =======================================================

private fun buildBodyDefinitions(rng: Random): List<CelestialBody> {
    val bodies = mutableListOf<CelestialBody>()

    repeat(5) {
        bodies += CelestialBody(
            sizeDp = rng.nextFloat() * 24f + 18f,
            shape = CelestialShape.STAR,
            baseAlpha = rng.nextFloat() * 0.35f + 0.35f,
            phase = rng.nextFloat() * TAU,
            twinkleSpeed = rng.nextFloat() * 2f + 1f,
            driftDpSec = rng.nextFloat() * 8f + 4f,
            driftAngle = rng.nextFloat() * TAU,
            rotation = rng.nextFloat() * 360f,
            color = GoldShimmer,
        )
    }

    repeat(7) {
        bodies += CelestialBody(
            sizeDp = rng.nextFloat() * 20f + 18f,
            shape = CelestialShape.SPARKLE,
            baseAlpha = rng.nextFloat() * 0.3f + 0.3f,
            phase = rng.nextFloat() * TAU,
            twinkleSpeed = rng.nextFloat() * 3f + 2f,
            driftDpSec = rng.nextFloat() * 10f + 4f,
            driftAngle = rng.nextFloat() * TAU,
            rotation = rng.nextFloat() * 360f,
            color = GoldShimmer,
        )
    }

    repeat(2) {
        bodies += CelestialBody(
            sizeDp = rng.nextFloat() * 30f + 72f,
            shape = if (rng.nextBoolean()) CelestialShape.SUN_WAVY
                    else CelestialShape.SUN_STRAIGHT,
            baseAlpha = rng.nextFloat() * 0.2f + 0.45f,
            phase = rng.nextFloat() * TAU,
            twinkleSpeed = rng.nextFloat() * 0.8f + 0.4f,
            driftDpSec = rng.nextFloat() * 5f + 2f,
            driftAngle = rng.nextFloat() * TAU,
            rotation = rng.nextFloat() * 360f,
            rotationSpeed = (rng.nextFloat() - 0.5f) * 6f,
            color = GoldShimmer,
        )
    }

    repeat(3) {
        bodies += CelestialBody(
            sizeDp = rng.nextFloat() * 24f + 48f,
            shape = if (rng.nextBoolean()) CelestialShape.MOON_CRESCENT
                    else CelestialShape.MOON_STARRY,
            baseAlpha = rng.nextFloat() * 0.2f + 0.4f,
            phase = rng.nextFloat() * TAU,
            twinkleSpeed = rng.nextFloat() * 0.6f + 0.3f,
            driftDpSec = rng.nextFloat() * 6f + 3f,
            driftAngle = rng.nextFloat() * TAU,
            rotation = rng.nextFloat() * 40f - 20f,
            rotationSpeed = (rng.nextFloat() - 0.5f) * 3f,
            color = GoldShimmer,
        )
    }

    return bodies
}

// =======================================================
//  Placement  -  pixel positions, non-overlapping
// =======================================================

private fun placeBodies(
    bodies: List<CelestialBody>,
    screenW: Float,
    screenH: Float,
    density: Float,
    rng: Random,
) {
    for (b in bodies) {
        val r = b.envelope(density)

        // Convert drift speed + angle into pixel velocity
        b.vx = b.driftDpSec * density * cos(b.driftAngle)
        b.vy = b.driftDpSec * density * sin(b.driftAngle)

        var placed = false
        repeat(300) {
            val cx = rng.nextFloat() * (screenW - 2f * r) + r
            val cy = rng.nextFloat() * (screenH - 2f * r) + r
            val overlaps = bodies.any { o ->
                o !== b && (o.x != 0f || o.y != 0f) &&
                    sqrt((cx - o.x).let { dx -> dx * dx } +
                         (cy - o.y).let { dy -> dy * dy }) <
                    r + o.envelope(density)
            }
            if (!overlaps) {
                b.x = cx; b.y = cy; placed = true
                return@repeat
            }
        }
        if (!placed) {
            b.x = rng.nextFloat() * (screenW - 2f * r) + r
            b.y = rng.nextFloat() * (screenH - 2f * r) + r
        }
    }
}

// =======================================================
//  Icon -> Painter mapping
// =======================================================

private val shapeToIcon: Map<CelestialShape, ImageVector> = mapOf(
    CelestialShape.STAR          to Icons.Filled.Star,
    CelestialShape.SPARKLE       to Icons.Filled.AutoAwesome,
    CelestialShape.SUN_WAVY      to Icons.Filled.WbSunny,
    CelestialShape.SUN_STRAIGHT  to Icons.Filled.LightMode,
    CelestialShape.MOON_CRESCENT to Icons.Filled.DarkMode,
    CelestialShape.MOON_STARRY   to Icons.Filled.NightsStay,
)

// =======================================================
//  Main composable
// =======================================================

@Composable
fun CelestialSurface(
    modifier: Modifier = Modifier,
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val spaceBackground = if (darkTheme) SpaceBlack else SpaceLight
    val iconColor       = if (darkTheme) GoldShimmer else CelestialGoldDim
    val glowColor       = if (darkTheme) GoldShimmer else CelestialGold

    val rng    = remember { Random(System.nanoTime()) }
    val bodies = remember { buildBodyDefinitions(rng) }

    // Screen pixel dimensions  -  written from Canvas, read by physics loop
    var screenW by remember { mutableFloatStateOf(0f) }
    var screenH by remember { mutableFloatStateOf(0f) }
    val density = LocalDensity.current.density

    val painters: Map<CelestialShape, Painter> =
        shapeToIcon.mapValues { (_, icon) -> rememberVectorPainter(icon) }

    // Tick counter drives recomposition each frame
    var frameTime by remember { mutableLongStateOf(0L) }

    // ---- animation / physics loop (all pixel-space) ----
    LaunchedEffect(Unit) {
        var lastNanos = awaitFrame()
        var placed = false

        while (isActive) {
            val now = awaitFrame()
            val dt = ((now - lastNanos) / 1_000_000_000f).coerceAtMost(0.05f)
            lastNanos = now

            val sw = screenW
            val sh = screenH
            if (sw <= 0f || sh <= 0f) { frameTime = now; continue }

            // One-time placement once real screen size is known
            if (!placed) {
                placeBodies(bodies, sw, sh, density, rng)
                placed = true
            }

            // -- movement --
            for (b in bodies) {
                b.phase += b.twinkleSpeed * dt
                b.x += b.vx * dt
                b.y += b.vy * dt
                if (b.rotationSpeed != 0f) b.rotation += b.rotationSpeed * dt
            }

            // -- wall bounce (envelope = padded radius) --
            for (b in bodies) {
                val r = b.envelope(density)
                if (b.x < r)      { b.x = r;      b.vx =  abs(b.vx) }
                if (b.x > sw - r) { b.x = sw - r;  b.vx = -abs(b.vx) }
                if (b.y < r)      { b.y = r;      b.vy =  abs(b.vy) }
                if (b.y > sh - r) { b.y = sh - r;  b.vy = -abs(b.vy) }
            }

            // -- body-body elastic collision (3 passes) --
            repeat(3) {
                for (i in bodies.indices) {
                    for (j in i + 1 until bodies.size) {
                        val a = bodies[i]
                        val b = bodies[j]
                        val minDist = a.envelope(density) + b.envelope(density)
                        val dx = b.x - a.x
                        val dy = b.y - a.y
                        val dist = sqrt(dx * dx + dy * dy)
                        if (dist < minDist && dist > 0.01f) {
                            val nx = dx / dist
                            val ny = dy / dist
                            // push apart
                            val overlap = (minDist - dist) / 2f
                            a.x -= nx * overlap
                            a.y -= ny * overlap
                            b.x += nx * overlap
                            b.y += ny * overlap
                            // elastic velocity exchange along normal
                            val dvDotN = (a.vx - b.vx) * nx + (a.vy - b.vy) * ny
                            if (dvDotN > 0f) {
                                a.vx -= dvDotN * nx
                                a.vy -= dvDotN * ny
                                b.vx += dvDotN * nx
                                b.vy += dvDotN * ny
                            }
                        }
                    }
                }
            }

            // -- final safety clamp --
            for (b in bodies) {
                val r = b.envelope(density)
                b.x = b.x.coerceIn(r, sw - r)
                b.y = b.y.coerceIn(r, sh - r)
            }

            frameTime = now
        }
    }

    // ---- rendering ----
    Box(modifier = modifier.background(spaceBackground)) {
        @Suppress("UNUSED_EXPRESSION")
        frameTime

        Canvas(modifier = Modifier.fillMaxSize()) {
            screenW = size.width
            screenH = size.height

            for (b in bodies) {
                if (b.x == 0f && b.y == 0f) continue   // not yet placed
                val painter = painters[b.shape] ?: continue
                val alpha   = (b.baseAlpha + 0.3f * sin(b.phase)).coerceIn(0.05f, 1f)
                val sizePx  = b.sizeDp.dp.toPx()
                val half    = sizePx / 2f
                val env     = sizePx * 0.75f            // envelope in draw space

                val cx = b.x.coerceIn(env, size.width  - env)
                val cy = b.y.coerceIn(env, size.height - env)

                if (b.hasGlow) {
                    val ga = (alpha * 0.35f).coerceIn(0f, 1f)
                    drawCircle(
                        brush = Brush.radialGradient(
                            colors = listOf(
                                glowColor.copy(alpha = ga),
                                glowColor.copy(alpha = ga * 0.3f),
                                Color.Transparent,
                            ),
                            center = Offset(cx, cy),
                            radius = sizePx * 1.6f,
                        ),
                        radius = sizePx * 1.6f,
                        center = Offset(cx, cy),
                    )
                }

                translate(left = cx - half, top = cy - half) {
                    rotate(degrees = b.rotation, pivot = Offset(half, half)) {
                        with(painter) {
                            draw(
                                size = Size(sizePx, sizePx),
                                alpha = alpha,
                                colorFilter = ColorFilter.tint(iconColor),
                            )
                        }
                    }
                }
            }
        }
        content()
    }
}

// =======================================================
//  Preview
// =======================================================

@Preview(name = "Dark", showBackground = true, widthDp = 360, heightDp = 640)
@Composable
fun CelestialSurfaceDarkPreview() {
    AndroidclientTheme(hikariTheme = HikariTheme.Sakura, darkTheme = true) {
        CelestialSurface(modifier = Modifier.fillMaxSize(), darkTheme = true) {
            Text(
                text = "Celestial (Dark)",
                modifier = Modifier.padding(24.dp),
                color = MaterialTheme.colorScheme.onBackground,
                style = MaterialTheme.typography.headlineMedium,
            )
        }
    }
}

@Preview(name = "Light", showBackground = true, widthDp = 360, heightDp = 640)
@Composable
fun CelestialSurfaceLightPreview() {
    AndroidclientTheme(hikariTheme = HikariTheme.Sakura, darkTheme = false) {
        CelestialSurface(modifier = Modifier.fillMaxSize(), darkTheme = false) {
            Text(
                text = "Celestial (Light)",
                modifier = Modifier.padding(24.dp),
                color = MaterialTheme.colorScheme.onBackground,
                style = MaterialTheme.typography.headlineMedium,
            )
        }
    }
}