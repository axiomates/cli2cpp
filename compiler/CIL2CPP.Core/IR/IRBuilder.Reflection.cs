using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// System.Type method interception for reflection support.
/// Intercepts property accessors, equality operators, and methods on System.Type
/// and emits calls to runtime reflection functions.
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

        // Handle System.Type and its base class System.Reflection.MemberInfo.
        // Properties like Name are declared on MemberInfo, so Roslyn emits
        // callvirt MemberInfo::get_Name() even when the receiver is a Type.
        if (declType != "System.Type" && declType != "System.Reflection.MemberInfo") return false;

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
                return EmitTypePropertyCall(block, stack, "cil2cpp::type_get_name", ref tempCounter);
            case "get_FullName":
                return EmitTypePropertyCall(block, stack, "cil2cpp::type_get_full_name", ref tempCounter);
            case "get_Namespace":
                return EmitTypePropertyCall(block, stack, "cil2cpp::type_get_namespace", ref tempCounter);
            case "get_BaseType":
                return EmitTypePropertyCall(block, stack, "cil2cpp::type_get_base_type", ref tempCounter);
            case "get_IsValueType":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_value_type", ref tempCounter);
            case "get_IsInterface":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_interface", ref tempCounter);
            case "get_IsAbstract":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_abstract", ref tempCounter);
            case "get_IsSealed":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_sealed", ref tempCounter);
            case "get_IsEnum":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_enum", ref tempCounter);
            case "get_IsArray":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_array", ref tempCounter);
            case "get_IsClass":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_class", ref tempCounter);
            case "get_IsPrimitive":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_primitive", ref tempCounter);
            case "get_IsGenericType":
                return EmitTypeBoolPropertyCall(block, stack, "cil2cpp::type_get_is_generic_type", ref tempCounter);
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
                // static Type GetTypeFromHandle(RuntimeTypeHandle) — already handled by ICallRegistry
                // but intercept here as well for robustness
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
        }

        return false;
    }

    /// <summary>
    /// Emit a Type property accessor that returns a reference type (String*, Type*).
    /// </summary>
    private static bool EmitTypePropertyCall(IRBasicBlock block, Stack<string> stack,
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
    private static bool EmitTypeBoolPropertyCall(IRBasicBlock block, Stack<string> stack,
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
}
