namespace MediatorLib;

/// <summary>
/// A registry interface for managing request and notification handlers in a mediator pattern implementation.
/// </summary>
public interface IHandlerRegistry
{
    /// <summary>
    /// Gets the mapping of request types to their corresponding handler types.
    /// </summary>
    /// <remarks>Each entry in the dictionary associates a request type with the type that handles it. This
    /// mapping can be used to resolve or instantiate handlers for specific request types at runtime.</remarks>
    Dictionary<Type, Type> RequestHandlers { get; }
    /// <summary>
    /// Gets the mapping of notification types to their associated handler types.
    /// </summary>
    /// <remarks>Each key in the dictionary represents a notification type, and the corresponding value is a
    /// list of handler types that process notifications of that type. This property is typically used to discover or
    /// register handlers for specific notification types at runtime.</remarks>
    Dictionary<Type, List<Type>> NotificationHandlers { get; }
}