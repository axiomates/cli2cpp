/**
 * CIL2CPP Runtime - Additional System.String methods
 * Contains: search, transform, format, join, split operations.
 */

#include <cil2cpp/bcl/System.String.h>
#include <cil2cpp/array.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/type_info.h>

#include <cstring>
#include <cctype>
#include <cstdio>
#include <vector>

namespace cil2cpp {

// ── Conversion helpers ────────────────────────────────────

String* string_from_bool(Boolean value) {
    return string_create_utf8(value ? "True" : "False");
}

String* string_from_char(Char value) {
    return string_create_utf16(&value, 1);
}

String* string_fast_allocate(Int32 length) {
    size_t size = sizeof(String) + (length * sizeof(Char));
    String* str = static_cast<String*>(gc::alloc(size, &System::String_TypeInfo));
    str->length = length;
    return str;
}

// ── Search / comparison ───────────────────────────────────

Int32 string_index_of(String* str, Char value) {
    return string_index_of(str, value, 0);
}

Int32 string_index_of(String* str, Char value, Int32 startIndex) {
    if (!str) return -1;
    for (Int32 i = startIndex; i < str->length; i++) {
        if (str->chars[i] == value) return i;
    }
    return -1;
}

Int32 string_index_of_string(String* str, String* value) {
    if (!str || !value) return -1;
    if (value->length == 0) return 0;
    if (value->length > str->length) return -1;

    for (Int32 i = 0; i <= str->length - value->length; i++) {
        if (std::memcmp(str->chars + i, value->chars, value->length * sizeof(Char)) == 0)
            return i;
    }
    return -1;
}

Int32 string_last_index_of(String* str, Char value) {
    if (!str) return -1;
    for (Int32 i = str->length - 1; i >= 0; i--) {
        if (str->chars[i] == value) return i;
    }
    return -1;
}

Boolean string_contains(String* str, Char value) {
    return string_index_of(str, value) >= 0;
}

Boolean string_contains_string(String* str, String* value) {
    return string_index_of_string(str, value) >= 0;
}

Boolean string_starts_with(String* str, String* value) {
    if (!str || !value) return false;
    if (value->length > str->length) return false;
    return std::memcmp(str->chars, value->chars, value->length * sizeof(Char)) == 0;
}

Boolean string_ends_with(String* str, String* value) {
    if (!str || !value) return false;
    if (value->length > str->length) return false;
    Int32 offset = str->length - value->length;
    return std::memcmp(str->chars + offset, value->chars, value->length * sizeof(Char)) == 0;
}

Int32 string_compare_ordinal(String* a, String* b) {
    if (a == b) return 0;
    if (!a) return -1;
    if (!b) return 1;
    Int32 minLen = a->length < b->length ? a->length : b->length;
    for (Int32 i = 0; i < minLen; i++) {
        if (a->chars[i] != b->chars[i])
            return a->chars[i] < b->chars[i] ? -1 : 1;
    }
    if (a->length == b->length) return 0;
    return a->length < b->length ? -1 : 1;
}

// ── Transformation ────────────────────────────────────────

String* string_to_upper(String* str) {
    if (!str) return nullptr;
    String* result = string_fast_allocate(str->length);
    for (Int32 i = 0; i < str->length; i++) {
        Char c = str->chars[i];
        result->chars[i] = (c >= 'a' && c <= 'z') ? static_cast<Char>(c - 32) : c;
    }
    return result;
}

String* string_to_lower(String* str) {
    if (!str) return nullptr;
    String* result = string_fast_allocate(str->length);
    for (Int32 i = 0; i < str->length; i++) {
        Char c = str->chars[i];
        result->chars[i] = (c >= 'A' && c <= 'Z') ? static_cast<Char>(c + 32) : c;
    }
    return result;
}

static bool is_whitespace(Char c) {
    return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
}

String* string_trim(String* str) {
    if (!str || str->length == 0) return str;
    Int32 start = 0, end = str->length - 1;
    while (start <= end && is_whitespace(str->chars[start])) start++;
    while (end >= start && is_whitespace(str->chars[end])) end--;
    if (start > end) return string_create_utf8("");
    return string_create_utf16(str->chars + start, end - start + 1);
}

String* string_trim_start(String* str) {
    if (!str || str->length == 0) return str;
    Int32 start = 0;
    while (start < str->length && is_whitespace(str->chars[start])) start++;
    if (start == 0) return str;
    return string_create_utf16(str->chars + start, str->length - start);
}

String* string_trim_end(String* str) {
    if (!str || str->length == 0) return str;
    Int32 end = str->length - 1;
    while (end >= 0 && is_whitespace(str->chars[end])) end--;
    if (end == str->length - 1) return str;
    return string_create_utf16(str->chars, end + 1);
}

String* string_replace(String* str, Char oldChar, Char newChar) {
    if (!str) return nullptr;
    String* result = string_fast_allocate(str->length);
    for (Int32 i = 0; i < str->length; i++) {
        result->chars[i] = (str->chars[i] == oldChar) ? newChar : str->chars[i];
    }
    return result;
}

String* string_replace_string(String* str, String* oldValue, String* newValue) {
    if (!str || !oldValue || oldValue->length == 0) return str;
    if (!newValue) newValue = string_create_utf8("");

    // Count occurrences
    std::vector<Int32> positions;
    for (Int32 i = 0; i <= str->length - oldValue->length; i++) {
        if (std::memcmp(str->chars + i, oldValue->chars, oldValue->length * sizeof(Char)) == 0) {
            positions.push_back(i);
            i += oldValue->length - 1;
        }
    }
    if (positions.empty()) return str;

    Int32 newLen = str->length + static_cast<Int32>(positions.size()) * (newValue->length - oldValue->length);
    String* result = string_fast_allocate(newLen);
    Int32 srcIdx = 0, dstIdx = 0;
    for (Int32 pos : positions) {
        Int32 copyLen = pos - srcIdx;
        if (copyLen > 0) {
            std::memcpy(result->chars + dstIdx, str->chars + srcIdx, copyLen * sizeof(Char));
            dstIdx += copyLen;
        }
        std::memcpy(result->chars + dstIdx, newValue->chars, newValue->length * sizeof(Char));
        dstIdx += newValue->length;
        srcIdx = pos + oldValue->length;
    }
    Int32 remaining = str->length - srcIdx;
    if (remaining > 0) {
        std::memcpy(result->chars + dstIdx, str->chars + srcIdx, remaining * sizeof(Char));
    }
    return result;
}

String* string_remove(String* str, Int32 startIndex) {
    if (!str || startIndex < 0 || startIndex >= str->length) return str;
    return string_create_utf16(str->chars, startIndex);
}

String* string_remove(String* str, Int32 startIndex, Int32 count) {
    if (!str || startIndex < 0 || count < 0 || startIndex + count > str->length) return str;
    Int32 newLen = str->length - count;
    String* result = string_fast_allocate(newLen);
    std::memcpy(result->chars, str->chars, startIndex * sizeof(Char));
    std::memcpy(result->chars + startIndex, str->chars + startIndex + count,
                (str->length - startIndex - count) * sizeof(Char));
    return result;
}

String* string_insert(String* str, Int32 startIndex, String* value) {
    if (!str || !value) return str;
    if (startIndex < 0 || startIndex > str->length) return str;
    Int32 newLen = str->length + value->length;
    String* result = string_fast_allocate(newLen);
    std::memcpy(result->chars, str->chars, startIndex * sizeof(Char));
    std::memcpy(result->chars + startIndex, value->chars, value->length * sizeof(Char));
    std::memcpy(result->chars + startIndex + value->length,
                str->chars + startIndex, (str->length - startIndex) * sizeof(Char));
    return result;
}

String* string_pad_left(String* str, Int32 totalWidth) {
    if (!str || totalWidth <= str->length) return str;
    Int32 padCount = totalWidth - str->length;
    String* result = string_fast_allocate(totalWidth);
    for (Int32 i = 0; i < padCount; i++) result->chars[i] = ' ';
    std::memcpy(result->chars + padCount, str->chars, str->length * sizeof(Char));
    return result;
}

String* string_pad_right(String* str, Int32 totalWidth) {
    if (!str || totalWidth <= str->length) return str;
    String* result = string_fast_allocate(totalWidth);
    std::memcpy(result->chars, str->chars, str->length * sizeof(Char));
    for (Int32 i = str->length; i < totalWidth; i++) result->chars[i] = ' ';
    return result;
}

// ── Concat with Object ────────────────────────────────────

static String* obj_to_string(Object* obj) {
    if (!obj) return string_create_utf8("");
    // If the object IS a String, return it directly (don't return type name)
    if (obj->__type_info && obj->__type_info == &System::String_TypeInfo) {
        return reinterpret_cast<String*>(obj);
    }
    return object_to_string(obj);
}

String* string_concat_obj(Object* a, Object* b) {
    return string_concat(obj_to_string(a), obj_to_string(b));
}

String* string_concat_obj3(Object* a, Object* b, Object* c) {
    return string_concat(string_concat(obj_to_string(a), obj_to_string(b)), obj_to_string(c));
}

// ── Format ────────────────────────────────────────────────

String* string_format(String* format, Array* args) {
    if (!format) return nullptr;

    // Convert each arg to string
    Int32 argCount = args ? args->length : 0;
    std::vector<String*> argStrings(argCount);
    for (Int32 i = 0; i < argCount; i++) {
        Object* obj = static_cast<Object**>(array_data(args))[i];
        argStrings[i] = obj_to_string(obj);
    }

    // Parse format string and build result
    std::vector<Char> result;
    result.reserve(format->length * 2);

    for (Int32 i = 0; i < format->length; i++) {
        Char c = format->chars[i];

        // Escaped braces {{ and }}
        if (c == '{' && i + 1 < format->length && format->chars[i + 1] == '{') {
            result.push_back('{');
            i++;
            continue;
        }
        if (c == '}' && i + 1 < format->length && format->chars[i + 1] == '}') {
            result.push_back('}');
            i++;
            continue;
        }

        if (c == '{') {
            // Parse {index} or {index:format} or {index,alignment}
            i++;
            Int32 index = 0;
            while (i < format->length && format->chars[i] >= '0' && format->chars[i] <= '9') {
                index = index * 10 + (format->chars[i] - '0');
                i++;
            }
            // Skip format specifier and alignment (consume until '}')
            while (i < format->length && format->chars[i] != '}') i++;

            if (index >= 0 && index < argCount && argStrings[index]) {
                String* s = argStrings[index];
                for (Int32 j = 0; j < s->length; j++)
                    result.push_back(s->chars[j]);
            }
        } else {
            result.push_back(c);
        }
    }

    return string_create_utf16(result.data(), static_cast<Int32>(result.size()));
}

// ── Join ──────────────────────────────────────────────────

String* string_join(String* separator, Array* values) {
    if (!values || values->length == 0) return string_create_utf8("");

    Int32 count = values->length;
    String** strings = static_cast<String**>(array_data(values));

    // Calculate total length
    Int32 totalLen = 0;
    for (Int32 i = 0; i < count; i++) {
        if (strings[i]) totalLen += strings[i]->length;
        if (i > 0 && separator) totalLen += separator->length;
    }

    String* result = string_fast_allocate(totalLen);
    Int32 pos = 0;
    for (Int32 i = 0; i < count; i++) {
        if (i > 0 && separator) {
            std::memcpy(result->chars + pos, separator->chars, separator->length * sizeof(Char));
            pos += separator->length;
        }
        if (strings[i]) {
            std::memcpy(result->chars + pos, strings[i]->chars, strings[i]->length * sizeof(Char));
            pos += strings[i]->length;
        }
    }
    return result;
}

// ── Split ─────────────────────────────────────────────────

Array* string_split(String* str, Char separator) {
    if (!str || str->length == 0) {
        return array_create(&System::String_TypeInfo, 0);
    }

    // Count segments
    Int32 count = 1;
    for (Int32 i = 0; i < str->length; i++) {
        if (str->chars[i] == separator) count++;
    }

    Array* result = array_create(&System::String_TypeInfo, count);
    String** data = static_cast<String**>(array_data(result));

    Int32 start = 0, idx = 0;
    for (Int32 i = 0; i <= str->length; i++) {
        if (i == str->length || str->chars[i] == separator) {
            data[idx++] = string_create_utf16(str->chars + start, i - start);
            start = i + 1;
        }
    }
    return result;
}

} // namespace cil2cpp
