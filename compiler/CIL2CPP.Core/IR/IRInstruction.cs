namespace CIL2CPP.Core.IR;

/// <summary>
/// Base class for all IR instructions.
/// </summary>
public abstract class IRInstruction
{
    /// <summary>Generate C++ code for this instruction.</summary>
    public abstract string ToCpp();

    /// <summary>Source location for debug mapping. Null in Release mode.</summary>
    public SourceLocation? DebugInfo { get; set; }
}

// ============ Concrete IR Instructions ============

public class IRComment : IRInstruction
{
    public string Text { get; set; } = "";
    public override string ToCpp() => $"// {Text}";
}

public class IRAssign : IRInstruction
{
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
    public override string ToCpp() => $"{Target} = {Value};";
}

public class IRDeclareLocal : IRInstruction
{
    public string TypeName { get; set; } = "";
    public string VarName { get; set; } = "";
    public string? InitValue { get; set; }
    public override string ToCpp() =>
        InitValue != null
            ? $"{TypeName} {VarName} = {InitValue};"
            : $"{TypeName} {VarName} = {{0}};";
}

public class IRReturn : IRInstruction
{
    public string? Value { get; set; }
    public override string ToCpp() =>
        Value != null ? $"return {Value};" : "return;";
}

public class IRCall : IRInstruction
{
    public string FunctionName { get; set; } = "";
    public List<string> Arguments { get; } = new();
    public string? ResultVar { get; set; }
    public bool IsVirtual { get; set; }
    public int VTableSlot { get; set; } = -1;
    public string? VTableReturnType { get; set; }
    public List<string>? VTableParamTypes { get; set; }
    public bool IsInterfaceCall { get; set; }
    public string? InterfaceTypeCppName { get; set; }

    public override string ToCpp()
    {
        var args = string.Join(", ", Arguments);
        string call;

        if (IsVirtual && IsInterfaceCall && VTableSlot >= 0 && Arguments.Count > 0)
        {
            var paramTypesStr = VTableParamTypes != null
                ? string.Join(", ", VTableParamTypes) : "void*";
            var fnPtrType = $"{VTableReturnType ?? "void"}(*)({paramTypesStr})";
            var thisExpr = Arguments[0];
            call = $"(({fnPtrType})(cil2cpp::type_get_interface_vtable_checked(((cil2cpp::Object*){thisExpr})->__type_info, &{InterfaceTypeCppName}_TypeInfo)->methods[{VTableSlot}]))({args})";
        }
        else if (IsVirtual && VTableSlot >= 0 && Arguments.Count > 0)
        {
            var paramTypesStr = VTableParamTypes != null
                ? string.Join(", ", VTableParamTypes) : "void*";
            var fnPtrType = $"{VTableReturnType ?? "void"}(*)({paramTypesStr})";
            var thisExpr = Arguments[0];
            call = $"(({fnPtrType})(((cil2cpp::Object*){thisExpr})->__type_info->vtable->methods[{VTableSlot}]))({args})";
        }
        else
        {
            call = $"{FunctionName}({args})";
        }

        return ResultVar != null ? $"{ResultVar} = {call};" : $"{call};";
    }
}

public class IRNewObj : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public string CtorName { get; set; } = "";
    public List<string> CtorArgs { get; } = new();
    public string ResultVar { get; set; } = "";

    public override string ToCpp()
    {
        var lines = new List<string>
        {
            $"{ResultVar} = ({TypeCppName}*)cil2cpp::gc::alloc(sizeof({TypeCppName}), &{TypeCppName}_TypeInfo);",
        };

        var allArgs = new List<string> { ResultVar };
        allArgs.AddRange(CtorArgs);
        lines.Add($"{CtorName}({string.Join(", ", allArgs)});");

        return string.Join("\n    ", lines);
    }
}

public class IRBinaryOp : IRInstruction
{
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
    public string Op { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() => $"{ResultVar} = {Left} {Op} {Right};";
}

public class IRUnaryOp : IRInstruction
{
    public string Operand { get; set; } = "";
    public string Op { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() => $"{ResultVar} = {Op}{Operand};";
}

public class IRBranch : IRInstruction
{
    public string TargetLabel { get; set; } = "";
    public override string ToCpp() => $"goto {TargetLabel};";
}

public class IRConditionalBranch : IRInstruction
{
    public string Condition { get; set; } = "";
    public string TrueLabel { get; set; } = "";
    public string? FalseLabel { get; set; }

