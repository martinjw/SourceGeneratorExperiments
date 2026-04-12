namespace MediatorLib;

/// <summary>
/// A handler for processing requests in a mediator pattern implementation. This interface defines a contract for handling requests of a specific type and returning a response of a specified type. The generic type parameters TRequest and TResponse represent the types of the request and response, respectively. Implementations of this interface are responsible for processing the incoming request and producing the appropriate response asynchronously.
/// </summary>
/// <typeparam name="TRequest">The type of the request to handle. Must implement the IRequest interface with the specified response type.</typeparam>
/// <typeparam name="TResponse">The type of the response produced by handling the request.</typeparam>
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the incoming request and produces a response asynchronously. The method takes a request of type TRequest and an optional CancellationToken for managing cancellation. Implementations of this method should contain the logic for processing the request and returning the appropriate response of type TResponse. The asynchronous nature of this method allows for non-blocking operations, making it suitable for scenarios where handling the request may involve I/O operations or other time-consuming tasks.
    /// </summary>
    /// <param name="request">The request instance to handle. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous handle operation. The task result contains the response to the request.</returns>
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}