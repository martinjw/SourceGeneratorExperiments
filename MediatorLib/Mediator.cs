using Microsoft.Extensions.DependencyInjection;

namespace MediatorLib
{
    /// <summary>
    /// The Mediator class implements the IMediator interface and serves as the central component of the mediator pattern. It is responsible for sending requests to their corresponding handlers and publishing notifications to all registered handlers. The Mediator uses an IServiceProvider to resolve handler instances and an IHandlerRegistry to keep track of which handlers are associated with which request and notification types.
    /// </summary>
    public class Mediator : IMediator
    {
        private readonly IServiceProvider _provider;
        private readonly IHandlerRegistry _registry;

        /// <summary>
        /// Creates a new instance of the Mediator class with the specified service provider and handler registry. The service provider is used to resolve handler instances at runtime, while the handler registry maintains the mappings of request and notification types to their corresponding handlers.
        /// </summary>
        /// <param name="provider">The service provider used to resolve handler instances.</param>
        /// <param name="registry">The handler registry that maintains the mappings of request and notification types to their corresponding handlers.</param>
        public Mediator(IServiceProvider provider, IHandlerRegistry registry)
        {
            _provider = provider;
            _registry = registry;
        }

        /// <summary>
        /// Sends a request to its corresponding handler and returns the response. The method first checks if there is a registered handler for the type of the request. If no handler is found, it throws an InvalidOperationException. If a handler is found, it resolves the handler instance from the service provider and invokes its HandleAsync method, passing in the request and cancellation token. The result of the handler's processing is returned as a Task of TResponse.
        /// </summary>
        /// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
        /// <param name="request">The request instance to send. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous send operation. The task result contains the response to the request.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public Task<TResponse> SendAsync<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            var requestType = request.GetType();

            if (!_registry.RequestHandlers.TryGetValue(requestType, out var handlerType))
                throw new InvalidOperationException($"No handler for {requestType.Name}");

            dynamic handler = _provider.GetRequiredService(handlerType);
            return handler.HandleAsync((dynamic)request, cancellationToken);
        }

        /// <summary>
        /// Publishes a notification to all registered handlers for the type of the notification. The method first checks if there are any registered handlers for the type of the notification. If no handlers are found, it simply returns. If handlers are found, it iterates through each handler type, resolves the handler instance from the service provider, and invokes its HandleAsync method, passing in the notification and cancellation token. The method returns a Task that represents the asynchronous publish operation.
        /// </summary>
        /// <typeparam name="TNotification">The type of the notification to publish.</typeparam>
        /// <param name="notification">The notification instance to publish. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous publish operation.</returns>
        public async Task PublishAsync<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            var notifType = notification.GetType();

            if (!_registry.NotificationHandlers.TryGetValue(notifType, out var handlers))
                return;

            foreach (var handlerType in handlers)
            {
                dynamic handler = _provider.GetRequiredService(handlerType);
                await handler.HandleAsync((dynamic)notification, cancellationToken);
            }
        }
    }
}