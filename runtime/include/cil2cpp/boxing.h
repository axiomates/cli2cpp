/**
 * CIL2CPP Runtime - Boxing/Unboxing
 * Converts between value types and heap-allocated objects.
 */

#pragma once

#include "object.h"
#include "gc.h"
#include "exception.h"
#include <cstring>

namespace cil2cpp {

/**
 * Box a value type. Allocates on GC heap.
 * Layout: [Object header] [value data]
 */
template<typename T>
inline Object* box(T value, TypeInfo* type) {
    Object* obj = static_cast<Object*>(gc::alloc(sizeof(Object) + sizeof(T), type));
    *reinterpret_cast<T*>(reinterpret_cast<char*>(obj) + sizeof(Object)) = value;
    return obj;
}

/**
 * Unbox: extract value from boxed object (unbox.any).
 * Returns a copy of the value.
 */
template<typename T>
inline T unbox(Object* obj) {
    if (!obj) throw_null_reference();
    return *reinterpret_cast<T*>(reinterpret_cast<char*>(obj) + sizeof(Object));
}

/**
 * Unbox: get pointer to value inside boxed object (unbox).
 * Returns a pointer to the in-place value.
 */
template<typename T>
inline T* unbox_ptr(Object* obj) {
    if (!obj) throw_null_reference();
    return reinterpret_cast<T*>(reinterpret_cast<char*>(obj) + sizeof(Object));
}

/**
 * Box a value type from a raw pointer (used for constrained callvirt on value types).
 * @param value_ptr Pointer to the value data
 * @param value_size Size of the value data in bytes
 * @param type TypeInfo for the value type
 */
inline Object* box_raw(const void* value_ptr, size_t value_size, TypeInfo* type) {
    Object* obj = static_cast<Object*>(gc::alloc(sizeof(Object) + value_size, type));
    std::memcpy(reinterpret_cast<char*>(obj) + sizeof(Object), value_ptr, value_size);
    return obj;
}

} // namespace cil2cpp
