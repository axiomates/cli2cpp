#include <cil2cpp/task.h>
#include <cil2cpp/gc.h>
#include <cil2cpp/type_info.h>

namespace cil2cpp {

static TypeInfo Task_TypeInfo_Internal = {
    .name = "Task",
    .namespace_name = "System.Threading.Tasks",
    .full_name = "System.Threading.Tasks.Task",
    .base_type = nullptr,
    .interfaces = nullptr,
    .interface_count = 0,
    .instance_size = sizeof(Task),
    .element_size = 0,
    .flags = TypeFlags::None,
    .vtable = nullptr,
    .fields = nullptr,
    .field_count = 0,
    .methods = nullptr,
    .method_count = 0,
    .default_ctor = nullptr,
    .finalizer = nullptr,
    .interface_vtables = nullptr,
    .interface_vtable_count = 0,
};

static Task* s_completed_task = nullptr;

Task* task_create_completed() {
    auto* t = static_cast<Task*>(gc::alloc(sizeof(Task), &Task_TypeInfo_Internal));
    t->f_status = 1; // RanToCompletion
    t->f_exception = nullptr;
    return t;
}

Task* task_get_completed() {
    if (!s_completed_task) {
        s_completed_task = task_create_completed();
    }
    return s_completed_task;
}

} // namespace cil2cpp
