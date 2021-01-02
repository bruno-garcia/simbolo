// Copyright (c) Ben A Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;

// Based on: https://github.com/benaadams/Ben.Demystifier/blob/87375e9013db462ad5af21bc308bc73c63cfe919/src/Ben.Demystifier/ResolvedMethod.cs
// ReSharper disable once CheckNamespace
namespace System.Diagnostics
{
    internal class ResolvedParameter
    {
        public string? Name { get; set; }

        public Type? ResolvedType { get; set; }

        public string? Prefix { get; set; }
        public bool IsDynamicType { get; set; }

        public override string ToString() => Append(new StringBuilder()).ToString();

        internal StringBuilder Append(StringBuilder sb)
        {
            if (!string.IsNullOrEmpty(Prefix))
            {
                sb.Append(Prefix)
                    .Append(" ");
            }

            if (IsDynamicType)
            {
                sb.Append("dynamic");
            }
            else if (ResolvedType != null)
            {
                AppendTypeName(sb);
            }
            else
            {
                sb.Append("?");
            }

            if (!string.IsNullOrEmpty(Name))
            {
                sb.Append(" ")
                    .Append(Name);
            }

            return sb;
        }

        protected virtual void AppendTypeName(StringBuilder sb) 
        {
            if (ResolvedType is not null)
            {
                sb.AppendTypeDisplayName(ResolvedType, fullName: false, includeGenericParameterNames: true);
            }
        }
    }
}