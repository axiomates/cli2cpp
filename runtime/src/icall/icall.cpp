/**
 * CIL2CPP Runtime - Internal Call Implementations
 *
 * C++ implementations for BCL [InternalCall] methods.
 */

#include <cil2cpp/icall.h>
#include <cil2cpp/string.h>
#include <cil2cpp/array.h>
#include <cil2cpp/type_info.h>
#include <cil2cpp/gc.h>

#include <chrono>
#include <cstring>
#include <thread>

namespace cil2cpp {
namespace icall {

// ===== System.Environment =====

String* Environment_get_NewLine() {
#ifdef _WIN32
    return string_literal("\r\n");
#else
    return string_literal("\n");
#endif
}

Int32 Environment_get_TickCount() {
    auto now = std::chrono::steady_clock::now();
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch());
    return static_cast<Int32>(ms.count());
}

Int64 Environment_get_TickCount64() {
    auto now = std::chrono::steady_clock::now();
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(now.time_since_epoch());
    return static_cast<Int64>(ms.count());
}

Int32 Environment_get_ProcessorCount() {
    auto count = std::thread::hardware_concurrency();
    return count > 0 ? static_cast<Int32>(count) : 1;
}

// ===== System.Buffer =====

void Buffer_Memmove(void* dest, void* src, UInt64 len) {
    if (dest && src && len > 0) {
        std::memmove(dest, src, static_cast<size_t>(len));
    }
}

void Buffer_BlockCopy(Object* src, Int32 srcOffset, Object* dst, Int32 dstOffset, Int32 count) {
    if (!src || !dst || count <= 0) return;
    auto srcBytes = reinterpret_cast<uint8_t*>(array_data(reinterpret_cast<Array*>(src)));
    auto dstBytes = reinterpret_cast<uint8_t*>(array_data(reinterpret_cast<Array*>(dst)));
    std::memmove(dstBytes + dstOffset, srcBytes + srcOffset, static_cast<size_t>(count));
}

// ===== System.Type =====

Object* Type_GetTypeFromHandle(void* handle) {
    // Stub: RuntimeTypeHandle → Type object
    // In a full implementation, this would look up the TypeInfo and create a Type wrapper.
    // For now, return nullptr (not yet needed for basic scenarios).
    (void)handle;
    return nullptr;
}

// ===== System.Threading.Monitor =====
// Single-threaded stubs — no actual locking needed

void Monitor_Enter(Object* obj) {
    (void)obj;
}

void Monitor_Exit(Object* obj) {
    (void)obj;
}

void Monitor_ReliableEnter(Object* obj, bool* lockTaken) {
    (void)obj;
    if (lockTaken) *lockTaken = true;
}

// ===== System.Runtime.CompilerServices.RuntimeHelpers =====

void RuntimeHelpers_InitializeArray(Object* array, void* fieldHandle) {
    // This is typically handled by the compiler via InitializeArray intrinsic,
    // but when called as an icall, we copy the data directly.
    if (!array || !fieldHandle) return;
    auto arr = reinterpret_cast<Array*>(array);
    auto dataPtr = array_data(arr);
    auto length = array_length(arr);
    if (length > 0 && dataPtr && arr->element_type) {
        auto elemSize = arr->element_type->element_size;
        std::memcpy(dataPtr, fieldHandle, static_cast<size_t>(length) * elemSize);
    }
}

bool RuntimeHelpers_IsReferenceOrContainsReferences() {
    // Conservative: assume it may contain references
    return true;
}

} // namespace icall
} // namespace cil2cpp
