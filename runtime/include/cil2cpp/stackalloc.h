/**
 * CIL2CPP Runtime - Stack Allocation (localloc)
 *
 * CIL2CPP_STACKALLOC is a MACRO (not function) because alloca/
 * _alloca must be called from the calling function's stack frame.
 * Wrapping in a function would allocate on the wrapper's frame,
 * which gets deallocated on return.
 */

#pragma once

#ifdef _MSC_VER
  #include <malloc.h>
  #define CIL2CPP_STACKALLOC(size) _alloca(size)
#else
  #include <alloca.h>
  #define CIL2CPP_STACKALLOC(size) alloca(size)
#endif
