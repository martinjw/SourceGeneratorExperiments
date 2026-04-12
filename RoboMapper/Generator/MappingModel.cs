using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace RoboMapper.Generator
{
    internal sealed record MappingDefinition(
        INamedTypeSymbol Source,
        INamedTypeSymbol Destination,
        bool Reverse,
        IReadOnlyList<PropertyMapping> CustomMappings);

    internal sealed record PropertyMapping(
        string DestinationName,
        string? SourceName);
}
