namespace MediatorLib;

/// <summary>
/// Marker for request handlers to be discovered
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequestHandlerAttribute : Attribute { }