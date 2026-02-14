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
            // Task<T>: f_status (Int32) + f_exception (Exception*) + f_result (T)
            fields.Add(MakeSyntheticField("f_status", "System.Int32", irType));
            fields.Add(MakeSyntheticField("f_exception", "System.Exception", irType));
            fields.Add(MakeSyntheticField("f_result", tResult, irType));
        }
        else if (openTypeName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter`"))
        {
            // TaskAwaiter<T>: f_task (Task<T>*)
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            fields.Add(MakeSyntheticField("f_task", taskKey, irType));
        }
        else if (openTypeName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`"))
        {
            // AsyncTaskMethodBuilder<T>: f_task (Task<T>*)
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            fields.Add(MakeSyntheticField("f_task", taskKey, irType));
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
                // Static factory: returns zero-initialized builder with completed task
                var builderType = GetMangledTypeNameForRef(methodRef.DeclaringType);
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{builderType} {tmp} = {{}}; {tmp}.f_task = cil2cpp::task_create_completed();"
                });
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
                    block.Instructions.Add(new IRCall
                    {
                        FunctionName = moveNextName,
                        Arguments = { smAddr },
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
                    // SetResult(T result) — generic version stores result
                    var result = stack.Count > 0 ? stack.Pop() : "0";
                    var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{thisArg}->f_task->f_result = {result}; {thisArg}->f_task->f_status = 1;"
                    });
                }
                else
                {
                    // SetResult() — non-generic version just marks complete
                    var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{thisArg}->f_task->f_status = 1;"
                    });
                }
                return true;
            }
            case "SetException":
            {
                // SetException(Exception ex) — mark faulted
                var ex = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = WrapThis(stack.Count > 0 ? stack.Pop() : "nullptr");
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{thisArg}->f_task->f_exception = {ex}; {thisArg}->f_task->f_status = 2;"
                });
                return true;
            }
            case "AwaitUnsafeOnCompleted" or "AwaitOnCompleted":
            {
                // AwaitUnsafeOnCompleted<TAwaiter, TSM>(ref TAwaiter, ref TSM)
                // In sync mode: just call MoveNext again (safe fallback)
                var smAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var awaiterAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var builderAddr = stack.Count > 0 ? stack.Pop() : "nullptr";

                var moveNextName = ResolveAwaitMoveNextName(methodRef);
                if (moveNextName != null)
                {
                    block.Instructions.Add(new IRCall
                    {
                        FunctionName = moveNextName,
                        Arguments = { smAddr },
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
                // Check for faulted task
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
                // Task<T>.Result property
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
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
                    Code = $"auto {tmp} = cil2cpp::task_is_completed({task});"
                });
                stack.Push(tmp);
                return true;
            }
            case "FromResult":
            {
                // Task.FromResult<T>(T value) — static generic method
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";

                // Get the concrete Task<T> type from the GenericInstanceMethod
                var taskTypeCpp = ResolveTaskTypeFromFromResult(methodRef);
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto* {tmp} = static_cast<{taskTypeCpp}*>(cil2cpp::task_create_completed()); {tmp}->f_result = {value};"
                });
                stack.Push(tmp);
                return true;
            }
            case "Delay":
            {
                // Task.Delay(int) — ignore delay, return completed task
                var ms = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::task_get_completed();"
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
