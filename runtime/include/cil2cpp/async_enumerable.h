/**
 * CIL2CPP Runtime - Async Enumerable Support
 * ValueTask, ValueTask<T>, AsyncIteratorMethodBuilder types.
 *
 * IAsyncEnumerable<T> / IAsyncEnumerator<T> state machines compile from IL.
 * This header provides the infrastructure types they reference.
 */

#pragma once

#include "object.h"
#include "task.h"

namespace cil2cpp {

// ── Thread-local for async iterator promise -> ValueTask bridge ──
// ManualResetValueTaskSourceCore.Reset() stores the pending Task here.
// ValueTask<bool>.ctor(IValueTaskSource, short) picks it up.
// This is safe because the sequence Reset -> MoveNext -> ctor is synchronous
// within a single MoveNextAsync() call on one thread.
extern thread_local Task* g_async_iter_current_task;

// ── Non-generic ValueTask (for DisposeAsync) ──
struct ValueTaskVoid {
    Task* f_task;   // null = default/completed
};

// ── Non-generic ValueTaskAwaiter ──
struct ValueTaskAwaiterVoid {
    Task* f_task;   // null = already completed
};

// ── AsyncIteratorMethodBuilder ──
struct AsyncIteratorMethodBuilder {
    Int32 f_dummy;  // placeholder (builder is stateless in our impl)
};

} // namespace cil2cpp

// Mangled-name aliases for generated code (non-generic types)
using System_Threading_Tasks_ValueTask = cil2cpp::ValueTaskVoid;
using System_Runtime_CompilerServices_ValueTaskAwaiter = cil2cpp::ValueTaskAwaiterVoid;
using System_Runtime_CompilerServices_AsyncIteratorMethodBuilder = cil2cpp::AsyncIteratorMethodBuilder;
