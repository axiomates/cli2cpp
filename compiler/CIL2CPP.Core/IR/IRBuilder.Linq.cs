using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// LINQ extension method interception (System.Linq.Enumerable).
/// Generates inline C++ code for the most common LINQ operations.
/// Operates eagerly (no deferred execution) on arrays and collections.
/// </summary>
public partial class IRBuilder
{
    private static bool IsLinqEnumerableType(TypeReference typeRef)
    {
        return typeRef.FullName == "System.Linq.Enumerable";
    }

    /// <summary>
    /// Intercept System.Linq.Enumerable extension method calls.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitLinqCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (!IsLinqEnumerableType(methodRef.DeclaringType)) return false;

        // Determine element type — from generic method args or from parameter type
        var gim = methodRef as GenericInstanceMethod;
        string? elemTypeCpp = null;
        string? elemTypeIL = null;
        string? resultTypeCpp = null;

        if (gim != null && gim.GenericArguments.Count > 0)
        {
            elemTypeIL = ResolveTypeRefOperand(gim.GenericArguments[0]);
            elemTypeCpp = CppNameMapper.GetCppTypeName(elemTypeIL);
            if (gim.GenericArguments.Count > 1)
            {
                var rIL = ResolveTypeRefOperand(gim.GenericArguments[1]);
                resultTypeCpp = CppNameMapper.GetCppTypeName(rIL);
            }
        }

        // Fallback: extract element type from first parameter's generic arg
        // (for non-generic overloads like Sum(IEnumerable<int>), Min, Max)
        if (elemTypeCpp == null && methodRef.Parameters.Count >= 1)
        {
            var paramType = methodRef.Parameters[0].ParameterType;
            if (paramType is GenericInstanceType git && git.GenericArguments.Count > 0)
            {
                elemTypeIL = ResolveTypeRefOperand(git.GenericArguments[0]);
                elemTypeCpp = CppNameMapper.GetCppTypeName(elemTypeIL);
            }
        }

        bool elemIsValue = elemTypeIL != null && CppNameMapper.IsValueType(elemTypeIL);

