using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Async/await BCL type interception (synchronous execution model).
/// Task, TaskAwaiter, AsyncTaskMethodBuilder method calls are intercepted
/// and emitted as inline C++ — same pattern as Nullable/ValueTuple.
/// </summary>
public partial class IRBuilder
{
    // ── Type detection ────────────────────────────────────────

    private static bool IsTaskType(TypeReference typeRef)
    {
        var name = typeRef is GenericInstanceType git
            ? git.ElementType.FullName : typeRef.FullName;
        return name is "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.Task`1";
    }

    private static bool IsTaskAwaiterType(TypeReference typeRef)
    {
        var name = typeRef is GenericInstanceType git
            ? git.ElementType.FullName : typeRef.FullName;
        return name is "System.Runtime.CompilerServices.TaskAwaiter"
            or "System.Runtime.CompilerServices.TaskAwaiter`1";
    }

    private static bool IsAsyncBuilderType(TypeReference typeRef)
    {
        var name = typeRef is GenericInstanceType git
            ? git.ElementType.FullName : typeRef.FullName;
        return name is "System.Runtime.CompilerServices.AsyncTaskMethodBuilder"
            or "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1";
    }

    /// <summary>
    /// Check if an open type name is one of the three async BCL generic types.
    /// Used by CreateGenericSpecializations to allow null CecilOpenType.
    /// </summary>
    internal static bool IsAsyncBclGenericType(string openTypeName)
    {
        return openTypeName.StartsWith("System.Threading.Tasks.Task`")
            || openTypeName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter`")
            || openTypeName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`");
    }

    // ── Synthetic field creation ──────────────────────────────

    /// <summary>
    /// Create synthetic fields for async BCL generic types (Task&lt;T&gt;, TaskAwaiter&lt;T&gt;,
    /// AsyncTaskMethodBuilder&lt;T&gt;) since Cecil cannot resolve their type definitions.
    /// </summary>
    internal List<IRField> CreateAsyncSyntheticFields(
        string openTypeName, IRType irType, Dictionary<string, string> typeParamMap)
    {
        var fields = new List<IRField>();
        var tResult = typeParamMap.GetValueOrDefault("TResult", "System.Int32");

        if (openTypeName.StartsWith("System.Threading.Tasks.Task`"))
        {
            // Task<T>: must match runtime Task struct layout + result field
            // Layout: status, exception, continuations, lock, result
            fields.Add(MakeSyntheticField("status", "System.Int32", irType));
            fields.Add(MakeSyntheticField("exception", "System.Exception", irType));
            fields.Add(MakeSyntheticField("continuations", "System.IntPtr", irType));
            fields.Add(MakeSyntheticField("lock", "System.IntPtr", irType));
            fields.Add(MakeSyntheticField("result", tResult, irType));
        }
        else if (openTypeName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter`"))
        {
            // TaskAwaiter<T>: task (Task<T>*)
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            fields.Add(MakeSyntheticField("task", taskKey, irType));
        }
        else if (openTypeName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`"))
        {
            // AsyncTaskMethodBuilder<T>: task (Task<T>*)
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            fields.Add(MakeSyntheticField("task", taskKey, irType));
        }

        return fields;
    }

    private static IRField MakeSyntheticField(string name, string typeName, IRType declaringType)
    {
        return new IRField
        {
            Name = name,
            CppName = CppNameMapper.MangleFieldName(name),
            FieldTypeName = typeName,
            IsStatic = false,
            IsPublic = true,
            DeclaringType = declaringType,
        };
    }

    // ── Method call interception ──────────────────────────────

    /// <summary>
    /// Handle calls to async BCL types. Returns true if the call was handled.
    /// Called from EmitMethodCall before normal resolution.
    /// </summary>
    private bool TryEmitAsyncCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (IsAsyncBuilderType(methodRef.DeclaringType))
            return TryEmitBuilderCall(block, stack, methodRef, ref tempCounter);
        if (IsTaskAwaiterType(methodRef.DeclaringType))
            return TryEmitAwaiterCall(block, stack, methodRef, ref tempCounter);
        if (IsTaskType(methodRef.DeclaringType))
            return TryEmitTaskCall(block, stack, methodRef, ref tempCounter);
        return false;
    }

