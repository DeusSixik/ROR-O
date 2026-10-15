using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Facepunch.Steamworks;
using HarmonyLib;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace ROR_O.patches
{
    internal readonly struct NetworkLinkSample
    {
        public readonly bool Active;
        public readonly bool Connecting;
        public readonly bool UsingRelay;
        public readonly byte Error;
        public readonly int BytesQueued;
        public readonly int PacketsQueued;
        public readonly int RttMilliseconds;

        public NetworkLinkSample(bool active, bool connecting, bool usingRelay, byte error, int bytesQueued, int packetsQueued, int rttMilliseconds)
        {
            Active = active;
            Connecting = connecting;
            UsingRelay = usingRelay;
            Error = error;
            BytesQueued = bytesQueued;
            PacketsQueued = packetsQueued;
            RttMilliseconds = rttMilliseconds;
        }
    }

    // Connection facts stay on the machine that can see them. The host measures every Steam session itself.
    // A client measures only its own session with the host. Other players' links are read from the host's Steam
    // lobby member data, which vanilla clients ignore. Nothing is sent through UNet, so a server without ROR-O
    // keeps working and simply never publishes that data.
    internal static class NetworkLinkDebug
    {
        private const string MemberDataKey = "roro_net";
        private const float SampleIntervalSeconds = 0.5f;
        private const float PublishIntervalSeconds = 1f;

        private static readonly Dictionary<ulong, NetworkLinkSample> LocalLinks = new Dictionary<ulong, NetworkLinkSample>();
        private static readonly Dictionary<ulong, NetworkLinkSample> HostReportedLinks = new Dictionary<ulong, NetworkLinkSample>();
        private static readonly StringBuilder PublishBuilder = new StringBuilder(256);
        private static readonly FieldInfo? NetworkClientField = AccessTools.Field(typeof(NetworkManager), "client");

        private static float nextSampleTime;
        private static float nextPublishTime;
        private static bool memberDataPublished;
        private static string publishedValue = "";

        public static void Tick()
        {
            if (!SteamP2PSettings.DebugOverlay)
            {
                if (memberDataPublished || LocalLinks.Count > 0 || HostReportedLinks.Count > 0)
                {
                    Clear();
                }

                return;
            }

            if (Time.unscaledTime < nextSampleTime)
            {
                return;
            }

            nextSampleTime = Time.unscaledTime + SampleIntervalSeconds;
            try
            {
                SampleLocalLinks();
                if (NetworkServer.active)
                {
                    PublishHostReport();
                }
                else
                {
                    ReadHostReport();
                }
            }
            catch (Exception exception)
            {
                RORO.GlobalLogger?.LogWarning($"Network debug sample failed: {exception.Message}");
            }
        }

        public static string? Describe(ulong steamId)
        {
            if (!SteamP2PSettings.DebugOverlay || steamId == 0 || steamId == LocalSteamId)
            {
                return null;
            }

            if (LocalLinks.TryGetValue(steamId, out NetworkLinkSample local))
            {
                return Format(local, reportedByHost: false);
            }

            if (HostReportedLinks.TryGetValue(steamId, out NetworkLinkSample reported))
            {
                return Format(reported, reportedByHost: true);
            }

            ulong hostSteamId = FindHostSteamId();
            if (!NetworkServer.active && hostSteamId != 0 && steamId != hostSteamId)
            {
                return "to host · no data";
            }

            return "no session";
        }

        private static ulong LocalSteamId => Client.Instance?.SteamId ?? 0;

        private static void Clear()
        {
            LocalLinks.Clear();
            HostReportedLinks.Clear();
            if (memberDataPublished)
            {
                TrySetMemberData("");
                memberDataPublished = false;
                publishedValue = "";
            }
        }

        private static void SampleLocalLinks()
        {
            LocalLinks.Clear();
            ulong self = LocalSteamId;
            if (NetworkServer.active)
            {
                foreach (NetworkConnection connection in NetworkServer.connections)
                {
                    SteamNetworkConnection? steam = connection as SteamNetworkConnection;
                    if (steam == null)
                    {
                        continue;
                    }

                    ulong steamId = steam.steamId.ID;
                    if (steamId == 0 || steamId == self)
                    {
                        continue;
                    }

                    NetworkLinkSample sample = ReadSession(steamId);
                    int rtt = (int)RttManager.GetConnectionRTTInMilliseconds(steam);
                    LocalLinks[steamId] = WithRtt(sample, rtt);
                }

                return;
            }

            ulong hostSteamId = FindHostSteamId();
            if (hostSteamId == 0 || hostSteamId == self)
            {
                return;
            }

            NetworkLinkSample hostLink = ReadSession(hostSteamId);
            int clientRtt = 0;
            if (NetworkManager.singleton is NetworkManagerSystem manager)
            {
                clientRtt = Mathf.RoundToInt(manager.clientRttFrame * 1000f);
            }

            LocalLinks[hostSteamId] = WithRtt(hostLink, clientRtt);
        }

        private static NetworkLinkSample ReadSession(ulong steamId)
        {
            Networking? networking = Client.Instance?.Networking;
            if (networking == null)
            {
                return default;
            }

            Networking.P2PSessionState state = default;
            if (!networking.GetP2PSessionState(steamId, ref state))
            {
                return default;
            }

            return new NetworkLinkSample(
                state.ConnectionActive != 0,
                state.Connecting != 0,
                state.UsingRelay != 0,
                state.P2PSessionError,
                state.BytesQueuedForSend,
                state.PacketsQueuedForSend,
                0);
        }

        private static NetworkLinkSample WithRtt(NetworkLinkSample sample, int rttMilliseconds)
        {
            return new NetworkLinkSample(
                sample.Active,
                sample.Connecting,
                sample.UsingRelay,
                sample.Error,
                sample.BytesQueued,
                sample.PacketsQueued,
                rttMilliseconds);
        }

        private static ulong FindHostSteamId()
        {
            if (NetworkManager.singleton == null || NetworkClientField == null)
            {
                return 0;
            }

            NetworkClient? networkClient = NetworkClientField.GetValue(NetworkManager.singleton) as NetworkClient;
            if (networkClient == null)
            {
                return 0;
            }

            return networkClient.connection is SteamNetworkConnection steam ? steam.steamId.ID : 0;
        }

        private static void PublishHostReport()
        {
            if (Time.unscaledTime < nextPublishTime && memberDataPublished)
            {
                return;
            }

            nextPublishTime = Time.unscaledTime + PublishIntervalSeconds;
            PublishBuilder.Clear();
            PublishBuilder.Append('1');
            foreach (KeyValuePair<ulong, NetworkLinkSample> pair in LocalLinks)
            {
                NetworkLinkSample sample = pair.Value;
                PublishBuilder.Append('|');
                PublishBuilder.Append(pair.Key.ToString(CultureInfo.InvariantCulture));
                PublishBuilder.Append(',');
                PublishBuilder.Append(Flags(sample).ToString(CultureInfo.InvariantCulture));
                PublishBuilder.Append(',');
                PublishBuilder.Append(sample.BytesQueued.ToString(CultureInfo.InvariantCulture));
                PublishBuilder.Append(',');
                PublishBuilder.Append(sample.PacketsQueued.ToString(CultureInfo.InvariantCulture));
                PublishBuilder.Append(',');
                PublishBuilder.Append(sample.RttMilliseconds.ToString(CultureInfo.InvariantCulture));
            }

            string value = PublishBuilder.ToString();
            if (value == publishedValue && memberDataPublished)
            {
                return;
            }

            if (TrySetMemberData(value))
            {
                publishedValue = value;
                memberDataPublished = true;
            }
        }

        private static void ReadHostReport()
        {
            HostReportedLinks.Clear();
            ulong hostSteamId = FindHostSteamId();
            if (hostSteamId == 0 || hostSteamId == LocalSteamId || Client.Instance?.Lobby == null || !Client.Instance.Lobby.IsValid)
            {
                return;
            }

            string value = Client.Instance.Lobby.GetMemberData(hostSteamId, MemberDataKey);
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            string[] entries = value.Split('|');
            for (int i = 1; i < entries.Length; i++)
            {
                string[] parts = entries[i].Split(',');
                if (parts.Length < 5
                    || !ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId)
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int flags)
                    || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int bytesQueued)
                    || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int packetsQueued)
                    || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int rtt))
                {
                    continue;
                }

                HostReportedLinks[steamId] = new NetworkLinkSample(
                    (flags & 1) != 0,
                    (flags & 2) != 0,
                    (flags & 4) != 0,
                    (byte)((flags >> 3) & 31),
                    bytesQueued,
                    packetsQueued,
                    rtt);
            }
        }

        private static int Flags(NetworkLinkSample sample)
        {
            int flags = 0;
            if (sample.Active)
            {
                flags |= 1;
            }

            if (sample.Connecting)
            {
                flags |= 2;
            }

            if (sample.UsingRelay)
            {
                flags |= 4;
            }

            flags |= (sample.Error & 31) << 3;
            return flags;
        }

        private static bool TrySetMemberData(string value)
        {
            Lobby? lobby = Client.Instance?.Lobby;
            if (lobby == null || !lobby.IsValid)
            {
                return false;
            }

            lobby.SetMemberData(MemberDataKey, value);
            return true;
        }

        private static string Format(NetworkLinkSample sample, bool reportedByHost)
        {
            string state;
            if (sample.Connecting && !sample.Active)
            {
                state = "connecting";
            }
            else if (sample.Active)
            {
                state = sample.UsingRelay ? "relay · up" : "direct · up";
            }
            else
            {
                state = sample.Error == 0 ? "down" : "down · " + ErrorName(sample.Error);
            }

            string line = sample.RttMilliseconds > 0 ? sample.RttMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms · " + state : state;
            if (sample.BytesQueued > 0)
            {
                line += " · q " + FormatBytes(sample.BytesQueued);
            }
            else if (sample.PacketsQueued > 0)
            {
                line += " · q " + sample.PacketsQueued.ToString(CultureInfo.InvariantCulture) + " pkt";
            }

            return reportedByHost ? "to host · " + line : line;
        }

        private static string ErrorName(byte error)
        {
            switch (error)
            {
                case 1:
                    return "not running";
                case 2:
                    return "no rights";
                case 3:
                    return "offline";
                case 4:
                    return "timeout";
                default:
                    return "err " + error.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string FormatBytes(int bytes)
        {
            if (bytes < 1024)
            {
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            }

            return (bytes / 1024f).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }
    }
}
