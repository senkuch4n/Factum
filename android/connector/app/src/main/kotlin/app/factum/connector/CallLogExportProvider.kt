/*
 * Factum Connector — app auxiliar de Factum para exportar el registro de llamadas por adb.
 * Copyright (C) Factum.
 *
 * This program is free software: you can redistribute it and/or modify it under the terms of the
 * GNU General Public License as published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version. See <https://www.gnu.org/licenses/>.
 */
package app.factum.connector

import android.content.ContentProvider
import android.content.ContentValues
import android.content.Context
import android.content.pm.PackageManager
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.os.Binder
import android.os.Build
import android.os.Process
import android.provider.CallLog

/**
 * ContentProvider de solo lectura que expone el registro de llamadas para que `adb shell content query`
 * lo exporte sin escribir archivos temporales en el dispositivo (SDD §5.3.1, §7.2). Exportado, pero solo
 * responde a adb (uid SHELL o root): cualquier app del celular recibe SecurityException.
 */
class CallLogExportProvider : ContentProvider() {

    override fun onCreate(): Boolean = true

    override fun query(
        uri: Uri,
        projection: Array<out String>?,
        selection: String?,
        selectionArgs: Array<out String>?,
        sortOrder: String?
    ): Cursor? {
        val callingUid = Binder.getCallingUid()
        if (callingUid != Process.SHELL_UID && callingUid != 0) {
            throw SecurityException("Factum connector: solo adb")
        }
        val ctx = context ?: return null
        return when (uri.lastPathSegment) {
            // Exportación completa y determinística: ignora selection/args/sort y ordena por _ID.
            "calls" -> ctx.contentResolver.query(
                CallLog.Calls.CONTENT_URI, projection, null, null, "${CallLog.Calls._ID} ASC"
            )
            "info" -> infoCursor(ctx)
            else -> null
        }
    }

    private fun infoCursor(ctx: Context): Cursor {
        val cols = arrayOf("package", "version_name", "version_code", "sdk_int", "read_call_log_granted")
        val cursor = MatrixCursor(cols, 1)
        val pkg = ctx.packageName
        var versionName = ""
        var versionCode = 0L
        try {
            val pi = ctx.packageManager.getPackageInfo(pkg, 0)
            versionName = pi.versionName ?: ""
            versionCode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                pi.longVersionCode
            } else {
                @Suppress("DEPRECATION") pi.versionCode.toLong()
            }
        } catch (_: PackageManager.NameNotFoundException) {
            // Deja los defaults.
        }
        val granted =
            ctx.checkSelfPermission(android.Manifest.permission.READ_CALL_LOG) == PackageManager.PERMISSION_GRANTED
        cursor.addRow(arrayOf<Any?>(pkg, versionName, versionCode, Build.VERSION.SDK_INT, if (granted) 1 else 0))
        return cursor
    }

    override fun getType(uri: Uri): String? = null

    override fun insert(uri: Uri, values: ContentValues?): Uri =
        throw UnsupportedOperationException("solo lectura")

    override fun update(
        uri: Uri,
        values: ContentValues?,
        selection: String?,
        selectionArgs: Array<out String>?
    ): Int = throw UnsupportedOperationException("solo lectura")

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int =
        throw UnsupportedOperationException("solo lectura")
}
