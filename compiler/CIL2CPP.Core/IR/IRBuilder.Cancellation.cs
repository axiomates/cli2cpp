using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// CancellationToken/CancellationTokenSource/TaskCompletionSource interception.
/// These are BCL types whose method bodies reference deep internal types.
/// We intercept calls and emit inline C++ to route to runtime functions.
/// </summary>
public partial class IRBuilder
{
    // ── Type detection ────────────────────────────────────────

    private static bool IsCancellationTokenSourceType(TypeReference typeRef)
    {
        return typeRef.FullName == "System.Threading.CancellationTokenSource";
    }

    private static bool IsCancellationTokenType(TypeReference typeRef)
    {
        return typeRef.FullName == "System.Threading.CancellationToken";
    }

    private static bool IsTaskCompletionSourceType(TypeReference typeRef)
    {
        var name = typeRef is GenericInstanceType git
            ? git.ElementType.FullName : typeRef.FullName;
        return name is "System.Threading.Tasks.TaskCompletionSource`1";
    }

    internal static bool IsCancellationBclGenericType(string openTypeName)
    {
        return openTypeName.StartsWith("System.Threading.Tasks.TaskCompletionSource`");
    }

    // ── Synthetic type creation ───────────────────────────────

    /// <summary>
    /// Create synthetic IRTypes for CancellationTokenSource and CancellationToken.
    /// These BCL types are not in user assemblies but may be referenced by IL.
    /// </summary>
    private void CreateCancellationSyntheticTypes()
    {
        // CancellationTokenSource (reference type)
        if (!_typeCache.ContainsKey("System.Threading.CancellationTokenSource"))
        {
            var ctsType = new IRType
            {
                ILFullName = "System.Threading.CancellationTokenSource",
                CppName = "System_Threading_CancellationTokenSource",
                Name = "CancellationTokenSource",
                Namespace = "System.Threading",
                IsValueType = false,
                IsSealed = false,
                IsRuntimeProvided = true,
            };
            ctsType.Fields.Add(new IRField
            {
                Name = "_state",
                CppName = "f__state",
                FieldTypeName = "System.Int32",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = ctsType,
            });
            _module.Types.Add(ctsType);
            _typeCache["System.Threading.CancellationTokenSource"] = ctsType;
        }

        // CancellationToken (value type)
        if (!_typeCache.ContainsKey("System.Threading.CancellationToken"))
        {
            var ctType = new IRType
            {
                ILFullName = "System.Threading.CancellationToken",
                CppName = "System_Threading_CancellationToken",
                Name = "CancellationToken",
                Namespace = "System.Threading",
                IsValueType = true,
                IsSealed = true,
            };
            ctType.Fields.Add(new IRField
            {
                Name = "_source",
                CppName = "f__source",
                FieldTypeName = "System.Threading.CancellationTokenSource",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = ctType,
            });
            _module.Types.Add(ctType);
            _typeCache["System.Threading.CancellationToken"] = ctType;

            // Register as value type for GetDefaultValue
            CppNameMapper.RegisterValueType("System.Threading.CancellationToken");
            CppNameMapper.RegisterValueType("System_Threading_CancellationToken");
        }
    }

    // ── Method interceptions ──────────────────────────────────

