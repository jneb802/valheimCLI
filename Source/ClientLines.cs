using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace valheimCLI
{
    /// <summary>
    /// A connection's socket thread reads its caller's lines here. <see cref="Next"/> returns null once the caller has
    /// closed the connection or the server stops, so the thread ends with its connection (#980: TcpClient.Connected is
    /// a stale last-operation flag, and a loop on it alone kept one thread per finished command polling forever).
    /// </summary>
    internal sealed class ClientLines
    {
        /// <summary>How long the thread sleeps between checks while its caller sends nothing.</summary>
        internal const int IdlePollMs = 50;

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly StreamReader _reader;
        private readonly Action<int> _sleep;

        internal ClientLines(TcpClient client, NetworkStream stream, StreamReader reader, Action<int>? sleep = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _sleep = sleep ?? Thread.Sleep;
        }

        /// <summary>The caller's next line, or null when it has closed the connection or <paramref name="running"/> turns false.</summary>
        internal string? Next(Func<bool> running)
        {
            while (running())
            {
                if (_stream.DataAvailable) return _reader.ReadLine();
                if (PeerConnection.IsClosed(_client)) return null;
                _sleep(IdlePollMs);
            }
            return null;
        }
    }
}
