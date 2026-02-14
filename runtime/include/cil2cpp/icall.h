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

// System.Threading.Monitor
void Monitor_Enter(Object* obj);
void Monitor_Exit(Object* obj);
void Monitor_ReliableEnter(Object* obj, bool* lockTaken);
bool Monitor_Wait(Object* obj, Int32 timeout_ms);
void Monitor_Pulse(Object* obj);
void Monitor_PulseAll(Object* obj);

// System.Threading.Interlocked
Int32 Interlocked_Increment_i32(Int32* location);
Int32 Interlocked_Decrement_i32(Int32* location);
Int32 Interlocked_Exchange_i32(Int32* location, Int32 value);
Int32 Interlocked_CompareExchange_i32(Int32* location, Int32 value, Int32 comparand);
Int32 Interlocked_Add_i32(Int32* location, Int32 value);
Int64 Interlocked_Increment_i64(Int64* location);
Int64 Interlocked_Decrement_i64(Int64* location);
Int64 Interlocked_Exchange_i64(Int64* location, Int64 value);
Int64 Interlocked_CompareExchange_i64(Int64* location, Int64 value, Int64 comparand);
Object* Interlocked_Exchange_obj(Object** location, Object* value);
Object* Interlocked_CompareExchange_obj(Object** location, Object* value, Object* comparand);

// System.Threading.Thread
void Thread_Sleep(Int32 milliseconds);

// System.Runtime.CompilerServices.RuntimeHelpers
void RuntimeHelpers_InitializeArray(Object* array, void* fieldHandle);
bool RuntimeHelpers_IsReferenceOrContainsReferences();

} // namespace icall
} // namespace cil2cpp