    /// <summary>
    /// Handle newobj for async BCL types. Returns true if handled.
    /// </summary>
    private bool TryEmitAsyncNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        // Async types are never constructed via newobj in practice.
        // Builder.Create() is a static factory; Task is GC-allocated inline.
        return false;
    }

    // ── AsyncTaskMethodBuilder interception ───────────────────

    private bool TryEmitBuilderCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        string WrapThis(string raw) => raw.StartsWith("&") ? $"({raw})" : raw;

        var isGeneric = methodRef.DeclaringType is GenericInstanceType;

        switch (methodRef.Name)
        {
            case "Create":
            {
                // Static factory: returns zero-initialized builder with pending task
                var builderType = GetMangledTypeNameForRef(methodRef.DeclaringType);
                var tmp = $"__t{tempCounter++}";

                if (methodRef.DeclaringType is GenericInstanceType builderGit
                    && builderGit.GenericArguments.Count > 0)
                {
                    // Generic builder: allocate correctly-sized Task<T> using gc::alloc
                    // (task_create_pending allocates sizeof(Task) which lacks the f_result field)
                    var taskTypeCpp = ResolveTaskTypeCppName(builderGit.GenericArguments[0]);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{builderType} {tmp} = {{}}; " +
                               $"{tmp}.f_task = static_cast<{taskTypeCpp}*>(cil2cpp::gc::alloc(sizeof({taskTypeCpp}), nullptr)); " +
                               $"cil2cpp::task_init_pending(reinterpret_cast<cil2cpp::Task*>({tmp}.f_task));"
                    });
                }
                else
                {
                    // Non-generic builder: use standard task_create_pending (sizeof(Task) is correct)
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{builderType} {tmp} = {{}}; {tmp}.f_task = cil2cpp::task_create_pending();"
                    });
                }
                stack.Push(tmp);
                return true;
            }
            case "Start":
            {
                // Start<TSM>(ref TSM stateMachine)
                // Stack: [builderAddr, smAddr]
                var smAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var builderAddr = stack.Count > 0 ? stack.Pop() : "nullptr";

                // Extract state machine type from GenericInstanceMethod
                var moveNextName = ResolveMoveNextName(methodRef);
                if (moveNextName != null)
                {
                    // For class state machines (Debug mode), smAddr is &loc_N (pointer to pointer).
                    // MoveNext expects a single pointer. Dereference if needed.
                    var smArg = smAddr;
                    if (methodRef is GenericInstanceMethod gim2 && gim2.GenericArguments.Count > 0)
                    {
                        var smTypeRef = gim2.GenericArguments[0];
                        bool isValueSm = false;
                        try { isValueSm = smTypeRef.Resolve()?.IsValueType == true; }
                        catch { }
                        if (!isValueSm && smArg.StartsWith("&"))
                            smArg = smArg[1..]; // Strip & — pointer variable itself is the correct arg
                    }
                    block.Instructions.Add(new IRCall
                    {
                        FunctionName = moveNextName,
                        Arguments = { smArg },
                    });
                }
                return true;
            }
            case "get_Task":
            {
                // Return builder->f_task
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f_task;"
                });
                stack.Push(tmp);
                return true;
            }
            case "SetResult":
            {
                if (isGeneric && methodRef.Parameters.Count == 1)
                {
                    // SetResult(T result) — generic version stores result, then completes
                    var result = stack.Count > 0 ? stack.Pop() : "0";
                    var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{thisArg}->f_task->f_result = {result}; cil2cpp::task_complete(reinterpret_cast<cil2cpp::Task*>({thisArg}->f_task));"
                    });
                }
                else
                {
                    // SetResult() — non-generic version completes with thread-safe API
                    var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"cil2cpp::task_complete({thisArg}->f_task);"
                    });
                }
                return true;
            }
            case "SetException":
            {
                // SetException(Exception ex) — fault with thread-safe API + continuations
                var ex = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::task_fault(reinterpret_cast<cil2cpp::Task*>({thisArg}->f_task), static_cast<cil2cpp::Exception*>({ex}));"
                });
                return true;
            }
            case "AwaitUnsafeOnCompleted" or "AwaitOnCompleted":
            {
                // AwaitUnsafeOnCompleted<TAwaiter, TSM>(ref TAwaiter, ref TSM)
                // Register a continuation on the awaiter's task that calls MoveNext.
                var smAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var awaiterAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var builderAddr = stack.Count > 0 ? stack.Pop() : "nullptr";

                var moveNextName = ResolveAwaitMoveNextName(methodRef);
                if (moveNextName != null)
                {
                    // Determine the state machine arg for continuation
                    var smArg = smAddr;
                    if (methodRef is GenericInstanceMethod gim2 && gim2.GenericArguments.Count >= 2)
                    {
                        var smTypeRef = gim2.GenericArguments[1];
                        bool isValueSm = false;
                        try { isValueSm = smTypeRef.Resolve()?.IsValueType == true; }
                        catch { }
                        if (!isValueSm && smArg.StartsWith("&"))
                            smArg = smArg[1..]; // Strip & — pointer variable itself is the correct arg
                    }

                    // Get the task from the awaiter to register continuation on
                    var awaiterDeref = awaiterAddr.StartsWith("&") ? $"({awaiterAddr})" : awaiterAddr;
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"cil2cpp::task_add_continuation({awaiterDeref}->f_task, " +
                               $"reinterpret_cast<void(*)(void*)>(&{moveNextName}), " +
                               $"static_cast<void*>({smArg}));"
                    });
                }
                return true;
            }
            case "SetStateMachine":
            {
                // No-op — consume stack args
                var sm = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                return true;
            }
            default:
                return false;
        }
    }

    // ── TaskAwaiter interception ──────────────────────────────

    private bool TryEmitAwaiterCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        string WrapThis(string raw) => raw.StartsWith("&") ? $"({raw})" : raw;
        var isGeneric = methodRef.DeclaringType is GenericInstanceType;

        switch (methodRef.Name)
        {
            case "get_IsCompleted":
            {
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_is_completed({thisArg}->f_task);"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetResult":
            {
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                // Wait for the task to complete (blocks if still pending)
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::task_wait({thisArg}->f_task);"
                });
                // Check for faulted task and rethrow exception
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({thisArg}->f_task && {thisArg}->f_task->f_status == 2 && {thisArg}->f_task->f_exception) cil2cpp::throw_exception({thisArg}->f_task->f_exception);"
                });
                if (isGeneric)
                {
                    // Generic version returns T
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = {thisArg}->f_task->f_result;"
                    });
                    stack.Push(tmp);
                }
                // Non-generic GetResult() returns void — nothing to push
                return true;
            }
            case "OnCompleted" or "UnsafeOnCompleted":
            {
                // No-op in synchronous mode — consume args
                var continuation = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                return true;
            }
            default:
                return false;
        }
    }

    // ── Task interception ─────────────────────────────────────

    private bool TryEmitTaskCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var isGeneric = methodRef.DeclaringType is GenericInstanceType;

        switch (methodRef.Name)
        {
            case "get_CompletedTask":
            {
                // Static property: Task.CompletedTask
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_get_completed();"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetAwaiter":
            {
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                if (isGeneric)
                {
                    var awaiterType = ResolveAwaiterTypeForTask(methodRef.DeclaringType);
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{awaiterType} {tmp} = {{}}; {tmp}.f_task = {task};"
                    });
                }
                else
                {
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"System_Runtime_CompilerServices_TaskAwaiter {tmp} = {{}}; {tmp}.f_task = {task};"
                    });
                }
                stack.Push(tmp);
                return true;
            }
            case "get_Result":
            {
                // Task<T>.Result property — wait for completion then extract
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::task_wait(reinterpret_cast<cil2cpp::Task*>({task}));"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({task} && {task}->f_status == 2 && {task}->f_exception) cil2cpp::throw_exception({task}->f_exception);"
                });
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {task}->f_result;"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_IsCompleted":
            {
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_is_completed(reinterpret_cast<cil2cpp::Task*>({task}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "FromResult":
            {
                // Task.FromResult<T>(T value) — allocate correctly-sized Task<T>
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";

                // Get the concrete Task<T> type from the GenericInstanceMethod
                var taskTypeCpp = ResolveTaskTypeFromFromResult(methodRef);
                // Split into separate instructions to avoid AddAutoDeclarations conflict
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = static_cast<{taskTypeCpp}*>(cil2cpp::gc::alloc(sizeof({taskTypeCpp}), nullptr));"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::task_init_completed(reinterpret_cast<cil2cpp::Task*>({tmp})); {tmp}->f_result = {value};"
                });
                stack.Push(tmp);
                return true;
            }
            case "Delay":
            {
                // Task.Delay(int milliseconds) — real async delay via thread pool
                var ms = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_delay({ms});"
                });
                stack.Push(tmp);
                return true;
            }
            case "Run":
            {
                // Task.Run(Action/Func<Task>) — run delegate on thread pool
                var del = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_run(static_cast<cil2cpp::Object*>({del}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "WhenAll":
            {
                // Task.WhenAll(Task[]) — complete when all tasks complete
                var tasks = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_when_all(static_cast<cil2cpp::Array*>(static_cast<cil2cpp::Object*>({tasks})));"
                });
                stack.Push(tmp);
                return true;
            }
            case "WhenAny":
            {
                // Task.WhenAny(Task[]) — complete when any task completes
                var tasks = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_when_any(static_cast<cil2cpp::Array*>(static_cast<cil2cpp::Object*>({tasks})));"
                });
                stack.Push(tmp);
                return true;
            }
            case "Wait":
            {
                // task.Wait() — block until completion
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::task_wait(reinterpret_cast<cil2cpp::Task*>({task}));"
                });
                return true;
            }
            case "get_Status":
            {
                // task.Status — return status field
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {task}->f_status;"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_Exception":
            {
                // task.Exception — return exception field
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = static_cast<cil2cpp::Object*>({task}->f_exception);"
                });
                stack.Push(tmp);
                return true;
            }
            default:
                return false;
        }
    }

    // ── Helper methods ────────────────────────────────────────

    /// <summary>
    /// Resolve MoveNext function name from Start&lt;TSM&gt; GenericInstanceMethod.
    /// </summary>
    private string? ResolveMoveNextName(MethodReference methodRef)
    {
        if (methodRef is GenericInstanceMethod gim && gim.GenericArguments.Count > 0)
        {
            var smType = gim.GenericArguments[0];
            var smTypeCpp = GetMangledTypeNameForRef(smType);
            return CppNameMapper.MangleMethodName(smTypeCpp, "MoveNext");
        }
        return null;
    }

    /// <summary>
    /// Resolve MoveNext function name from AwaitUnsafeOnCompleted&lt;TA, TSM&gt;.
    /// The state machine type is the second generic argument.
    /// </summary>
    private string? ResolveAwaitMoveNextName(MethodReference methodRef)
    {
        if (methodRef is GenericInstanceMethod gim && gim.GenericArguments.Count >= 2)
        {
            var smType = gim.GenericArguments[1];
            var smTypeCpp = GetMangledTypeNameForRef(smType);
            return CppNameMapper.MangleMethodName(smTypeCpp, "MoveNext");
        }
        return null;
    }

    /// <summary>
    /// Resolve TaskAwaiter&lt;T&gt; type name for Task&lt;T&gt;.GetAwaiter().
    /// </summary>
    private string ResolveAwaiterTypeForTask(TypeReference taskType)
    {
        if (taskType is GenericInstanceType git && git.GenericArguments.Count > 0)
        {
            var tResult = git.GenericArguments[0].FullName;
            var awaiterKey = $"System.Runtime.CompilerServices.TaskAwaiter`1<{tResult}>";
            if (_typeCache.TryGetValue(awaiterKey, out var awaiterType))
                return awaiterType.CppName;
            return CppNameMapper.MangleGenericInstanceTypeName(
                "System.Runtime.CompilerServices.TaskAwaiter`1", new List<string> { tResult });
        }
        return "System_Runtime_CompilerServices_TaskAwaiter";
    }

    /// <summary>
    /// Resolve Task&lt;T&gt; C++ type name from a generic type argument T.
    /// </summary>
    private string ResolveTaskTypeCppName(TypeReference tResultRef)
    {
        var tResult = tResultRef.FullName;
        var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
        if (_typeCache.TryGetValue(taskKey, out var taskType))
            return taskType.CppName;
        return CppNameMapper.MangleGenericInstanceTypeName(
            "System.Threading.Tasks.Task`1", new List<string> { tResult });
    }

    /// <summary>
    /// Resolve Task&lt;T&gt; C++ type name from Task.FromResult&lt;T&gt;(T).
    /// </summary>
    private string ResolveTaskTypeFromFromResult(MethodReference methodRef)
    {
        if (methodRef is GenericInstanceMethod gim && gim.GenericArguments.Count > 0)
        {
            var tResult = gim.GenericArguments[0].FullName;
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            if (_typeCache.TryGetValue(taskKey, out var taskType))
                return taskType.CppName;
            return CppNameMapper.MangleGenericInstanceTypeName(
                "System.Threading.Tasks.Task`1", new List<string> { tResult });
        }
        return "System_Threading_Tasks_Task";
    }
}
