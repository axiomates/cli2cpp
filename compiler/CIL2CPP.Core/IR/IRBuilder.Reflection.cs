using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// System.Type, System.Reflection.MethodInfo, FieldInfo, ParameterInfo interception.
/// Intercepts reflection API calls and emits calls to runtime reflection functions.
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Handle calls to System.Type methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitTypeCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var declType = methodRef.DeclaringType.FullName;

        // Dispatch to the appropriate handler based on declaring type
        if (declType is "System.Type")
            return TryEmitTypePropertyCall(block, stack, methodRef, ref tempCounter);
        if (declType is "System.Reflection.MemberInfo")
            return TryEmitMemberInfoCall(block, stack, methodRef, ref tempCounter);
        if (declType is "System.Reflection.MethodInfo" or "System.Reflection.MethodBase")
            return TryEmitMethodInfoCall(block, stack, methodRef, ref tempCounter);
        if (declType is "System.Reflection.FieldInfo")
            return TryEmitFieldInfoCall(block, stack, methodRef, ref tempCounter);
        if (declType is "System.Reflection.ParameterInfo")
            return TryEmitParameterInfoCall(block, stack, methodRef, ref tempCounter);

        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  System.Reflection.MemberInfo interception (universal dispatch)
    //  Called when the IL declaring type is MemberInfo but receiver
    //  could be Type, MethodInfo, or FieldInfo.
    // ════════════════════════════════════════════════════════════════

    private bool TryEmitMemberInfoCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var name = methodRef.Name;

        switch (name)
        {
            case "get_Name":
            {
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::memberinfo_get_name(" +
                           $"reinterpret_cast<cil2cpp::Object*>({thisArg}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_DeclaringType":
            {
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                           $"cil2cpp::memberinfo_get_declaring_type(" +
                           $"reinterpret_cast<cil2cpp::Object*>({thisArg})));"
                });
                stack.Push(tmp);
                return true;
            }
        }

        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  System.Type interception
    // ════════════════════════════════════════════════════════════════

    private bool TryEmitTypePropertyCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var name = methodRef.Name;

        // Static operators: op_Equality / op_Inequality (2 args, no this)
        if (name == "op_Equality" && methodRef.Parameters.Count == 2)
        {
            var right = stack.Count > 0 ? stack.Pop() : "nullptr";
            var left = stack.Count > 0 ? stack.Pop() : "nullptr";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRBinaryOp
            {
                Left = left, Right = right, Op = "==", ResultVar = tmp
            });
            stack.Push(tmp);
            return true;
        }
        if (name == "op_Inequality" && methodRef.Parameters.Count == 2)
        {
            var right = stack.Count > 0 ? stack.Pop() : "nullptr";
            var left = stack.Count > 0 ? stack.Pop() : "nullptr";
            var tmp = $"__t{tempCounter++}";
            block.Instructions.Add(new IRBinaryOp
            {
                Left = left, Right = right, Op = "!=", ResultVar = tmp
            });
            stack.Push(tmp);
            return true;
        }

        // Instance property accessors (takes 'this' from stack, returns value)
        switch (name)
        {
            case "get_Name":
                return EmitTypePropRef(block, stack, "cil2cpp::type_get_name", ref tempCounter);
            case "get_FullName":
                return EmitTypePropRef(block, stack, "cil2cpp::type_get_full_name", ref tempCounter);
            case "get_Namespace":
                return EmitTypePropRef(block, stack, "cil2cpp::type_get_namespace", ref tempCounter);
            case "get_BaseType":
                return EmitTypePropRef(block, stack, "cil2cpp::type_get_base_type", ref tempCounter);
            case "get_IsValueType":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_value_type", ref tempCounter);
            case "get_IsInterface":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_interface", ref tempCounter);
            case "get_IsAbstract":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_abstract", ref tempCounter);
            case "get_IsSealed":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_sealed", ref tempCounter);
            case "get_IsEnum":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_enum", ref tempCounter);
            case "get_IsArray":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_array", ref tempCounter);
            case "get_IsClass":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_class", ref tempCounter);
            case "get_IsPrimitive":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_primitive", ref tempCounter);
            case "get_IsGenericType":
                return EmitTypePropBool(block, stack, "cil2cpp::type_get_is_generic_type", ref tempCounter);
        }

        // Instance methods with parameters
        switch (name)
        {
            case "IsAssignableFrom":
            {
                var otherType = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::type_is_assignable_from_managed(" +
                           $"reinterpret_cast<cil2cpp::Type*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::Type*>({otherType}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "IsSubclassOf":
            {
                var otherType = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::type_is_subclass_of_managed(" +
                           $"reinterpret_cast<cil2cpp::Type*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::Type*>({otherType}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "Equals":
            {
                var other = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::type_equals(" +
                           $"reinterpret_cast<cil2cpp::Type*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::Object*>({other}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "ToString":
            {
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::type_to_string(" +
                           $"reinterpret_cast<cil2cpp::Type*>({thisArg}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetTypeFromHandle":
            {
                // static Type GetTypeFromHandle(RuntimeTypeHandle) — also handled by ICallRegistry
                var handle = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                           $"cil2cpp::type_get_type_from_handle(reinterpret_cast<void*>({handle})));"
                });
                stack.Push(tmp);
                return true;
            }
            // ── New: GetMethods / GetFields / GetMethod / GetField ──
            case "GetMethods":
            {
                // GetMethods() or GetMethods(BindingFlags) — we ignore BindingFlags for now
                if (methodRef.Parameters.Count > 0)
                {
                    var flags = stack.Count > 0 ? stack.Pop() : "0";
                }
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                           $"cil2cpp::type_get_methods(reinterpret_cast<cil2cpp::Type*>({thisArg})));"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetFields":
            {
                if (methodRef.Parameters.Count > 0)
                {
                    var flags = stack.Count > 0 ? stack.Pop() : "0";
                }
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                           $"cil2cpp::type_get_fields(reinterpret_cast<cil2cpp::Type*>({thisArg})));"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetMethod":
            {
                // GetMethod(string) — simple name-based lookup
                // GetMethod(string, BindingFlags) — ignore flags
                // GetMethod(string, Type[]) — ignore param types
                // Pop extra parameters beyond name
                for (int i = methodRef.Parameters.Count - 1; i > 0; i--)
                {
                    if (stack.Count > 0) stack.Pop();
                }
                var nameArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                           $"cil2cpp::type_get_method(reinterpret_cast<cil2cpp::Type*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::String*>({nameArg})));"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetField":
            {
                // GetField(string) or GetField(string, BindingFlags)
                if (methodRef.Parameters.Count > 1)
                {
                    var flags = stack.Count > 0 ? stack.Pop() : "0";
                }
                var nameArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                           $"cil2cpp::type_get_field(reinterpret_cast<cil2cpp::Type*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::String*>({nameArg})));"
                });
                stack.Push(tmp);
                return true;
            }
        }

        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  System.Reflection.MethodInfo / MethodBase interception
    // ════════════════════════════════════════════════════════════════

    private bool TryEmitMethodInfoCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var name = methodRef.Name;

        // Pointer equality/inequality operators
        if (name == "op_Equality" && methodRef.Parameters.Count == 2)
            return EmitPtrOp(block, stack, "==", ref tempCounter);
        if (name == "op_Inequality" && methodRef.Parameters.Count == 2)
            return EmitPtrOp(block, stack, "!=", ref tempCounter);

        switch (name)
        {
            case "get_Name":
                return EmitMemberPropRef(block, stack, "cil2cpp::methodinfo_get_name",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "get_DeclaringType":
                return EmitMemberPropRef(block, stack, "cil2cpp::methodinfo_get_declaring_type",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "get_ReturnType":
                return EmitMemberPropRef(block, stack, "cil2cpp::methodinfo_get_return_type",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "get_IsPublic":
                return EmitMemberPropBool(block, stack, "cil2cpp::methodinfo_get_is_public",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "get_IsStatic":
                return EmitMemberPropBool(block, stack, "cil2cpp::methodinfo_get_is_static",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "get_IsVirtual":
                return EmitMemberPropBool(block, stack, "cil2cpp::methodinfo_get_is_virtual",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "get_IsAbstract":
                return EmitMemberPropBool(block, stack, "cil2cpp::methodinfo_get_is_abstract",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "ToString":
                return EmitMemberPropRef(block, stack, "cil2cpp::methodinfo_to_string",
                    "cil2cpp::ManagedMethodInfo", ref tempCounter);
            case "GetParameters":
            {
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = reinterpret_cast<cil2cpp::Object*>(" +
                           $"cil2cpp::methodinfo_get_parameters(" +
                           $"reinterpret_cast<cil2cpp::ManagedMethodInfo*>({thisArg})));"
                });
                stack.Push(tmp);
                return true;
            }
            case "Invoke":
            {
                // Invoke(object obj, object[] parameters) → object
                var parameters = stack.Count > 0 ? stack.Pop() : "nullptr";
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::methodinfo_invoke(" +
                           $"reinterpret_cast<cil2cpp::ManagedMethodInfo*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::Object*>({obj}), " +
                           $"reinterpret_cast<cil2cpp::Array*>({parameters}));"
                });
                stack.Push(tmp);
                return true;
            }
        }

        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  System.Reflection.FieldInfo interception
    // ════════════════════════════════════════════════════════════════

    private bool TryEmitFieldInfoCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var name = methodRef.Name;

        if (name == "op_Equality" && methodRef.Parameters.Count == 2)
            return EmitPtrOp(block, stack, "==", ref tempCounter);
        if (name == "op_Inequality" && methodRef.Parameters.Count == 2)
            return EmitPtrOp(block, stack, "!=", ref tempCounter);

        switch (name)
        {
            case "get_Name":
                return EmitMemberPropRef(block, stack, "cil2cpp::fieldinfo_get_name",
                    "cil2cpp::ManagedFieldInfo", ref tempCounter);
            case "get_DeclaringType":
                return EmitMemberPropRef(block, stack, "cil2cpp::fieldinfo_get_declaring_type",
                    "cil2cpp::ManagedFieldInfo", ref tempCounter);
            case "get_FieldType":
                return EmitMemberPropRef(block, stack, "cil2cpp::fieldinfo_get_field_type",
                    "cil2cpp::ManagedFieldInfo", ref tempCounter);
            case "get_IsPublic":
                return EmitMemberPropBool(block, stack, "cil2cpp::fieldinfo_get_is_public",
                    "cil2cpp::ManagedFieldInfo", ref tempCounter);
            case "get_IsStatic":
                return EmitMemberPropBool(block, stack, "cil2cpp::fieldinfo_get_is_static",
                    "cil2cpp::ManagedFieldInfo", ref tempCounter);
            case "get_IsInitOnly":
                return EmitMemberPropBool(block, stack, "cil2cpp::fieldinfo_get_is_init_only",
                    "cil2cpp::ManagedFieldInfo", ref tempCounter);
            case "ToString":
                return EmitMemberPropRef(block, stack, "cil2cpp::fieldinfo_to_string",
                    "cil2cpp::ManagedFieldInfo", ref tempCounter);
            case "GetValue":
            {
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = cil2cpp::fieldinfo_get_value(" +
                           $"reinterpret_cast<cil2cpp::ManagedFieldInfo*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::Object*>({obj}));"
                });
                stack.Push(tmp);
                return true;
            }
            case "SetValue":
            {
                var value = stack.Count > 0 ? stack.Pop() : "nullptr";
                var obj = stack.Count > 0 ? stack.Pop() : "nullptr";
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"cil2cpp::fieldinfo_set_value(" +
                           $"reinterpret_cast<cil2cpp::ManagedFieldInfo*>({thisArg}), " +
                           $"reinterpret_cast<cil2cpp::Object*>({obj}), " +
                           $"reinterpret_cast<cil2cpp::Object*>({value}));"
                });
                return true;
            }
        }

        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  System.Reflection.ParameterInfo interception
    // ════════════════════════════════════════════════════════════════

    private bool TryEmitParameterInfoCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        var name = methodRef.Name;

        switch (name)
        {
            case "get_Name":
                return EmitMemberPropRef(block, stack, "cil2cpp::parameterinfo_get_name",
                    "cil2cpp::ManagedParameterInfo", ref tempCounter);
            case "get_ParameterType":
                return EmitMemberPropRef(block, stack, "cil2cpp::parameterinfo_get_parameter_type",
                    "cil2cpp::ManagedParameterInfo", ref tempCounter);
            case "get_Position":
            {
                var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t {tmp} = cil2cpp::parameterinfo_get_position(" +
                           $"reinterpret_cast<cil2cpp::ManagedParameterInfo*>({thisArg}));"
                });
                stack.Push(tmp);
                return true;
            }
        }

        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  Shared helpers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emit a pointer comparison operator (== or !=).
    /// </summary>
    private static bool EmitPtrOp(IRBasicBlock block, Stack<string> stack,
        string op, ref int tempCounter)
    {
        var right = stack.Count > 0 ? stack.Pop() : "nullptr";
        var left = stack.Count > 0 ? stack.Pop() : "nullptr";
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRBinaryOp
        {
            Left = left, Right = right, Op = op, ResultVar = tmp
        });
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Emit a Type property accessor that returns a reference type (String*, Type*).
    /// </summary>
    private static bool EmitTypePropRef(IRBasicBlock block, Stack<string> stack,
        string cppFunction, ref int tempCounter)
    {
        var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = {cppFunction}(reinterpret_cast<cil2cpp::Type*>({thisArg}));"
        });
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Emit a Type property accessor that returns a bool.
    /// </summary>
    private static bool EmitTypePropBool(IRBasicBlock block, Stack<string> stack,
        string cppFunction, ref int tempCounter)
    {
        var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = static_cast<bool>({cppFunction}(reinterpret_cast<cil2cpp::Type*>({thisArg})));"
        });
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Emit a MemberInfo-derived property accessor returning a reference type.
    /// </summary>
    private static bool EmitMemberPropRef(IRBasicBlock block, Stack<string> stack,
        string cppFunction, string cppCastType, ref int tempCounter)
    {
        var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = {cppFunction}(reinterpret_cast<{cppCastType}*>({thisArg}));"
        });
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Emit a MemberInfo-derived property accessor returning a bool.
    /// </summary>
    private static bool EmitMemberPropBool(IRBasicBlock block, Stack<string> stack,
        string cppFunction, string cppCastType, ref int tempCounter)
    {
        var thisArg = stack.Count > 0 ? stack.Pop() : "nullptr";
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = static_cast<bool>({cppFunction}(reinterpret_cast<{cppCastType}*>({thisArg})));"
        });
        stack.Push(tmp);
        return true;
    }
}
