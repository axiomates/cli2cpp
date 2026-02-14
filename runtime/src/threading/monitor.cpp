/**
 * CIL2CPP Runtime - Monitor Implementation
 *
 * ECMA-335 compliant sync block table approach:
 * - Each Object has __sync_block (uint32_t), initially 0
 * - Global table maps sync block indices → recursive_mutex + condition_variable
 * - Reentrant locks (same thread can acquire multiple times)
 * - Thread-safe slot allocation via atomic CAS
 */

#include <cil2cpp/threading.h>
#include <cil2cpp/exception.h>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <mutex>
#include <vector>

namespace cil2cpp {
namespace monitor {

struct SyncBlock {
    std::recursive_mutex mutex;
    std::condition_variable_any condvar;
};

// Global sync block table — slot 0 is unused (0 means "no sync block")
static std::vector<SyncBlock*> g_sync_table;
static std::mutex g_table_lock;
static std::atomic<uint32_t> g_next_index{1};

/**
 * Get or allocate a sync block for an object.
 * Uses atomic CAS on __sync_block for thread-safe allocation.
 */
static SyncBlock* get_sync_block(Object* obj) {
    // Fast path: sync block already assigned
    auto* slot = reinterpret_cast<std::atomic<uint32_t>*>(&obj->__sync_block);
    uint32_t index = slot->load(std::memory_order_acquire);
    if (index != 0) {
        std::lock_guard<std::mutex> guard(g_table_lock);
        return g_sync_table[index];
    }

    // Slow path: allocate a new sync block
    uint32_t new_index = g_next_index.fetch_add(1, std::memory_order_relaxed);
    auto* block = new SyncBlock();

    {
        std::lock_guard<std::mutex> guard(g_table_lock);
        if (g_sync_table.size() <= new_index) {
            g_sync_table.resize(new_index + 1, nullptr);
        }
        g_sync_table[new_index] = block;
    }

    // Try to assign our new index; another thread may have beaten us
    uint32_t expected = 0;
    if (slot->compare_exchange_strong(expected, new_index, std::memory_order_acq_rel)) {
        return block;
    }

    // Another thread assigned first — use theirs, discard ours
    {
        std::lock_guard<std::mutex> guard(g_table_lock);
        g_sync_table[new_index] = nullptr;
    }
    delete block;

    // Use the winner's sync block
    std::lock_guard<std::mutex> guard(g_table_lock);
    return g_sync_table[expected];
}

void enter(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
    block->mutex.lock();
}

void exit(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
    block->mutex.unlock();
}

void reliable_enter(Object* obj, bool* lockTaken) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
    block->mutex.lock();
    if (lockTaken) *lockTaken = true;
}

bool wait(Object* obj, Int32 timeout_ms) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);

    if (timeout_ms < 0) {
        // Infinite wait
        block->condvar.wait(block->mutex);
        return true;
    }

    auto status = block->condvar.wait_for(
        block->mutex,
        std::chrono::milliseconds(timeout_ms)
    );
    return status == std::cv_status::no_timeout;
}

void pulse(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
    block->condvar.notify_one();
}

void pulse_all(Object* obj) {
    if (!obj) throw_null_reference();
    auto* block = get_sync_block(obj);
    block->condvar.notify_all();
}

} // namespace monitor
} // namespace cil2cpp