    public override string ToCpp()
    {
        if (FalseLabel != null)
            return $"if ({Condition}) goto {TrueLabel}; else goto {FalseLabel};";
        return $"if ({Condition}) goto {TrueLabel};";
    }
}

public class IRLabel : IRInstruction
{
    public string LabelName { get; set; } = "";
    public override string ToCpp() => $"{LabelName}:";
}

public class IRSwitch : IRInstruction
{
    public string ValueExpr { get; set; } = "";
    public List<string> CaseLabels { get; } = new();

    public override string ToCpp()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"switch ({ValueExpr}) {{");
        for (int i = 0; i < CaseLabels.Count; i++)
            sb.AppendLine($"        case {i}: goto {CaseLabels[i]};");
        sb.Append("    }");
        return sb.ToString();
    }
}

public class IRFieldAccess : IRInstruction
{
    public string ObjectExpr { get; set; } = "";
    public string FieldCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }

    public override string ToCpp()
    {
        if (IsStore)
            return $"{ObjectExpr}->{FieldCppName} = {StoreValue};";
        return $"{ResultVar} = {ObjectExpr}->{FieldCppName};";
    }
}

public class IRStaticFieldAccess : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public string FieldCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }

    public override string ToCpp()
    {
        var fullName = $"{TypeCppName}_statics.{FieldCppName}";
        if (IsStore)
            return $"{fullName} = {StoreValue};";
        return $"{ResultVar} = {fullName};";
    }
}

public class IRArrayAccess : IRInstruction
{
    public string ArrayExpr { get; set; } = "";
    public string IndexExpr { get; set; } = "";
    public string ElementType { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsStore { get; set; }
    public string? StoreValue { get; set; }

    public override string ToCpp()
    {
        if (IsStore)
            return $"cil2cpp::array_set<{ElementType}>({ArrayExpr}, {IndexExpr}, {StoreValue});";
        return $"{ResultVar} = cil2cpp::array_get<{ElementType}>({ArrayExpr}, {IndexExpr});";
    }
}

public class IRCast : IRInstruction
{
    public string SourceExpr { get; set; } = "";
    public string TargetTypeCpp { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsSafe { get; set; } // 'as' vs cast

    public override string ToCpp()
    {
        if (IsSafe)
            return $"{ResultVar} = ({TargetTypeCpp})cil2cpp::object_as((cil2cpp::Object*){SourceExpr}, &{TargetTypeCpp.TrimEnd('*')}_TypeInfo);";
        return $"{ResultVar} = ({TargetTypeCpp})cil2cpp::object_cast((cil2cpp::Object*){SourceExpr}, &{TargetTypeCpp.TrimEnd('*')}_TypeInfo);";
    }
}

public class IRConversion : IRInstruction
{
    public string SourceExpr { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() => $"{ResultVar} = static_cast<{TargetType}>({SourceExpr});";
}

public class IRNullCheck : IRInstruction
{
    public string Expr { get; set; } = "";
    public override string ToCpp() => $"cil2cpp::null_check({Expr});";
}

public class IRInitObj : IRInstruction
{
    public string AddressExpr { get; set; } = "";
    public string TypeCppName { get; set; } = "";
    public override string ToCpp() =>
        $"std::memset({AddressExpr}, 0, sizeof({TypeCppName}));";
}

public class IRBox : IRInstruction
{
    public string ValueExpr { get; set; } = "";
    public string ValueTypeCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public override string ToCpp() =>
        $"{ResultVar} = cil2cpp::box<{ValueTypeCppName}>({ValueExpr}, &{ValueTypeCppName}_TypeInfo);";
}

public class IRUnbox : IRInstruction
{
    public string ObjectExpr { get; set; } = "";
    public string ValueTypeCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsUnboxAny { get; set; }
    public override string ToCpp() => IsUnboxAny
        ? $"{ResultVar} = cil2cpp::unbox<{ValueTypeCppName}>({ObjectExpr});"
        : $"{ResultVar} = cil2cpp::unbox_ptr<{ValueTypeCppName}>({ObjectExpr});";
}

public class IRStaticCtorGuard : IRInstruction
{
    public string TypeCppName { get; set; } = "";
    public override string ToCpp() => $"{TypeCppName}_ensure_cctor();";
}

// ============ Exception Handling Instructions ============

public class IRTryBegin : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_TRY";
}

public class IRCatchBegin : IRInstruction
{
    public string? ExceptionTypeCppName { get; set; }
    public override string ToCpp() => ExceptionTypeCppName != null
        ? $"CIL2CPP_CATCH({ExceptionTypeCppName})" : "CIL2CPP_CATCH_ALL";
}

public class IRFinallyBegin : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_FINALLY";
}

public class IRTryEnd : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_END_TRY";
}

