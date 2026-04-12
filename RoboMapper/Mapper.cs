using System;

namespace RoboMapper;

/// <summary>
/// Provides functionality to map objects from one type to another using configured mappings.
/// </summary>
public sealed class Mapper : IMapper
{
    /// <summary>
    /// Maps the specified source object to a new instance of the specified destination type.
    /// </summary>
    /// <remarks>This method uses the configured mappings to convert the source object to the specified
    /// destination type. Ensure that a mapping exists between the source and destination types before calling this
    /// method.</remarks>
    /// <typeparam name="TDestination">The type to which the source object is mapped.</typeparam>
    /// <param name="source">The object to map to the destination type. Cannot be null.</param>
    /// <returns>An instance of type TDestination with values mapped from the source object.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the source parameter is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no mapping is configured between the source object's type and the destination type.</exception>
    public TDestination Map<TDestination>(object source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var srcType = source.GetType();
        var destType = typeof(TDestination);

        if (!MapperConfiguration.Instance.TryGetMap(srcType, destType, out var del) || del is null)
            throw new InvalidOperationException($"No mapping from {srcType} to {destType}.");

        return ((Func<object, TDestination>)del)(source);
    }
}