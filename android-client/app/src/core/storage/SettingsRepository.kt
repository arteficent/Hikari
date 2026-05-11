package com.example.android_client.core.storage

import android.content.Context
import android.util.Log
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "settings")

class SettingsRepository(private val context: Context) {

    companion object {
        private const val TAG = "SettingsRepository"
    }

    private val serverDomainKey = stringPreferencesKey("server_domain")
    private val themeKey = stringPreferencesKey("hikari_theme")

    val serverDomain: Flow<String?> = context.dataStore.data.map {
        it[serverDomainKey]
    }

    val themeName: Flow<String> = context.dataStore.data.map {
        it[themeKey] ?: "Wisteria"
    }

    suspend fun saveTheme(name: String) {
        context.dataStore.edit { it[themeKey] = name }
    }

    suspend fun saveServerDomain(domain: String) {
        Log.d(TAG, "saveServerDomain() called with domain: $domain")
        context.dataStore.edit {
            it[serverDomainKey] = domain
        }
    }

    suspend fun clearServerDomain() {
        Log.d(TAG, "clearServerDomain() called")
        context.dataStore.edit {
            it.remove(serverDomainKey)
        }
    }
}
