using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using valheimCLI;
using valheimCLI.Extensions;
using Xunit;

namespace valheimCLI.Tests;

public sealed class DisconnectedRequestTests
{
    [Fact]
    public void DisconnectAbandonsAQueuedRequestBeforeItsHandlerRuns()
    {
        RequestBroker broker = new RequestBroker();
        RequestBroker.Request request = broker.Submit("cli_extension sample/long", 30);
        RequestBroker.Response response = broker.WaitWithDisconnect(request, _ => { }, disconnected: () => true);

        Assert.False(response.Completed);
        Assert.Contains(response.Lines, line => line.StartsWith("ERROR: code=command_disconnected", StringComparison.Ordinal));
        Assert.True(broker.IsAbandoned(request.Id));
        Assert.False(broker.TryDequeue(out _));
        Assert.Equal(1, broker.ExpiredSkipped);
    }

    [Fact]
    public void DisconnectDisposesAnActiveExtensionBeforeTheNextMutation()
    {
        RequestBroker broker = new RequestBroker();
        OperationGate gate = new OperationGate();
        using ExtensionRegistry registry = new ExtensionRegistry(gate, _ => null);
        bool disposed = false;
        IEnumerator Long(ExtensionContext context)
        {
            try { while (true) yield return null; }
            finally { disposed = true; }
        }
        registry.Register("sample", "1", 1, new ExtensionCommand("long", "test", Long, readOnly: false));
        RequestBroker.Request request = broker.Submit("cli_extension sample/long", 30);
        Assert.True(broker.TryDequeue(out _));
        broker.MarkAsync(request.Id);
        ExtensionResult? result = null;
        registry.Begin("sample/long", [], request.Id, () => broker.IsAbandoned(request.Id), reply => result = reply);
        registry.Tick();
        Assert.False(gate.IsFree);

        RequestBroker.Response response = broker.WaitWithDisconnect(request, _ => { }, disconnected: () => true);
        Assert.False(response.Completed);
        registry.Tick();

        Assert.True(disposed);
        Assert.Equal("cancelled", result!.Code);
        Assert.True(gate.IsFree);
        Assert.Contains(response.Lines, line => line.StartsWith("ERROR: code=command_disconnected", StringComparison.Ordinal));
    }

    [Fact]
    public void ACompletedRequestKeepsItsResultEvenIfTheClientClosesAfterward()
    {
        RequestBroker broker = new RequestBroker();
        RequestBroker.Request request = broker.Submit("cli_extension sample/fast", 30);
        Assert.True(broker.TryDequeue(out _));
        broker.Output(request.Id, "OK: finished");
        broker.Complete(request.Id);
        RequestBroker.Response response = broker.WaitWithDisconnect(request, _ => { }, disconnected: () => true);
        Assert.True(response.Completed);
        Assert.Equal(["OK: finished"], response.Lines);
    }

    [Fact]
    public void SocketProbeSeesFinAfterTheCallingClientCloses()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using TcpClient caller = new TcpClient();
        caller.Connect(IPAddress.Loopback, port);
        using TcpClient accepted = listener.AcceptTcpClient();
        Assert.False(PeerConnection.IsClosed(accepted));
        caller.Close();
        Assert.True(SpinWait.SpinUntil(() => PeerConnection.IsClosed(accepted), TimeSpan.FromSeconds(2)));
    }
}
