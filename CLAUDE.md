# Claude Development Notes

## Firebird Configuration Rules

**IMPORTANT: These rules must be strictly followed for all Firebird-related development:**

1. **Firebird DLL Storage**: The Firebird DLLs will be stored in the project folder structure:
   - `FirebirdDlls\v25` for Firebird 2.5 files
   - `FirebirdDlls\v50` for Firebird 5.0 files

2. **No Firebird DLLs in Root Project Folder**: There should be NO Firebird DLLs in the root project folder - ONLY in the subfolders.

3. **Build Time Copy - FB 5.0**: At build time, Firebird 5.0 DLLs will be copied to the base output folder.

4. **Build Time Copy - FB 2.5**: At build time, the Firebird 2.5 files will be copied to a subfolder under the base output folder.

5. **All Firebird Connections Are Embedded**: All Firebird connections are embedded mode only. There is no Firebird server running.

6. **CRITICAL - engine13.dll Placement**: `engine13.dll` MUST be placed in TWO locations:
   - Base output folder: `engine13.dll`
   - Plugins subfolder: `plugins\engine13.dll`

7. **NO plugins.conf File**: The `plugins.conf` file must NOT be present as it causes configuration conflicts.

## Current Implementation Status - WORKING CONFIGURATION

- ✅ Project file configured to copy FB 5.0 files from `FirebirdDlls\v50` to output root
- ✅ Project file configured to copy FB 2.5 files from `FirebirdDlls\v25` to output subfolder
- ✅ FirebirdDllSwitcher class removed (no longer needed with build-time deployment)
- ✅ `engine13.dll` copied to both base folder AND plugins subfolder
- ✅ `plugins.conf` excluded from build (causes conflicts)
- ✅ Connection string configured for embedded mode with ServerType=1
- ✅ Firebird 2.5 uses ClientLibrary parameter to specify v25 subfolder path
- ✅ Both Firebird 2.5 and 5.0 connections working reliably

## Connection String Patterns

**Firebird 5.0**: `User=SYSDBA;Password=xxx;Database=path;ServerType=1;`

**Firebird 2.5**: `User=SYSDB;Password=xxx;Database=path;ServerType=1;ClientLibrary=path\to\v25\fbembed.dll;`

## Critical Insight

The Firebird 2.5 interface (via ClientLibrary parameter) loads some Firebird 5.0 components, which explains why `engine13.dll` must be accessible in both the base folder and plugins subfolder for proper operation.