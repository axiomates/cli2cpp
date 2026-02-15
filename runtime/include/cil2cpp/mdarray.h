/**
 * CIL2CPP Runtime - Multi-Dimensional Array Type
 * Corresponds to T[,], T[,,], etc. in C#.
 *
 * MdArray is separate from Array (1D). The __sync_block MDARRAY_FLAG
 * bit distinguishes them at runtime so System.Array ICall functions
 * (get_Rank, GetLength, get_Length) can dispatch correctly.
 */

#pragma once

#include "object.h"

namespace cil2cpp {

/// Bit 31 of __sync_block marks the object as a multi-dimensional array.
constexpr UInt32 MDARRAY_FLAG = 0x80000000u;

/// Check if an Object* is a multi-dimensional array.
inline bool is_mdarray(Object* obj) {
    return obj && (obj->__sync_block & MDARRAY_FLAG) != 0;
}

/**
 * Multi-dimensional array (rank >= 2).
 * Memory layout (contiguous allocation):
 *   MdArray header
 *   Int32 lengths[rank]
 *   Int32 lower_bounds[rank]   (always 0 for C# T[,])
 *   char  data[total_length * element_size]
 */
struct MdArray : Object {
    TypeInfo* element_type;
    Int32 rank;
    Int32 total_length;
};

/// Get pointer to the per-dimension lengths array (immediately after the struct).
inline Int32* mdarray_lengths(MdArray* arr) {
    return reinterpret_cast<Int32*>(reinterpret_cast<char*>(arr) + sizeof(MdArray));
}

/// Get pointer to the per-dimension lower-bounds array (after lengths).
inline Int32* mdarray_lower_bounds(MdArray* arr) {
    return mdarray_lengths(arr) + arr->rank;
}

/// Get pointer to element data (after lower_bounds).
inline void* mdarray_data(MdArray* arr) {
    return reinterpret_cast<char*>(mdarray_lower_bounds(arr) + arr->rank);
}

/**
 * Create a multi-dimensional array.
 * @param element_type  Element TypeInfo
 * @param rank          Number of dimensions (>= 2)
 * @param lengths       Array of dimension lengths (rank elements)
 */
MdArray* mdarray_create(TypeInfo* element_type, Int32 rank, const Int32* lengths);

/**
 * Bounds-check indices and return a pointer to the element.
 * Throws IndexOutOfRangeException on failure.
 */
void* mdarray_get_element_ptr(MdArray* arr, const Int32* indices);

/**
 * Get the length of a specific dimension.
 */
Int32 mdarray_get_length(MdArray* arr, Int32 dimension);

/**
 * Get the total element count.
 */
inline Int32 mdarray_get_total_length(MdArray* arr) {
    return arr ? arr->total_length : 0;
}

/**
 * Get the rank (number of dimensions).
 */
inline Int32 mdarray_get_rank(MdArray* arr) {
    return arr ? arr->rank : 0;
}

} // namespace cil2cpp
