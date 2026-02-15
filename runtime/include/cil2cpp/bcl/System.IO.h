/**
 * CIL2CPP Runtime - System.IO BCL types
 * Provides File, Directory, and Path operations.
 */

#pragma once

#include "../types.h"
#include "../string.h"
#include "../array.h"

namespace cil2cpp {
namespace System {
namespace IO {

// ===== System.IO.File =====
String*  File_ReadAllText(String* path);
void     File_WriteAllText(String* path, String* contents);
String** File_ReadAllLines(String* path, Int32* outCount);  // internal
Array*   File_ReadAllLines(String* path);
void     File_WriteAllLines(String* path, Array* lines);
void     File_AppendAllText(String* path, String* contents);
Boolean  File_Exists(String* path);
void     File_Delete(String* path);
void     File_Copy(String* source, String* dest);
void     File_Copy(String* source, String* dest, Boolean overwrite);
Byte*    File_ReadAllBytes(String* path, Int32* outLength);  // internal
Array*   File_ReadAllBytes_Array(String* path);
void     File_WriteAllBytes(String* path, Array* bytes);

// ===== System.IO.Directory =====
Boolean  Directory_Exists(String* path);
void     Directory_CreateDirectory(String* path);
void     Directory_Delete(String* path);

// ===== System.IO.Path =====
String*  Path_Combine(String* path1, String* path2);
String*  Path_Combine(String* path1, String* path2, String* path3);
String*  Path_GetFileName(String* path);
String*  Path_GetDirectoryName(String* path);
String*  Path_GetExtension(String* path);
String*  Path_GetFileNameWithoutExtension(String* path);
Char     Path_GetDirectorySeparatorChar();
Boolean  Path_IsPathRooted(String* path);
String*  Path_GetFullPath(String* path);

} // namespace IO
} // namespace System
} // namespace cil2cpp
