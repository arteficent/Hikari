package com.example.android_client

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.authDataStore: DataStore<Preferences> by preferencesDataStore(name = "auth")

class AuthRepository(private val context: Context) {

    private val tokenKey = stringPreferencesKey("auth_token")
    private val refreshTokenKey = stringPreferencesKey("refresh_token")

    val token: Flow<String?> = context.authDataStore.data.map {
        it[tokenKey]
    }

    val refreshToken: Flow<String?> = context.authDataStore.data.map {
        it[refreshTokenKey]
    }

    suspend fun saveTokens(token: String, refreshToken: String) {
        context.authDataStore.edit {
            it[tokenKey] = token
            it[refreshTokenKey] = refreshToken
        }
    }
}
