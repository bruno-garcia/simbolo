// Copyright (c) Ben A Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Internal;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace System.Diagnostics
{
    internal class EnhancedStackTrace
    {
        private static readonly Type StackTraceHiddenAttributeType = Type.GetType("System.Diagnostics.StackTraceHiddenAttribute", false);

        public static List<EnhancedStackFrame> GetFrames(Exception exception)
        {
            var needFileInfo = true;
            var stackTrace = new StackTrace(exception, needFileInfo);

            return GetFrames(stackTrace);
        }

        private static List<EnhancedStackFrame> GetFrames(StackTrace stackTrace)
        {
            var frames = new List<EnhancedStackFrame>();
            var stackFrames = stackTrace.GetFrames();

            if (stackFrames == null)
            {
                return frames;
            }

            for (var i = 0; i < stackFrames.Length; i++)
            {
                var frame = stackFrames[i];
                var method = frame.GetMethod();

                // Always show last stackFrame
                if (method != null && !ShowInStackTrace(method) && i < stackFrames.Length - 1)
                {
                    continue;
                }

                var fileName = frame.GetFileName();
                var row = frame.GetFileLineNumber();
                var column = frame.GetFileColumnNumber();

                var methodDisplayString = GetMethodDisplayString(method);
                if (methodDisplayString is null)
                {
                    continue;
                }
                var stackFrame = new EnhancedStackFrame(frame, methodDisplayString, fileName, row, column);

                frames.Add(stackFrame);
            }
            return frames;
        }

        internal static ResolvedMethod? GetMethodDisplayString(MethodBase? originMethod)
        {
            // Special case: no method available
            if (originMethod is null)
            {
                return null;
            }

            var method = originMethod;

            var methodDisplayInfo = new ResolvedMethod
            {
                SubMethodBase = method
            };

            // Type name
            var type = method.DeclaringType;

            var subMethodName = method.Name;
            var methodName = method.Name;

            if (type != null && type.IsDefined(typeof(CompilerGeneratedAttribute)) &&
                (typeof(IAsyncStateMachine).IsAssignableFrom(type) || typeof(IEnumerator).IsAssignableFrom(type)))
            {
                methodDisplayInfo.IsAsync = typeof(IAsyncStateMachine).IsAssignableFrom(type);

                // Convert StateMachine methods to correct overload +MoveNext()
                if (!TryResolveStateMachineMethod(ref method, out type))
                {
                    methodDisplayInfo.SubMethodBase = null;
                    subMethodName = null;
                }

                methodName = method.Name;
            }

            // Method name
            methodDisplayInfo.MethodBase = method;
            methodDisplayInfo.Name = methodName;
            if (method.Name.IndexOf("<", StringComparison.Ordinal) >= 0)
            {
                if (TryResolveGeneratedName(ref method, out type, out methodName, out subMethodName, out var kind, out var ordinal))
                {
                    methodName = method.Name;
                    methodDisplayInfo.MethodBase = method;
                    methodDisplayInfo.Name = methodName;
                    methodDisplayInfo.Ordinal = ordinal;
                }
                else
                {
                    methodDisplayInfo.MethodBase = null;
                }

                methodDisplayInfo.IsLambda = kind == GeneratedNameKind.LambdaMethod;

                if (methodDisplayInfo.IsLambda && type != null)
                {
                    if (methodName == ".cctor")
                    {
                        if (type.IsGenericTypeDefinition && !type.IsConstructedGenericType)
                        {
                            // TODO: diagnose type's generic type arguments from frame's "this" or something
                        }
                        else
                        {
                            var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                            foreach (var field in fields)
                            {
                                var value = field.GetValue(field);
                                if (value is Delegate d)
                                {
                                    if (ReferenceEquals(d.Method, originMethod) &&
                                        d.Target.ToString() == originMethod.DeclaringType?.ToString())
                                    {
                                        methodDisplayInfo.Name = field.Name;
                                        methodDisplayInfo.IsLambda = false;
                                        method = originMethod;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (subMethodName != methodName)
            {
                methodDisplayInfo.SubMethod = subMethodName;
            }

            // ResolveStateMachineMethod may have set declaringType to null
            if (type != null)
            {
                methodDisplayInfo.DeclaringType = type;
            }

            if (method is MethodInfo mi)
            {
                var returnParameter = mi.ReturnParameter;
                if (returnParameter != null)
                {
                    methodDisplayInfo.ReturnParameter = GetParameter(returnParameter);
                }
                else if (mi.ReturnType != null)
                {
                    methodDisplayInfo.ReturnParameter = new ResolvedParameter
                    {
                        Prefix = "",
                        Name = "",
                        ResolvedType = mi.ReturnType,
                    };
                }
            }

            if (method.IsGenericMethod)
            {
                var genericArguments = method.GetGenericArguments();
                var genericArgumentsString = string.Join(", ", genericArguments
                    .Select(arg => TypeNameHelper.GetTypeDisplayName(arg, fullName: false, includeGenericParameterNames: true)));
                methodDisplayInfo.GenericArguments += "<" + genericArgumentsString + ">";
                methodDisplayInfo.ResolvedGenericArguments = genericArguments;
            }

            // Method parameters
            var parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                var parameterList = new List<ResolvedParameter>(parameters.Length);
                foreach (var parameter in parameters)
                {
                    var param = GetParameter(parameter);
                    if (param is null)
                    {
                        continue;
                    }
                    parameterList.Add(param);
                }

                methodDisplayInfo.Parameters = parameterList;
            }

            if (methodDisplayInfo.SubMethodBase == methodDisplayInfo.MethodBase)
            {
                methodDisplayInfo.SubMethodBase = null;
            }
            else if (methodDisplayInfo.SubMethodBase != null)
            {
                parameters = methodDisplayInfo.SubMethodBase.GetParameters();
                if (parameters.Length > 0)
                {
                    var parameterList = new List<ResolvedParameter>(parameters.Length);
                    foreach (var parameter in parameters)
                    {
                        var param = GetParameter(parameter);
                        if (param is null || (param.Name?.StartsWith("<") ?? true))
                        {
                            continue;
                        }

                        parameterList.Add(param);
                    }

                    methodDisplayInfo.SubMethodParameters = parameterList;
                }
            }

            return methodDisplayInfo;
        }

        private static bool TryResolveGeneratedName(ref MethodBase method, out Type? type, out string methodName, out string? subMethodName, out GeneratedNameKind kind, out int? ordinal)
        {
            kind = GeneratedNameKind.None;
            type = method.DeclaringType;
            subMethodName = null;
            ordinal = null;
            methodName = method.Name;

            var generatedName = methodName;

            if (!TryParseGeneratedName(generatedName, out kind, out var openBracketOffset, out var closeBracketOffset))
            {
                return false;
            }

            methodName = generatedName.Substring(openBracketOffset + 1, closeBracketOffset - openBracketOffset - 1);

            switch (kind)
            {
                case GeneratedNameKind.LocalFunction:
                    {
                        var localNameStart = generatedName.IndexOf((char)kind, closeBracketOffset + 1);
                        if (localNameStart < 0) break;
                        localNameStart += 3;

                        if (localNameStart < generatedName.Length)
                        {
                            var localNameEnd = generatedName.IndexOf("|", localNameStart, StringComparison.Ordinal);
                            if (localNameEnd > 0)
                            {
                                subMethodName = generatedName.Substring(localNameStart, localNameEnd - localNameStart);
                            }
                        }
                        break;
                    }
                case GeneratedNameKind.LambdaMethod:
                    subMethodName = "";
                    break;
            }

            var dt = method.DeclaringType;
            if (dt == null)
            {
                return false;
            }

            var matchHint = GetMatchHint(kind, method);

            var matchName = methodName;

            var candidateMethods = dt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == matchName);
            if (TryResolveSourceMethod(candidateMethods, kind, matchHint, ref method, ref type, out ordinal)) return true;

            var candidateConstructors = dt.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == matchName);
            if (TryResolveSourceMethod(candidateConstructors, kind, matchHint, ref method, ref type, out ordinal)) return true;

            const int maxResolveDepth = 10;
            for (var i = 0; i < maxResolveDepth; i++)
            {
                dt = dt.DeclaringType;
                if (dt == null)
                {
                    return false;
                }

                candidateMethods = dt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == matchName);
                if (TryResolveSourceMethod(candidateMethods, kind, matchHint, ref method, ref type, out ordinal)) return true;

                candidateConstructors = dt.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == matchName);
                if (TryResolveSourceMethod(candidateConstructors, kind, matchHint, ref method, ref type, out ordinal)) return true;

                if (methodName == ".cctor")
                {
                    candidateConstructors = dt.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.Name == matchName);
                    foreach (var cctor in candidateConstructors)
                    {
                        method = cctor;
                        type = dt;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryResolveSourceMethod(IEnumerable<MethodBase> candidateMethods, GeneratedNameKind kind, string? matchHint, ref MethodBase method, ref Type? type, out int? ordinal)
        {
            ordinal = null;
            foreach (var candidateMethod in candidateMethods)
            {
                var methodBody = candidateMethod.GetMethodBody();
                if (methodBody is not null && kind == GeneratedNameKind.LambdaMethod)
                {
                    foreach (var v in methodBody.LocalVariables)
                    {
                        if (v.LocalType == type)
                        {
                            GetOrdinal(method, ref ordinal);

                        }
                        method = candidateMethod;
                        type = method.DeclaringType;
                        return true;
                    }
                }

                try
                {
                    var rawIl = methodBody?.GetILAsByteArray();
                    if (rawIl == null)
                    {
                        continue;
                    }
                    var reader = new ILReader(rawIl);
                    while (reader.Read(candidateMethod))
                    {
                        if (reader.Operand is MethodBase mb)
                        {
                            if (method == mb || (matchHint != null && method.Name.Contains(matchHint)))
                            {
                                if (kind == GeneratedNameKind.LambdaMethod)
                                {
                                    GetOrdinal(method, ref ordinal);
                                }

                                method = candidateMethod;
                                type = method.DeclaringType;
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    // https://github.com/benaadams/Ben.Demystifier/issues/32
                    // Skip methods where il can't be interpreted
                }
            }

            return false;
        }

        private static void GetOrdinal(MethodBase method, ref int? ordinal)
        {
            var lamdaStart = method.Name.IndexOf((char)GeneratedNameKind.LambdaMethod + "__", StringComparison.Ordinal) + 3;
            if (lamdaStart > 3)
            {
                var secondStart = method.Name.IndexOf("_", lamdaStart, StringComparison.Ordinal) + 1;
                if (secondStart > 0)
                {
                    lamdaStart = secondStart;
                }

                if (!int.TryParse(method.Name.Substring(lamdaStart), out var foundOrdinal))
                {
                    ordinal = null;
                    return;
                }

                ordinal = foundOrdinal;

                var methods = method.DeclaringType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (methods is null)
                {
                    ordinal = null;
                    return;
                }
                var startName = method.Name.Substring(0, lamdaStart);
                var count = 0;
                foreach (var m in methods)
                {
                    if (m.Name.Length > lamdaStart && m.Name.StartsWith(startName))
                    {
                        count++;

                        if (count > 1)
                        {
                            break;
                        }
                    }
                }


                if (count <= 1)
                {
                    ordinal = null;
                }
            }
        }

        static string? GetMatchHint(GeneratedNameKind kind, MethodBase method)
        {
            var methodName = method.Name;

            switch (kind)
            {
                case GeneratedNameKind.LocalFunction:
                    var start = methodName.IndexOf("|", StringComparison.Ordinal);
                    if (start < 1) return null;
                    var end = methodName.IndexOf("_", start, StringComparison.Ordinal) + 1;
                    if (end <= start) return null;

                    return methodName.Substring(start, end - start);
            }
            return null;
        }

        // Parse the generated name. Returns true for names of the form
        // [CS$]<[middle]>c[__[suffix]] where [CS$] is included for certain
        // generated names, where [middle] and [__[suffix]] are optional,
        // and where c is a single character in [1-9a-z]
        // (csharp\LanguageAnalysis\LIB\SpecialName.cpp).
        internal static bool TryParseGeneratedName(
            string name,
            out GeneratedNameKind kind,
            out int openBracketOffset,
            out int closeBracketOffset)
        {
            openBracketOffset = -1;
            if (name.StartsWith("CS$<", StringComparison.Ordinal))
            {
                openBracketOffset = 3;
            }
            else if (name.StartsWith("<", StringComparison.Ordinal))
            {
                openBracketOffset = 0;
            }

            if (openBracketOffset >= 0)
            {
                closeBracketOffset = IndexOfBalancedParenthesis(name, openBracketOffset, '>');
                if (closeBracketOffset >= 0 && closeBracketOffset + 1 < name.Length)
                {
                    int c = name[closeBracketOffset + 1];
                    if ((c >= '1' && c <= '9') || (c >= 'a' && c <= 'z')) // Note '0' is not special.
                    {
                        kind = (GeneratedNameKind)c;
                        return true;
                    }
                }
            }

            kind = GeneratedNameKind.None;
            openBracketOffset = -1;
            closeBracketOffset = -1;
            return false;
        }


        private static int IndexOfBalancedParenthesis(string str, int openingOffset, char closing)
        {
            var opening = str[openingOffset];

            var depth = 1;
            for (var i = openingOffset + 1; i < str.Length; i++)
            {
                var c = str[i];
                if (c == opening)
                {
                    depth++;
                }
                else if (c == closing)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string GetPrefix(ParameterInfo parameter)
        {
            if (Attribute.IsDefined(parameter, typeof(ParamArrayAttribute), false))
            {
                return "params";
            }

            if (parameter.IsOut)
            {
                return "out";
            }

            if (parameter.IsIn)
            {
                return "in";
            }

            if (parameter.ParameterType.IsByRef)
            {
                return "ref";
            }

            return string.Empty;
        }

        private static ResolvedParameter? GetParameter(ParameterInfo? parameter)
        {
            if (parameter is null)
            {
                return null;
            }
            var prefix = GetPrefix(parameter);
            var parameterType = parameter.ParameterType;

            if (parameterType.IsGenericType)
            {
                var customAttribs = parameter.GetCustomAttributes(inherit: false);

                var tupleNameAttribute = customAttribs.OfType<Attribute>().FirstOrDefault(a => a.IsTupleElementNameAttribue());

                var tupleNames = tupleNameAttribute?.GetTransformerNames();

                if (tupleNames?.Count > 0)
                {
                    return GetValueTupleParameter(tupleNames, prefix, parameter.Name, parameterType);
                }
            }

            if (parameterType.IsByRef)
            {
                parameterType = parameterType.GetElementType();
            }

            return new ResolvedParameter
            {
                Prefix = prefix,
                Name = parameter.Name,
                ResolvedType = parameterType,
                IsDynamicType = parameter.IsDefined(typeof(DynamicAttribute), false)
            };
        }

        private static ResolvedParameter GetValueTupleParameter(IList<string> tupleNames, string prefix, string name, Type parameterType)
        {
            return new ValueTupleResolvedParameter
            {
                TupleNames = tupleNames,
                Prefix = prefix,
                Name = name,
                ResolvedType = parameterType
            };
        }

        private static bool ShowInStackTrace(MethodBase method)
        {
            // https://github.com/dotnet/runtime/blob/7c18d4d6488dab82124d475d1199def01d1d252c/src/libraries/System.Private.CoreLib/src/System/Diagnostics/StackTrace.cs#L348-L361
            if ((method.MethodImplementationFlags & MethodImplAttributes.AggressiveInlining) != 0)
            {
                // Aggressive Inlines won't normally show in the StackTrace; however for Tier0 Jit and
                // cross-assembly AoT/R2R these inlines will be blocked until Tier1 Jit re-Jits
                // them when they will inline. We don't show them in the StackTrace to bring consistency
                // between this first-pass asm and fully optimized asm.
                return false;
            }

            if (StackTraceHiddenAttributeType != null)
            {
                // Don't show any methods marked with the StackTraceHiddenAttribute
                // https://github.com/dotnet/coreclr/pull/14652
                if (IsStackTraceHidden(method))
                {
                    return false;
                }
            }

            var type = method.DeclaringType;

            if (type == null)
            {
                return true;
            }

            if (type == typeof(Task<>) && method.Name == "InnerInvoke")
            {
                return false;
            }
            if (type == typeof(ValueTask<>) && method.Name == "get_Result")
            {
                return false;
            }
            if (type == typeof(Task))
            {
                switch (method.Name)
                {
                    case "ExecuteWithThreadLocal":
                    case "Execute":
                    case "ExecutionContextCallback":
                    case "ExecuteEntry":
                    case "InnerInvoke":
                        return false;
                }
            }
            if (type == typeof(ExecutionContext))
            {
                switch (method.Name)
                {
                    case "RunInternal":
                    case "Run":
                        return false;
                }
            }

            // Used on .NET Core 2.x
            // https://github.com/benaadams/Ben.Demystifier/issues/118
            if (StackTraceHiddenAttributeType != null)
            {
                // Don't show any types marked with the StackTraceHiddenAttribute
                // https://github.com/dotnet/coreclr/pull/14652
                if (IsStackTraceHidden(type))
                {
                    return false;
                }
            } 
            // Fallbacks for runtime pre-StackTraceHiddenAttribute
            if (type == typeof(ExceptionDispatchInfo) && method.Name == "Throw")
            {
                return false;
            } 
            if (type == typeof(TaskAwaiter) ||
                type == typeof(TaskAwaiter<>) ||
                type == typeof(ValueTaskAwaiter) ||
                type == typeof(ValueTaskAwaiter<>) ||
                type == typeof(ConfiguredValueTaskAwaitable.ConfiguredValueTaskAwaiter) ||
                type == typeof(ConfiguredValueTaskAwaitable<>.ConfiguredValueTaskAwaiter) ||
                type == typeof(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter) ||
                type == typeof(ConfiguredTaskAwaitable<>.ConfiguredTaskAwaiter))
            {
                switch (method.Name)
                {
                    case "HandleNonSuccessAndDebuggerNotification":
                    case "ThrowForNonSuccess":
                    case "ValidateEnd":
                    case "GetResult":
                        return false;
                }
            }
            else if (type.FullName == "System.ThrowHelper")
            {
                return false;
            }

            return true;
        }

        private static bool IsStackTraceHidden(MemberInfo memberInfo)
        {
            if (!memberInfo.Module.Assembly.ReflectionOnly)
            {
                return memberInfo.GetCustomAttributes(StackTraceHiddenAttributeType, false).Length != 0;
            }

            IEnumerable<CustomAttributeData> attributes;
            try
            {
                attributes = memberInfo.GetCustomAttributesData();
            }
            catch (NotImplementedException)
            {
                return false;
            }

            foreach (var attribute in attributes)
            {
                // reflection-only attribute, match on name
                if (attribute.AttributeType.FullName == StackTraceHiddenAttributeType.FullName)
                {
                    return true;
                }
            }

            return false;
        }

        // https://github.com/dotnet/runtime/blob/c985bdcec2a9190e733bcada413a193d5ff60c0d/src/libraries/System.Private.CoreLib/src/System/Diagnostics/StackTrace.cs#L375-L430
        private static bool TryResolveStateMachineMethod(ref MethodBase method, [NotNullWhen(true)] out Type declaringType)
        {
            if (method.DeclaringType is null)
            {
                declaringType = null!;
                return false;
            }
            declaringType = method.DeclaringType;

            var parentType = declaringType.DeclaringType;
            if (parentType is null)
            {
                return false;
            }

            static MethodInfo[]? GetDeclaredMethods(Type type) =>
                type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            var methods = GetDeclaredMethods(parentType);
            if (methods == null)
            {
                return false;
            }

            foreach (MethodInfo candidateMethod in methods)
            {
                var attributes = candidateMethod.GetCustomAttributes<StateMachineAttribute>(inherit: false);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse - Taken from CoreFX
                if (attributes is null)
                {
                    continue;
                }

                bool foundAttribute = false, foundIteratorAttribute = false;
                foreach (StateMachineAttribute asma in attributes)
                {
                    if (asma.StateMachineType == declaringType)
                    {
                        foundAttribute = true;
                        foundIteratorAttribute |= asma is IteratorStateMachineAttribute
#if NETSTANDARD2_1
                            || asma is AsyncIteratorStateMachineAttribute
#endif
                            ;
                    }
                }

                if (foundAttribute)
                {
                    // If this is an iterator (sync or async), mark the iterator as changed, so it gets the + annotation
                    // of the original method. Non-iterator async state machines resolve directly to their builder methods
                    // so aren't marked as changed.
                    method = candidateMethod;
                    declaringType = candidateMethod.DeclaringType!;
                    return foundIteratorAttribute;
                }
            }
            return false;
        }

        internal enum GeneratedNameKind
        {
            None = 0,

            // Used by EE:
            ThisProxyField = '4',
            HoistedLocalField = '5',
            DisplayClassLocalOrField = '8',
            LambdaMethod = 'b',
            LambdaDisplayClass = 'c',
            StateMachineType = 'd',
            LocalFunction = 'g', // note collision with Deprecated_InitializerLocal, however this one is only used for method names

            // Used by EnC:
            AwaiterField = 'u',
            HoistedSynthesizedLocalField = 's',

            // Currently not parsed:
            StateMachineStateField = '1',
            IteratorCurrentBackingField = '2',
            StateMachineParameterProxyField = '3',
            ReusableHoistedLocalField = '7',
            LambdaCacheField = '9',
            FixedBufferField = 'e',
            AnonymousType = 'f',
            TransparentIdentifier = 'h',
            AnonymousTypeField = 'i',
            AutoPropertyBackingField = 'k',
            IteratorCurrentThreadIdField = 'l',
            IteratorFinallyMethod = 'm',
            BaseMethodWrapper = 'n',
            AsyncBuilderField = 't',
            DynamicCallSiteContainerType = 'o',
            DynamicCallSiteField = 'p'
        }
        
        /// <summary>
        /// Tries to convert a given <paramref name="filePath"/> to a full path.
        /// Returns original value if the conversion isn't possible or a given path is relative.
        /// </summary>
        public static string? TryGetFullPath(string? filePath)
        {
            if (Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return Uri.UnescapeDataString(uri.AbsolutePath);
            }

            return filePath;
        }
    }
}
