using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MediatorLib.Mediator
{
    /// <summary>
    /// The Mediator class implements the IMediator interface and serves as the central component of the mediator pattern. It is responsible for sending requests to their corresponding handlers and publishing notifications to all registered handlers. The Mediator uses an IServiceProvider to resolve handler instances and an IHandlerRegistry to keep track of which handlers are associated with which request and notification types.
    /// </summary>
    public class Mediator : IMediator
    {
        private readonly IServiceProvider _provider;
        private readonly IEnumerable<IHandlerRegistry> _registries;

        /// <summary>
        /// Creates a new instance of the Mediator class with the specified service provider and handler registries. The service provider is used to resolve handler instances at runtime, while the handler registries maintain the mappings of request and notification types to their corresponding handlers.
        /// </summary>
        /// <param name="provider">The service provider used to resolve handler instances.</param>
        /// <param name="registries">The handler registries that maintain the mappings of request and notification types to their corresponding handlers.</param>
        public Mediator(IServiceProvider provider, IEnumerable<IHandlerRegistry> registries)
        {
            _provider = provider;
            _registries = registries;
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

            if (!TryResolveHandlerType(requestType, out var handlerType))
            {
                #if DEBUG
                Trace.TraceWarning(BuildRegisteredHandlersWarning(requestType));
                #endif
                throw new InvalidOperationException($"No handler for {requestType.Name}");
            }

            var handler = _provider.GetRequiredService(handlerType);
            return InvokeHandle<TResponse>(handler, handlerType, request, cancellationToken);
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var requestType = request.GetType();

            if (!TryResolveHandlerType(requestType, out var handlerType))
            { 
                #if DEBUG
                Trace.TraceWarning(BuildRegisteredHandlersWarning(requestType));
                #endif   
                throw new InvalidOperationException($"No handler for {requestType.Name}");
            }

            var handler = _provider.GetRequiredService(handlerType);
            return InvokeHandle<TResponse>(handler, handlerType, request, cancellationToken);
        }

        public async Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        {
            var requestType = request.GetType();

            if (!TryResolveHandlerType(requestType, out var handlerType))
                throw new InvalidOperationException($"No handler for {requestType.Name}");

            var handler = _provider.GetRequiredService(handlerType);
            await InvokeHandleVoid(handler, handlerType, request, cancellationToken);
        }

        private bool TryResolveHandlerType(Type requestType, out Type handlerType)
        {
            foreach (var registry in _registries)
            {
                if (registry.RequestHandlers.TryGetValue(requestType, out handlerType))
                    return true;
            }

            if (!requestType.IsGenericType)
            {
                handlerType = null!;
                return false;
            }

            var genericRequestType = requestType.GetGenericTypeDefinition();
            foreach (var registry in _registries)
            {
                if (registry.RequestHandlers.TryGetValue(genericRequestType, out var genericHandlerType))
                {
                    handlerType = CloseGenericHandlerType(genericHandlerType, requestType);
                    return true;
                }
            }

            handlerType = null!;
            return false;
        }

        private static Task<TResponse> InvokeHandle<TResponse>(object handler, Type handlerType, object request, CancellationToken cancellationToken)
        {
            var handleMethod = handlerType.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Instance)
                            ?? handlerType.GetMethod("Handle", BindingFlags.Public | BindingFlags.Instance);

            if (handleMethod == null)
                throw new InvalidOperationException($"Handler {handlerType.Name} does not have a public Handle or HandleAsync method");

            var result = handleMethod.Invoke(handler, new[] { request, cancellationToken });
            return (Task<TResponse>)result!;
        }

        private static Task InvokeHandleVoid(object handler, Type handlerType, object request, CancellationToken cancellationToken)
        {
            var handleMethod = handlerType.GetMethod("HandleAsync", BindingFlags.Public | BindingFlags.Instance)
                            ?? handlerType.GetMethod("Handle", BindingFlags.Public | BindingFlags.Instance);

            if (handleMethod == null)
                throw new InvalidOperationException($"Handler {handlerType.Name} does not have a public Handle or HandleAsync method");

            var result = handleMethod.Invoke(handler, new[] { request, cancellationToken });
            return (Task)result!;
        }

        private string BuildRegisteredHandlersWarning(Type requestType)
        {
            var entries = _registries
                .SelectMany(registry => registry.RequestHandlers.Select(kvp => $"Request: {kvp.Key.FullName} -> {kvp.Value.FullName}")
                    .Concat(registry.NotificationHandlers.SelectMany(kvp => kvp.Value.Select(handlerType => $"Notification: {kvp.Key.FullName} -> {handlerType.FullName}"))))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .ToArray();

            return entries.Length == 0
                ? $"No handler found for {requestType.FullName}. No handlers are registered in the mediator registries."
                : $"No handler found for {requestType.FullName}. Registered handlers:{Environment.NewLine}{string.Join(Environment.NewLine, entries)}";
        }

        private static Type CloseGenericHandlerType(Type handlerType, Type requestType)
        {
            if (!handlerType.IsGenericTypeDefinition)
                return handlerType;

            var requestTypeArguments = requestType.GetGenericArguments();
            var handlerTypeArgumentsCount = handlerType.GetGenericArguments().Length;

            if (handlerTypeArgumentsCount != requestTypeArguments.Length)
            {
                throw new InvalidOperationException(
                    $"Unable to close open generic handler {handlerType.Name} for request {requestType.Name}: " +
                    $"handler expects {handlerTypeArgumentsCount} generic arguments but request provides {requestTypeArguments.Length}.");
            }

            return handlerType.MakeGenericType(requestTypeArguments);
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

            if (!_registries.Any(registry => registry.NotificationHandlers.TryGetValue(notifType, out var handlers)))
                return;

            foreach (var registry in _registries)
            {
                if (registry.NotificationHandlers.TryGetValue(notifType, out var handlers))
                {
                    foreach (var handlerType in handlers)
                    {
                        var handler = _provider.GetRequiredService(handlerType);
                        await InvokeHandleVoid(handler, handlerType, notification, cancellationToken);
                    }
                }
            }
        }
    }
}