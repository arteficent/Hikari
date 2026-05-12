package com.example.android_client.core.network

import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject

/**
 * Lightweight JWT helpers — decode the payload locally so the UI can branch on
 * the current user's identity (id, username, roles) without an extra network
 * round trip. Signature is NOT verified here; the server is the source of
 * truth for authorization. We treat decoded claims as advisory hints only.
 */
object JwtDecoder {

    data class Claims(
        val userId: String?,
        val username: String?,
        val roles: List<String>
    ) {
        val isRoot: Boolean get() = roles.any { it.equals("Root", ignoreCase = true) }
        // Root inherits all Admin powers, so isAdmin returns true for Root too.
        val isAdmin: Boolean
            get() = isRoot || roles.any { it.equals("Admin", ignoreCase = true) }
    }

    fun decode(token: String?): Claims? {
        if (token.isNullOrBlank()) return null
        val parts = token.split(".")
        if (parts.size < 2) return null
        return try {
            val payload = String(
                Base64.decode(parts[1], Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
            )
            val obj = JSONObject(payload)
            Claims(
                userId = obj.optString("sub").takeIf { it.isNotBlank() },
                username = obj.optString("username").takeIf { it.isNotBlank() },
                roles = extractRoles(obj)
            )
        } catch (_: Exception) {
            null
        }
    }

    private fun extractRoles(obj: JSONObject): List<String> {
        // ASP.NET emits role claims under the long URI; .NET also serialises them as
        // a JSON string when there's a single role, or an array when there are many.
        val keys = listOf(
            "role",
            "roles",
            "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
        )
        for (k in keys) {
            val v = obj.opt(k) ?: continue
            return when (v) {
                is JSONArray -> List(v.length()) { v.optString(it) }.filter { it.isNotBlank() }
                is String -> if (v.isBlank()) emptyList() else listOf(v)
                else -> emptyList()
            }
        }
        return emptyList()
    }
}
