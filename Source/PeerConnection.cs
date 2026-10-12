using System;
using System.Net.Sockets;

namespace valheimCLI
{
    /// <summary>Socket-thread probe while waiting for an async game command. Connected is a stale last-operation flag.</summary>
    internal static class PeerConnection
    {
        internal static bool IsClosed(TcpClient client)
        {
            try
            {
                Socket socket = client.Client;
                return socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0;
            }
            catch (ObjectDisposedException) { return true; }
            catch (SocketException) { return true; }
        }
    }
}
