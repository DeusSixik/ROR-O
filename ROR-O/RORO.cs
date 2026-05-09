using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ROR_O
{
    [BepInPlugin(Modguid, ModName, ModVersion)]
    public sealed class RORO : BaseUnityPlugin
    {
        public static ManualLogSource? GlobalLogger { get; private set; }
        
        private const string Modguid = "net.sixik.plugin.roro";
        private const string ModName = "ROR-O";
        private const string ModVersion = "1.0.0";

        private void Awake()
        {
            GlobalLogger = Logger;
            GlobalLogger.LogInfo($"Initialize ROR-O mod: {ModName} v{ModVersion}");
            
            var harmony = new Harmony(Modguid);
            harmony.PatchAll();
            GlobalLogger.LogInfo("Harmony patches applied successfully!");
        }
    }
}
