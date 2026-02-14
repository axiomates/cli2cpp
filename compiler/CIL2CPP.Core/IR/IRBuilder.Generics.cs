using Mono.Cecil;
using CIL2CPP.Core.IL;

namespace CIL2CPP.Core.IR;

public partial class IRBuilder
{
    // Active generic type parameter map (set during ConvertMethodBodyWithGenerics)
    private Dictionary<string, string>? _activeTypeParamMap;

    /// <summary>
    /// Pass 0: Scan all method bodies for GenericInstanceType references.
    /// Collects unique generic instantiations (e.g., Wrapper`1&lt;System.Int32&gt;).
    /// </summary>
    private void ScanGenericInstantiations()
    {
        foreach (var typeDef in _reader.GetAllTypes())
        {
            foreach (var methodDef in typeDef.Methods)
            {
                if (!methodDef.HasBody) continue;
                var cecilMethod = methodDef.GetCecilMethod();
                if (!cecilMethod.HasBody) continue;

                // Scan local variables
                foreach (var local in cecilMethod.Body.Variables)
                {
                    CollectGenericType(local.VariableType);
                }

                // Scan instructions
                foreach (var instr in cecilMethod.Body.Instructions)
                {
                    switch (instr.Operand)
                    {
                        case MethodReference methodRef:
                            CollectGenericType(methodRef.DeclaringType);
                            if (methodRef.ReturnType is GenericInstanceType)
                                CollectGenericType(methodRef.ReturnType);
                            foreach (var p in methodRef.Parameters)
                                CollectGenericType(p.ParameterType);
                            break;
                        case FieldReference fieldRef:
                            CollectGenericType(fieldRef.DeclaringType);
                            CollectGenericType(fieldRef.FieldType);
                            break;
                        case TypeReference typeRef:
                            CollectGenericType(typeRef);
                            break;
                    }
                }
            }
        }
    }

    private void CollectGenericType(TypeReference typeRef)
    {
        if (typeRef is not GenericInstanceType git) return;

        // Skip if any type argument is still a generic parameter (unresolved)
        if (git.GenericArguments.Any(a => a is GenericParameter))
            return;

        var openTypeName = git.ElementType.FullName;
        var typeArgs = git.GenericArguments.Select(a => a.FullName).ToList();
        var key = $"{openTypeName}<{string.Join(",", typeArgs)}>";

        if (_genericInstantiations.ContainsKey(key)) return;

        var mangledName = CppNameMapper.MangleGenericInstanceTypeName(openTypeName, typeArgs);
        var cecilOpenType = git.ElementType.Resolve();

        _genericInstantiations[key] = new GenericInstantiationInfo(
            openTypeName, typeArgs, mangledName, cecilOpenType);
    }

