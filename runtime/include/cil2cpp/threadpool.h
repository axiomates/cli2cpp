/**
 * CIL2CPP Runtime - Thread Pool
 * Fixed-size worker pool for async task execution.
 */

#pragma once

#include "types.h"

namespace cil2cpp {

struct Object;
struct Delegate;
struct Task;

namespace threadpool {

/**
 * Initialize the thread pool.
 * @param num_threads Number of worker threads (0 = hardware_concurrency)
 */
void init(int num_threads = 0);

/** Shutdown the thread pool (waits for pending work to complete). */
void shutdown();

/** Check if thread pool is initialized. */
bool is_initialized();

/**
 * Queue a work item (C function pointer + state).
 * The function will be called on a worker thread.
 */
void queue_work(void (*func)(void*), void* state);

/**
 * Queue a delegate invocation on the thread pool.
 * For Task.Run(() => ...) support.
 */
void queue_delegate(Delegate* del);

} // namespace threadpool
} // namespace cil2cpp
