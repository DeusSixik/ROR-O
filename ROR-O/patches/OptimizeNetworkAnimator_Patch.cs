using HarmonyLib;
using UnityEngine.Networking;

namespace ROR_O.patches
{
    [HarmonyPatch(typeof(NetworkAnimator), "FixedUpdate")]
    public static class OptimizeNetworkAnimatorFixedUpdatePatch
    {
        private delegate bool CheckAnimStateChangedDelegate(NetworkAnimator instance, ref int stateHash, ref float normalizedTime);
        private delegate void WriteParametersDelegate(NetworkAnimator instance, NetworkWriter writer, bool forceAll);
        private delegate void SendMessageDelegate(NetworkAnimator instance, short msgType, MessageBase msg);

        private static readonly AccessTools.FieldRef<NetworkAnimator, NetworkWriter> ParameterWriterRef =
            AccessTools.FieldRefAccess<NetworkAnimator, NetworkWriter>("m_ParameterWriter");

        private static readonly System.Func<NetworkAnimator, bool> GetSendMessagesAllowed =
            AccessTools.MethodDelegate<System.Func<NetworkAnimator, bool>>(
                AccessTools.PropertyGetter(typeof(NetworkAnimator), "sendMessagesAllowed"));

        private static readonly CheckAnimStateChangedDelegate CheckAnimStateChanged =
            AccessTools.MethodDelegate<CheckAnimStateChangedDelegate>(
                AccessTools.Method(typeof(NetworkAnimator), "CheckAnimStateChanged"));

        private static readonly WriteParametersDelegate WriteParameters =
            AccessTools.MethodDelegate<WriteParametersDelegate>(
                AccessTools.Method(typeof(NetworkAnimator), "WriteParameters"));

        private static readonly SendMessageDelegate SendMessage =
            AccessTools.MethodDelegate<SendMessageDelegate>(
                AccessTools.Method(typeof(NetworkAnimator), "SendMessage"));

        private static readonly WriterBackedAnimationMessage CachedMessage = new WriterBackedAnimationMessage();

        private static bool Prefix(NetworkAnimator __instance)
        {
            if (!GetSendMessagesAllowed(__instance))
            {
                return false;
            }

            NetworkWriter parameterWriter = ParameterWriterRef(__instance);
            if (parameterWriter == null)
            {
                parameterWriter = new NetworkWriter();
                ParameterWriterRef(__instance) = parameterWriter;
            }

            OptimizeNetworkAnimatorSendRatePatch.RunCheckSendRate(__instance, parameterWriter);

            int stateHash = 0;
            float normalizedTime = 0f;
            if (!CheckAnimStateChanged(__instance, ref stateHash, ref normalizedTime))
            {
                return false;
            }

            parameterWriter.SeekZero();
            WriteParameters(__instance, parameterWriter, false);

            CachedMessage.netId = __instance.netId;
            CachedMessage.stateHash = stateHash;
            CachedMessage.normalizedTime = normalizedTime;
            CachedMessage.parametersWriter = parameterWriter;
            CachedMessage.parameterLength = parameterWriter.Position;

            SendMessage(__instance, 40, CachedMessage);
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkAnimator), "CheckSendRate")]
    public static class OptimizeNetworkAnimatorSendRatePatch
    {
        private delegate void WriteParametersDelegate(NetworkAnimator instance, NetworkWriter writer, bool forceAll);
        private delegate void SendMessageDelegate(NetworkAnimator instance, short msgType, MessageBase msg);

        private static readonly AccessTools.FieldRef<NetworkAnimator, NetworkWriter> ParameterWriterRef =
            AccessTools.FieldRefAccess<NetworkAnimator, NetworkWriter>("m_ParameterWriter");

        private static readonly AccessTools.FieldRef<NetworkAnimator, float> SendTimerRef =
            AccessTools.FieldRefAccess<NetworkAnimator, float>("m_SendTimer");

        private static readonly System.Func<NetworkAnimator, bool> GetSendMessagesAllowed =
            AccessTools.MethodDelegate<System.Func<NetworkAnimator, bool>>(
                AccessTools.PropertyGetter(typeof(NetworkAnimator), "sendMessagesAllowed"));

        private static readonly WriteParametersDelegate WriteParameters =
            AccessTools.MethodDelegate<WriteParametersDelegate>(
                AccessTools.Method(typeof(NetworkAnimator), "WriteParameters"));

        private static readonly SendMessageDelegate SendMessage =
            AccessTools.MethodDelegate<SendMessageDelegate>(
                AccessTools.Method(typeof(NetworkAnimator), "SendMessage"));

        private static readonly WriterBackedAnimationParametersMessage CachedMessage =
            new WriterBackedAnimationParametersMessage();

        private static bool Prefix(NetworkAnimator __instance)
        {
            RunCheckSendRate(__instance, ParameterWriterRef(__instance));
            return false;
        }

        internal static void RunCheckSendRate(NetworkAnimator instance, NetworkWriter parameterWriter)
        {
            if (!GetSendMessagesAllowed(instance))
            {
                return;
            }

            float sendInterval = instance.GetNetworkSendInterval();
            if (sendInterval == 0f)
            {
                return;
            }

            if (SendTimerRef(instance) >= UnityEngine.Time.time)
            {
                return;
            }

            SendTimerRef(instance) = UnityEngine.Time.time + sendInterval;

            if (parameterWriter == null)
            {
                parameterWriter = new NetworkWriter();
                ParameterWriterRef(instance) = parameterWriter;
            }

            parameterWriter.SeekZero();
            WriteParameters(instance, parameterWriter, true);

            CachedMessage.netId = instance.netId;
            CachedMessage.parametersWriter = parameterWriter;
            CachedMessage.parameterLength = parameterWriter.Position;

            SendMessage(instance, 41, CachedMessage);
        }
    }

    internal sealed class WriterBackedAnimationMessage : MessageBase
    {
        public NetworkInstanceId netId;
        public int stateHash;
        public float normalizedTime;
        public NetworkWriter? parametersWriter;
        public int parameterLength;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(netId);
            writer.WritePackedUInt32((uint)stateHash);
            writer.Write(normalizedTime);

            if (parametersWriter == null || parameterLength <= 0)
            {
                writer.WriteBytesAndSize(null, 0);
                return;
            }

            writer.WriteBytesAndSize(parametersWriter.AsArray(), parameterLength);
        }
    }

    internal sealed class WriterBackedAnimationParametersMessage : MessageBase
    {
        public NetworkInstanceId netId;
        public NetworkWriter? parametersWriter;
        public int parameterLength;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(netId);

            if (parametersWriter == null || parameterLength <= 0)
            {
                writer.WriteBytesAndSize(null, 0);
                return;
            }

            writer.WriteBytesAndSize(parametersWriter.AsArray(), parameterLength);
        }
    }
}
