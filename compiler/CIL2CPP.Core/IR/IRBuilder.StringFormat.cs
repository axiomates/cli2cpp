using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// String.Format interception — packs variadic args into an Object[] array
/// then calls cil2cpp::string_format(format, argsArray).
/// </summary>
public partial class IRBuilder
{
    private bool TryEmitStringFormatCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (methodRef.DeclaringType.FullName != "System.String" || methodRef.Name != "Format")
            return false;

        // String.Format overloads:
        //   Format(string, object)        — 2 params
        //   Format(string, object, object) — 3 params
        //   Format(string, object, object, object) — 4 params
        //   Format(string, object[])       — 2 params (array overload)
        //   Format(IFormatProvider, string, ...) — skip provider overloads
        int paramCount = methodRef.Parameters.Count;

        // Skip IFormatProvider overloads (first param is IFormatProvider)
        if (paramCount >= 2 &&
            methodRef.Parameters[0].ParameterType.FullName == "System.IFormatProvider")
            return false;

        if (paramCount == 2)
        {
            // Could be (string, object) or (string, object[])
            var secondParam = methodRef.Parameters[1].ParameterType;
            if (secondParam.FullName == "System.Object[]" || secondParam.IsArray)
            {
                // String.Format(string, object[]) — array already on stack
                var argsExpr = stack.Pop();
                var fmtExpr = stack.Pop();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto* __fmt_arr = reinterpret_cast<cil2cpp::Array*>({argsExpr});"
                });
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{tmp} = cil2cpp::string_format({fmtExpr}, __fmt_arr);"
                });
                stack.Push(tmp);
                return true;
            }
            else
            {
                // String.Format(string, object) — pack single arg
                return EmitStringFormatPack(block, stack, ref tempCounter, 1);
            }
        }
        else if (paramCount == 3)
        {
            return EmitStringFormatPack(block, stack, ref tempCounter, 2);
        }
        else if (paramCount == 4)
        {
            return EmitStringFormatPack(block, stack, ref tempCounter, 3);
        }

        return false;
    }

    /// <summary>
    /// Pack N object args into an Object[] array and call string_format.
    /// </summary>
    private bool EmitStringFormatPack(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, int argCount)
    {
        // Pop args in reverse order (last arg on top)
        var args = new string[argCount];
        for (int i = argCount - 1; i >= 0; i--)
            args[i] = stack.Pop();
        var fmtExpr = stack.Pop();

        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arrVar = $"__fmt_a{id}";

        // Create Object[] array and fill
        var code = $"auto* {arrVar} = cil2cpp::array_create(nullptr, {argCount}); ";
        for (int i = 0; i < argCount; i++)
        {
            code += $"static_cast<cil2cpp::Object**>(cil2cpp::array_data({arrVar}))[{i}] = " +
                    $"(cil2cpp::Object*)({args[i]}); ";
        }
        block.Instructions.Add(new IRRawCpp { Code = code });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = cil2cpp::string_format({fmtExpr}, {arrVar});"
        });
        stack.Push(tmp);
        return true;
    }
}
