namespace Glance.Core.Media;

/// <summary>An uploaded image, GIF or .zt file was rejected; the message is safe to show to the user.</summary>
public sealed class InvalidMediaException : Exception
{
    public InvalidMediaException(string message) : base(message) { }
}
