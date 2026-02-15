using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// IAsyncEnumerable/IAsyncEnumerator support.
/// Intercepts ValueTask, ValueTask&lt;T&gt;, ManualResetValueTaskSourceCore&lt;T&gt;,
/// AsyncIteratorMethodBuilder, ValueTaskAwaiter&lt;T&gt; BCL calls.
/// The async iterator state machine is compiled from IL normally — only the
/// infrastructure types need interception.
/// </summary>
public partial class IRBuilder
{
    // ── Type detection ────────────────────────────────────────

    private static bool IsValueTaskType(TypeReference typeRef)
    {
        var name = typeRef is GenericInstanceType git
            ? git.ElementType.FullName : typeRef.FullName;
        return name is "System.Threading.Tasks.ValueTask"
            or "System.Threading.Tasks.ValueTask`1";
    }

    private static bool IsValueTaskAwaiterType(TypeReference typeRef)
    {
        var name = typeRef is GenericInstanceType git
            ? git.ElementType.FullName : typeRef.FullName;
        return name is "System.Runtime.CompilerServices.ValueTaskAwaiter"
            or "System.Runtime.CompilerServices.ValueTaskAwaiter`1";
    }

    private static bool IsAsyncIteratorBuilderType(TypeReference typeRef)
    {
        return typeRef.FullName == "System.Runtime.CompilerServices.AsyncIteratorMethodBuilder";
    }

    private static bool IsManualResetValueTaskSourceCoreType(TypeReference typeRef)
    {
        var name = typeRef is GenericInstanceType git
            ? git.ElementType.FullName : typeRef.FullName;
        return name is "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1";
    }

    /// <summary>
    /// Check if an open type name is one of the async enumerable BCL generic types.
    /// Used by CreateGenericSpecializations to allow null CecilOpenType.
    /// </summary>
    internal static bool IsAsyncEnumerableBclGenericType(string openTypeName)
    {
        return openTypeName.StartsWith("System.Threading.Tasks.ValueTask`")
            || openTypeName.StartsWith("System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`")
            || openTypeName.StartsWith("System.Runtime.CompilerServices.ValueTaskAwaiter`");
    }

    // ── Synthetic field creation ──────────────────────────────

    /// <summary>
    /// Create synthetic fields for async enumerable BCL generic types
    /// (ValueTask&lt;T&gt;, ManualResetValueTaskSourceCore&lt;T&gt;, ValueTaskAwaiter&lt;T&gt;).
    /// </summary>
    internal List<IRField> CreateAsyncEnumerableSyntheticFields(
        string openTypeName, IRType irType, Dictionary<string, string> typeParamMap)
    {
        var fields = new List<IRField>();
        var tResult = typeParamMap.GetValueOrDefault("TResult", "System.Boolean");

        if (openTypeName.StartsWith("System.Threading.Tasks.ValueTask`"))
        {
            // ValueTask<T>: task (Task<T>*) + result (T)
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            fields.Add(MakeSyntheticField("task", taskKey, irType));
            fields.Add(MakeSyntheticField("result", tResult, irType));
        }
        else if (openTypeName.StartsWith("System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`"))
        {
            // ManualResetValueTaskSourceCore<T>: task (Task<T>*)
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            fields.Add(MakeSyntheticField("task", taskKey, irType));
        }
        else if (openTypeName.StartsWith("System.Runtime.CompilerServices.ValueTaskAwaiter`"))
        {
            // ValueTaskAwaiter<T>: task (Task<T>*) + result (T) for immediate completions
            var taskKey = $"System.Threading.Tasks.Task`1<{tResult}>";
            fields.Add(MakeSyntheticField("task", taskKey, irType));
            fields.Add(MakeSyntheticField("result", tResult, irType));
        }

        return fields;
    }

    // ── Synthetic non-generic types ───────────────────────────

