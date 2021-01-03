using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Simbolo.StackFrameData
{
    public class StackTraceInformation
    {
        public IEnumerable<StackFrameInformation> StackFrameInformation { get; }
        public IDictionary<Guid, DebugMeta> DebugMetas { get; }

        public StackTraceInformation(
            IEnumerable<StackFrameInformation> stackFrameInformation,
            IDictionary<Guid, DebugMeta> debugMetas)
        {
            StackFrameInformation = stackFrameInformation;
            DebugMetas = debugMetas;
        }
        
        internal StackTraceInformation()
        {
            StackFrameInformation = Enumerable.Empty<StackFrameInformation>();
            DebugMetas = new Dictionary<Guid, DebugMeta>(0);
        }

        public override string ToString() => ToString("default");

        public string ToString(string format) =>
            format switch
            {
                "json" => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
                _ => ToStringDotnet(),
            };

        private string ToStringDotnet()
        {
            var builder = new StringBuilder(256);
            foreach (var info in StackFrameInformation)
            {
                if (info.Method is null)
                {
                    continue;
                }

                builder.Append("   at ");
                
                builder.Append(info.Method);
                if (info.FileName is not null)
                {
                    builder.Append(" in ");
                    builder.Append(info.FileName);
                } 
                else if (info.Mvid is not null)
                {
                    builder.Append(" in ");
                    // Mono format
                    builder.Append('<');
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0:n}", info.Mvid.Value);
                    if (info.Aotid is not null)
                    {
                        builder.Append('#');
                        builder.Append(info.Aotid);
                    }
                    builder.Append('>');
                }

                if (info.LineNumber is not null)
                {
                    builder.Append(":line ");
                    builder.Append(info.LineNumber);
                }

                if (info.ColumnNumber is not null)
                {
                    builder.Append(':');
                    builder.Append(info.ColumnNumber);
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}