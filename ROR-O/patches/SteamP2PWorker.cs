using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Configuration;
using Facepunch.Steamworks;
using HarmonyLib;

namespace ROR_O.patches
{
    internal static class SteamP2PNative
    {
        private const string Library = "steam_api64.dll";

        [DllImport(Library, EntryPoint = "SteamAPI_ISteamNetworking_SendP2PPacket")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SendP2PPacket(IntPtr steamNetworking, ulong steamIdRemote, IntPtr data, uint length, int sendType, int channel);

        [DllImport(Library, EntryPoint = "SteamAPI_ISteamNetworking_IsP2PPacketAvailable")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsP2PPacketAvailable(IntPtr steamNetworking, out uint messageSize, int channel);

        [DllImport(Library, EntryPoint = "SteamAPI_ISteamNetworking_ReadP2PPacket")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ReadP2PPacket(IntPtr steamNetworking, IntPtr destination, uint destinationSize, out uint messageSize, out ulong steamIdRemote, int channel);

        [DllImport(Library, EntryPoint = "SteamAPI_ISteamNetworking_CloseP2PSessionWithUser")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool CloseP2PSessionWithUser(IntPtr steamNetworking, ulong steamIdRemote);

        // SteamAPI_RunCallbacks frees this for the main thread; any other thread calling Steam has to do it itself.
        [DllImport(Library, EntryPoint = "SteamAPI_ReleaseCurrentThreadMemory")]
        public static extern void ReleaseCurrentThreadMemory();
    }

    // Runs every ISteamNetworking call of the client interface on one background thread. Sends, session closes and
    // session accepts share one FIFO queue so Steam sees them in the same order vanilla issues them; received packets
    // are handed back to the main thread, which still runs all UNet and game code.
    internal sealed unsafe class SteamP2PWorker
    {
        // The main thread takes received packets once per frame, so polling faster only burns Steam calls.
        private const long ReceivePollIntervalMilliseconds = 2;
        private const long IdleChannelSweepMilliseconds = 250;
        private const long ReleaseThreadMemoryMilliseconds = 1000;
        private const long BlockedPeerTimeoutMilliseconds = 5000;
        private const int PooledBufferSize = 2048;
        private const int MaxPooledBuffers = 1024;
        private const int StopTimeoutMilliseconds = 1000;
        private const int GameplayChannel = 0;

        private enum CommandKind : byte
        {
            Send,
            Close,
        }

        private struct Command
        {
            public CommandKind Kind;
            public ulong SteamId;
            public byte[]? Buffer;
            public int Length;
            public int SendType;
            public int Channel;
        }

        private struct Packet
        {
            public ulong SteamId;
            public byte[] Buffer;
            public int Length;
            public int Channel;
        }

        private sealed class BlockedPeer
        {
            public readonly Queue<Command> Commands = new Queue<Command>();
            public long LastProgressTime;
        }

        private static readonly FieldInfo? NativeNetworkingField = AccessTools.Field(typeof(Networking), "networking");

        private static bool bindingFailed;
        private static bool threadedSteamWasEnabled;
        private static int directCallDepth;

        public static SteamP2PWorker? Active { get; private set; }

        private readonly Networking owner;
        private readonly IntPtr steamNetworking;
        private readonly Thread thread;
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly object commandLock = new object();
        private readonly object packetLock = new object();
        private readonly object poolLock = new object();
        private readonly Stack<byte[]> bufferPool = new Stack<byte[]>();
        private readonly Stopwatch workerClock = new Stopwatch();
        private readonly Dictionary<ulong, BlockedPeer> blockedPeers = new Dictionary<ulong, BlockedPeer>();
        private readonly List<ulong> blockedPeerScratch = new List<ulong>();

        private Queue<Command> pendingCommands = new Queue<Command>();
        private Queue<Command> executingCommands = new Queue<Command>();
        private Queue<Packet> receivedPackets = new Queue<Packet>();
        private Queue<Packet> dispatchingPackets = new Queue<Packet>();

        private volatile int[] listenChannels = Array.Empty<int>();
        private volatile bool running = true;
        private volatile Exception? fault;

        private long packetsSent;
        private long packetsReceived;
        private long sendFailures;
        private long reportedSendFailures;

        private SteamP2PWorker(Networking owner, IntPtr steamNetworking)
        {
            this.owner = owner;
            this.steamNetworking = steamNetworking;
            thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "ROR-O Steam P2P",
            };
        }

        public static bool IsBoundTo(Networking networking)
        {
            return Active != null && ReferenceEquals(Active.owner, networking);
        }

        // Steam runs the session-request callback on the main thread and expects Close, the first reply, and Accept
        // to finish before that callback returns. Those calls must not wait on this thread.
        public static bool DirectCalls => directCallDepth > 0;

        public static void BeginDirectCalls()
        {
            directCallDepth++;
        }

        public static void EndDirectCalls()
        {
            if (directCallDepth > 0)
            {
                directCallDepth--;
            }
        }

        // Called from Networking.Update on the main thread. Returns true when the worker owns this interface.
        public static bool TryDispatch(Networking networking, List<int> currentListenChannels)
        {
            SteamP2PWorker? worker = Active;
            if (worker != null && !ReferenceEquals(worker.owner, networking))
            {
                // Server networking ticks beside the client. Only a replaced client interface should drop the old thread.
                if (Client.Instance == null || !ReferenceEquals(Client.Instance.Networking, networking))
                {
                    return false;
                }

                ReleaseBinding(dispatchRemaining: true);
                worker = null;
            }

            if (worker == null)
            {
                if (!ShouldBind(networking))
                {
                    return false;
                }

                worker = TryStart(networking, currentListenChannels);
                if (worker == null)
                {
                    return false;
                }
            }

            if (worker.fault != null || !SteamP2PSettings.Threaded)
            {
                if (worker.fault != null)
                {
                    RORO.GlobalLogger?.LogError($"Steam P2P I/O thread failed, falling back to the main thread: {worker.fault}");
                    bindingFailed = true;
                }

                StopActive(dispatchRemaining: true);
                return false;
            }

            worker.SyncListenChannels(currentListenChannels);
            worker.ReportSendFailures();
            worker.Dispatch();
            return true;
        }

        public static void StopActive(bool dispatchRemaining)
        {
            SteamP2PWorker? worker = Active;
            if (worker == null)
            {
                return;
            }

            Active = null;
            worker.Stop(dispatchRemaining);
        }

        // A new Steam interface, a disposed one, or turning the setting back on must not keep a stale pointer.
        public static void ReleaseBinding(bool dispatchRemaining)
        {
            StopActive(dispatchRemaining);
            bindingFailed = false;
        }

        private static bool ShouldBind(Networking networking)
        {
            bool enabled = SteamP2PSettings.Threaded;
            if (enabled && !threadedSteamWasEnabled)
            {
                bindingFailed = false;
            }

            threadedSteamWasEnabled = enabled;
            return !bindingFailed
                && enabled
                && networking.OnP2PData != null
                && Client.Instance != null
                && ReferenceEquals(Client.Instance.Networking, networking);
        }

        private static SteamP2PWorker? TryStart(Networking networking, List<int> currentListenChannels)
        {
            try
            {
                IntPtr pointer = GetInterfacePointer(networking);
                if (pointer == IntPtr.Zero)
                {
                    bindingFailed = true;
                    RORO.GlobalLogger?.LogWarning("Steam P2P I/O thread is unavailable: the ISteamNetworking pointer was not found. Using the main thread.");
                    return null;
                }

                Marshal.PrelinkAll(typeof(SteamP2PNative));
                SteamP2PNative.IsP2PPacketAvailable(pointer, out _, GameplayChannel);

                var worker = new SteamP2PWorker(networking, pointer);
                worker.SyncListenChannels(currentListenChannels);
                Active = worker;
                worker.workerClock.Start();
                worker.thread.Start();
                RORO.GlobalLogger?.LogInfo("Steam P2P I/O thread started.");
                return worker;
            }
            catch (Exception exception)
            {
                bindingFailed = true;
                Active = null;
                RORO.GlobalLogger?.LogWarning($"Steam P2P I/O thread is unavailable, using the main thread: {exception}");
                return null;
            }
        }

        private static IntPtr GetInterfacePointer(Networking networking)
        {
            object? nativeNetworking = NativeNetworkingField?.GetValue(networking);
            if (nativeNetworking == null)
            {
                return IntPtr.Zero;
            }

            object? platform = AccessTools.Field(nativeNetworking.GetType(), "platform")?.GetValue(nativeNetworking);
            if (platform == null || platform.GetType().Name != "Win64")
            {
                return IntPtr.Zero;
            }

            object? pointer = AccessTools.Field(platform.GetType(), "_ptr")?.GetValue(platform);
            return pointer is IntPtr value ? value : IntPtr.Zero;
        }

        public void EnqueueSend(ulong steamId, byte[]? data, int length, Networking.SendType sendType, int channel)
        {
            int copyLength = data == null ? 0 : Math.Max(0, Math.Min(length, data.Length));
            byte[]? copy = null;
            if (copyLength > 0)
            {
                copy = RentBuffer(copyLength);
                Buffer.BlockCopy(data!, 0, copy, 0, copyLength);
            }

            Enqueue(new Command
            {
                Kind = CommandKind.Send,
                SteamId = steamId,
                Buffer = copy,
                Length = copyLength,
                SendType = (int)sendType,
                Channel = channel,
            });
        }

        public void EnqueueClose(ulong steamId)
        {
            Enqueue(new Command { Kind = CommandKind.Close, SteamId = steamId });
        }

        private void Enqueue(Command command)
        {
            lock (commandLock)
            {
                pendingCommands.Enqueue(command);
            }

            wake.Set();
        }

        private void SyncListenChannels(List<int> channels)
        {
            int[] snapshot = listenChannels;
            bool same = snapshot.Length == channels.Count;
            for (int i = 0; same && i < snapshot.Length; i++)
            {
                same = snapshot[i] == channels[i];
            }

            if (!same)
            {
                listenChannels = channels.ToArray();
            }
        }

        private void Dispatch()
        {
            if (dispatchingPackets.Count == 0)
            {
                lock (packetLock)
                {
                    Queue<Packet> swap = receivedPackets;
                    receivedPackets = dispatchingPackets;
                    dispatchingPackets = swap;
                }
            }

            while (dispatchingPackets.Count > 0)
            {
                Packet packet = dispatchingPackets.Dequeue();
                try
                {
                    owner.OnP2PData?.Invoke(packet.SteamId, packet.Buffer, packet.Length, packet.Channel);
                }
                finally
                {
                    ReturnBuffer(packet.Buffer);
                }
            }
        }

        private void ReportSendFailures()
        {
            long failures = Interlocked.Read(ref sendFailures);
            if (failures != reportedSendFailures)
            {
                RORO.GlobalLogger?.LogWarning($"Steam rejected {failures - reportedSendFailures} P2P packet(s) sent from the I/O thread.");
                reportedSendFailures = failures;
            }
        }

        private void Stop(bool dispatchRemaining)
        {
            running = false;
            wake.Set();
            bool joined = thread.Join(StopTimeoutMilliseconds);
            if (joined)
            {
                ExecuteCommands();
                long deadline = workerClock.ElapsedMilliseconds + StopTimeoutMilliseconds;
                while (blockedPeers.Count > 0 && workerClock.ElapsedMilliseconds < deadline)
                {
                    RetryBlockedPeers();
                    Thread.Sleep(1);
                }

                blockedPeerScratch.Clear();
                blockedPeerScratch.AddRange(blockedPeers.Keys);
                for (int i = 0; i < blockedPeerScratch.Count; i++)
                {
                    DropBlockedPeer(blockedPeerScratch[i]);
                }
            }
            else
            {
                RORO.GlobalLogger?.LogWarning("Steam P2P I/O thread did not stop in time.");
            }

            if (dispatchRemaining)
            {
                lock (packetLock)
                {
                    while (receivedPackets.Count > 0)
                    {
                        dispatchingPackets.Enqueue(receivedPackets.Dequeue());
                    }
                }

                Dispatch();
            }

            RORO.GlobalLogger?.LogInfo(
                $"Steam P2P I/O thread stopped: sent={Interlocked.Read(ref packetsSent)} received={Interlocked.Read(ref packetsReceived)} rejected={Interlocked.Read(ref sendFailures)}");
        }

        private void Run()
        {
            long nextReceiveTime = 0;
            long nextSweepTime = 0;
            long nextReleaseTime = ReleaseThreadMemoryMilliseconds;
            try
            {
                while (running)
                {
                    RetryBlockedPeers();
                    ExecuteCommands();

                    long now = workerClock.ElapsedMilliseconds;
                    if (now >= nextReceiveTime)
                    {
                        nextReceiveTime = now + ReceivePollIntervalMilliseconds;
                        bool sweepIdleChannels = now >= nextSweepTime;
                        if (sweepIdleChannels)
                        {
                            nextSweepTime = now + IdleChannelSweepMilliseconds;
                        }

                        int[] channels = listenChannels;
                        for (int i = 0; i < channels.Length; i++)
                        {
                            if (sweepIdleChannels || channels[i] == GameplayChannel)
                            {
                                ReceiveAll(channels[i]);
                            }
                        }
                    }

                    if (now >= nextReleaseTime)
                    {
                        nextReleaseTime = now + ReleaseThreadMemoryMilliseconds;
                        SteamP2PNative.ReleaseCurrentThreadMemory();
                    }

                    long untilReceive = nextReceiveTime - workerClock.ElapsedMilliseconds;
                    int timeout = blockedPeers.Count > 0 ? 1 : (int)Math.Max(1, Math.Min(untilReceive, ReceivePollIntervalMilliseconds));
                    wake.WaitOne(timeout);
                }

                ExecuteCommands();
                SteamP2PNative.ReleaseCurrentThreadMemory();
            }
            catch (Exception exception)
            {
                fault = exception;
            }
        }

        private void ExecuteCommands()
        {
            DrainExecutingCommands();
            lock (commandLock)
            {
                Queue<Command> swap = pendingCommands;
                pendingCommands = executingCommands;
                executingCommands = swap;
            }

            DrainExecutingCommands();
        }

        private void DrainExecutingCommands()
        {
            while (executingCommands.Count > 0)
            {
                Command command = executingCommands.Dequeue();
                switch (command.Kind)
                {
                    case CommandKind.Send:
                        if (IsReliable(command) && blockedPeers.TryGetValue(command.SteamId, out BlockedPeer? blocked))
                        {
                            blocked.Commands.Enqueue(command);
                        }
                        else if (!TrySend(command))
                        {
                            if (IsReliable(command))
                            {
                                blocked = new BlockedPeer { LastProgressTime = workerClock.ElapsedMilliseconds };
                                blocked.Commands.Enqueue(command);
                                blockedPeers.Add(command.SteamId, blocked);
                            }
                            else
                            {
                                Interlocked.Increment(ref sendFailures);
                                ReleaseCommand(command);
                            }
                        }

                        break;
                    case CommandKind.Close:
                        DropBlockedPeer(command.SteamId);
                        SteamP2PNative.CloseP2PSessionWithUser(steamNetworking, command.SteamId);
                        break;
                }
            }
        }

        // UNet re-queues a reliable packet when the transport refuses it (Steam does so while its per-connection send
        // buffer is full), so a refused reliable packet and everything reliable after it for that peer must wait.
        private void RetryBlockedPeers()
        {
            if (blockedPeers.Count == 0)
            {
                return;
            }

            long now = workerClock.ElapsedMilliseconds;
            blockedPeerScratch.Clear();
            foreach (KeyValuePair<ulong, BlockedPeer> pair in blockedPeers)
            {
                BlockedPeer blocked = pair.Value;
                while (blocked.Commands.Count > 0 && TrySend(blocked.Commands.Peek()))
                {
                    blocked.Commands.Dequeue();
                    blocked.LastProgressTime = now;
                }

                if (blocked.Commands.Count == 0 || now - blocked.LastProgressTime > BlockedPeerTimeoutMilliseconds)
                {
                    blockedPeerScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < blockedPeerScratch.Count; i++)
            {
                DropBlockedPeer(blockedPeerScratch[i]);
            }
        }

        private void DropBlockedPeer(ulong steamId)
        {
            if (!blockedPeers.TryGetValue(steamId, out BlockedPeer? blocked))
            {
                return;
            }

            blockedPeers.Remove(steamId);
            while (blocked.Commands.Count > 0)
            {
                Interlocked.Increment(ref sendFailures);
                ReleaseCommand(blocked.Commands.Dequeue());
            }
        }

        private bool TrySend(Command command)
        {
            bool sent;
            fixed (byte* data = command.Buffer)
            {
                sent = SteamP2PNative.SendP2PPacket(steamNetworking, command.SteamId, (IntPtr)data, (uint)command.Length, command.SendType, command.Channel);
            }

            if (sent)
            {
                Interlocked.Increment(ref packetsSent);
                ReleaseCommand(command);
            }

            return sent;
        }

        private static bool IsReliable(Command command)
        {
            return command.SendType == (int)Networking.SendType.Reliable || command.SendType == (int)Networking.SendType.ReliableWithBuffering;
        }

        private void ReleaseCommand(Command command)
        {
            if (command.Buffer != null)
            {
                ReturnBuffer(command.Buffer);
            }
        }

        private void ReceiveAll(int channel)
        {
            while (running && SteamP2PNative.IsP2PPacketAvailable(steamNetworking, out uint size, channel))
            {
                byte[] buffer = RentBuffer((int)size);
                bool read;
                uint length;
                ulong sender;
                fixed (byte* destination = buffer)
                {
                    read = SteamP2PNative.ReadP2PPacket(steamNetworking, (IntPtr)destination, size, out length, out sender, channel);
                }

                if (!read)
                {
                    ReturnBuffer(buffer);
                    return;
                }

                lock (packetLock)
                {
                    receivedPackets.Enqueue(new Packet
                    {
                        SteamId = sender,
                        Buffer = buffer,
                        Length = (int)length,
                        Channel = channel,
                    });
                }

                Interlocked.Increment(ref packetsReceived);
            }
        }

        private byte[] RentBuffer(int size)
        {
            if (size > PooledBufferSize)
            {
                return new byte[size];
            }

            lock (poolLock)
            {
                if (bufferPool.Count > 0)
                {
                    return bufferPool.Pop();
                }
            }

            return new byte[PooledBufferSize];
        }

        private void ReturnBuffer(byte[] buffer)
        {
            if (buffer.Length != PooledBufferSize)
            {
                return;
            }

            lock (poolLock)
            {
                if (bufferPool.Count < MaxPooledBuffers)
                {
                    bufferPool.Push(buffer);
                }
            }
        }
    }

    internal static class SteamP2PSettings
    {
        private static ConfigEntry<bool>? threaded;
        private static ConfigEntry<bool>? debugOverlay;

        public static bool Threaded => threaded?.Value ?? true;

        public static bool DebugOverlay => debugOverlay?.Value ?? false;

        public static ConfigEntry<bool>? ThreadedEntry => threaded;

        public static ConfigEntry<bool>? DebugEntry => debugOverlay;

        public static void Bind(ConfigFile config)
        {
            threaded = config.Bind(
                "Network",
                "Threaded Steam P2P",
                true,
                "Moves Steam P2P packet sends and reads off the main thread to a background thread. The packet format does not change, so it works with players who do not have ROR-O. Takes effect immediately.");
            debugOverlay = config.Bind(
                "Network",
                "Debug Overlay",
                false,
                "Shows each player's Steam connection under their name in the lobby and on the ally card. The host measures every link. A client measures its own link to the host, and sees other players' links to the host only when the host also has ROR-O. A server without ROR-O is left unchanged.");
        }
    }
}
