/**
 * CIL2CPP Runtime - Async Task Types
 * Synchronous execution model: all Tasks complete immediately.
 */

#pragma once

#include "object.h"
#include "exception.h"

namespace cil2cpp {

/**
 * Task (reference type, GC-allocated).
 * Non-generic base; generic Task<T> is monomorphized by the compiler.
 */
struct Task : Object {
    Int32 f_status;         // 0=running, 1=completed, 2=faulted
    Exception* f_exception;
};

/**
 * TaskAwaiter (value type, stack-allocated).
 */
struct TaskAwaiter {
    Task* f_task;
};

/**
 * AsyncTaskMethodBuilder (value type, stack-allocated).
 */
struct AsyncTaskMethodBuilder {
    Task* f_task;
};

/**
 * Create a new completed Task (GC-allocated).
 */
Task* task_create_completed();

/**
 * Get or create a singleton completed Task (cached).
 */
Task* task_get_completed();

/**
 * Check if a Task has completed (status >= 1).
 */
inline bool task_is_completed(Task* t) {
    return t != nullptr && t->f_status >= 1;
}

} // namespace cil2cpp

// Mangled-name aliases for generated code
using System_Threading_Tasks_Task = cil2cpp::Task;
using System_Runtime_CompilerServices_TaskAwaiter = cil2cpp::TaskAwaiter;
using System_Runtime_CompilerServices_AsyncTaskMethodBuilder = cil2cpp::AsyncTaskMethodBuilder;

// IAsyncStateMachine interface (used as parameter type in SetStateMachine)
using System_Runtime_CompilerServices_IAsyncStateMachine = cil2cpp::Object;