    /// <summary>
    /// Create synthetic IRTypes for non-generic ValueTask, AsyncIteratorMethodBuilder,
    /// ValueTaskAwaiter. Also pre-registers ValueTask&lt;bool&gt; as a value type for
    /// BclProxy return type resolution.
    /// Called in Build() before CreateBclInterfaceProxies.
    /// </summary>
    private void CreateAsyncEnumerableSyntheticTypes()
    {
        // Non-generic ValueTask (value type)
        if (!_typeCache.ContainsKey("System.Threading.Tasks.ValueTask"))
        {
            var vtType = new IRType
            {
                ILFullName = "System.Threading.Tasks.ValueTask",
                CppName = "System_Threading_Tasks_ValueTask",
                Name = "ValueTask",
                Namespace = "System.Threading.Tasks",
                IsValueType = true,
                IsSealed = true,
                IsRuntimeProvided = true,
            };
            vtType.Fields.Add(new IRField
            {
                Name = "task",
                CppName = "f_task",
                FieldTypeName = "System.Threading.Tasks.Task",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = vtType,
            });
            _module.Types.Add(vtType);
            _typeCache["System.Threading.Tasks.ValueTask"] = vtType;
            CppNameMapper.RegisterValueType("System.Threading.Tasks.ValueTask");
            CppNameMapper.RegisterValueType("System_Threading_Tasks_ValueTask");
        }

        // AsyncIteratorMethodBuilder (value type)
        if (!_typeCache.ContainsKey("System.Runtime.CompilerServices.AsyncIteratorMethodBuilder"))
        {
            var builderType = new IRType
            {
                ILFullName = "System.Runtime.CompilerServices.AsyncIteratorMethodBuilder",
                CppName = "System_Runtime_CompilerServices_AsyncIteratorMethodBuilder",
                Name = "AsyncIteratorMethodBuilder",
                Namespace = "System.Runtime.CompilerServices",
                IsValueType = true,
                IsSealed = true,
                IsRuntimeProvided = true,
            };
            builderType.Fields.Add(new IRField
            {
                Name = "dummy",
                CppName = "f_dummy",
                FieldTypeName = "System.Int32",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = builderType,
            });
            _module.Types.Add(builderType);
            _typeCache["System.Runtime.CompilerServices.AsyncIteratorMethodBuilder"] = builderType;
            CppNameMapper.RegisterValueType("System.Runtime.CompilerServices.AsyncIteratorMethodBuilder");
            CppNameMapper.RegisterValueType("System_Runtime_CompilerServices_AsyncIteratorMethodBuilder");
        }

        // Non-generic ValueTaskAwaiter (value type)
        if (!_typeCache.ContainsKey("System.Runtime.CompilerServices.ValueTaskAwaiter"))
        {
            var awaiterType = new IRType
            {
                ILFullName = "System.Runtime.CompilerServices.ValueTaskAwaiter",
                CppName = "System_Runtime_CompilerServices_ValueTaskAwaiter",
                Name = "ValueTaskAwaiter",
                Namespace = "System.Runtime.CompilerServices",
                IsValueType = true,
                IsSealed = true,
                IsRuntimeProvided = true,
            };
            awaiterType.Fields.Add(new IRField
            {
                Name = "task",
                CppName = "f_task",
                FieldTypeName = "System.Threading.Tasks.Task",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = awaiterType,
            });
            _module.Types.Add(awaiterType);
            _typeCache["System.Runtime.CompilerServices.ValueTaskAwaiter"] = awaiterType;
            CppNameMapper.RegisterValueType("System.Runtime.CompilerServices.ValueTaskAwaiter");
            CppNameMapper.RegisterValueType("System_Runtime_CompilerServices_ValueTaskAwaiter");
        }

        // Pre-register ValueTask<bool> as a value type so BclProxy MoveNextAsync return type is correct
        CppNameMapper.RegisterValueType("System.Threading.Tasks.ValueTask`1<System.Boolean>");
        var vtBoolMangled = CppNameMapper.MangleGenericInstanceTypeName(
            "System.Threading.Tasks.ValueTask`1", new List<string> { "System.Boolean" });
        CppNameMapper.RegisterValueType(vtBoolMangled);

        // Also register ManualResetValueTaskSourceCore<bool> and ValueTaskAwaiter<bool> as value types
        CppNameMapper.RegisterValueType("System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1<System.Boolean>");
        CppNameMapper.RegisterValueType(CppNameMapper.MangleGenericInstanceTypeName(
            "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1", new List<string> { "System.Boolean" }));
        CppNameMapper.RegisterValueType("System.Runtime.CompilerServices.ValueTaskAwaiter`1<System.Boolean>");
        CppNameMapper.RegisterValueType(CppNameMapper.MangleGenericInstanceTypeName(
            "System.Runtime.CompilerServices.ValueTaskAwaiter`1", new List<string> { "System.Boolean" }));

        // Ensure Task<bool> is in generic instantiations for CreateGenericSpecializations
        // (it's referenced by ManualResetValueTaskSourceCore<bool> synthetic fields but may not appear in user IL)
        var taskBoolKey = "System.Threading.Tasks.Task`1<System.Boolean>";
        if (!_genericInstantiations.ContainsKey(taskBoolKey))
        {
            var mangledName = CppNameMapper.MangleGenericInstanceTypeName(
                "System.Threading.Tasks.Task`1", new List<string> { "System.Boolean" });
            _genericInstantiations[taskBoolKey] = new GenericInstantiationInfo(
                "System.Threading.Tasks.Task`1",
                new List<string> { "System.Boolean" },
                mangledName, null);
        }

        // ValueTaskSourceStatus enum (value type mapped to int32_t)
        CreateBclEnumType("System.Threading.Tasks.Sources.ValueTaskSourceStatus",
            "System.Threading.Tasks.Sources", "ValueTaskSourceStatus");

        // ValueTaskSourceOnCompletedFlags enum (value type mapped to int32_t)
        CreateBclEnumType("System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags",
            "System.Threading.Tasks.Sources", "ValueTaskSourceOnCompletedFlags");

        // ExceptionDispatchInfo — opaque reference type (we intercept its methods)
        if (!_typeCache.ContainsKey("System.Runtime.ExceptionServices.ExceptionDispatchInfo"))
        {
            var ediType = new IRType
            {
                ILFullName = "System.Runtime.ExceptionServices.ExceptionDispatchInfo",
                CppName = "System_Runtime_ExceptionServices_ExceptionDispatchInfo",
                Name = "ExceptionDispatchInfo",
                Namespace = "System.Runtime.ExceptionServices",
                IsSealed = true,
            };
            // Store the exception reference
            ediType.Fields.Add(new IRField
            {
                Name = "exception",
                CppName = "f_exception",
                FieldTypeName = "System.Exception",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = ediType,
            });
            _module.Types.Add(ediType);
            _typeCache["System.Runtime.ExceptionServices.ExceptionDispatchInfo"] = ediType;
        }
    }

