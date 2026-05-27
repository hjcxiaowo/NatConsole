using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NATConsole.Protocol;

public static class MessageFraming
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const int MaxPayloadBytes = 10 * 1024 * 1024;

    public static async Task SendAsync(Stream stream, TunnelMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (json.Length > MaxPayloadBytes)
            throw new InvalidOperationException($"消息过大: {json.Length} 字节");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, json.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(json, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<TunnelMessage?> ReceiveAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct))
            return null;

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaxPayloadBytes)
            throw new InvalidDataException($"非法帧长度: {length}");

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct))
            return null;

        return JsonSerializer.Deserialize<TunnelMessage>(payload, JsonOptions);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
                return offset > 0 ? throw new EndOfStreamException() : false;
            offset += read;
        }
        return true;
    }
}

public sealed class TunnelConnection : IAsyncDisposable
{
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public TunnelConnection(NetworkStream stream)
    {
        _stream = stream;
    }

    public async Task SendAsync(TunnelMessage message, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await MessageFraming.SendAsync(_stream, message, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task<TunnelMessage?> ReceiveAsync(CancellationToken ct) =>
        MessageFraming.ReceiveAsync(_stream, ct);

    public async ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        await _stream.DisposeAsync();
    }
}
