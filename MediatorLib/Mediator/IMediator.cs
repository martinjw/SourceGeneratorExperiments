namespace MediatorLib.Mediator
{
    /// <summary>
    /// A mediator interface that defines methods for sending requests and publishing notifications in a mediator pattern implementation.
    /// </summary>
    public interface IMediator
    {
        /// <summary>
        /// Sends the specified request asynchronously and returns the response when available.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
        /// <param name="request">The request to send. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous send operation. The task result contains the response to the request.</returns>
        Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously send a request to a single handler with no response
        /// </summary>
        /// <param name="request">Request object</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>A task that represents the send operation.</returns>
        Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest;

        /// <summary>
        /// Asynchronously publishes a notification to all registered handlers.
        /// </summary>
        /// <typeparam name="TNotification">The type of notification to publish. Must implement the INotification interface.</typeparam>
        /// <param name="notification">The notification instance to be published to handlers. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the publish operation.</param>
        /// <returns>A task that represents the asynchronous publish operation.</returns>
        Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification;
    }
}