    /// <summary>
    /// Intercept CancellationTokenSource method calls.
    /// </summary>
    private bool TryEmitCancellationTokenSourceCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsCancellationTokenSourceType(methodRef.DeclaringType)) return false;

        switch (methodRef.Name)
        {
            case "get_Token":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Threading_CancellationToken {tmp}; {tmp}.f__source = reinterpret_cast<cil2cpp::CancellationTokenSource*>({thisExpr});"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_IsCancellationRequested":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::cts_is_cancellation_requested(reinterpret_cast<cil2cpp::CancellationTokenSource*>({thisExpr}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "Cancel":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                // Cancel(bool) has 1 param — just pop it and ignore
                if (methodRef.Parameters.Count > 0 && stack.Count > 0) stack.Pop();
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::cts_cancel(reinterpret_cast<cil2cpp::CancellationTokenSource*>({thisExpr}));"
                });
                return true;
            }
            case "CancelAfter":
            {
                var millisecondsExpr = stack.Count > 0 ? stack.Pop() : "0";
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::cts_cancel_after(reinterpret_cast<cil2cpp::CancellationTokenSource*>({thisExpr}), {millisecondsExpr});"
                });
                return true;
            }
            case "Dispose":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::cts_dispose(reinterpret_cast<cil2cpp::CancellationTokenSource*>({thisExpr}));"
                });
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Intercept CancellationToken method calls.
    /// </summary>
    private bool TryEmitCancellationTokenCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsCancellationTokenType(methodRef.DeclaringType)) return false;

        switch (methodRef.Name)
        {
            case "get_IsCancellationRequested":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                // thisExpr is a pointer to CancellationToken (value type via ldloca)
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::ct_is_cancellation_requested(*reinterpret_cast<cil2cpp::CancellationToken*>({thisExpr}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_CanBeCanceled":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::ct_can_be_canceled(*reinterpret_cast<cil2cpp::CancellationToken*>({thisExpr}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "ThrowIfCancellationRequested":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::ct_throw_if_cancellation_requested(*reinterpret_cast<cil2cpp::CancellationToken*>({thisExpr}));"
                });
                return true;
            }
            case "get_None":
            {
                // Static method — no 'this'
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Threading_CancellationToken {tmp} = cil2cpp::ct_get_none();"
                });
                stack.Push(tmp);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Intercept TaskCompletionSource method calls.
    /// TCS wraps a Task — we route to tcs_* runtime functions.
    /// </summary>
    private bool TryEmitTaskCompletionSourceCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsTaskCompletionSourceType(methodRef.DeclaringType)) return false;

        // Determine the Task<T> result type for generic TCS<T>
        string? resultTypeCpp = null;
        if (methodRef.DeclaringType is GenericInstanceType git && git.GenericArguments.Count > 0)
        {
            resultTypeCpp = CppNameMapper.GetCppTypeName(git.GenericArguments[0].FullName);
        }

        switch (methodRef.Name)
        {
            case "get_Task":
            {
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                // TCS stores task in first field after Object header
                // The generated type has f_task field
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = ({thisExpr})->f_task;"
                });
                stack.Push(tmp);
                return true;
            }
            case "SetResult":
            {
                var resultExpr = stack.Count > 0 ? stack.Pop() : "0";
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                // Store result in task, then complete
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"({thisExpr})->f_task->f_result = {resultExpr}; cil2cpp::tcs_set_result(reinterpret_cast<cil2cpp::Task*>(({thisExpr})->f_task));"
                });
                return true;
            }
            case "SetException":
            {
                var exExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::tcs_set_exception(reinterpret_cast<cil2cpp::Task*>(({thisExpr})->f_task), reinterpret_cast<cil2cpp::Exception*>({exExpr}));"
                });
                return true;
            }
            case "TrySetResult":
            {
                var resultExpr = stack.Count > 0 ? stack.Pop() : "0";
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::tcs_try_set_result(reinterpret_cast<cil2cpp::Task*>(({thisExpr})->f_task)); if ({tmp}) ({thisExpr})->f_task->f_result = {resultExpr};"
                });
                stack.Push(tmp);
                return true;
            }
            case "TrySetException":
            {
                var exExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::tcs_try_set_exception(reinterpret_cast<cil2cpp::Task*>(({thisExpr})->f_task), reinterpret_cast<cil2cpp::Exception*>({exExpr}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "TrySetCanceled":
            {
                // Pop CancellationToken param if present
                if (methodRef.Parameters.Count > 0 && stack.Count > 0) stack.Pop();
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::tcs_try_set_canceled(reinterpret_cast<cil2cpp::Task*>(({thisExpr})->f_task));"
                });
                stack.Push(tmp);
                return true;
            }
            case "SetCanceled":
            {
                // Pop CancellationToken param if present
                if (methodRef.Parameters.Count > 0 && stack.Count > 0) stack.Pop();
                var thisExpr = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::tcs_set_canceled(reinterpret_cast<cil2cpp::Task*>(({thisExpr})->f_task));"
                });
                return true;
            }
        }

        return false;
    }

    // ── NewObj interceptions ──────────────────────────────────

    /// <summary>
    /// Intercept CancellationTokenSource constructor.
    /// </summary>
    private bool TryEmitCancellationTokenSourceNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsCancellationTokenSourceType(ctorRef.DeclaringType)) return false;

        var tmp = $"__t{tempCounter++}";
        // CTS() or CTS(int millisecondsDelay)
        if (ctorRef.Parameters.Count == 1)
        {
            var delay = stack.Count > 0 ? stack.Pop() : "0";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<System_Threading_CancellationTokenSource*>(cil2cpp::cts_create()); cil2cpp::cts_cancel_after(reinterpret_cast<cil2cpp::CancellationTokenSource*>({tmp}), {delay});"
            });
        }
        else
        {
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"auto {tmp} = reinterpret_cast<System_Threading_CancellationTokenSource*>(cil2cpp::cts_create());"
            });
        }
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Intercept TaskCompletionSource constructor.
    /// </summary>
    private bool TryEmitTaskCompletionSourceNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (!IsTaskCompletionSourceType(ctorRef.DeclaringType)) return false;

        // Pop constructor params (TaskCreationOptions, object state, etc.)
        for (int i = 0; i < ctorRef.Parameters.Count; i++)
        {
            if (stack.Count > 0) stack.Pop();
        }

        // Determine the generic Task<T> type
        var tcsType = ctorRef.DeclaringType;
        string taskTypeCpp = "System_Threading_Tasks_Task";
        if (tcsType is GenericInstanceType git && git.GenericArguments.Count > 0)
        {
            var taskFullName = $"System.Threading.Tasks.Task`1<{git.GenericArguments[0].FullName}>";
            taskTypeCpp = CppNameMapper.MangleGenericInstanceTypeName(
                "System.Threading.Tasks.Task`1",
                new[] { git.GenericArguments[0].FullName }.ToList());
        }

        // Determine the TCS mangled type name
        var tcsCppName = GetMangledTypeNameForRef(tcsType);

        var tmp = $"__t{tempCounter++}";
        // Allocate TCS object (reference type) + create pending task
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {tmp} = static_cast<{tcsCppName}*>(cil2cpp::gc::alloc(sizeof({tcsCppName}), &{tcsCppName}_TypeInfo));"
        });
        // Create pending task and store in f_task field
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp}->f_task = static_cast<{taskTypeCpp}*>(cil2cpp::gc::alloc(sizeof({taskTypeCpp}), &{taskTypeCpp}_TypeInfo)); cil2cpp::task_init_pending(reinterpret_cast<cil2cpp::Task*>({tmp}->f_task));"
        });

        stack.Push(tmp);
        return true;
    }
}
