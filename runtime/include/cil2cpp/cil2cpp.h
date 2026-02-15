/**
 * CIL2CPP Runtime Library
 * Main header file - includes all runtime components
 */

#pragma once

#include "types.h"
#include "object.h"
#include "string.h"
#include "array.h"
#include "mdarray.h"
#include "stackalloc.h"
#include "gc.h"
#include "exception.h"
#include "type_info.h"
#include "boxing.h"
#include "delegate.h"
#include "icall.h"
#include "checked.h"
#include "task.h"
#include "threadpool.h"
#include "cancellation.h"
#include "async_enumerable.h"
#include "threading.h"
#include "reflection.h"
#include "memberinfo.h"
#include "collections.h"

// BCL types
#include "bcl/System.Object.h"
#include "bcl/System.String.h"
#include "bcl/System.Console.h"
#include "bcl/System.IO.h"

namespace cil2cpp {

/**
 * Initialize the CIL2CPP runtime.
 * Must be called before any other runtime functions.
 */
void runtime_init();

/**
 * Shutdown the CIL2CPP runtime.
 * Performs final GC and cleanup.
 */
void runtime_shutdown();

} // namespace cil2cpp

// Math helpers for Sign (no std:: equivalent)
namespace cil2cpp {
inline int32_t math_sign_i32(int32_t x) { return (x > 0) - (x < 0); }
inline int32_t math_sign_i64(int64_t x) { return (x > 0) - (x < 0); }
inline int32_t math_sign_f64(double x) { return (x > 0.0) - (x < 0.0); }
} // namespace cil2cpp

// System.Object methods (used by generated code)
void System_Object__ctor(void* obj);
inline void System_Object_Finalize(void*) {} // Finalize is a no-op for System.Object

// Entry point macro for generated code
#define CIL2CPP_MAIN(EntryClass, EntryMethod) \
    int main(int argc, char* argv[]) { \
        cil2cpp::runtime_init(); \
        EntryMethod(); \
        cil2cpp::runtime_shutdown(); \
        return 0; \
    }