public class IRThrow : IRInstruction
{
    public string ExceptionExpr { get; set; } = "";
    public override string ToCpp() =>
        $"cil2cpp::throw_exception(static_cast<cil2cpp::Exception*>({ExceptionExpr}));";
}

public class IRRethrow : IRInstruction
{
    public override string ToCpp() => "CIL2CPP_RETHROW;";
}

public class IRRawCpp : IRInstruction
{
    public string Code { get; set; } = "";
    public override string ToCpp() => Code;
}

// ============ Delegate Instructions ============

public class IRLoadFunctionPointer : IRInstruction
{
    public string MethodCppName { get; set; } = "";
    public string ResultVar { get; set; } = "";
    public bool IsVirtual { get; set; }
    public string? ObjectExpr { get; set; }
    public int VTableSlot { get; set; } = -1;

    public override string ToCpp()
    {
        if (IsVirtual && VTableSlot >= 0 && ObjectExpr != null)
            return $"{ResultVar} = ((cil2cpp::Object*){ObjectExpr})->__type_info->vtable->methods[{VTableSlot}];";
        return $"{ResultVar} = (void*){MethodCppName};";
    }
}

public class IRDelegateCreate : IRInstruction
{
    public string DelegateTypeCppName { get; set; } = "";
    public string TargetExpr { get; set; } = "";
    public string FunctionPtrExpr { get; set; } = "";
    public string ResultVar { get; set; } = "";

    public override string ToCpp() =>
        $"{ResultVar} = cil2cpp::delegate_create(&{DelegateTypeCppName}_TypeInfo, (cil2cpp::Object*){TargetExpr}, {FunctionPtrExpr});";
}

public class IRDelegateInvoke : IRInstruction
{
    public string DelegateExpr { get; set; } = "";
    public string ReturnTypeCpp { get; set; } = "void";
    public List<string> ParamTypes { get; } = new();
    public List<string> Arguments { get; } = new();
    public string? ResultVar { get; set; }

    public override string ToCpp()
    {
        var del = $"((cil2cpp::Delegate*){DelegateExpr})";

        // Instance call: target is first arg, then user args
        var instanceParamTypes = new List<string> { "cil2cpp::Object*" };
        instanceParamTypes.AddRange(ParamTypes);
        var instanceFnPtr = $"{ReturnTypeCpp}(*)({string.Join(", ", instanceParamTypes)})";
        var instanceArgs = new List<string> { $"{del}->target" };
        instanceArgs.AddRange(Arguments);
        var instanceCall = $"(({instanceFnPtr})({del}->method_ptr))({string.Join(", ", instanceArgs)})";

        // Static call: no target arg
        var staticFnPtr = ParamTypes.Count > 0
            ? $"{ReturnTypeCpp}(*)({string.Join(", ", ParamTypes)})"
            : $"{ReturnTypeCpp}(*)()";
        var staticCall = $"(({staticFnPtr})({del}->method_ptr))({string.Join(", ", Arguments)})";

        var assign = ResultVar != null ? $"{ResultVar} = " : "";
        return $"if ({del}->target) {{ {assign}{instanceCall}; }} else {{ {assign}{staticCall}; }}";
    }
}
