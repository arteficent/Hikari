package com.example.android_client.ui.theme

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp

/**
 * A paper-textured card surface with a subtle animated gold shimmer,
 * evoking glittery washi paper from the reference artwork.
 */
@Composable
fun PaperSurface(
    modifier: Modifier = Modifier,
    content: @Composable () -> Unit
) {
    val shimmerTransition = rememberInfiniteTransition(label = "shimmer")
    val shimmerOffset by shimmerTransition.animateFloat(
        initialValue = -300f,
        targetValue = 600f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 3500, easing = LinearEasing),
            repeatMode = RepeatMode.Restart
        ),
        label = "shimmerOffset"
    )

    val shimmerBrush = Brush.linearGradient(
        colors = listOf(
            MaterialTheme.colorScheme.surface,
            GoldShimmer.copy(alpha = 0.15f),
            MaterialTheme.colorScheme.surface,
        ),
        start = Offset(shimmerOffset, shimmerOffset * 0.6f),
        end = Offset(shimmerOffset + 300f, shimmerOffset * 0.6f + 180f),
    )

    Surface(
        modifier = modifier,
        shape = MaterialTheme.shapes.medium,
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 0.5.dp,
        shadowElevation = 2.dp,
        border = BorderStroke(0.5.dp, MaterialTheme.colorScheme.outlineVariant),
    ) {
        Box(modifier = Modifier.background(shimmerBrush)) {
            content()
        }
    }
}

@Preview(showBackground = true)
@Composable
fun PaperSurfacePreview() {
    AndroidclientTheme {
        PaperSurface(modifier = Modifier.padding(16.dp)) {
            Text(
                text = "PaperSurface preview",
                modifier = Modifier.padding(50.dp),
                color = MaterialTheme.colorScheme.onSurface,
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}