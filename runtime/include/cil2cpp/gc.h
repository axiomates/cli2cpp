/**
 * CIL2CPP Runtime - Garbage Collector (BoehmGC wrapper)
 */

#pragma once

#include "types.h"

namespace cil2cpp {
namespace gc {

/**
 * GC configuration options.
 * BoehmGC manages heap sizing automatically; this struct is kept
 * for API compatibility.
 */
struct GCConfig {};

/**
 * Initialize the garbage collector.
 */
void init(const GCConfig& config = GCConfig{});

/**
 * Shutdown the garbage collector.
 */
void shutdown();

/**
 * Allocate memory for a managed object.
 * @param size Size in bytes to allocate
 * @param type Type information for the object
 * @return Pointer to allocated memory, or nullptr on failure
 */
void* alloc(size_t size, TypeInfo* type);

/**
 * Allocate memory for an array.
 * @param element_type Type of array elements
 * @param length Number of elements
 * @return Pointer to allocated array
 */
void* alloc_array(TypeInfo* element_type, size_t length);

/**
 * Trigger a full garbage collection cycle.
 */
void collect();

/**
 * Enable or disable incremental garbage collection.
 * Incremental mode spreads collection work across multiple small steps,
 * reducing pause times. Enabled by default at init.
 * Note: BoehmGC does not support disabling incremental mode once enabled.
 */
void set_incremental(bool enabled);

/**
 * Perform a small amount of incremental collection work.
 * @return true if more work remains, false if collection is complete.
 */
bool collect_a_little();

/**
 * Register the current thread with the GC.
 * Must be called at the start of every managed thread (except the main thread).
 * Required for BoehmGC to scan the thread's stack for roots.
 */
void register_thread();

/**
 * Unregister the current thread from the GC.
 * Must be called before a managed thread exits.
 */
void unregister_thread();

/**
 * Add a root reference (no-op -BoehmGC scans roots automatically).
 */
void add_root(void** root);

/**
 * Remove a root reference (no-op -BoehmGC scans roots automatically).
 */
void remove_root(void** root);

/**
 * Write barrier (no-op -BoehmGC does not require write barriers).
 */
inline void write_barrier(Object* obj, Object* value) {
    (void)obj;
    (void)value;
}

/**
 * GC statistics.
 */
struct GCStats {
    size_t total_allocated;
    size_t total_freed;
    size_t current_heap_size;
    size_t collection_count;
    double total_pause_time_ms;
};

/**
 * Get current GC statistics.
 */
GCStats get_stats();

} // namespace gc
} // namespace cil2cpp
