using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LaminarVR.AdaptiveMeditation.Runtime.Networking
{
    public readonly struct ComponentBWebSocketMessage
    {
        private ComponentBWebSocketMessage(string text, bool closeReceived)
        {
            Text = text;
            CloseReceived = closeReceived;
        }

        public string Text { get; }

        public bool CloseReceived { get; }

        public static ComponentBWebSocketMessage FromText(string text)
        {
            return new ComponentBWebSocketMessage(text, false);
        }

        public static ComponentBWebSocketMessage Closed =>
            new ComponentBWebSocketMessage(null, true);
    }

    public interface IComponentBWebSocketConnection : IDisposable
    {
        bool IsOpen { get; }

        Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

        Task SendTextAsync(
            string text,
            CancellationToken cancellationToken);

        Task<ComponentBWebSocketMessage> ReceiveTextAsync(
            int maximumMessageBytes,
            CancellationToken cancellationToken);

        void Abort();
    }

    public interface IComponentBWebSocketConnectionFactory
    {
        IComponentBWebSocketConnection Create();
    }

    public sealed class ClientComponentBWebSocketConnectionFactory
        : IComponentBWebSocketConnectionFactory
    {
        public IComponentBWebSocketConnection Create()
        {
            return new ClientComponentBWebSocketConnection();
        }
    }

    public sealed class ClientComponentBWebSocketConnection
        : IComponentBWebSocketConnection
    {
        private const int ReceiveBufferBytes = 4096;

        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly byte[] receiveBuffer = new byte[ReceiveBufferBytes];

        public bool IsOpen => socket.State == WebSocketState.Open;

        public Task ConnectAsync(
            Uri endpoint,
            CancellationToken cancellationToken)
        {
            return socket.ConnectAsync(endpoint, cancellationToken);
        }

        public Task SendTextAsync(
            string text,
            CancellationToken cancellationToken)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException(
                    "The Component B WebSocket is not open.");
            }

            var payload = StrictUtf8.GetBytes(text ?? string.Empty);
            return socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }

        public async Task<ComponentBWebSocketMessage> ReceiveTextAsync(
            int maximumMessageBytes,
            CancellationToken cancellationToken)
        {
            if (maximumMessageBytes < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumMessageBytes));
            }

            using (var stream = new MemoryStream())
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(receiveBuffer),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return ComponentBWebSocketMessage.Closed;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        throw new InvalidDataException(
                            "Component B sent a non-text WebSocket message.");
                    }

                    if (stream.Length + result.Count > maximumMessageBytes)
                    {
                        throw new InvalidDataException(
                            "Component B WebSocket message exceeded its configured limit.");
                    }

                    stream.Write(receiveBuffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        return ComponentBWebSocketMessage.FromText(
                            StrictUtf8.GetString(stream.ToArray()));
                    }
                }
            }
        }

        public void Abort()
        {
            socket.Abort();
        }

        public void Dispose()
        {
            socket.Dispose();
        }
    }
}
