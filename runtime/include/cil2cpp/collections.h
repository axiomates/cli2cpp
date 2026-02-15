/**
 * CIL2CPP Runtime - Generic Collections
 * List<T> and Dictionary<K,V> runtime support.
 */

#pragma once

#include "object.h"
#include "array.h"

namespace cil2cpp {

// ===== List<T> =====

/**
 * Base struct for all List<T> specializations.
 * Memory layout must match the compiler-generated synthetic fields.
 * Fields: _items (Array*), _size (Int32), _version (Int32), _elemType (TypeInfo*)
 */
struct ListBase : Object {
    void* items;            // GC-allocated backing buffer (raw bytes)
    Int32 count;            // Current element count
    Int32 version;          // Modification counter (enumerator invalidation)
    TypeInfo* elem_type;    // Element type info
    Int32 capacity;         // Current buffer capacity (element count)
};

/**
 * Create a new list.
 * @param list_type TypeInfo for the List<T> specialization (set on __type_info)
 * @param elem_type TypeInfo for element T (used for backing array creation)
 * @param capacity Initial capacity (0 = no allocation until first add)
 * @return Allocated ListBase* (caller casts to generated type)
 */
void* list_create(TypeInfo* list_type, TypeInfo* elem_type, Int32 capacity);

/**
 * Add an element. Copies elem_size bytes from element_ptr.
 * Grows backing array if needed (doubles capacity, minimum 4).
 */
void list_add(void* list, const void* element_ptr);

/**
 * Get pointer to element at index (bounds-checked).
 * For value types: returns T*, dereference to get T.
 * For reference types: returns Object**, dereference to get Object*.
 */
void* list_get_ref(void* list, Int32 index);

/**
 * Set element at index (bounds-checked). Copies elem_size bytes.
 */
void list_set(void* list, Int32 index, const void* element_ptr);

/** Get current element count. */
Int32 list_get_count(void* list);

/** Remove element at index, shifting remaining elements. */
void list_remove_at(void* list, Int32 index);

/** Clear all elements (count=0, keeps capacity). */
void list_clear(void* list);

/** Check if list contains element (uses vtable Equals for ref types, memcmp for value types). */
Boolean list_contains(void* list, const void* element_ptr);

/** Find index of element (-1 if not found). */
Int32 list_index_of(void* list, const void* element_ptr);

/** Insert element at index, shifting elements right. */
void list_insert(void* list, Int32 index, const void* element_ptr);

/** Remove first occurrence of element, returns true if found. */
Boolean list_remove(void* list, const void* element_ptr);

/** Get backing array capacity. */
Int32 list_get_capacity(void* list);

// ===== Dictionary<K,V> =====

/**
 * Base struct for all Dictionary<K,V> specializations.
 * Uses separate chaining hash table with packed entries.
 */
struct DictBase : Object {
    Array* buckets;         // Int32[] — bucket head indices (-1 = empty)
    void* entries;          // Packed entry data (GC-allocated byte block)
    Int32 count;            // Number of active entries (includes free slots)
    Int32 capacity;         // Current entry capacity
    Int32 free_list;        // Head of free entry chain (-1 = none)
    Int32 free_count;       // Number of free entries
    TypeInfo* key_type;     // Key element type info
    TypeInfo* value_type;   // Value element type info
    Int32 key_size;         // Cached: sizeof(K) or sizeof(void*) for ref types
    Int32 value_size;       // Cached: sizeof(V) or sizeof(void*) for ref types
    Int32 entry_stride;     // 8 + key_size + value_size (per-entry total size)
};

/**
 * Create a new dictionary.
 * @param dict_type TypeInfo for the Dictionary<K,V> specialization
 * @param key_type TypeInfo for key K
 * @param value_type TypeInfo for value V
 * @return Allocated DictBase*
 */
void* dict_create(TypeInfo* dict_type, TypeInfo* key_type, TypeInfo* value_type);

/**
 * Set a key-value pair (add or update).
 * @param key Pointer to key data
 * @param value Pointer to value data
 */
void dict_set(void* dict, const void* key, const void* value);

/**
 * Get pointer to value for key. Throws KeyNotFoundException if not found.
 * Returns void* pointing to value storage (dereference for value/ref types).
 */
void* dict_get_ref(void* dict, const void* key);

/**
 * Try to get value for key. Copies value to value_out if found.
 * @return true if key was found
 */
Boolean dict_try_get_value(void* dict, const void* key, void* value_out);

/** Check if key exists. */
Boolean dict_contains_key(void* dict, const void* key);

/** Remove entry by key. Returns true if found. */
Boolean dict_remove(void* dict, const void* key);

/** Get number of entries (count - free_count). */
Int32 dict_get_count(void* dict);

/** Clear all entries. */
void dict_clear(void* dict);

// ===== Element comparison helpers =====

/** Compare two elements using vtable Equals for ref types, memcmp for value types. */
Boolean element_equals(const void* a, const void* b, TypeInfo* type);

/** Hash an element using vtable GetHashCode for ref types, FNV-1a for value types. */
Int32 element_hash(const void* element, TypeInfo* type);

} // namespace cil2cpp
