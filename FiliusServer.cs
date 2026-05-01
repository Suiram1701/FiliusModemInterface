using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FiliusModemInterface.Filius;
using FiliusModemInterface.Filius.Vermittlungsschicht;
using FiliusModemInterface.JavaObjectStream;
using static FiliusModemInterface.Program;

namespace FiliusModemInterface;

public class FiliusServer(IPAddress ip, int port)
{
    private const int _maxConnections = 20;
    private const int _idleProcessTimeout = 50;

    private readonly TcpListener _listener = new(ip, port);
    private int _clientCounter = 0;
    private readonly Dictionary<int, Client> _clients = new();
    private readonly Dictionary<int, Task> _handles = new();

    private readonly ConcurrentDictionary<string, int> _macTable = new();
    private readonly Dictionary<string, string> _respondArp = [];
    private readonly Dictionary<string, Func<ProtocolDataUnit, CancellationToken, Task<ProtocolDataUnit>>> _macHandlers = [];
    
    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();
        try
        {
            LogInfo($"Started broadcasting server. Listening on {_listener.LocalEndpoint}");
            while (!ct.IsCancellationRequested)
            {
                TcpClient newClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                if (_clients.Count >= _maxConnections)
                {
                    newClient.Close();
                    LogWarning($"Disposed incoming connection. Max parallel connections of {_maxConnections} reached!");
                    continue;
                }

                LogInfo($"New client connected {newClient.Client.RemoteEndPoint}");
                _handles.Add(_clientCounter, HandleClientAsync(_clientCounter, newClient, ct));
                _clientCounter++;
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    public bool RespondOn(string mac, IPAddress ip, Func<ProtocolDataUnit, CancellationToken, Task<ProtocolDataUnit>> onFrame)
    {
        if (_macHandlers.ContainsKey(mac))
            return false;

        _respondArp[ip.ToString()] = mac;
        _macHandlers[mac] = onFrame;
        LogInfo($"Starts responding on {ip} with {mac}");
        return true;
    }
    
    private async Task HandleClientAsync(int id, TcpClient client, CancellationToken ct)
    {
        byte[] magicBytes = [0xAC, 0xED, 0x00, 0x05];
        
        await using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(magicBytes, 0, magicBytes.Length, ct);
        
        var magicBytesCheck = new byte[4];
        await stream.ReadExactlyAsync(magicBytesCheck, 0, magicBytesCheck.Length, ct);
        if (!magicBytesCheck.SequenceEqual(new ReadOnlySpan<byte>(magicBytes)))
        {
            LogError($"Failed to receive magic bytes for client {id}. Disposing client.");
            client.Close();
            return;
        }
        
        long lastPosition = 0;
        
        var buffer = new byte[1024];
        using MemoryStream memory = new();
        using JavaObjectReader objectReader = new(memory);
        using JavaObjectWriter objectWriter = new(stream, JavaObjectSerializer.GetClassDesc);
        _clients.Add(id, new Client(client, stream, objectReader, objectWriter, new SemaphoreSlim(1, 1)));
        
        while (!ct.IsCancellationRequested && client.Connected)
        {
            try
            {
                while (true)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(_idleProcessTimeout);

                    var bytesRead = 0;
                    try
                    {
                        bytesRead = await stream.ReadAsync(buffer.AsMemory(), cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (bytesRead == 0)
                        break;
                    await memory.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                }

                if (memory.Position - lastPosition == 0)
                    continue;

                try
                {
                    var sw = Stopwatch.StartNew();
                    memory.Seek(lastPosition, SeekOrigin.Begin);

                    var frame = objectReader.ReadObject<EthernetFrame>();
                    _ = HandleFrameAsync(id, frame, ct).ConfigureAwait(false);

                    lastPosition = memory.Length;
                    
                    sw.Stop();
                    LogInfo($"Handling frame took: {sw.Elapsed}");
                }
                catch (EndOfStreamException)
                {
                    LogInfo($"Rollback {id} ...");
                }
                finally
                {
                    memory.Seek(0, SeekOrigin.End);
                }
            }
            catch (IOException ioEx) when (ioEx.InnerException is SocketException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogError($"An error occured while reading client {id}: {ex}");
            }
        }
        
        LogError($"Client {id} disconnected");
        
        _clients.Remove(id);
        _handles.Remove(id);

        foreach (string index in _macTable.Where(kvp => kvp.Value == id).Select(kvp => kvp.Key))
            _macTable.Remove(index, out _);
    }

    private async Task HandleFrameAsync(int sourcePort, EthernetFrame frame, CancellationToken ct)
    {
        _macTable[frame.SourceMac] = sourcePort;
    
        if (frame.Payload is ArpPaket { Operation: ArpPaket.Request } arp &&
            _respondArp.TryGetValue(arp.TargetIP, out string? respondMac))
        {
            EthernetFrame response = new()
            {
                SourceMac = respondMac,
                DestinationMac = frame.SourceMac,
                Type = EthernetFrame.ARP,
                Payload = new ArpPaket
                {
                    ArpPacketNumber = 0,
                    ArpPacketNumberCounter = 0,
                    Type = EthernetFrame.IP,
                    Operation = ArpPaket.Reply,
                    SourceMac = respondMac,
                    SourceIP = arp.TargetIP,
                    TargetMac = frame.SourceMac,
                    TargetIP = arp.SourceIP
                }
            };
            await TryWriteClientAsync(sourcePort, response, ct).ConfigureAwait(false);
            return;
        }
        
        if (_macHandlers.TryGetValue(frame.DestinationMac, out Func<ProtocolDataUnit, CancellationToken, Task<ProtocolDataUnit>>? func))
        {
            EthernetFrame response = new()
            {
                SourceMac = frame.DestinationMac,
                DestinationMac = frame.SourceMac,
                Type = EthernetFrame.IP,
                Payload = await func(frame.Payload, ct).ConfigureAwait(false)
            };
            await TryWriteClientAsync(sourcePort, response, ct).ConfigureAwait(false);
            return;
        }
        
        int targetPort = _macTable.GetValueOrDefault(frame.DestinationMac, defaultValue: -1);
        if (targetPort != -1)
        {
            await TryWriteClientAsync(targetPort, frame, ct).ConfigureAwait(false);
        }
        else
        {
            int[] targetPorts = _clients.Keys.Except([sourcePort]).ToArray();
            await Parallel.ForEachAsync(targetPorts, ct, async (id, innerCt) => 
                await TryWriteClientAsync(id, frame, innerCt));
        }
    }
    
    private async Task<bool> TryWriteClientAsync(int port, EthernetFrame frame, CancellationToken ct)
    {
        Client client = _clients[port];
            
        try
        {
            await client.WriteLock.WaitAsync(ct);
            
            client.Writer.WriteObject(frame);
            await client.Stream.FlushAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            if (ex is not IOException)     // Ignore in case the connection broke
                LogError($"An error occurred while writing to client {port}: {ex}");
            return false;
        }
        finally
        {
            client.WriteLock.Release();
        }
    }

    private record Client(TcpClient TcpClient, NetworkStream Stream, JavaObjectReader Reader, JavaObjectWriter Writer, SemaphoreSlim WriteLock);
}