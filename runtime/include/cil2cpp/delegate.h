/**
 * CIL2CPP Runtime - Delegate Support
 * Delegates are first-class function objects in .NET.
 */

#pragma once

#include "object.h"

namespace cil2cpp {

// Forward declaration
struct Array;

/**
 * Delegate object layout.
 * Extends Object with target and method pointer.
 * Corresponds to System.Delegate in .NET.
 */
struct Delegate : Object {
    Object* target;          // 'this' for instance delegates, nullptr for static
    void* method_ptr;        // Function pointer to the target method
    Array* invocation_list;  // nullptr for single-cast, Array of Delegate* for multicast
    Int32 invocation_count;  // 0 for single-cast, >0 for multicast
};

/**
 * Create a new delegate instance.
 */
Delegate* delegate_create(TypeInfo* type, Object* target, void* method_ptr);

/**
 * Combine two delegates into a multicast delegate.
 * Corresponds to System.Delegate.Combine().
 */
Object* delegate_combine(Object* a, Object* b);

/**
 * Remove a delegate from a multicast delegate.
 * Corresponds to System.Delegate.Remove().
 */
Object* delegate_remove(Object* source, Object* value);

/**
 * Get the invocation list of a delegate.
 * For single-cast delegates, returns the delegate itself.
 * For multicast, returns the array of delegates.
 */
Int32 delegate_get_invocation_count(Delegate* del);
Delegate* delegate_get_invocation_item(Delegate* del, Int32 index);

} // namespace cil2cpp
