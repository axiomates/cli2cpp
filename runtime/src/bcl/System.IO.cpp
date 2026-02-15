/**
 * CIL2CPP Runtime - System.IO implementation
 * File, Directory, and Path operations using C standard library.
 */

#include <cil2cpp/bcl/System.IO.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/exception.h>

#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <sys/stat.h>

#ifdef _WIN32
#include <direct.h>
#include <io.h>
#define stat _stat
#define mkdir(path, mode) _mkdir(path)
#define unlink _unlink
#define rmdir _rmdir
#define access _access
#define F_OK 0
#else
#include <unistd.h>
#include <sys/types.h>
#endif

namespace cil2cpp {
namespace System {
namespace IO {

// ── Helpers ──────────────────────────────────────────────

static char* to_utf8(String* str) {
    if (!str) return nullptr;
    return string_to_utf8(str);
}

// ── System.IO.File ──────────────────────────────────────

String* File_ReadAllText(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) {
        throw_argument_null();
        return nullptr;
    }

    FILE* f = fopen(utf8Path, "rb");
    if (!f) {
        throw_file_not_found(utf8Path);
        std::free(utf8Path);
        return nullptr;
    }

    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);

    char* buf = static_cast<char*>(std::malloc(size + 1));
    size_t bytesRead = fread(buf, 1, size, f);
    buf[bytesRead] = '\0';
    fclose(f);
    std::free(utf8Path);

    // Skip UTF-8 BOM if present
    char* content = buf;
    if (bytesRead >= 3 &&
        static_cast<unsigned char>(buf[0]) == 0xEF &&
        static_cast<unsigned char>(buf[1]) == 0xBB &&
        static_cast<unsigned char>(buf[2]) == 0xBF) {
        content = buf + 3;
    }

    String* result = string_create_utf8(content);
    std::free(buf);
    return result;
}

void File_WriteAllText(String* path, String* contents) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) {
        throw_argument_null();
        return;
    }

    FILE* f = fopen(utf8Path, "wb");
    if (!f) {
        throw_io_exception("Could not open file for writing.");
        std::free(utf8Path);
        return;
    }

    if (contents && contents->length > 0) {
        char* utf8Content = string_to_utf8(contents);
        if (utf8Content) {
            fwrite(utf8Content, 1, std::strlen(utf8Content), f);
            std::free(utf8Content);
        }
    }

    fclose(f);
    std::free(utf8Path);
}

Array* File_ReadAllLines(String* path) {
    String* text = File_ReadAllText(path);
    if (!text || text->length == 0) {
        return static_cast<Array*>(gc::alloc(sizeof(Array), nullptr));
    }

    // Count lines
    Int32 lineCount = 1;
    for (Int32 i = 0; i < text->length; i++) {
        if (text->chars[i] == u'\n') lineCount++;
    }

    // Allocate array
    Array* arr = static_cast<Array*>(
        gc::alloc(sizeof(Array) + lineCount * sizeof(String*), nullptr));
    arr->length = lineCount;
    auto** items = reinterpret_cast<String**>(array_data(arr));

    // Split into lines
    Int32 lineIdx = 0;
    Int32 start = 0;
    for (Int32 i = 0; i <= text->length; i++) {
        bool isEnd = (i == text->length);
        bool isNewline = !isEnd && text->chars[i] == u'\n';

        if (isEnd || isNewline) {
            Int32 end = i;
            // Strip \r before \n
            if (end > start && text->chars[end - 1] == u'\r') end--;
            items[lineIdx++] = string_create_utf16(text->chars + start, end - start);
            start = i + 1;
        }
    }
    arr->length = lineIdx;  // Adjust if trailing newline

    return arr;
}

void File_WriteAllLines(String* path, Array* lines) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) {
        throw_argument_null();
        return;
    }

    FILE* f = fopen(utf8Path, "wb");
    if (!f) {
        throw_io_exception("Could not open file for writing.");
        std::free(utf8Path);
        return;
    }

    if (lines) {
        auto** items = reinterpret_cast<String**>(array_data(lines));
        for (Int32 i = 0; i < lines->length; i++) {
            if (items[i]) {
                char* line = string_to_utf8(items[i]);
                if (line) {
                    fwrite(line, 1, std::strlen(line), f);
                    std::free(line);
                }
            }
            // Write platform line ending
#ifdef _WIN32
            fwrite("\r\n", 1, 2, f);
#else
            fwrite("\n", 1, 1, f);
#endif
        }
    }

    fclose(f);
    std::free(utf8Path);
}