    /// <summary>
    /// Create specialized IRTypes for each generic instantiation found in Pass 0.
    /// </summary>
    private void CreateGenericSpecializations()
    {
        foreach (var (key, info) in _genericInstantiations)
        {
            if (info.CecilOpenType == null) continue;

            var openType = info.CecilOpenType;

            // Build type parameter map: { "T" → "System.Int32", ... }
            var typeParamMap = new Dictionary<string, string>();
            for (int i = 0; i < openType.GenericParameters.Count && i < info.TypeArguments.Count; i++)
            {
                typeParamMap[openType.GenericParameters[i].Name] = info.TypeArguments[i];
            }

            var irType = new IRType
            {
                ILFullName = key,
                CppName = info.MangledName,
                Name = info.MangledName,
                Namespace = openType.Namespace,
                IsValueType = openType.IsValueType,
                IsInterface = openType.IsInterface,
                IsAbstract = openType.IsAbstract,
                IsSealed = openType.IsSealed,
                IsGenericInstance = true,
                GenericArguments = info.TypeArguments,
            };

            // Register value types
            if (openType.IsValueType)
                CppNameMapper.RegisterValueType(key);

            // Fields: substitute generic parameters
            foreach (var fieldDef in openType.Fields)
            {
                var fieldTypeName = ResolveGenericTypeName(fieldDef.FieldType, typeParamMap);
                var irField = new IRField
                {
                    Name = fieldDef.Name,
                    CppName = CppNameMapper.MangleFieldName(fieldDef.Name),
                    FieldTypeName = fieldTypeName,
                    IsStatic = fieldDef.IsStatic,
                    IsPublic = fieldDef.IsPublic,
                    DeclaringType = irType,
                };
                if (fieldDef.IsStatic)
                    irType.StaticFields.Add(irField);
                else
                    irType.Fields.Add(irField);
            }

            // Calculate instance size
            CalculateInstanceSize(irType);

            // Methods: create shells with substituted types
            foreach (var methodDef in openType.Methods)
            {
                var returnTypeName = ResolveGenericTypeName(methodDef.ReturnType, typeParamMap);
                var cppName = CppNameMapper.MangleMethodName(info.MangledName, methodDef.Name);

                var irMethod = new IRMethod
                {
                    Name = methodDef.Name,
                    CppName = cppName,
                    DeclaringType = irType,
                    ReturnTypeCpp = CppNameMapper.GetCppTypeForDecl(returnTypeName),
                    IsStatic = methodDef.IsStatic,
                    IsVirtual = methodDef.IsVirtual,
                    IsAbstract = methodDef.IsAbstract,
                    IsConstructor = methodDef.IsConstructor,
                    IsStaticConstructor = methodDef.IsConstructor && methodDef.IsStatic,
                };

                // Parameters
                foreach (var paramDef in methodDef.Parameters)
                {
                    var paramTypeName = ResolveGenericTypeName(paramDef.ParameterType, typeParamMap);
                    irMethod.Parameters.Add(new IRParameter
                    {
                        Name = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                        CppName = paramDef.Name.Length > 0 ? paramDef.Name : $"p{paramDef.Index}",
                        CppTypeName = CppNameMapper.GetCppTypeForDecl(paramTypeName),
                        Index = paramDef.Index,
                    });
                }

                // Local variables
                if (methodDef.HasBody)
                {
                    foreach (var localDef in methodDef.Body.Variables)
                    {
                        var localTypeName = ResolveGenericTypeName(localDef.VariableType, typeParamMap);
                        irMethod.Locals.Add(new IRLocal
                        {
                            Index = localDef.Index,
                            CppName = $"loc_{localDef.Index}",
                            CppTypeName = CppNameMapper.GetCppTypeForDecl(localTypeName),
                        });
                    }
                }

                irType.Methods.Add(irMethod);

                // Convert method body with generic substitution context
                if (methodDef.HasBody && !methodDef.IsAbstract)
                {
                    var methodInfo = new IL.MethodInfo(methodDef);
                    ConvertMethodBodyWithGenerics(methodInfo, irMethod, typeParamMap);
                }
            }

            _module.Types.Add(irType);
            _typeCache[key] = irType;
        }
    }

    /// <summary>
    /// Resolve a Cecil TypeReference to an IL type name, substituting generic parameters.
    /// </summary>
    private string ResolveGenericTypeName(TypeReference typeRef, Dictionary<string, string> typeParamMap)
    {
        if (typeRef is GenericParameter gp)
        {
            return typeParamMap.TryGetValue(gp.Name, out var resolved) ? resolved : gp.FullName;
        }

        if (typeRef is GenericInstanceType git)
        {
            var openName = git.ElementType.FullName;
            var args = git.GenericArguments.Select(a => ResolveGenericTypeName(a, typeParamMap)).ToList();
            var key = $"{openName}<{string.Join(",", args)}>";
            return key;
        }

        if (typeRef is ArrayType at)
        {
            return ResolveGenericTypeName(at.ElementType, typeParamMap) + "[]";
        }

        if (typeRef is ByReferenceType brt)
        {
            return ResolveGenericTypeName(brt.ElementType, typeParamMap) + "&";
        }

        return typeRef.FullName;
    }

