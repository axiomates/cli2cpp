/**
 * CIL2CPP Runtime - System.Delegate implementation
 */

#include <cil2cpp/delegate.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/type_info.h>

namespace cil2cpp {

Delegate* delegate_create(TypeInfo* type, Object* target, void* method_ptr) {
    auto* del = static_cast<Delegate*>(gc::alloc(sizeof(Delegate), type));
    del->target = target;
    del->method_ptr = method_ptr;
    return del;
}

Object* delegate_combine(Object* a, Object* b) {
    // Phase 3: single-cast only — return the newer delegate
    if (b != nullptr) return b;
    return a;
}

Object* delegate_remove(Object* source, Object* value) {
    if (source == nullptr || value == nullptr) return source;

    auto* src = static_cast<Delegate*>(source);
    auto* val = static_cast<Delegate*>(value);

    // Single-cast: if method_ptr matches, remove it
    if (src->method_ptr == val->method_ptr && src->target == val->target)
        return nullptr;

    return source;
}

} // namespace cil2cpp
