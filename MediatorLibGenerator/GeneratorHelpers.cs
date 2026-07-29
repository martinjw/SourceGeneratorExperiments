using System;
using System.Linq;

namespace MediatorLibGenerator
{
    internal static class GeneratorHelpers
    {
        public static string ResolveGeneratedNamespace(string? rootNamespace, string? assemblyName)
        {
            var candidate = string.IsNullOrWhiteSpace(rootNamespace)
                ? assemblyName
                : rootNamespace;

            if (string.IsNullOrWhiteSpace(candidate))
                return "MediatorLib.Mediator";

            var normalized = string.Join(".", candidate
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static segment =>
                {
                    var sanitized = segment
                        .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_')
                        .ToArray();

                    if (sanitized.Length == 0)
                        return "_";

                    if (!char.IsLetter(sanitized[0]) && sanitized[0] != '_')
                        return "_" + new string(sanitized);

                    return new string(sanitized);
                }));

            return string.IsNullOrWhiteSpace(normalized)
                ? "MediatorLib.Mediator"
                : normalized;
        }
    }
}
