using System;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class ScreenReceiver : NetworkBehaviour
{
    [Header("Python Streamer Settings (owner only)")]
    public string streamerIP = "172.16.211.23";
    public int streamerPort = 9999;

    [Header("UI")]
    public RawImage rawImage;

    const int MaxChunkSize = 1000;
    const int TextureWidth = 1280;
    const int TextureHeight = 720;
    public float sendInterval = 0.25f;

    private TcpClient tcpClient;
    private NetworkStream tcpStream;
    private Thread receiverThread;
    private bool tcpConnected = false;
    private bool stopReceiver = false;

    private ConcurrentQueue<byte[]> ownerFrameQueue = new ConcurrentQueue<byte[]>();

    class FrameAssembler
    {
        public List<byte> Buffer = new List<byte>();
        public int ExpectedChunks = 0;
        public int ReceivedChunks = 0;
    }
    private Dictionary<ulong, FrameAssembler> assemblers = new Dictionary<ulong, FrameAssembler>();
    private Dictionary<ulong, FrameAssembler> assemblies = new Dictionary<ulong, FrameAssembler>();

    private Texture2D displayTexture;
    private float sendTimer = 0f;

    void Awake()
    {
        displayTexture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGB24, false);
        if (rawImage != null)
            rawImage.texture = displayTexture;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        ConnectToPythonStreamer(streamerIP, streamerPort);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        StopOwnerReceiver();
    }

    void Update()
    {
        if (IsOwner)
        {
            sendTimer += Time.deltaTime;
            while (ownerFrameQueue.TryDequeue(out byte[] rawFrame))
            {
                if (displayTexture.LoadImage(rawFrame))
                {
                    displayTexture.Apply();
                    if (rawImage != null)
                        rawImage.texture = displayTexture;
                }

                if (sendTimer >= sendInterval)
                {
                    sendTimer = 0f;
                    byte[] compressed = displayTexture.EncodeToJPG(45);
                    SendCompressedFrame(compressed);
                }
            }
        }
    }

    // ---------------- Manual connection ----------------
    public void ConnectToPythonStreamer(string ip, int port)
    {
        streamerIP = ip;
        streamerPort = port;
        Thread connectThread = new Thread(() =>
        {
            try
            {
                tcpClient = new TcpClient();
                tcpClient.Connect(streamerIP, streamerPort);
                tcpStream = tcpClient.GetStream();
                tcpConnected = true;
                stopReceiver = false;
                Debug.Log($"[Owner] Connected to Python streamer at {ip}:{port}");

                receiverThread = new Thread(OwnerReceiveLoop) { IsBackground = true };
                receiverThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Owner] Manual connect failed: {e.Message}");
                tcpConnected = false;
            }
        });
        connectThread.IsBackground = true;
        connectThread.Start();
    }

    void StopOwnerReceiver()
    {
        stopReceiver = true;
        tcpConnected = false;
        try { tcpStream?.Close(); tcpClient?.Close(); } catch { }
        try { receiverThread?.Join(200); } catch { }
    }

    void OwnerReceiveLoop()
    {
        try
        {
            while (tcpConnected && !stopReceiver)
            {
                byte[] lenBuf = ReadExactly(tcpStream, 8);
                long size = BitConverter.ToInt64(lenBuf, 0);
                if (size <= 0) continue;
                byte[] frame = ReadExactly(tcpStream, (int)size);
                ownerFrameQueue.Enqueue(frame);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[Owner] Receive loop ended: " + e.Message);
        }
        finally
        {
            tcpConnected = false;
        }
    }

    static byte[] ReadExactly(NetworkStream s, int size)
    {
        byte[] buf = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            int read = s.Read(buf, offset, size - offset);
            if (read == 0) throw new Exception("Disconnected");
            offset += read;
        }
        return buf;
    }

    // ---------------- Chunked RPC sending ----------------
    void SendCompressedFrame(byte[] data)
    {
        int totalChunks = Mathf.CeilToInt((float)data.Length / MaxChunkSize);
        for (int i = 0; i < totalChunks; ++i)
        {
            int chunkSize = Math.Min(MaxChunkSize, data.Length - i * MaxChunkSize);
            byte[] chunk = new byte[chunkSize];
            Buffer.BlockCopy(data, i * MaxChunkSize, chunk, 0, chunkSize);
            SendFrameChunkServerRpc(chunk, i, totalChunks, NetworkManager.Singleton.LocalClientId, new NetworkObjectReference(this.NetworkObject));
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void SendFrameChunkServerRpc(byte[] chunk, int chunkIndex, int totalChunks, ulong clientID, NetworkObjectReference networkObjectReference = default)
    {
        if (!assemblies.TryGetValue(clientID, out FrameAssembler fa))
        {
            fa = new FrameAssembler();
            assemblies[clientID] = fa;
        }

        if (chunkIndex == 0)
        {
            fa.Buffer.Clear();
            fa.ReceivedChunks = 0;
            fa.ExpectedChunks = totalChunks;
        }

        fa.Buffer.AddRange(chunk);
        fa.ReceivedChunks++;

        if (fa.ReceivedChunks >= fa.ExpectedChunks)
        {
            byte[] image = fa.Buffer.ToArray();
            ScreenReceiver receiver = networkObjectReference.TryGet(out NetworkObject netObj) ? netObj.GetComponent<ScreenReceiver>() : null;
            if (receiver != null)
            {
                if (receiver.displayTexture.LoadImage(image))
                {
                    receiver.displayTexture.Apply();
                    if (receiver.rawImage != null)
                        receiver.rawImage.texture = receiver.displayTexture;
                }
            }
            BroadcastFrameChunksToClients(image, clientID);
            fa.Buffer.Clear();
            fa.ReceivedChunks = 0;
            fa.ExpectedChunks = 0;
        }
    }

    void BroadcastFrameChunksToClients(byte[] image, ulong originClientId)
    {
        int totalChunks = Mathf.CeilToInt((float)image.Length / MaxChunkSize);
        for (int i = 0; i < totalChunks; ++i)
        {
            int chunkSize = Math.Min(MaxChunkSize, image.Length - i * MaxChunkSize);
            byte[] chunk = new byte[chunkSize];
            Buffer.BlockCopy(image, i * MaxChunkSize, chunk, 0, chunkSize);
            BroadcastFrameChunkClientRpc(chunk, i, totalChunks, originClientId);
        }
    }

    [ClientRpc]
    void BroadcastFrameChunkClientRpc(byte[] chunk, int chunkIndex, int totalChunks, ulong originClientId)
    {
        // Skip if this client is the one who sent the frame (already have it locally)
        if (originClientId == NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        Debug.Log($"[Client {NetworkManager.Singleton.LocalClientId}] Receiving chunk {chunkIndex}/{totalChunks} from client {originClientId}");

        if (!assemblers.TryGetValue(originClientId, out FrameAssembler fa))
        {
            fa = new FrameAssembler();
            assemblers[originClientId] = fa;
        }

        if (chunkIndex == 0)
        {
            fa.Buffer.Clear();
            fa.ReceivedChunks = 0;
            fa.ExpectedChunks = totalChunks;
        }

        fa.Buffer.AddRange(chunk);
        fa.ReceivedChunks++;

        if (fa.ReceivedChunks >= fa.ExpectedChunks)
        {
            byte[] full = fa.Buffer.ToArray();
            try
            {
                if (displayTexture.LoadImage(full))
                {
                    displayTexture.Apply();
                    if (rawImage != null)
                        rawImage.texture = displayTexture;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error applying frame: " + e.Message);
            }

            fa.Buffer.Clear();
            fa.ReceivedChunks = 0;
            fa.ExpectedChunks = 0;
        }
    }

    public override void OnDestroy()
    {
        StopOwnerReceiver();
    }
}
