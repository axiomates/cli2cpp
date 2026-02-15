/**
 * CIL2CPP Runtime - Async Enumerable Support
 * Thread-local for ManualResetValueTaskSourceCore <-> ValueTask bridge.
 */

#include <cil2cpp/async_enumerable.h>

namespace cil2cpp {

thread_local Task* g_async_iter_current_task = nullptr;

} // namespace cil2cpp
