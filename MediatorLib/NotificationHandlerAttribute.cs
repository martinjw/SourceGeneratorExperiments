namespace MediatorLib;

/// <summary>
/// Marker for notification handlers to be discovered
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotificationHandlerAttribute : Attribute { }