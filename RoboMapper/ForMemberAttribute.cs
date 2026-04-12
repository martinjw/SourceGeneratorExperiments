using System;

namespace RoboMapper;

/// <summary>
/// Attribute marker for mapped classes (optional)
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ForMemberAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the ForMemberAttribute class, specifying the source and destination types and the
    /// corresponding member names to map between them.
    /// </summary>
    /// <param name="source">The type that contains the source member to be mapped.</param>
    /// <param name="destination">The type that contains the destination member to be mapped.</param>
    /// <param name="destMember">The name of the member in the destination type to which the value will be mapped.</param>
    /// <param name="sourceMember">The name of the member in the source type from which the value will be mapped.</param>
    public ForMemberAttribute(Type source, Type destination, string destMember, string sourceMember)
    {
        Source = source;
        Destination = destination;
        DestinationMember = destMember;
        SourceMember = sourceMember;
    }

    public Type Source { get; }
    public Type Destination { get; }
    public string DestinationMember { get; }
    public string SourceMember { get; }
}