    /// <summary>
    /// Convert a method body from an open generic type with generic parameter substitution.
    /// </summary>
    private void ConvertMethodBodyWithGenerics(IL.MethodInfo methodDef, IRMethod irMethod,
        Dictionary<string, string> typeParamMap)
    {
        _activeTypeParamMap = typeParamMap;
        try
        {
            ConvertMethodBody(methodDef, irMethod);
        }
        finally
        {
            _activeTypeParamMap = null;
        }
    }

    /// <summary>
    /// Resolve a Cecil TypeReference FullName through the active generic type param map.
    /// </summary>
    private string ResolveActiveGenericType(TypeReference typeRef)
    {
        if (_activeTypeParamMap == null) return typeRef.FullName;
        return ResolveGenericTypeName(typeRef, _activeTypeParamMap);
    }

    /// <summary>
    /// Resolve a Cecil TypeReference to a cache key, handling GenericInstanceType.
    /// </summary>
    private string ResolveCacheKey(TypeReference typeRef)
    {
        if (typeRef is GenericInstanceType git)
        {
            var openTypeName = git.ElementType.FullName;
            var typeArgs = git.GenericArguments.Select(a =>
            {
                if (a is GenericParameter gp && _activeTypeParamMap != null)
                    return _activeTypeParamMap.TryGetValue(gp.Name, out var r) ? r : a.FullName;
                return a.FullName;
            }).ToList();
            return $"{openTypeName}<{string.Join(",", typeArgs)}>";
        }

        if (typeRef is GenericParameter gp2 && _activeTypeParamMap != null)
            return _activeTypeParamMap.TryGetValue(gp2.Name, out var resolved) ? resolved : typeRef.FullName;

        return typeRef.FullName;
    }

    /// <summary>
    /// Get the C++ type name for a Cecil TypeReference, handling generics.
    /// </summary>
    private string GetCppTypeNameForRef(TypeReference typeRef)
    {
        var key = ResolveCacheKey(typeRef);
        if (_typeCache.TryGetValue(key, out var irType))
        {
            if (irType.IsValueType)
                return irType.CppName;
            return irType.CppName + "*";
        }
        return CppNameMapper.GetCppTypeForDecl(key);
    }

    /// <summary>
    /// Get the C++ mangled type name for a Cecil TypeReference.
    /// </summary>
    private string GetMangledTypeNameForRef(TypeReference typeRef)
    {
        var key = ResolveCacheKey(typeRef);
        if (_typeCache.TryGetValue(key, out var irType))
            return irType.CppName;
        return CppNameMapper.MangleTypeName(key);
    }

    /// <summary>
    /// Resolve a type name string (possibly containing generic syntax) to C++ declaration type.
    /// </summary>
    private string ResolveTypeForDecl(string ilTypeName)
    {
        if (_typeCache.TryGetValue(ilTypeName, out var cached))
        {
            if (cached.IsValueType)
                return cached.CppName;
            return cached.CppName + "*";
        }

        var backtickIdx = ilTypeName.IndexOf('`');
        if (backtickIdx > 0 && ilTypeName.Contains('<'))
        {
            var angleBracket = ilTypeName.IndexOf('<');
            var openTypeName = ilTypeName[..angleBracket];
            var argsStr = ilTypeName[(angleBracket + 1)..^1];
            var args = argsStr.Split(',').Select(a => a.Trim()).ToList();
            var key = $"{openTypeName}<{string.Join(",", args)}>";

            if (_typeCache.TryGetValue(key, out var genericCached))
            {
                if (genericCached.IsValueType)
                    return genericCached.CppName;
                return genericCached.CppName + "*";
            }
        }

        return CppNameMapper.GetCppTypeForDecl(ilTypeName);
    }
}
