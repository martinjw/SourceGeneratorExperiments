namespace MediatorLib;
/// <summary>
/// Interface for handling notifications in a mediator pattern implementation.
/// </summary>
/// <typeparam name="TNotification"></typeparam>
public interface INotificationHandler<TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Handles the given notification asynchronously.
    /// </summary>
    /// <param name="notification">The notification instance to handle. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous handle operation.</returns>
    Task HandleAsync(TNotification notification, CancellationToken cancellationToken = default);
}