namespace MediatorLib;

/// <summary>
/// Marker interface for requests in a mediator pattern implementation. Represents a request that can be sent through the mediator to be handled by a corresponding request handler. The generic type parameter TResponse specifies the type of response expected from handling the request.
/// </summary>
/// <typeparam name="TResponse"></typeparam>
public interface IRequest<TResponse> { }