        switch (methodRef.Name)
        {
            case "Count" when methodRef.Parameters.Count == 1:
                return EmitLinqCount(block, stack, ref tempCounter);

            case "Count" when methodRef.Parameters.Count == 2:
                return EmitLinqCountPredicate(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "Any" when methodRef.Parameters.Count == 1:
                return EmitLinqAnyNoArg(block, stack, ref tempCounter);

            case "Any" when methodRef.Parameters.Count == 2:
                return EmitLinqAnyPredicate(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "All" when methodRef.Parameters.Count == 2:
                return EmitLinqAll(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "First" when methodRef.Parameters.Count == 1:
                return EmitLinqFirst(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "FirstOrDefault" when methodRef.Parameters.Count == 1:
                return EmitLinqFirstOrDefault(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "Last" when methodRef.Parameters.Count == 1:
                return EmitLinqLast(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "Sum" when methodRef.Parameters.Count == 1 && elemTypeCpp is "int32_t" or "int64_t" or "double" or "float":
                return EmitLinqSum(block, stack, ref tempCounter, elemTypeCpp!);

            case "Min" when methodRef.Parameters.Count == 1 && elemTypeCpp is "int32_t" or "int64_t" or "double" or "float":
                return EmitLinqMinMax(block, stack, ref tempCounter, elemTypeCpp!, isMin: true);

            case "Max" when methodRef.Parameters.Count == 1 && elemTypeCpp is "int32_t" or "int64_t" or "double" or "float":
                return EmitLinqMinMax(block, stack, ref tempCounter, elemTypeCpp!, isMin: false);

            case "ToArray" when methodRef.Parameters.Count == 1:
                return EmitLinqToArray(block, stack, ref tempCounter, elemTypeCpp);

            case "ToList" when methodRef.Parameters.Count == 1:
                return EmitLinqToList(block, stack, ref tempCounter, elemTypeCpp, elemTypeIL);

            case "Where" when methodRef.Parameters.Count == 2:
                return EmitLinqWhere(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "Select" when methodRef.Parameters.Count == 2 && resultTypeCpp != null:
                return EmitLinqSelect(block, stack, ref tempCounter,
                    elemTypeCpp, elemIsValue, resultTypeCpp);

            case "Contains" when methodRef.Parameters.Count == 2:
                return EmitLinqContains(block, stack, ref tempCounter, elemTypeCpp, elemIsValue);

            case "Reverse" when methodRef.Parameters.Count == 1:
                return EmitLinqReverse(block, stack, ref tempCounter, elemTypeCpp);
        }

        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────
    // All generated code uses FLAT layout (no compound blocks wrapping __tN).
    // __tN assignment is always on a separate IRRawCpp so AddAutoDeclarations
    // can correctly prepend `auto`.
    // Loop variables use __linq_{prefix}{id} to avoid collisions.

    private static string CastToArray(string sourceExpr)
        => $"reinterpret_cast<cil2cpp::Array*>({sourceExpr})";

    private static string ArrElem(string arrVar, string idxVar, string elemTypeCpp)
        => $"static_cast<{elemTypeCpp}*>(cil2cpp::array_data({arrVar}))[{idxVar}]";

    /// <summary>
    /// Generate inline C++ for calling a delegate with one argument.
    /// Handles target (instance) vs no-target (static) dispatch.
    /// </summary>
    private static string DelegateCallStmt(string delVar, string argExpr,
        string paramTypeCpp, string returnTypeCpp, string resultVar)
    {
        var instFn = $"{returnTypeCpp}(*)(cil2cpp::Object*, {paramTypeCpp})";
        var staticFn = $"{returnTypeCpp}(*)({paramTypeCpp})";
        return $"if ({delVar}->target) {resultVar} = (({instFn})({delVar}->method_ptr))({delVar}->target, {argExpr}); " +
               $"else {resultVar} = (({staticFn})({delVar}->method_ptr))({argExpr});";
    }

    // ── Terminal operations ────────────────────────────────────

    private bool EmitLinqCount(IRBasicBlock block, Stack<string> stack, ref int tempCounter)
    {
        var src = stack.Pop();
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = cil2cpp::array_length({CastToArray(src)});"
        });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqCountPredicate(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var pred = stack.Pop();
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var del = $"__linq_d{id}";
        var cnt = $"__linq_c{id}";
        var idx = $"__linq_i{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {del} = (cil2cpp::Delegate*)({pred}); " +
                $"int32_t {cnt} = 0; " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) {{ " +
                $"auto __e = {ArrElem(arr, idx, elemTypeCpp)}; " +
                $"bool __r; {DelegateCallStmt(del, "__e", elemTypeCpp, "bool", "__r")} " +
                $"if (__r) {cnt}++; }}"
        });
        block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {cnt};" });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqAnyNoArg(IRBasicBlock block, Stack<string> stack, ref int tempCounter)
    {
        var src = stack.Pop();
        var tmp = $"__t{tempCounter++}";
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = (cil2cpp::array_length({CastToArray(src)}) > 0);"
        });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqAnyPredicate(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var pred = stack.Pop();
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var del = $"__linq_d{id}";
        var found = $"__linq_f{id}";
        var idx = $"__linq_i{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {del} = (cil2cpp::Delegate*)({pred}); " +
                $"bool {found} = false; " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) {{ " +
                $"auto __e = {ArrElem(arr, idx, elemTypeCpp)}; " +
                $"bool __r; {DelegateCallStmt(del, "__e", elemTypeCpp, "bool", "__r")} " +
                $"if (__r) {{ {found} = true; break; }} }}"
        });
        block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {found};" });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqAll(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var pred = stack.Pop();
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var del = $"__linq_d{id}";
        var ok = $"__linq_ok{id}";
        var idx = $"__linq_i{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {del} = (cil2cpp::Delegate*)({pred}); " +
                $"bool {ok} = true; " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) {{ " +
                $"auto __e = {ArrElem(arr, idx, elemTypeCpp)}; " +
                $"bool __r; {DelegateCallStmt(del, "__e", elemTypeCpp, "bool", "__r")} " +
                $"if (!__r) {{ {ok} = false; break; }} }}"
        });
        block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {ok};" });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqFirst(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"if ({arr}->length == 0) cil2cpp::throw_invalid_operation();"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = {ArrElem(arr, "0", elemTypeCpp)};"
        });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqFirstOrDefault(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var defVal = CppNameMapper.GetDefaultValue(elemTypeCpp);

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)};"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = ({arr}->length > 0) ? {ArrElem(arr, "0", elemTypeCpp)} : {defVal};"
        });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqLast(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"if ({arr}->length == 0) cil2cpp::throw_invalid_operation();"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = {ArrElem(arr, $"({arr}->length - 1)", elemTypeCpp)};"
        });
        stack.Push(tmp);
        return true;
    }

    // ── Aggregate operations ────────────────────────────────────

    private bool EmitLinqSum(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string elemTypeCpp)
    {
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var sum = $"__linq_s{id}";
        var idx = $"__linq_i{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"{elemTypeCpp} {sum} = 0; " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) " +
                $"{sum} += static_cast<{elemTypeCpp}*>(cil2cpp::array_data({arr}))[{idx}];"
        });
        block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {sum};" });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqMinMax(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string elemTypeCpp, bool isMin)
    {
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var val = $"__linq_v{id}";
        var idx = $"__linq_i{id}";
        var op = isMin ? "<" : ">";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"if ({arr}->length == 0) cil2cpp::throw_invalid_operation(); " +
                $"{elemTypeCpp} {val} = static_cast<{elemTypeCpp}*>(cil2cpp::array_data({arr}))[0]; " +
                $"for (int32_t {idx} = 1; {idx} < {arr}->length; {idx}++) {{ " +
                $"auto __v = static_cast<{elemTypeCpp}*>(cil2cpp::array_data({arr}))[{idx}]; " +
                $"if (__v {op} {val}) {val} = __v; }}"
        });
        block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {val};" });
        stack.Push(tmp);
        return true;
    }

    // ── Transformation operations ──────────────────────────────

    private bool EmitLinqToArray(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp)
    {
        if (elemTypeCpp == null) return false;
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var res = $"__linq_r{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {res} = cil2cpp::array_create({arr}->element_type, {arr}->length); " +
                $"std::memcpy(cil2cpp::array_data({res}), cil2cpp::array_data({arr}), " +
                $"{arr}->length * sizeof({elemTypeCpp}));"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = reinterpret_cast<cil2cpp::Array*>({res});"
        });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqToList(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, string? elemTypeIL)
    {
        if (elemTypeCpp == null || elemTypeIL == null) return false;
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var list = $"__linq_l{id}";
        var idx = $"__linq_i{id}";
        var listType = CppNameMapper.MangleGenericInstanceTypeName(
            "System.Collections.Generic.List`1", new List<string> { elemTypeIL });

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {list} = static_cast<{listType}*>(cil2cpp::list_create(&{listType}_TypeInfo, {arr}->element_type, {arr}->length)); " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) {{ " +
                $"auto __e = {ArrElem(arr, idx, elemTypeCpp)}; " +
                $"cil2cpp::list_add({list}, &__e); }}"
        });
        block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {list};" });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqWhere(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var pred = stack.Pop();
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var del = $"__linq_d{id}";
        var cnt = $"__linq_c{id}";
        var res = $"__linq_r{id}";
        var wi = $"__linq_wi{id}";
        var idx = $"__linq_i{id}";

        // Two-pass: count matches, then allocate and fill
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {del} = (cil2cpp::Delegate*)({pred}); " +
                $"int32_t {cnt} = 0; " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) {{ " +
                $"auto __e = {ArrElem(arr, idx, elemTypeCpp)}; " +
                $"bool __r; {DelegateCallStmt(del, "__e", elemTypeCpp, "bool", "__r")} " +
                $"if (__r) {cnt}++; }} " +
                $"auto* {res} = cil2cpp::array_create({arr}->element_type, {cnt}); " +
                $"int32_t {wi} = 0; " +
                $"for (int32_t {idx}2 = 0; {idx}2 < {arr}->length; {idx}2++) {{ " +
                $"auto __e = {ArrElem(arr, $"{idx}2", elemTypeCpp)}; " +
                $"bool __r; {DelegateCallStmt(del, "__e", elemTypeCpp, "bool", "__r")} " +
                $"if (__r) static_cast<{elemTypeCpp}*>(cil2cpp::array_data({res}))[{wi}++] = __e; }}"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = reinterpret_cast<cil2cpp::Array*>({res});"
        });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqSelect(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter,
        string? elemTypeCpp, bool elemIsValue, string? resultTypeCpp)
    {
        if (elemTypeCpp == null || resultTypeCpp == null) return false;
        var sel = stack.Pop();
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var del = $"__linq_d{id}";
        var res = $"__linq_r{id}";
        var idx = $"__linq_i{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {del} = (cil2cpp::Delegate*)({sel}); " +
                $"auto* {res} = cil2cpp::array_create(nullptr, {arr}->length); " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) {{ " +
                $"auto __e = {ArrElem(arr, idx, elemTypeCpp)}; " +
                $"{resultTypeCpp} __r; {DelegateCallStmt(del, "__e", elemTypeCpp, resultTypeCpp, "__r")} " +
                $"static_cast<{resultTypeCpp}*>(cil2cpp::array_data({res}))[{idx}] = __r; }}"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = reinterpret_cast<cil2cpp::Array*>({res});"
        });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqContains(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp, bool elemIsValue)
    {
        if (elemTypeCpp == null) return false;
        var val = stack.Pop();
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var found = $"__linq_f{id}";
        var idx = $"__linq_i{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"bool {found} = false; " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) {{ " +
                $"if ({ArrElem(arr, idx, elemTypeCpp)} == {val}) {{ {found} = true; break; }} }}"
        });
        block.Instructions.Add(new IRRawCpp { Code = $"{tmp} = {found};" });
        stack.Push(tmp);
        return true;
    }

    private bool EmitLinqReverse(IRBasicBlock block, Stack<string> stack,
        ref int tempCounter, string? elemTypeCpp)
    {
        if (elemTypeCpp == null) return false;
        var src = stack.Pop();
        var id = tempCounter;
        var tmp = $"__t{tempCounter++}";
        var arr = $"__linq_a{id}";
        var res = $"__linq_r{id}";
        var idx = $"__linq_i{id}";

        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto* {arr} = {CastToArray(src)}; " +
                $"auto* {res} = cil2cpp::array_create({arr}->element_type, {arr}->length); " +
                $"for (int32_t {idx} = 0; {idx} < {arr}->length; {idx}++) " +
                $"static_cast<{elemTypeCpp}*>(cil2cpp::array_data({res}))[{idx}] = " +
                $"static_cast<{elemTypeCpp}*>(cil2cpp::array_data({arr}))[{arr}->length - 1 - {idx}];"
        });
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"{tmp} = reinterpret_cast<cil2cpp::Array*>({res});"
        });
        stack.Push(tmp);
        return true;
    }
}
