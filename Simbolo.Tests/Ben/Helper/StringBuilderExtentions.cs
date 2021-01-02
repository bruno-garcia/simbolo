// Copyright (c) Ben A Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text;

namespace System.Diagnostics 
{
    public static class StringBuilderExtensions
    {
        public static StringBuilder AppendDemystified(this StringBuilder builder, Exception exception)
        {
            try
            {
                var stackTrace = EnhancedStackTrace.GetFrames(exception);

                builder.Append(exception.GetType());
                if (!string.IsNullOrEmpty(exception.Message))
                {
                    builder.Append(": ").Append(exception.Message);
                }
                builder.Append(Environment.NewLine);

                Append(stackTrace, builder);

                if (exception is AggregateException aggEx)
                {
                    foreach (var ex in aggEx.InnerExceptions)
                    {
                        builder.AppendInnerException(ex);
                    }
                }

                if (exception.InnerException != null)
                {
                    builder.AppendInnerException(exception.InnerException);
                }
            }
            catch
            {
                // Processing exceptions shouldn't throw exceptions; if it fails
            }

            return builder;
        }

        internal static string ToStackTraceString(this List<EnhancedStackFrame> frames)
        {
            var b = new StringBuilder();
            Append(frames, b);
            return b.ToString();
        }
        
        internal static void Append(List<EnhancedStackFrame> frames, StringBuilder sb)
        {
            for (var i = 0; i < frames.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(Environment.NewLine);
                }

                var frame = frames[i];

                sb.Append("   at ");
                frame.MethodInfo.Append(sb);

                var filePath = frame.GetFileName();
                if (!string.IsNullOrEmpty(filePath))
                {
                    sb.Append(" in ");
                    sb.Append(TryGetFullPath(filePath));

                }

                var lineNo = frame.GetFileLineNumber();
                if (lineNo != 0)
                {
                    sb.Append(":line ");
                    sb.Append(lineNo);
                }
            }
        }
        
        private static string TryGetFullPath(string filePath)
        {
            if (Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return Uri.UnescapeDataString(uri.AbsolutePath);
            }

            return filePath;
        }

        private static void AppendInnerException(this StringBuilder builder, Exception exception) 
            => builder.Append(" ---> ")
                .AppendDemystified(exception)
                .AppendLine()
                .Append("   --- End of inner exception stack trace ---");
    }
}