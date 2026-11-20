using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using valheimCLI;
using Xunit;

namespace valheimCLI.Tests;

/// <summary>#980: a connection's socket thread ends when its caller closes the connection.</summary>
public sealed class ClientLinesTests
{
    [Fact]
    public void EveryConnectionThreadEndsWhenItsCallerCloses()
    {
        // The CommandServer shape: one thread per accepted connection reading lines until Next returns null.
        // Each caller sends one command, reads the reply and closes, as valheim-test does per command.
        const int Commands = 40;
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var threads = new List<Thread>();
        bool running = true;
        try
        {
            for (int i = 0; i < Commands; i++)
            {
                using (TcpClient caller = new TcpClient())
                {
                    caller.Connect(IPAddress.Loopback, port);
                    TcpClient accepted = listener.AcceptTcpClient();
                    Thread thread = new Thread(() => Serve(accepted, () => Volatile.Read(ref running))) { IsBackground = true };
                    thread.Start();
                    threads.Add(thread);

                    NetworkStream stream = caller.GetStream();
                    var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    var reader = new StreamReader(stream, new UTF8Encoding(false));
                    writer.WriteLine("PING");
                    Assert.Equal("PONG", reader.ReadLine());
                }
            }

            // The server keeps running: only the callers' close may end the threads.
            foreach (Thread thread in threads)
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "a connection thread kept running after its caller closed");
        }
        finally
        {
            Volatile.Write(ref running, false);
        }
    }

    [Fact]
    public void AnOpenIdleConnectionWaitsAndStopsWithTheServer()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using TcpClient caller = new TcpClient();
        caller.Connect(IPAddress.Loopback, port);
        using TcpClient accepted = listener.AcceptTcpClient();
        using NetworkStream stream = accepted.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        int polls = 0;
        var lines = new ClientLines(accepted, stream, reader, _ => polls++);

        // Still connected and silent: Next keeps polling until the server stops.
        Assert.Null(lines.Next(() => polls < 5));
        Assert.Equal(5, polls);
    }

    [Fact]
    public void ALineSentBeforeTheCallerClosesIsStillRead()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using TcpClient accepted = Accept(listener, port, caller =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes("CMD:help\n");
            caller.GetStream().Write(bytes, 0, bytes.Length);
        });
        using NetworkStream stream = accepted.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        var lines = new ClientLines(accepted, stream, reader, Thread.Sleep);

        Assert.True(SpinWait.SpinUntil(() => stream.DataAvailable, TimeSpan.FromSeconds(2)));
        Assert.Equal("CMD:help", lines.Next(() => true));
        // The caller has closed: Next ends on that, well before this bound would stop it.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(lines.Next(() => clock.Elapsed < TimeSpan.FromSeconds(5)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"Next waited {clock.Elapsed.TotalSeconds:F1} s after the caller closed");
    }

    private static TcpClient Accept(TcpListener listener, int port, Action<TcpClient> send)
    {
        using TcpClient caller = new TcpClient();
        caller.Connect(IPAddress.Loopback, port);
        TcpClient accepted = listener.AcceptTcpClient();
        send(caller);
        return accepted;
    }

    private static void Serve(TcpClient client, Func<bool> running)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
        {
            var lines = new ClientLines(client, stream, reader);
            try
            {
                string? line;
                while ((line = lines.Next(running)) != null)
                    if (line == "PING") writer.WriteLine("PONG");
            }
            catch (IOException) { }
        }
    }
}
