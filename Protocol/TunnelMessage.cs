using System.Text.Json.Serialization;

namespace NATConsole.Protocol;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegisterMessage), "register")]
[JsonDerivedType(typeof(RegisterAckMessage), "registerAck")]
[JsonDerivedType(typeof(HttpProxyRequestMessage), "httpRequest")]
[JsonDerivedType(typeof(HttpProxyResponseMessage), "httpResponse")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PongMessage), "pong")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record TunnelMessage;

public sealed record RegisterMessage(
    string Token,
    string TunnelId,
    string LocalHost,
    int LocalPort) : TunnelMessage;

public sealed record RegisterAckMessage(
    bool Success,
    string? PublicBaseUrl,
    string? Message) : TunnelMessage;

public sealed record HttpProxyRequestMessage(
    Guid RequestId,
    string Method,
    string Path,
    string QueryString,
    Dictionary<string, string> Headers,
    byte[]? Body) : TunnelMessage;

public sealed record HttpProxyResponseMessage(
    Guid RequestId,
    int StatusCode,
    Dictionary<string, string> Headers,
    byte[]? Body) : TunnelMessage;

public sealed record PingMessage : TunnelMessage;

public sealed record PongMessage : TunnelMessage;

public sealed record ErrorMessage(string Message) : TunnelMessage;
