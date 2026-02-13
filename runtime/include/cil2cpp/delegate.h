/**
 * CIL2CPP Runtime - Delegate Support
 * Delegates are first-class function objects in .NET.
 */

#pragma once

#include "object.h"

namespace cil2cpp {

/**
 * Delegate object layout.
 * Extends Object with target and method pointer.
 * Corresponds to System.Delegate in .NET.
 */
struct Delegate : Object {
    Object* target;      // 'this' for instance delegates, nullptr for static
    void* method_ptr;    // Function pointer to the target method
};

/**
 * Create a new delegate instance.
 * @param type TypeInfo for the delegate type
 * @param target Target object (nullptr for static methods)
 * @param method_ptr Function pointer to the target method
 * @return Pointer to the new Delegate object
 */
Delegate* delegate_create(TypeInfo* type, Object* target, void* method_ptr);

/**
 * Combine two delegates into a multicast delegate.
 * Corresponds to System.Delegate.Combine().
 * Note: Phase 3 only supports single-cast delegates; returns 'b' for now.
 * @param a First delegate (or nullptr)
 * @param b Second delegate (or nullptr)
 * @return Combined delegate
 */
Object* delegate_combine(Object* a, Object* b);

/**
 * Remove a delegate from a multicast delegate.
 * Corresponds to System.Delegate.Remove().
 * Note: Phase 3 only supports single-cast delegates; returns nullptr if match.
 * @param source Source delegate
 * @param value Delegate to remove
 * @return Remaining delegate, or nullptr
 */
Object* delegate_remove(Object* source, Object* value);

} // namespace cil2cpp