void File_AppendAllText(String* path, String* contents) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) {
        throw_argument_null();
        return;
    }

    FILE* f = fopen(utf8Path, "ab");
    if (!f) {
        throw_io_exception("Could not open file for appending.");
        std::free(utf8Path);
        return;
    }

    if (contents && contents->length > 0) {
        char* utf8Content = string_to_utf8(contents);
        if (utf8Content) {
            fwrite(utf8Content, 1, std::strlen(utf8Content), f);
            std::free(utf8Content);
        }
    }

    fclose(f);
    std::free(utf8Path);
}

Boolean File_Exists(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) return false;

    struct stat st;
    bool exists = (::stat(utf8Path, &st) == 0) && !(st.st_mode & S_IFDIR);
    std::free(utf8Path);
    return exists;
}

void File_Delete(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) return;
    unlink(utf8Path);
    std::free(utf8Path);
}

void File_Copy(String* source, String* dest) {
    File_Copy(source, dest, false);
}

void File_Copy(String* source, String* dest, Boolean overwrite) {
    char* srcPath = to_utf8(source);
    char* dstPath = to_utf8(dest);
    if (!srcPath || !dstPath) {
        std::free(srcPath);
        std::free(dstPath);
        throw_argument_null();
        return;
    }

    if (!overwrite) {
        struct stat st;
        if (::stat(dstPath, &st) == 0) {
            std::free(srcPath);
            std::free(dstPath);
            throw_io_exception("The file already exists.");
            return;
        }
    }

    FILE* fin = fopen(srcPath, "rb");
    if (!fin) {
        throw_file_not_found(srcPath);
        std::free(srcPath);
        std::free(dstPath);
        return;
    }

    FILE* fout = fopen(dstPath, "wb");
    if (!fout) {
        fclose(fin);
        throw_io_exception("Could not open destination file for writing.");
        std::free(srcPath);
        std::free(dstPath);
        return;
    }

    char buf[8192];
    size_t n;
    while ((n = fread(buf, 1, sizeof(buf), fin)) > 0) {
        fwrite(buf, 1, n, fout);
    }

    fclose(fin);
    fclose(fout);
    std::free(srcPath);
    std::free(dstPath);
}

Array* File_ReadAllBytes_Array(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) {
        throw_argument_null();
        return nullptr;
    }

    FILE* f = fopen(utf8Path, "rb");
    if (!f) {
        throw_file_not_found(utf8Path);
        std::free(utf8Path);
        return nullptr;
    }

    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);

    Array* arr = static_cast<Array*>(
        gc::alloc(sizeof(Array) + size, nullptr));
    arr->length = static_cast<Int32>(size);

    fread(array_data(arr), 1, size, f);
    fclose(f);
    std::free(utf8Path);
    return arr;
}

void File_WriteAllBytes(String* path, Array* bytes) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) {
        throw_argument_null();
        return;
    }

    FILE* f = fopen(utf8Path, "wb");
    if (!f) {
        throw_io_exception("Could not open file for writing.");
        std::free(utf8Path);
        return;
    }

    if (bytes && bytes->length > 0) {
        fwrite(array_data(bytes), 1, bytes->length, f);
    }

    fclose(f);
    std::free(utf8Path);
}

// ── System.IO.Directory ─────────────────────────────────

Boolean Directory_Exists(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) return false;

    struct stat st;
    bool exists = (::stat(utf8Path, &st) == 0) && (st.st_mode & S_IFDIR);
    std::free(utf8Path);
    return exists;
}

void Directory_CreateDirectory(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) return;

    // Create parent directories recursively
    for (char* p = utf8Path + 1; *p; p++) {
        if (*p == '/' || *p == '\\') {
            char saved = *p;
            *p = '\0';
            mkdir(utf8Path, 0755);
            *p = saved;
        }
    }
    mkdir(utf8Path, 0755);
    std::free(utf8Path);
}

