using System;
using System.Collections.Generic;

namespace RoboMapper;

/// <summary>
/// Provides a configuration container for managing type mapping delegates between source and destination types.
/// </summary>
/// <remarks>The generator will emit another partial class that fills _maps.</remarks>
public sealed partial class MapperConfiguration
{
    /// <summary>
    /// The static instance of the MapperConfiguration class, serving as a singleton for managing mapping configurations across the application.
    /// </summary>
    public static MapperConfiguration Instance { get; } = new MapperConfiguration();

    private readonly Dictionary<(Type Source, Type Destination), Delegate> _maps = new();

    /// <summary>
    /// Attempts to retrieve a mapping delegate for the specified source and destination types.
    /// </summary>
    /// <param name="source">The source type for which to retrieve the mapping delegate. Cannot be null.</param>
    /// <param name="destination">The destination type for which to retrieve the mapping delegate. Cannot be null.</param>
    /// <param name="map">When this method returns, contains the mapping delegate if found; otherwise, null.</param>
    /// <returns>true if a mapping delegate exists for the specified source and destination types; otherwise, false.</returns>
    public bool TryGetMap(Type source, Type destination, out Delegate? map)
        => _maps.TryGetValue((source, destination), out map);

    /// <summary>
    /// Registers a mapping delegate for converting objects from the specified source type to the specified destination
    /// type.
    /// </summary>
    /// <remarks>This method is typically used by source generators or code generation tools to register type
    /// mappings at runtime. If a mapping for the specified source and destination types already exists, it will be
    /// overwritten.</remarks>
    /// <param name="source">The type of the source object to be mapped.</param>
    /// <param name="destination">The type of the destination object to map to.</param>
    /// <param name="map">A delegate that performs the mapping from the source type to the destination type. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="map"/> is null.</exception>
    public void RegisterMap(Type source, Type destination, Delegate map)
    {
        _maps[(source, destination)] = map ?? throw new ArgumentNullException(nameof(map));
    }
}