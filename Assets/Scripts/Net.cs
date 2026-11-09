using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

// Host / join through Unity Relay (a join code, no open ports) on Unity Transport.
// Star topology: clients talk only to the host. A message is a byte array whose first byte is its type.
public class Net : IDisposable
{
    public bool IsHost { get; private set; }
    public string Code { get; private set; }
    public Action<int, byte[]> onMessage;          // (from: player id on the host, 0 = host on a client, data)
    public Action<int> onConnected, onDisconnected; // host: player id; client: 0 = the host

    NetworkDriver driver;
    NetworkPipeline reliable, unreliable;
    readonly List<NetworkConnection> conns = new(); // host: player id = index + 1; client: [0] = host

    static async Task SignIn()
    {
        var options = new InitializationOptions();
#if UNITY_EDITOR
        // Multiplayer Play Mode instances share PlayerPrefs (= the same anonymous player): one profile per editor process
        options.SetProfile("p" + System.Diagnostics.Process.GetCurrentProcess().Id);
#endif
        if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync(options);
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public async Task Host(int maxPlayers)
    {
        await SignIn();
        var alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
        Code = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
        var data = alloc.ToRelayServerData("dtls");
        Create(ref data);
        IsHost = true;
        if (driver.Bind(NetworkEndpoint.AnyIpv4) != 0 || driver.Listen() != 0) throw new Exception("Impossibile aprire la partita");
    }

    public async Task Join(string code)
    {
        await SignIn();
        var join = await RelayService.Instance.JoinAllocationAsync(code.Trim().ToUpperInvariant());
        var data = join.ToRelayServerData("dtls");
        Create(ref data);
        Code = code;
        conns.Add(driver.Connect(data.Endpoint));
    }

    void Create(ref RelayServerData data)
    {
        var settings = new NetworkSettings(Allocator.Temp);
        settings.WithRelayParameters(ref data);
        settings.WithFragmentationStageParameters(payloadCapacity: 32 * 1024); // level text + ball list fit in one message
        driver = NetworkDriver.Create(settings);
        reliable = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        unreliable = driver.CreatePipeline(typeof(FragmentationPipelineStage));
    }

    public void Update()
    {
        if (!driver.IsCreated) return;
        driver.ScheduleUpdate().Complete();
        if (IsHost)
        {
            NetworkConnection c;
            while ((c = driver.Accept()) != default)
            {
                conns.Add(c);
                onConnected?.Invoke(conns.Count);
            }
        }
        for (int i = 0; i < conns.Count; i++)
        {
            int from = IsHost ? i + 1 : 0;
            NetworkEvent.Type type;
            while (driver.IsCreated && conns[i].IsCreated && (type = driver.PopEventForConnection(conns[i], out var reader)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Data)
                {
                    var bytes = new byte[reader.Length];
                    reader.ReadBytes(bytes.AsSpan());
                    onMessage?.Invoke(from, bytes);
                }
                else if (type == NetworkEvent.Type.Connect) onConnected?.Invoke(from);
                else if (type == NetworkEvent.Type.Disconnect)
                {
                    conns[i] = default;
                    onDisconnected?.Invoke(from);
                }
            }
        }
    }

    // host: to = player id, -1 = everyone; client: always to the host
    public void Send(int to, byte[] data, bool reliableSend = true)
    {
        if (!driver.IsCreated) return;
        if (!IsHost) SendTo(conns[0], data, reliableSend);
        else if (to < 0) foreach (var c in conns) SendTo(c, data, reliableSend);
        else if (to - 1 < conns.Count) SendTo(conns[to - 1], data, reliableSend);
    }

    public void Kick(int id)
    {
        if (IsHost && id - 1 < conns.Count && conns[id - 1].IsCreated) driver.Disconnect(conns[id - 1]);
        if (IsHost && id - 1 < conns.Count) conns[id - 1] = default;
    }

    void SendTo(NetworkConnection c, byte[] data, bool reliableSend)
    {
        if (!c.IsCreated) return;
        if (driver.BeginSend(reliableSend ? reliable : unreliable, c, out var w) != 0) return;
        w.WriteBytes(data.AsSpan());
        driver.EndSend(w);
    }

    public void Dispose()
    {
        if (!driver.IsCreated) return;
        driver.ScheduleUpdate().Complete();
        foreach (var c in conns) if (c.IsCreated) driver.Disconnect(c);
        driver.ScheduleUpdate().Complete();
        driver.Dispose();
    }

    // message helpers
    public static byte[] Msg(byte type, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(type);
        body?.Invoke(w);
        w.Flush();
        return ms.ToArray();
    }
}