void Directory_Delete(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) return;
    rmdir(utf8Path);
    std::free(utf8Path);
}

// ── System.IO.Path ──────────────────────────────────────

static bool is_separator(Char c) {
    return c == u'/' || c == u'\\';
}

String* Path_Combine(String* path1, String* path2) {
    if (!path1 || path1->length == 0) return path2;
    if (!path2 || path2->length == 0) return path1;

    // If path2 is rooted, return path2
    if (path2->length > 0 && (is_separator(path2->chars[0]) ||
        (path2->length >= 2 && path2->chars[1] == u':'))) {
        return path2;
    }

    bool needsSep = !is_separator(path1->chars[path1->length - 1]);
    Int32 newLen = path1->length + (needsSep ? 1 : 0) + path2->length;

    String* result = string_fast_allocate(newLen);
    std::memcpy(result->chars, path1->chars, path1->length * sizeof(Char));

    Int32 offset = path1->length;
    if (needsSep) {
#ifdef _WIN32
        result->chars[offset++] = u'\\';
#else
        result->chars[offset++] = u'/';
#endif
    }
    std::memcpy(result->chars + offset, path2->chars, path2->length * sizeof(Char));
    return result;
}

String* Path_Combine(String* path1, String* path2, String* path3) {
    return Path_Combine(Path_Combine(path1, path2), path3);
}

String* Path_GetFileName(String* path) {
    if (!path || path->length == 0) return path;

    Int32 last = -1;
    for (Int32 i = path->length - 1; i >= 0; i--) {
        if (is_separator(path->chars[i])) { last = i; break; }
    }

    if (last < 0) return path;
    return string_create_utf16(path->chars + last + 1, path->length - last - 1);
}

String* Path_GetDirectoryName(String* path) {
    if (!path || path->length == 0) return nullptr;

    Int32 last = -1;
    for (Int32 i = path->length - 1; i >= 0; i--) {
        if (is_separator(path->chars[i])) { last = i; break; }
    }

    if (last < 0) return nullptr;
    if (last == 0) return string_create_utf16(path->chars, 1);
    return string_create_utf16(path->chars, last);
}

String* Path_GetExtension(String* path) {
    if (!path || path->length == 0) return string_create_utf8("");

    for (Int32 i = path->length - 1; i >= 0; i--) {
        if (path->chars[i] == u'.') {
            return string_create_utf16(path->chars + i, path->length - i);
        }
        if (is_separator(path->chars[i])) break;
    }
    return string_create_utf8("");
}

String* Path_GetFileNameWithoutExtension(String* path) {
    String* name = Path_GetFileName(path);
    if (!name || name->length == 0) return name;

    for (Int32 i = name->length - 1; i >= 0; i--) {
        if (name->chars[i] == u'.') {
            return string_create_utf16(name->chars, i);
        }
    }
    return name;
}

Char Path_GetDirectorySeparatorChar() {
#ifdef _WIN32
    return u'\\';
#else
    return u'/';
#endif
}

Boolean Path_IsPathRooted(String* path) {
    if (!path || path->length == 0) return false;
    if (is_separator(path->chars[0])) return true;
    // Windows drive letter: C:
    if (path->length >= 2 && path->chars[1] == u':') return true;
    return false;
}

String* Path_GetFullPath(String* path) {
    char* utf8Path = to_utf8(path);
    if (!utf8Path) return nullptr;

#ifdef _WIN32
    char fullPath[_MAX_PATH];
    if (_fullpath(fullPath, utf8Path, _MAX_PATH)) {
        std::free(utf8Path);
        return string_create_utf8(fullPath);
    }
#else
    char* fullPath = realpath(utf8Path, nullptr);
    if (fullPath) {
        String* result = string_create_utf8(fullPath);
        std::free(fullPath);
        std::free(utf8Path);
        return result;
    }
#endif

    std::free(utf8Path);
    return path;  // fallback: return as-is
}

} // namespace IO
} // namespace System
} // namespace cil2cpp