    /// <summary>
    /// Create a BCL enum type as a value type.
    /// </summary>
    private void CreateBclEnumType(string ilFullName, string ns, string name)
    {
        if (_typeCache.ContainsKey(ilFullName)) return;
        var cppName = CppNameMapper.MangleTypeName(ilFullName);
        var irType = new IRType
        {
            ILFullName = ilFullName,
            CppName = cppName,
            Name = name,
            Namespace = ns,
            IsEnum = true,
            IsValueType = true,
            IsSealed = true,
            EnumUnderlyingType = "System.Int32",
        };
        _module.Types.Add(irType);
        _typeCache[ilFullName] = irType;
        CppNameMapper.RegisterValueType(ilFullName);
        CppNameMapper.RegisterValueType(cppName);
    }

    // ── Method call interceptions ─────────────────────────────

    /// <summary>
    /// Handle calls to async enumerable BCL types.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitAsyncEnumerableCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (IsManualResetValueTaskSourceCoreType(methodRef.DeclaringType))
            return TryEmitPromiseCoreCall(block, stack, methodRef, ref tempCounter);
        if (IsAsyncIteratorBuilderType(methodRef.DeclaringType))
            return TryEmitAsyncIteratorBuilderCall(block, stack, methodRef, ref tempCounter);
        if (IsValueTaskType(methodRef.DeclaringType))
            return TryEmitValueTaskCall_AE(block, stack, methodRef, ref tempCounter);
        if (IsValueTaskAwaiterType(methodRef.DeclaringType))
            return TryEmitValueTaskAwaiterCall(block, stack, methodRef, ref tempCounter);
        return false;
    }

    /// <summary>
    /// Handle newobj for async enumerable BCL types.
    /// </summary>
    private bool TryEmitAsyncEnumerableNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsValueTaskType(ctorRef.DeclaringType)) return false;
        return TryEmitValueTaskNewObj(block, stack, ctorRef, ref tempCounter);
    }

    // ── ManualResetValueTaskSourceCore<T> interception ────────

    private bool TryEmitPromiseCoreCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        string WrapThis(string raw) => raw.StartsWith("&") ? $"({raw})" : raw;

        switch (methodRef.Name)
        {
            case "Reset":
            {
                // Reset(): create a new pending Task<bool> and store in promise + TLS
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisAddr);

                // Resolve the Task<TResult> type for allocation
                var taskTypeCpp = ResolvePromiseTaskType(methodRef.DeclaringType);

                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{w}->f_task = static_cast<{taskTypeCpp}*>(cil2cpp::gc::alloc(sizeof({taskTypeCpp}), nullptr)); " +
                           $"cil2cpp::task_init_pending(reinterpret_cast<cil2cpp::Task*>({w}->f_task)); " +
                           $"cil2cpp::g_async_iter_current_task = reinterpret_cast<cil2cpp::Task*>({w}->f_task);"
                });
                return true;
            }
            case "SetResult":
            {
                // SetResult(TResult value): complete the task with the value
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisAddr);

                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{w}->f_task->f_result = static_cast<decltype({w}->f_task->f_result)>({value}); " +
                           $"cil2cpp::task_complete(reinterpret_cast<cil2cpp::Task*>({w}->f_task));"
                });
                return true;
            }
            case "SetException":
            {
                // SetException(Exception ex): fault the task
                var ex = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisAddr);

                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::task_fault(reinterpret_cast<cil2cpp::Task*>({w}->f_task), " +
                           $"static_cast<cil2cpp::Exception*>({ex}));"
                });
                return true;
            }
            case "get_Version":
            {
                // get_Version: return 0 (we don't use versioning)
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int16_t {tmp} = 0;"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetResult":
            {
                // GetResult(short token): return the result from the task
                var token = stack.Count > 0 ? stack.Pop() : "0";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisAddr);

                // Wait for completion
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({w}->f_task) cil2cpp::task_wait(reinterpret_cast<cil2cpp::Task*>({w}->f_task));"
                });
                // Check for faulted
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({w}->f_task && reinterpret_cast<cil2cpp::Task*>({w}->f_task)->f_status == 2 && " +
                           $"reinterpret_cast<cil2cpp::Task*>({w}->f_task)->f_exception) " +
                           $"cil2cpp::throw_exception(reinterpret_cast<cil2cpp::Task*>({w}->f_task)->f_exception);"
                });
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {w}->f_task ? {w}->f_task->f_result : decltype({w}->f_task->f_result){{}};"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetStatus":
            {
                // GetStatus(short token): return ValueTaskSourceStatus (int)
                var token = stack.Count > 0 ? stack.Pop() : "0";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisAddr);
                var tmp = $"__t{tempCounter++}";
                // Pending=0, Succeeded=1, Faulted=2
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t {tmp} = ({w}->f_task == nullptr) ? 1 : " +
                           $"(cil2cpp::task_is_completed(reinterpret_cast<cil2cpp::Task*>({w}->f_task)) ? " +
                           $"(reinterpret_cast<cil2cpp::Task*>({w}->f_task)->f_status == 2 ? 2 : 1) : 0);"
                });
                stack.Push(tmp);
                return true;
            }
            case "OnCompleted":
            {
                // OnCompleted(Action<object>, object, short, ValueTaskSourceOnCompletedFlags)
                // Register continuation callback on the task
                var flags = stack.Count > 0 ? stack.Pop() : "0";
                var token = stack.Count > 0 ? stack.Pop() : "0";
                var state = stack.Count > 0 ? stack.Pop() : "nullptr";
                var continuation = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                // No-op for synchronous execution; the task is always complete
                // when the consumer checks (since we run MoveNext synchronously)
                return true;
            }
            default:
                return false;
        }
    }

    // ── AsyncIteratorMethodBuilder interception ───────────────

    private bool TryEmitAsyncIteratorBuilderCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        switch (methodRef.Name)
        {
            case "Create":
            {
                // Static factory: return zero-initialized builder
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Runtime_CompilerServices_AsyncIteratorMethodBuilder {tmp} = {{}};"
                });
                stack.Push(tmp);
                return true;
            }
            case "MoveNext":
            {
                // MoveNext<TSM>(ref TSM stateMachine): call sm.MoveNext()
                var smAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var builderAddr = stack.Count > 0 ? stack.Pop() : "nullptr";

                var moveNextName = ResolveMoveNextName(methodRef);
                if (moveNextName != null)
                {
                    // Handle class vs struct state machines
                    var smArg = smAddr;
                    if (methodRef is GenericInstanceMethod gim && gim.GenericArguments.Count > 0)
                    {
                        var smTypeRef = gim.GenericArguments[0];
                        bool isValueSm = false;
                        try { isValueSm = smTypeRef.Resolve()?.IsValueType == true; }
                        catch { }
                        if (!isValueSm && smArg.StartsWith("&"))
                            smArg = smArg[1..];
                    }
                    block.Instructions.Add(new IRCall
                    {
                        FunctionName = moveNextName,
                        Arguments = { smArg },
                    });
                }
                return true;
            }
            case "Complete":
            {
                // Complete(): no-op
                var builderAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                return true;
            }
            case "AwaitUnsafeOnCompleted" or "AwaitOnCompleted":
            {
                // Same as AsyncTaskMethodBuilder.AwaitUnsafeOnCompleted
                var smAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var awaiterAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var builderAddr = stack.Count > 0 ? stack.Pop() : "nullptr";

                var moveNextName = ResolveAwaitMoveNextName(methodRef);
                if (moveNextName != null)
                {
                    var smArg = smAddr;
                    if (methodRef is GenericInstanceMethod gim && gim.GenericArguments.Count >= 2)
                    {
                        var smTypeRef = gim.GenericArguments[1];
                        bool isValueSm = false;
                        try { isValueSm = smTypeRef.Resolve()?.IsValueType == true; }
                        catch { }
                        if (!isValueSm && smArg.StartsWith("&"))
                            smArg = smArg[1..];
                    }

                    // The awaiter might be TaskAwaiter or ValueTaskAwaiter — both have f_task
                    var awaiterDeref = awaiterAddr.StartsWith("&") ? $"({awaiterAddr})" : awaiterAddr;
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"cil2cpp::task_add_continuation(reinterpret_cast<cil2cpp::Task*>({awaiterDeref}->f_task), " +
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
                var builderAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                return true;
            }
            default:
                return false;
        }
    }

    // ── ValueTask / ValueTask<T> interception ─────────────────

    private bool TryEmitValueTaskCall_AE(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        string WrapThis(string raw) => raw.StartsWith("&") ? $"({raw})" : raw;
        var isGeneric = methodRef.DeclaringType is GenericInstanceType;

        switch (methodRef.Name)
        {
            case "GetAwaiter":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "{}";

                // Determine if this is a value (struct) or pointer — ValueTask is a value type
                // In IL, GetAwaiter is called on a value (ldloc) or via address (ldloca)
                var tmp = $"__t{tempCounter++}";
                if (isGeneric)
                {
                    var awaiterType = ResolveValueTaskAwaiterType(methodRef.DeclaringType);
                    // thisExpr could be a pointer (from ldloca) or a value
                    var taskAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{WrapThis(thisExpr)}->f_task" : $"{thisExpr}.f_task";
                    var resultAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{WrapThis(thisExpr)}->f_result" : $"{thisExpr}.f_result";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"{awaiterType} {tmp} = {{}}; {tmp}.f_task = {taskAccess}; {tmp}.f_result = {resultAccess};"
                    });
                }
                else
                {
                    var taskAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{WrapThis(thisExpr)}->f_task" : $"{thisExpr}.f_task";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"System_Runtime_CompilerServices_ValueTaskAwaiter {tmp} = {{}}; {tmp}.f_task = {taskAccess};"
                    });
                }
                stack.Push(tmp);
                return true;
            }
            case "get_IsCompleted":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisExpr);
                var tmp = $"__t{tempCounter++}";
                var taskAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{w}->f_task" : $"{thisExpr}.f_task";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({taskAccess} == nullptr) || cil2cpp::task_is_completed(reinterpret_cast<cil2cpp::Task*>({taskAccess}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_IsCompletedSuccessfully":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisExpr);
                var tmp = $"__t{tempCounter++}";
                var taskAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{w}->f_task" : $"{thisExpr}.f_task";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({taskAccess} == nullptr) || (reinterpret_cast<cil2cpp::Task*>({taskAccess})->f_status == 1);"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_Result":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisExpr);
                var taskAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{w}->f_task" : $"{thisExpr}.f_task";

                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({taskAccess}) cil2cpp::task_wait(reinterpret_cast<cil2cpp::Task*>({taskAccess}));"
                });
                if (isGeneric)
                {
                    var tmp = $"__t{tempCounter++}";
                    var resultAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{w}->f_result" : $"{thisExpr}.f_result";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = {taskAccess} ? {taskAccess}->f_result : {resultAccess};"
                    });
                    stack.Push(tmp);
                }
                return true;
            }
            case "AsTask":
            {
                // ValueTask.AsTask() / ValueTask<T>.AsTask() — return the underlying task
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisExpr);
                var tmp = $"__t{tempCounter++}";
                var taskAccess = thisExpr.StartsWith("&") || thisExpr.StartsWith("(") ? $"{w}->f_task" : $"{thisExpr}.f_task";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {taskAccess};"
                });
                stack.Push(tmp);
                return true;
            }
            case ".ctor":
            {
                // ValueTask.ctor / ValueTask<T>.ctor called on existing value (ldloca + call .ctor)
                // Handle same cases as NewObj but writing into the address on stack
                return EmitValueTaskCtorCall(block, stack, methodRef, ref tempCounter, isGeneric);
            }
            default:
                return false;
        }
    }

    // ── ValueTaskAwaiter / ValueTaskAwaiter<T> interception ───

    private bool TryEmitValueTaskAwaiterCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        string WrapThis(string raw) => raw.StartsWith("&") ? $"({raw})" : raw;
        var isGeneric = methodRef.DeclaringType is GenericInstanceType;

        switch (methodRef.Name)
        {
            case "get_IsCompleted":
            {
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisAddr);
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({w}->f_task == nullptr) || cil2cpp::task_is_completed(reinterpret_cast<cil2cpp::Task*>({w}->f_task));"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetResult":
            {
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = WrapThis(thisAddr);

                // Wait for completion (only if there's a task — null means immediate result)
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({w}->f_task) cil2cpp::task_wait(reinterpret_cast<cil2cpp::Task*>({w}->f_task));"
                });
                // Check for faulted task
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"if ({w}->f_task && reinterpret_cast<cil2cpp::Task*>({w}->f_task)->f_status == 2 && " +
                           $"reinterpret_cast<cil2cpp::Task*>({w}->f_task)->f_exception) " +
                           $"cil2cpp::throw_exception(reinterpret_cast<cil2cpp::Task*>({w}->f_task)->f_exception);"
                });
                if (isGeneric)
                {
                    // If f_task is null, this was an immediate completion — use f_result from the awaiter
                    // If f_task exists, it completed via task — read f_task->f_result
                    var tmp = $"__t{tempCounter++}";
                    block.Instructions.Add(new IRRawCpp
                    {
                        Code = $"auto {tmp} = {w}->f_task ? {w}->f_task->f_result : {w}->f_result;"
                    });
                    stack.Push(tmp);
                }
                // Non-generic GetResult returns void
                return true;
            }
            case "OnCompleted" or "UnsafeOnCompleted":
            {
                // Consume args — continuations handled by AwaitUnsafeOnCompleted
                var continuation = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                return true;
            }
            default:
                return false;
        }
    }

    // ── ValueTask NewObj interception ──────────────────────────

    private bool TryEmitValueTaskNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        var isGeneric = ctorRef.DeclaringType is GenericInstanceType;
        var vtTypeCpp = GetMangledTypeNameForRef(ctorRef.DeclaringType);

        if (ctorRef.Parameters.Count == 0)
        {
            // default ValueTask() — completed
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{vtTypeCpp} {tmp} = {{}};"
            });
            stack.Push(tmp);
            return true;
        }

        if (ctorRef.Parameters.Count == 1)
        {
            var paramType = ctorRef.Parameters[0].ParameterType;

            if (paramType.FullName == "System.Threading.Tasks.Task" ||
                paramType.FullName.StartsWith("System.Threading.Tasks.Task`"))
            {
                // ValueTask(Task) / ValueTask<T>(Task<T>) — wrap a task
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{vtTypeCpp} {tmp} = {{}}; {tmp}.f_task = {task};"
                });
                stack.Push(tmp);
                return true;
            }

            if (isGeneric)
            {
                // ValueTask<T>(T result) — immediate result
                var result = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{vtTypeCpp} {tmp} = {{}}; {tmp}.f_result = {result};"
                });
                stack.Push(tmp);
                return true;
            }
        }

        if (ctorRef.Parameters.Count == 2)
        {
            // ValueTask(IValueTaskSource, short) or ValueTask<T>(IValueTaskSource<T>, short)
            // Stack: [source (state machine), token (short)]
            var token = stack.Count > 0 ? stack.Pop() : "0";
            var source = stack.Count > 0 ? stack.Pop() : "nullptr";

            var tmp = $"__t{tempCounter++}";
            // Grab the task from TLS (stored by ManualResetValueTaskSourceCore.Reset)
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{vtTypeCpp} {tmp} = {{}}; {tmp}.f_task = reinterpret_cast<decltype({tmp}.f_task)>(cil2cpp::g_async_iter_current_task);"
            });
            stack.Push(tmp);
            return true;
        }

        return false;
    }

    // ── Helper methods ────────────────────────────────────────

    /// <summary>
    /// Resolve the Task&lt;TResult&gt; C++ type for ManualResetValueTaskSourceCore&lt;TResult&gt;.
    /// </summary>
    private string ResolvePromiseTaskType(TypeReference promiseType)
    {
        if (promiseType is GenericInstanceType git && git.GenericArguments.Count > 0)
        {
            return ResolveTaskTypeCppName(git.GenericArguments[0]);
        }
        // Default to Task<bool> for async iterator pattern
        return CppNameMapper.MangleGenericInstanceTypeName(
            "System.Threading.Tasks.Task`1", new List<string> { "System.Boolean" });
    }

    /// <summary>
    /// Resolve ValueTaskAwaiter&lt;T&gt; type name from ValueTask&lt;T&gt;.
    /// </summary>
    private string ResolveValueTaskAwaiterType(TypeReference valueTaskType)
    {
        if (valueTaskType is GenericInstanceType git && git.GenericArguments.Count > 0)
        {
            var tResult = git.GenericArguments[0].FullName;
            var awaiterKey = $"System.Runtime.CompilerServices.ValueTaskAwaiter`1<{tResult}>";
            if (_typeCache.TryGetValue(awaiterKey, out var awaiterType))
                return awaiterType.CppName;
            return CppNameMapper.MangleGenericInstanceTypeName(
                "System.Runtime.CompilerServices.ValueTaskAwaiter`1", new List<string> { tResult });
        }
        return "System_Runtime_CompilerServices_ValueTaskAwaiter";
    }

    // ── ValueTask .ctor call (on existing value, not newobj) ─────

    /// <summary>
    /// Handle ValueTask.ctor / ValueTask&lt;T&gt;.ctor called via ldloca + call .ctor pattern.
    /// </summary>
    private bool EmitValueTaskCtorCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter, bool isGeneric)
    {
        var paramCount = methodRef.Parameters.Count;

        if (paramCount == 0)
        {
            // Default ctor: just pop the 'this' address (already zero-initialized)
            var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
            return true;
        }

        if (paramCount == 1)
        {
            var paramType = methodRef.Parameters[0].ParameterType;
            if (paramType.FullName == "System.Threading.Tasks.Task" ||
                paramType.FullName.StartsWith("System.Threading.Tasks.Task`"))
            {
                var task = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = thisAddr.StartsWith("&") ? $"({thisAddr})" : thisAddr;
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{w}->f_task = {task};"
                });
                return true;
            }
            if (isGeneric)
            {
                // ValueTask<T>(T result)
                var result = stack.Count > 0 ? stack.Pop() : "0";
                var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var w = thisAddr.StartsWith("&") ? $"({thisAddr})" : thisAddr;
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{w}->f_result = {result};"
                });
                return true;
            }
        }

        if (paramCount == 2)
        {
            // ValueTask(IValueTaskSource, short) or ValueTask<T>(IValueTaskSource<T>, short)
            var token = stack.Count > 0 ? stack.Pop() : "0";
            var source = stack.Count > 0 ? stack.Pop() : "nullptr";
            var thisAddr = stack.Count > 0 ? stack.Pop() : "nullptr";
            var w = thisAddr.StartsWith("&") ? $"({thisAddr})" : thisAddr;
            // Grab the task from TLS (stored by ManualResetValueTaskSourceCore.Reset)
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"{w}->f_task = reinterpret_cast<decltype({w}->f_task)>(cil2cpp::g_async_iter_current_task);"
            });
            return true;
        }

        return false;
    }

    // ── ExceptionDispatchInfo interception ────────────────────────

    /// <summary>
    /// Check if a method call is on ExceptionDispatchInfo.
    /// </summary>
    internal static bool IsExceptionDispatchInfoType(TypeReference typeRef)
    {
        return typeRef.FullName == "System.Runtime.ExceptionServices.ExceptionDispatchInfo";
    }

    /// <summary>
    /// Handle ExceptionDispatchInfo.Capture() and .Throw() calls.
    /// </summary>
    internal bool TryEmitExceptionDispatchInfoCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsExceptionDispatchInfoType(methodRef.DeclaringType)) return false;

        switch (methodRef.Name)
        {
            case "Capture":
            {
                // static Capture(Exception) → ExceptionDispatchInfo
                // We wrap the exception in an ExceptionDispatchInfo object
                var ex = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                var ediCpp = "System_Runtime_ExceptionServices_ExceptionDispatchInfo";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{ediCpp}* {tmp} = static_cast<{ediCpp}*>(cil2cpp::gc::alloc(sizeof({ediCpp}), nullptr)); " +
                           $"{tmp}->f_exception = static_cast<cil2cpp::Exception*>({ex});"
                });
                stack.Push(tmp);
                return true;
            }
            case "Throw":
            {
                // instance Throw() → rethrow the captured exception
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::throw_exception(static_cast<cil2cpp::Exception*>({thisExpr}->f_exception));"
                });
                return true;
            }
            case "get_SourceException":
            {
                // get_SourceException → return the captured exception
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisExpr}->f_exception;"
                });
                stack.Push(tmp);
                return true;
            }
            default:
                return false;
        }
    }
}
