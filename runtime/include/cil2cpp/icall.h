/**
 * CIL2CPP Runtime - Internal Call Declarations
 *
 * C++ implementations for [InternalCall] methods in the .NET BCL.
 * These are called by generated code when the compiler encounters
 * [MethodImpl(MethodImplOptions.InternalCall)] methods.
 */

#pragma once

#include "types.h"
#include "object.h"
#include "string.h"

namespace cil2cpp {
namespace icall {

// System.Environment
String* Environment_get_NewLine();
Int32 Environment_get_TickCount();
Int64 Environment_get_TickCount64();
Int32 Environment_get_ProcessorCount();

// System.Buffer
void Buffer_Memmove(void* dest, void* src, UInt64 len);
void Buffer_BlockCopy(Object* src, Int32 srcOffset, Object* dst, Int32 dstOffset, Int32 count);

// System.Type
Object* Type_GetTypeFromHandle(void* handle);

// System.Threading.Monitor (single-threaded stubs)
void Monitor_Enter(Object* obj);
void Monitor_Exit(Object* obj);
void Monitor_ReliableEnter(Object* obj, bool* lockTaken);

// System.Runtime.CompilerServices.RuntimeHelpers
void RuntimeHelpers_InitializeArray(Object* array, void* fieldHandle);
bool RuntimeHelpers_IsReferenceOrContainsReferences();

} // namespace icall
} // namespace cil2cpp
