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
        private const string VersionKey = "roro_ver";
        private const float SampleIntervalSeconds = 0.5f;
        private const float PublishIntervalSeconds = 1f;

        private static readonly Dictionary<ulong, NetworkLinkSample> LocalLinks = new Dictionary<ulong, NetworkLinkSample>();
        private static readonly Dictionary<ulong, NetworkLinkSample> HostReportedLinks = new Dictionary<ulong, NetworkLinkSample>();
        private static readonly StringBuilder PublishBuilder = new StringBuilder(256);
        private static readonly FieldInfo? NetworkClientField = AccessTools.Field(typeof(NetworkManager), "client");

        private static readonly Dictionary<ulong, VersionProbe> VersionProbes = new Dictionary<ulong, VersionProbe>();
        private static readonly Dictionary<ulong, float> VersionWaitStart = new Dictionary<ulong, float>();
        private static readonly Dictionary<ulong, LinkTextCache> LinkText = new Dictionary<ulong, LinkTextCache>();

        private static float nextSampleTime;
        private static float nextPublishTime;
        private static bool memberDataPublished;
        private static string publishedValue = "";
        private static string publishedVersion = "";
        private static ulong publishedVersionLobby;
        private static int trackedMemberCount = -1;
        private static int republishLeft;
        private static float nextRepublishTime;
        private static float nextSelfCheckTime;
        private static string cachedBuildId = "";
        private static bool buildIdCached;

        private const float VersionProbeIntervalSeconds = 1f;
        private const float VersionWaitSeconds = 5f;
        private const float RepublishDelaySeconds = 1.5f;
        private const int RepublishBursts = 3;
        private const float SelfCheckIntervalSeconds = 5f;
        private const string NoSessionText = "no session";
        private const string ToHostNoDataText = "to host · no data";

        private struct VersionProbe
        {
            public string Value;
            public float NextProbeTime;
            public bool Known;
        }

        private struct LinkTextCache
        {
            public NetworkLinkSample Sample;
            public bool ReportedByHost;
            public bool HasSample;
            public string Text;
        }

        // Vanilla only writes one lobby-wide build_id, which is the host. A player with this mod also
        // publishes their own build on their lobby member data. Steam does not deliver member data that
        // was set before someone joined, so a new player would otherwise see an earlier client as empty.
        // Each membership change publishes the local build again, a few times, until the new peer can read it.
        public static void PublishLocalVersion()
        {
            Lobby? lobby = Client.Instance?.Lobby;
            if (lobby == null || !lobby.IsValid)
            {
                if (publishedVersionLobby != 0)
                {
                    ResetVersionState();
                }

                return;
            }

            ulong lobbyId = lobby.CurrentLobby;
            if (lobbyId != publishedVersionLobby)
            {
                ResetVersionState();
                publishedVersionLobby = lobbyId;
            }

            string mine = LocalBuildId();
            if (mine.Length == 0)
            {
                return;
            }

            int members = lobby.NumMembers;
            if (members != trackedMemberCount)
            {
                trackedMemberCount = members;
                republishLeft = RepublishBursts;
                nextRepublishTime = 0f;
            }
            else if (republishLeft <= 0 && publishedVersion == mine && LocalSteamId != 0 && Time.unscaledTime >= nextSelfCheckTime)
            {
                nextSelfCheckTime = Time.unscaledTime + SelfCheckIntervalSeconds;
                string visible = lobby.GetMemberData(LocalSteamId, VersionKey);
                if (!string.Equals(visible, mine, StringComparison.Ordinal))
                {
                    republishLeft = 1;
                    nextRepublishTime = 0f;
                }
            }

            if (republishLeft <= 0 && publishedVersion == mine)
            {
                return;
            }

            if (republishLeft > 0 && Time.unscaledTime < nextRepublishTime)
            {
                return;
            }

            lobby.SetMemberData(VersionKey, mine);
            publishedVersion = mine;
            if (republishLeft > 0)
            {
                republishLeft--;
                nextRepublishTime = Time.unscaledTime + RepublishDelaySeconds;
            }
        }

        public static bool TryDescribeVersion(ulong steamId, out string version, out bool mismatch)
        {
            version = "";
            mismatch = false;
            if (steamId == 0)
            {
                return false;
            }

            if (steamId == LocalSteamId)
            {
                version = LocalBuildId();
                return version.Length > 0;
            }

            if (!TryReadRemoteVersion(steamId, out version))
            {
                version = "";
                return false;
            }

            string mine = LocalBuildId();
            mismatch = mine.Length > 0 && !string.Equals(mine, version, StringComparison.Ordinal);
            return true;
        }

        // A player who already published a build keeps that value if Steam briefly returns an empty key.
        // Someone with no build yet stays unknown, so the label can still say that the version is missing.
        public static bool ShouldWaitForVersion(ulong steamId)
        {
            if (steamId == 0 || steamId == LocalSteamId)
            {
                return false;
            }

            if (!VersionWaitStart.TryGetValue(steamId, out float started))
            {
                VersionWaitStart[steamId] = Time.unscaledTime;
                return true;
            }

            return Time.unscaledTime - started < VersionWaitSeconds;
        }

        public static bool Tick()
        {
            if (!SteamP2PSettings.DebugOverlay)
            {
                if (memberDataPublished || LocalLinks.Count > 0 || HostReportedLinks.Count > 0)
                {
                    Clear();
                }

                return false;
            }

            if (Time.unscaledTime < nextSampleTime)
            {
                return false;
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

            return true;
        }

        public static string? Describe(ulong steamId)
        {
            if (!SteamP2PSettings.DebugOverlay || steamId == 0 || steamId == LocalSteamId)
            {
                return null;
            }

            if (LocalLinks.TryGetValue(steamId, out NetworkLinkSample local))
            {
                return CachedFormat(steamId, local, reportedByHost: false);
            }

            if (HostReportedLinks.TryGetValue(steamId, out NetworkLinkSample reported))
            {
                return CachedFormat(steamId, reported, reportedByHost: true);
            }

            ulong hostSteamId = FindHostSteamId();
            if (!NetworkServer.active && hostSteamId != 0 && steamId != hostSteamId)
            {
                return ToHostNoDataText;
            }

            return NoSessionText;
        }

        private static void ResetVersionState()
        {
            publishedVersion = "";
            publishedVersionLobby = 0;
            trackedMemberCount = -1;
            republishLeft = 0;
            nextRepublishTime = 0f;
            nextSelfCheckTime = 0f;
            VersionProbes.Clear();
            VersionWaitStart.Clear();
        }

        private static bool TryReadRemoteVersion(ulong steamId, out string version)
        {
            version = "";
            float now = Time.unscaledTime;
            if (VersionProbes.TryGetValue(steamId, out VersionProbe probe) && now < probe.NextProbeTime)
            {
                version = probe.Value;
                return probe.Known;
            }

            Lobby? lobby = Client.Instance?.Lobby;
            if (lobby == null || !lobby.IsValid)
            {
                return false;
            }

            string reported = lobby.GetMemberData(steamId, VersionKey);
            if (!IsVersionToken(reported) && IsKnownHost(steamId))
            {
                reported = lobby.CurrentLobbyData != null ? lobby.CurrentLobbyData.GetData("build_id") : "";
            }

            if (!IsVersionToken(reported))
            {
                if (probe.Known)
                {
                    probe.NextProbeTime = now + VersionProbeIntervalSeconds;
                    VersionProbes[steamId] = probe;
                    version = probe.Value;
                    return true;
                }

                VersionProbes[steamId] = new VersionProbe
                {
                    Value = "",
                    NextProbeTime = now + VersionProbeIntervalSeconds,
                    Known = false,
                };
                return false;
            }

            if (!probe.Known || !string.Equals(probe.Value, reported, StringComparison.Ordinal))
            {
                probe.Value = reported;
            }

            probe.Known = true;
            probe.NextProbeTime = now + VersionProbeIntervalSeconds;
            VersionProbes[steamId] = probe;
            version = probe.Value;
            return true;
        }

        private static string CachedFormat(ulong steamId, NetworkLinkSample sample, bool reportedByHost)
        {
            if (LinkText.TryGetValue(steamId, out LinkTextCache cached)
                && cached.HasSample
                && cached.ReportedByHost == reportedByHost
                && SameSample(cached.Sample, sample))
            {
                return cached.Text;
            }

            string text = Format(sample, reportedByHost);
            LinkText[steamId] = new LinkTextCache
            {
                Sample = sample,
                ReportedByHost = reportedByHost,
                HasSample = true,
                Text = text,
            };
            return text;
        }

        private static bool SameSample(NetworkLinkSample left, NetworkLinkSample right)
        {
            return left.Active == right.Active
                && left.Connecting == right.Connecting
                && left.UsingRelay == right.UsingRelay
                && left.Error == right.Error
                && left.BytesQueued == right.BytesQueued
                && left.PacketsQueued == right.PacketsQueued
                && left.RttMilliseconds == right.RttMilliseconds;
        }

        private static ulong LocalSteamId => Client.Instance?.SteamId ?? 0;

        private static string LocalBuildId()
        {
            if (buildIdCached)
            {
                return cachedBuildId;
            }

            try
            {
                string buildId = RoR2Application.GetBuildId();
                if (!IsVersionToken(buildId))
                {
                    return "";
                }

                cachedBuildId = buildId;
                buildIdCached = true;
                return cachedBuildId;
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static bool IsVersionToken(string? value)
        {
            if (string.IsNullOrEmpty(value) || value!.Length > 40)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= '0' && c <= '9')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || c == '.'
                    || c == '#'
                    || c == '*'
                    || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsKnownHost(ulong steamId)
        {
            if (steamId == 0)
            {
                return false;
            }

            if (steamId == LocalSteamId && NetworkServer.active)
            {
                return true;
            }

            Lobby? lobby = Client.Instance?.Lobby;
            return lobby != null && lobby.IsValid && IsLobbyHost(lobby, steamId);
        }

        private static bool IsLobbyHost(Lobby lobby, ulong steamId)
        {
            if (lobby.Owner == steamId)
            {
                return true;
            }

            SteamworksLobbyManager? manager = PlatformSystems.lobbyManager as SteamworksLobbyManager;
            return manager != null && manager.serverId.isValid && manager.serverId.ID == steamId;
        }

        private static void Clear()
        {
            LocalLinks.Clear();
            HostReportedLinks.Clear();
            LinkText.Clear();
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
