namespace RoboMapper;

/// <summary>
/// Defines a contract for mapping an object to a new instance of a specified destination type. 
/// </summary>
/// <remarks>Implementations of this interface typically perform object-to-object mapping, copying data
/// from the source object to a new instance of the destination type. This is commonly used to transform data
/// between layers or models in an application.</remarks>
public interface IMapper
{
    /// <summary>
    /// Define mapping contract from a source object to a new instance of the specified destination type.
    /// </summary>
    /// <typeparam name="TDestination">The type of the destination object.</typeparam>
    /// <param name="source">The source object to map from.</param>
    /// <returns>A new instance of the destination type with values mapped from the source object.</returns>
    TDestination Map<TDestination>(object source);
}