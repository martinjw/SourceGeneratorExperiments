using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace MediatorLibGenerator
{
    internal sealed record MappingDefinition(
        INamedTypeSymbol Source,
        INamedTypeSymbol Destination,
        bool Reverse,
        IReadOnlyList<PropertyMapping> CustomMappings,
        IReadOnlyList<string> AfterMapActions);

    internal sealed record PropertyMapping(
        string DestinationName,
    string? SourceName,
    string? MapFromExpression);

    /// <summary>
    /// Compares MappingDefinition instances based only on Source and Destination types,
    /// ignoring other properties like CustomMappings and AfterMapActions.
    /// This is used for deduplication to prevent generating the same mapping method multiple times.
    /// </summary>
    internal sealed class MappingDefinitionTypeComparer : IEqualityComparer<MappingDefinition>
    {
        public bool Equals(MappingDefinition? x, MappingDefinition? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            return SymbolEqualityComparer.Default.Equals(x.Source, y.Source)
                && SymbolEqualityComparer.Default.Equals(x.Destination, y.Destination);
        }

        public int GetHashCode(MappingDefinition obj)
        {
            if (obj is null) return 0;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(obj.Source);
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(obj.Destination);
                return hash;
            }
        }
    }
}
