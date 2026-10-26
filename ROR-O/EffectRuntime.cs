using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using BepInEx.Configuration;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace ROR_O
{
    // EffectDef loads its prefab as a Preload address. The next scene change puts that address on a 12 second
    // release list, and the next spawn then waits for the load inside SpawnEffect. Idle pools are destroyed
    // 45 seconds after their last use, so the following spawn instantiates a new copy. This pins the address
    // for the run. Pool copies are not built for the whole catalog: a spawn records how many copies of that
    // effect were checked out at once, and calm frames later build up to that observed peak for the effects
    // this run actually uses, so the next burst reuses copies instead of instantiating in combat.
    internal sealed class EffectRuntime : MonoBehaviour
    {
        private const float CalmFrameSeconds = 0.012f;
        // A warm is an Instantiate plus EffectManagerHelper.Initialize on the main thread. Never start
        // one on a frame that is already a hitch, and after the opening window combat gets at most
        // one warm per interval instead of one on every calm frame.
        private const float HitchFrameSeconds = 0.033f;
        private const float BusyWarmIntervalSeconds = 0.5f;
        private const float ExpensiveWarmMs = 3f;
        private static readonly Vector3 Park = new Vector3(0f, -5000f, 0f);
        private static readonly Vector3 CullBounds = new Vector3(12f, 12f, 12f);
        private static readonly Plane[] Frustum = new Plane[6];

        private static ConfigEntry<bool>? warmEffects;
        private static ConfigEntry<bool>? keepPools;
        private static ConfigEntry<bool>? keepCopiesActive;
        private static ConfigEntry<int>? activeParkLimit;
        private static ConfigEntry<bool>? warmFullPeak;
        private static ConfigEntry<bool>? warmShaders;
        private static ConfigEntry<int>? warmPeakCopies;
        private static ConfigEntry<int>? loadsPerFrame;
        private static ConfigEntry<int>? warmsPerFrame;
        private static ConfigEntry<bool>? cullSecondary;
        private static ConfigEntry<float>? cullDistance;
        private static ConfigEntry<bool>? cullOffScreen;
        private static ConfigEntry<int>? cullInterval;
        private static ConfigEntry<bool>? showStatus;
        private static ConfigEntry<bool>? showShaderStatus;
        private static ConfigEntry<bool>? logStatus;
        private static float nextLogTime;

        private static readonly StringBuilder statusBuilder = new StringBuilder(384);
        private static readonly Dictionary<VFXAttributes, EffectCullState> gates = new Dictionary<VFXAttributes, EffectCullState>();
        private static readonly Queue<int> warmQueue = new Queue<int>();
        // Reused across warms: checking out several helpers at once is the only way to make a pool
        // grow past the copies it already holds. Returning each one immediately re-pops the same copy.
        private static readonly List<EffectManagerHelper> heldHelpers = new List<EffectManagerHelper>(4);

        private static AsyncOperationHandle<GameObject>[] handles = System.Array.Empty<AsyncOperationHandle<GameObject>>();
        private static bool[] directPins = System.Array.Empty<bool>();
        private static AssetReferenceT<GameObject>?[] addresses = System.Array.Empty<AssetReferenceT<GameObject>?>();
        private static GameObject?[] prefabs = System.Array.Empty<GameObject?>();
        private static byte[] warmed = System.Array.Empty<byte>();
        private static bool[] settled = System.Array.Empty<bool>();
        private static int[] peakInUse = System.Array.Empty<int>();
        private static bool[] queued = System.Array.Empty<bool>();

        private static int pinCursor;
        private static bool directPinFailed;
        private static int warmedPrefabs;
        private static int instancesBuilt;
        private static int shaderCursor;
        private static int shaderVariants;
        private static readonly HashSet<string> shaderVariantKeys = new HashSet<string>();
        private static readonly List<ShaderVariantCollection> shaderCollections = new List<ShaderVariantCollection>();
        private static readonly List<Shader> pinnedShaders = new List<Shader>();
        private static readonly List<AsyncOperationHandle<GameObject>> shaderHolds = new List<AsyncOperationHandle<GameObject>>();
        private static readonly List<GameObject> shaderHoldPrefabs = new List<GameObject>();
        private static readonly Dictionary<int, ShaderWarmInfo> shaderInfo = new Dictionary<int, ShaderWarmInfo>();
        private static readonly List<PassType> passScratch = new List<PassType>(4);
        private static readonly List<string> keywordScratch = new List<string>(16);
        private static readonly List<string> keywordCombo = new List<string>(16);
        private static readonly StringBuilder variantKey = new StringBuilder(160);
        private static readonly string[] FogKeywordNames = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
        private static ShaderVariantCollection? shaderBatch;
        private static int shaderBatchCount;
        private static bool[] shaderHeld = System.Array.Empty<bool>();
        private static ShaderTagId lightModeTag;
        private static bool lightModeTagReady;
        private static readonly List<string> batchKeys = new List<string>(24);
        private static readonly Dictionary<string, WarmBatchMark> variantMarks = new Dictionary<string, WarmBatchMark>();
        private static readonly List<string> traceScratch = new List<string>(16);
        private static readonly List<string> traceCombo = new List<string>(16);
        private static readonly StringBuilder traceKey = new StringBuilder(160);
        private static readonly List<string> shaderTraceLines = new List<string>(6);
        private static byte[] effectTrace = System.Array.Empty<byte>();
        private static int traceFogStamp = int.MinValue;
        private static int traceMisses;
        private static int traceRecorded;
        private static int traceCompiled;
        private static int traceEmpty;
        private static int traceUnloaded;
        private static int traceNotPooled;
        private static int traceRepeats;
        private static int shaderWarmCalls;
        private static long shaderWarmMicroseconds;
        private static float shaderStatusRefresh;
        private static string shaderStatusText = "Shader compile status is not ready yet.";
        private static int culledEffects;
        private static int frame;
        private static float lastWarmTime;
        private static float warmNotBefore;
        private static bool frustumReady;
        private static float sceneReadyTime;
        private static Camera? gameplayCamera;
        private static bool cullWasEnabled;
        private static Run? observedRun;
        private static float lastStatusRefresh;
        private static string statusText = "Effect status is not ready yet.";
        private static GUIStyle? statusStyle;
        private static GUIStyle? shaderStyle;

        internal static ConfigEntry<SettingsProfiles.Profile>? Profile { get; private set; }
        internal static ConfigEntry<bool>? WarmEffects => warmEffects;
        internal static ConfigEntry<bool>? KeepPoolsEntry => keepPools;
        internal static ConfigEntry<bool>? KeepCopiesActiveEntry => keepCopiesActive;
        internal static ConfigEntry<int>? ActiveParkLimitEntry => activeParkLimit;
        internal static ConfigEntry<bool>? WarmFullPeakEntry => warmFullPeak;
        internal static ConfigEntry<bool>? WarmShadersEntry => warmShaders;
        internal static ConfigEntry<int>? WarmPeakCopiesEntry => warmPeakCopies;
        internal static ConfigEntry<int>? LoadsPerFrameEntry => loadsPerFrame;
        internal static ConfigEntry<int>? WarmsPerFrameEntry => warmsPerFrame;
        internal static ConfigEntry<bool>? CullSecondary => cullSecondary;
        internal static ConfigEntry<float>? CullDistanceEntry => cullDistance;
        internal static ConfigEntry<bool>? CullOffScreenEntry => cullOffScreen;
        internal static ConfigEntry<int>? CullIntervalEntry => cullInterval;
        internal static ConfigEntry<bool>? ShowStatusEntry => showStatus;
        internal static ConfigEntry<bool>? ShowShaderStatusEntry => showShaderStatus;
        internal static bool ShowShaderStatus => showShaderStatus?.Value == true;
        internal const float ShaderStatusBlockHeight = 250f;
        internal static ConfigEntry<bool>? LogStatusEntry => logStatus;

        internal static bool KeepPools => keepPools?.Value ?? true;
        internal static bool KeepCopiesActive => keepCopiesActive?.Value == true;
        internal static int ActiveParkLimit => activeParkLimit != null ? Clamp(activeParkLimit.Value, 0, 512) : 96;

        internal static void Bind(ConfigFile config)
        {
            const string section = "Effects";

            warmEffects = config.Bind(
                section,
                "Warm Effect Prefabs",
                true,
                "Pins effect prefabs for the run and, while frames are short, builds pool copies up to the largest number of copies of that effect this run has had in use at once. Effects the run never spawns are only pinned, never instantiated.");

            keepPools = config.Bind(
                section,
                "Keep Effect Pools",
                true,
                "Keeps idle effect copies until the stage unloads. The game otherwise destroys a pool 45 seconds after its last use, and the next spawn instantiates a new copy.");

            keepCopiesActive = config.Bind(
                section,
                "Keep Effect Copies Active",
                false,
                "<color=#FFDD44>[Experimental]</color> Copies of frequently spawned effects stay active while parked instead of being deactivated, up to the Active Parked Copies cap. The next spawn skips SetActive. Particles are stopped, renderers and lights are hidden, and the copy leaves the live effect list the same way a deactivate would. A script on the prefab that keeps ticking while it is parked still runs, which is why only hot effects under the cap stay active.");

            activeParkLimit = config.Bind(
                section,
                "Active Parked Copies",
                96,
                "<color=#FFDD44>[Experimental]</color> Most pool copies that may stay active while parked at once. Every active parked copy still ticks its scripts, so this cap is what keeps the saving on hot effects from taxing every frame.");

            warmFullPeak = config.Bind(
                section,
                "Warm Full Observed Peak",
                false,
                "<color=#FFDD44>[Experimental]</color> Builds pool copies up to the largest concurrency an effect actually reached this run (at most the game's pool capacity of 20), ignoring Warm Peak Copies. More copies held ready means more memory and more inactive objects per stage.");

            warmShaders = config.Bind(
                section,
                "Warm Effect Shaders",
                false,
                "<color=#FFDD44>[Experimental]</color> Compiles the shader variants pooled effects draw: the material's own keywords, soft particles and GPU instancing when those are on, and each fog mode the shader declares, on the passes those materials use. Effects marked do-not-pool are skipped. A prefab is held for the session per shader so the run-end release does not drop the compiled programs; if a held shader is gone anyway, the pass starts over. This waits until the lobby. The black screen between stages does as many as a frame can take, and whatever is left continues during play, one prefab on a short frame. The boot splash, the intro cutscene, and the title menu are left alone.");

            warmPeakCopies = config.Bind(
                section,
                "Warm Peak Copies",
                4,
                "Most inactive copies to build ahead of time for an effect this run actually uses, based on how many copies it has had in use at once. Zero only pins the prefabs.");

            // Warm All Effect Copies instantiated every poolable effect at stage start, up to
            // Warm Peak Copies each. That pass stalled the opening, so it is not bound.
            // warmAllCopies = config.Bind(
            //     section,
            //     "Warm All Effect Copies",
            //     false,
            //     "At the start of a stage, instantiates inactive copies of every poolable effect, up to Warm Peak Copies each, instead of only the effects this run has already used. Copies are scene objects, so this runs after the loading screen, and it can stall the opening of a stage.");

            loadsPerFrame = config.Bind(
                section,
                "Asset Loads Per Frame",
                4,
                "How many effect prefab loads to start on one frame. The load itself stays in the background.");

            warmsPerFrame = config.Bind(
                section,
                "Instance Warms Per Frame",
                1,
                "How many inactive effect copies to instantiate on one calm frame.");

            cullSecondary = config.Bind(
                section,
                "Cull Secondary Effects",
                true,
                "Stops secondary particle systems and optional lights past the cull distance. The game marks those as safe to turn off. Main particles and projectile hits keep running.");

            cullDistance = config.Bind(
                section,
                "Cull Distance",
                55f,
                "Distance in meters at which secondary particles and optional lights stop. They resume closer than about three quarters of this.");

            cullOffScreen = config.Bind(
                section,
                "Cull Off Screen",
                true,
                "Also stops secondary particles and optional lights when the effect is outside the camera. They resume once it is visible again.");

            cullInterval = config.Bind(
                section,
                "Cull Interval",
                4,
                "How many frames pass between secondary-effect distance checks.");

            showStatus = config.Bind(
                section,
                "Show Effect Status",
                false,
                "Draws the live effect-pool counts on screen.");

            showShaderStatus = config.Bind(
                section,
                "Show Shader Compile",
                false,
                "Draws effect-shader warmup progress and, on the first spawn of each pooled effect, checks the variant that spawn will draw. A hit means that variant is in a collection whose WarmUp already returned. This release player does not report Shader.CreateGPUProgram, so a hit is not proof the driver built a GPU program. The panel shows how long that WarmUp batch took.");

            logStatus = config.Bind(
                section,
                "Log Effect Status",
                false,
                "Writes the effect-pool counts to the BepInEx log once a second while this is on.");

            Profile = SettingsProfiles.Begin(config, section);
            SettingsProfiles.Add(warmEffects, true, true, true);
            SettingsProfiles.Add(keepPools, true, true, true);
            SettingsProfiles.Add(keepCopiesActive, false, false, false);
            SettingsProfiles.Add(activeParkLimit, 96, 96, 96);
            SettingsProfiles.Add(warmFullPeak, false, false, false);
            SettingsProfiles.Add(warmShaders, false, false, false);
            SettingsProfiles.Add(warmPeakCopies, 2, 4, 6);
            // SettingsProfiles.Add(warmAllCopies, false, false, false);
            SettingsProfiles.Add(loadsPerFrame, 2, 4, 4);
            SettingsProfiles.Add(warmsPerFrame, 1, 1, 1);
            SettingsProfiles.Add(cullSecondary, false, true, true);
            SettingsProfiles.Add(cullDistance, 80f, 55f, 28f);
            SettingsProfiles.Add(cullOffScreen, false, true, true);
            SettingsProfiles.Add(cullInterval, 8, 4, 2);
            SettingsProfiles.Add(showStatus, false, false, false);
            SettingsProfiles.Add(showShaderStatus, false, false, false);
            SettingsProfiles.Add(logStatus, false, false, false);
            SettingsProfiles.End();
        }

        private static void LogStatus()
        {
            RefreshStatus();
            RORO.GlobalLogger?.LogInfo(statusText.Replace('\n', ' '));
        }

        internal static void NoteEffectDisabled(VFXAttributes attributes)
        {
            if (!attributes || !gates.TryGetValue(attributes, out EffectCullState? gate) || gate == null)
            {
                return;
            }

            gates.Remove(attributes);
            if (!gate.secondaryStopped)
            {
                return;
            }

            // Pooling disables the effect while lights are still off. Reset restarts particles
            // on the next spawn and does not touch these lights, so restore them here.
            RestoreLights(attributes.optionalLights, gate);
            gate.secondaryStopped = false;
            if (culledEffects > 0)
            {
                culledEffects--;
            }
        }

        // Postfix on SpawnEffect. The pool has already checked this copy out, so InUseCount is the
        // live concurrency of that effect. Only a new peak queues work; the common spawn costs one
        // array read and one dictionary count.
        internal static void NoteEffectSpawned(EffectIndex effectIndex)
        {
            int index = (int)effectIndex;
            if (showShaderStatus?.Value == true && index >= 0 && index < prefabs.Length)
            {
                TraceSpawn(index);
            }

            if (warmEffects?.Value != true)
            {
                return;
            }

            if (index < 0 || index >= peakInUse.Length)
            {
                return;
            }

            long started = HitchProbes.Armed ? Stopwatch.GetTimestamp() : 0L;
            try
            {
                NoteSpawn(index);
            }
            finally
            {
                HitchProbes.Note(HitchProbes.Id.EffectPeak, started);
            }
        }

        private static void NoteSpawn(int index)
        {
            GameObject? prefab = prefabs[index];
            if (!prefab)
            {
                AsyncOperationHandle<GameObject> handle = handles[index];
                if (!handle.IsValid() || !handle.IsDone)
                {
                    return;
                }

                prefab = handle.Result;
                if (!prefab)
                {
                    return;
                }

                prefabs[index] = prefab;
            }

            EffectPools.NotePrefabSpawn(prefab);
            int inUse = EffectPools.CheckoutCount(prefab);
            if (inUse <= peakInUse[index])
            {
                return;
            }

            peakInUse[index] = inUse;
            EnqueueWarm(index);
        }

        private static void EnqueueWarm(int index)
        {
            if (index < 0 || index >= queued.Length || queued[index])
            {
                return;
            }

            queued[index] = true;
            warmQueue.Enqueue(index);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Run.onRunDestroyGlobal += OnRunDestroy;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Run.onRunDestroyGlobal -= OnRunDestroy;
            ReleaseDirectPins();
            ReleaseShaderHolds();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
            {
                return;
            }

            // Scene unload already destroyed the pool copies. Pins stay valid across a stage change
            // because they are held until the run ends, and the peaks stay valid for the same run,
            // so the copies this run needs are queued to be built again on the new stage.
            EffectPools.ResetParkedActive();
            pinCursor = 0;
            warmedPrefabs = 0;
            instancesBuilt = 0;
            warmNotBefore = 0f;
            sceneReadyTime = Time.unscaledTime;
            System.Array.Clear(warmed, 0, warmed.Length);
            System.Array.Clear(settled, 0, settled.Length);
            System.Array.Clear(prefabs, 0, prefabs.Length);
            warmQueue.Clear();
            System.Array.Clear(queued, 0, queued.Length);
            for (int i = 0; i < peakInUse.Length; i++)
            {
                if (peakInUse[i] > 0)
                {
                    EnqueueWarm(i);
                }
            }
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            gates.Clear();
            culledEffects = 0;
            cullWasEnabled = false;
            gameplayCamera = null;
        }

        private void Update()
        {
            MaybeLogStatus();
            if (warmEffects?.Value != true || !EffectCatalog.availability.available)
            {
                return;
            }

            if (EffectManager.DisableAllEffectSpawning)
            {
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            if (LoadingPhase.BlocksWarmup(sceneName))
            {
                return;
            }

            int count = EnsureCatalog();
            if (count == 0)
            {
                return;
            }

            // One prefab per frame, including the character select. The ring is the progress of
            // that lazy pass. A single pin used to scan every assembly and freeze the frame;
            // the loader is resolved at plugin load, so a pin here is only the asset start.
            bool loading = LoadingPhase.IsLoading;
            float frameSeconds = Time.unscaledDeltaTime;
            bool frameBlocked = !loading && (frameSeconds > HitchFrameSeconds || Time.unscaledTime < warmNotBefore);
            if (!frameBlocked && pinCursor < count)
            {
                long pinStart = Stopwatch.GetTimestamp();
                if (Pin(pinCursor))
                {
                    pinCursor++;
                    float pinMs = (float)((Stopwatch.GetTimestamp() - pinStart) * 1000d / Stopwatch.Frequency);
                    if (!loading && pinMs > ExpensiveWarmMs)
                    {
                        float cooldown = pinMs * 0.008f;
                        warmNotBefore = Time.unscaledTime + (cooldown > 1f ? 1f : cooldown);
                    }
                }

                HitchProbes.Note(HitchProbes.Id.EffectPin, pinStart);
            }

            LoadingProgress.Set("Effect prefabs", pinCursor, count, pinCursor < count);
            // Shader variants are global, so a compile started in the character select is not thrown
            // away when the stage activates. Outside the fade, one prefab on a short frame.
            if (warmShaders?.Value == true && shaderCursor < count && (loading || !frameBlocked))
            {
                WarmShaderStep(count, loading);
            }

            if (loading)
            {
                return;
            }

            // Copies are built only after a run exists. Doing it in the menu instantiates effects
            // into the lobby scene, and the lobby's own effect is one of those prefabs.
            Run? run = Run.instance;
            if (!run)
            {
                return;
            }

            // Peaks describe one run's content. A new run object starts a fresh set, so copies are
            // not built for effects only a previous run ever spawned.
            if (observedRun != run)
            {
                observedRun = run;
                System.Array.Clear(peakInUse, 0, peakInUse.Length);
                System.Array.Clear(warmed, 0, warmed.Length);
                warmQueue.Clear();
                System.Array.Clear(queued, 0, queued.Length);
            }

            int waiting = warmQueue.Count;
            if (waiting == 0)
            {
                PublishCopyProgress(0);
                return;
            }

            // The first seconds of a stage are still quiet enough to rebuild copies this run
            // has already used. Combat after that waits out a long frame and spaces warms.
            bool opening = Time.unscaledTime - sceneReadyTime < 15f;
            if (frameBlocked)
            {
                PublishCopyProgress(waiting);
                return;
            }

            bool busy = CharacterBody.readOnlyInstancesList.Count > 1;
            bool calm = frameSeconds <= CalmFrameSeconds;
            bool throttled = Time.unscaledTime - lastWarmTime < BusyWarmIntervalSeconds;
            bool combatWait = !opening && busy;
            if (throttled && (combatWait || !calm))
            {
                PublishCopyProgress(waiting);
                return;
            }

            int budget = Clamp(warmsPerFrame?.Value ?? 1, 1, 2);
            int built = 0;
            int checks = waiting;
            long frameStart = Stopwatch.GetTimestamp();
            int processed = 0;
            while (checks-- > 0 && warmQueue.Count > 0)
            {
                if (built >= budget)
                {
                    break;
                }

                int index = warmQueue.Dequeue();
                processed++;
                if (index >= queued.Length)
                {
                    continue;
                }

                queued[index] = false;
                if (TryWarm(index))
                {
                    built++;
                    lastWarmTime = Time.unscaledTime;
                }
                else if (!settled[index] && warmed[index] < PeakTarget(index))
                {
                    // Prefab still loading. Requeue once per frame at most: the snapshot count
                    // above keeps this from spinning inside one Update.
                    EnqueueWarm(index);
                }
            }

            if (processed > 0)
            {
                HitchProbes.Note(HitchProbes.Id.EffectWarm, frameStart);
            }

            PublishCopyProgress(warmQueue.Count);
        }

        private static void PublishCopyProgress(int waiting)
        {
            int done = warmedPrefabs;
            int total = warmedPrefabs + waiting;
            if (done < 0)
            {
                done = 0;
            }

            LoadingProgress.Set("Effect copies", done, total, waiting > 0);
        }

        private static void MaybeLogStatus()
        {
            if (logStatus?.Value != true)
            {
                nextLogTime = 0f;
                return;
            }

            float now = Time.unscaledTime;
            if (nextLogTime != 0f && now < nextLogTime)
            {
                return;
            }

            nextLogTime = now + 1f;
            LogStatus();
        }

        private void LateUpdate()
        {
            // The menu and lobby have their own long-lived effects. Culling those, or holding a camera
            // from the previous scene, both breaks the lobby effect and throws once that camera is gone.
            bool inRun = (bool)Run.instance;
            bool enabled = inRun && cullSecondary?.Value == true;
            if (!enabled)
            {
                if (cullWasEnabled || culledEffects > 0)
                {
                    ResumeAll();
                    cullWasEnabled = false;
                }

                return;
            }

            cullWasEnabled = true;
            int interval = Clamp(cullInterval?.Value ?? 4, 1, 30);
            frame++;
            if (frame % interval != 0)
            {
                return;
            }

            if (!gameplayCamera)
            {
                gameplayCamera = Camera.main;
            }

            long cullStart = HitchProbes.Armed ? Stopwatch.GetTimestamp() : 0L;
            Camera? camera = gameplayCamera;
            if (!camera)
            {
                HitchProbes.Note(HitchProbes.Id.EffectCull, cullStart);
                return;
            }

            float distance = Mathf.Max(5f, cullDistance?.Value ?? 55f);
            float stopSqr = distance * distance;
            float resume = distance * 0.75f;
            float resumeSqr = resume * resume;
            bool offScreen = cullOffScreen?.Value == true;
            // Frustum planes are built on first use inside Consider. A pass where every live
            // effect is already past the cull distance never needs them.
            frustumReady = false;

            Vector3 cameraPosition = camera!.transform.position;
            ReadOnlyCollection<VFXAttributes> live = VFXAttributes.readonlyVFXList;
            for (int i = 0; i < live.Count; i++)
            {
                VFXAttributes? attributes = live[i];
                if (!attributes)
                {
                    continue;
                }

                Consider(attributes, cameraPosition, stopSqr, resumeSqr, offScreen);
            }

            HitchProbes.Note(HitchProbes.Id.EffectCull, cullStart);
        }

        private void OnGUI()
        {
            if (showShaderStatus?.Value == true)
            {
                if (Time.unscaledTime - shaderStatusRefresh >= 0.5f)
                {
                    shaderStatusRefresh = Time.unscaledTime;
                    RefreshShaderStatus();
                }

                shaderStyle ??= new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    wordWrap = true
                };
                shaderStyle.normal.textColor = Color.white;
                float width = 740f;
                GUI.Label(new Rect(Screen.width - width - 16f, 16f + LoadingProgressView.BlockHeight, width, ShaderStatusBlockHeight), shaderStatusText, shaderStyle);
            }

            if (showStatus?.Value != true)
            {
                return;
            }

            // OnGUI fires several times per frame, so a frame-count gate still rebuilt the
            // counts many times a second. The profiler put that at over a millisecond a call.
            if (Time.unscaledTime - lastStatusRefresh >= 0.5f)
            {
                lastStatusRefresh = Time.unscaledTime;
                RefreshStatus();
            }

            if (statusStyle == null)
            {
                statusStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    wordWrap = false
                };
                statusStyle.normal.textColor = Color.white;
            }

            GUI.Label(new Rect(16f, 16f + LoadingProgressView.BlockHeight, 720f, 180f), statusText, statusStyle);
        }

        private static int EnsureCatalog()
        {
            int count = EffectCatalog.effectCount;
            if (count == handles.Length && count == directPins.Length)
            {
                return count;
            }

            ReleaseDirectPins();
            handles = new AsyncOperationHandle<GameObject>[count];
            directPins = new bool[count];
            addresses = new AssetReferenceT<GameObject>?[count];
            prefabs = new GameObject?[count];
            warmed = new byte[count];
            settled = new bool[count];
            peakInUse = new int[count];
            queued = new bool[count];
            warmQueue.Clear();
            pinCursor = 0;
            warmedPrefabs = 0;
            instancesBuilt = 0;
            ForgetShaderWarmup(true);
            shaderHeld = new bool[count];
            effectTrace = new byte[count];
            return count;
        }

        private static bool Pin(int index)
        {
            if (settled[index] || prefabs[index] || handles[index].IsValid())
            {
                return true;
            }

            AssetReferenceT<GameObject>? knownAddress = addresses[index];
            if (knownAddress != null)
            {
                return StartPin(index, knownAddress);
            }

            EffectDef? def = EffectCatalog.GetEffectDef((EffectIndex)index);
            if (def == null)
            {
                settled[index] = true;
                return true;
            }

            AssetOrDirectReference<GameObject>? reference = EffectPools.PrefabReference(def);
            if (reference == null)
            {
                settled[index] = true;
                return true;
            }

            AssetReferenceT<GameObject>? address = reference.address;
            if (address != null && address.RuntimeKeyIsValid())
            {
                addresses[index] = address;
                return StartPin(index, address);
            }

            if (reference.directRef)
            {
                prefabs[index] = reference.directRef;
                return true;
            }

            settled[index] = true;
            return true;
        }

        // False means the load could not be started yet. The caller leaves the cursor here and retries.
        private static bool StartPin(int index, AssetReferenceT<GameObject> address)
        {
            if (!LoadingPhase.IsLoading)
            {
                handles[index] = EffectPools.PinAddress(address);
                return true;
            }

            try
            {
                AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(address);
                if (!handle.IsValid())
                {
                    return false;
                }

                handles[index] = handle;
                directPins[index] = true;
                return true;
            }
            catch (System.Exception exception)
            {
                if (!directPinFailed)
                {
                    directPinFailed = true;
                    RORO.GlobalLogger?.LogWarning($"Effect prefab load did not start: {exception.Message}");
                }

                return false;
            }
        }

        private static void OnRunDestroy(Run run)
        {
            int first = ReleaseDirectPins();
            if (first >= 0 && first < pinCursor)
            {
                pinCursor = first;
            }
        }

        // Handles started directly are ours. The game does not release them on run end.
        // Returns the first index that was released, or -1.
        private static int ReleaseDirectPins()
        {
            int first = -1;
            int count = directPins.Length < handles.Length ? directPins.Length : handles.Length;
            for (int i = 0; i < count; i++)
            {
                if (!directPins[i])
                {
                    continue;
                }

                directPins[i] = false;
                AsyncOperationHandle<GameObject> handle = handles[i];
                handles[i] = default;
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                if (first < 0)
                {
                    first = i;
                }
            }

            return first;
        }

        // Pooled effects are the short-lived ones: shots, impacts, tracers, orbs, item procs.
        // DoNotPool stays out. The variant is the material keywords plus the runtime axes the
        // shader actually declares (soft particles, GPU instancing, fog). PassType comes from
        // the pass LightMode. A second addressable ref holds one prefab per shader for the
        // session; Addressables.Release of the run pin does not keep the GPU program alive.
        private static void WarmShaderStep(int count, bool rush)
        {
            if (ShadersWereEvicted())
            {
                ForgetShaderWarmup(true);
                shaderHeld = new bool[count];
            }

            long started = Stopwatch.GetTimestamp();
            int processed = 0;
            while (shaderCursor < count)
            {
                EffectDef? def = EffectCatalog.GetEffectDef((EffectIndex)shaderCursor);
                if (def == null || !def.shouldPool)
                {
                    shaderCursor++;
                    continue;
                }

                GameObject? prefab = prefabs[shaderCursor];
                if (!prefab)
                {
                    AsyncOperationHandle<GameObject> handle = handles[shaderCursor];
                    if (!handle.IsValid())
                    {
                        // Not pinned yet. Skipping here would move the cursor past prefabs the pin
                        // loop has not started, and those variants would never be compiled.
                        if (shaderCursor >= pinCursor)
                        {
                            break;
                        }

                        shaderCursor++;
                        continue;
                    }

                    if (!handle.IsDone)
                    {
                        break;
                    }

                    prefab = handle.Result;
                    prefabs[shaderCursor] = prefab;
                }

                int index = shaderCursor;
                shaderCursor++;
                processed++;
                if (prefab)
                {
                    CollectShaderVariants(prefab!, index);
                }

                // In play, compile this prefab now. Saving variants up for a batch of 24
                // would land that whole compile on one later frame.
                if (!rush || shaderBatchCount >= 24)
                {
                    FlushShaderBatch();
                }

                if (!rush || LoadingPhase.BudgetExpired(started, processed))
                {
                    break;
                }
            }

            if (shaderCursor >= count)
            {
                FlushShaderBatch();
            }

            if (!rush)
            {
                float warmMs = (float)((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
                if (warmMs > ExpensiveWarmMs)
                {
                    float cooldown = warmMs * 0.008f;
                    warmNotBefore = Time.unscaledTime + (cooldown > 1f ? 1f : cooldown);
                }
            }

            LoadingProgress.Set("Effect shaders", shaderCursor, count, shaderCursor < count);
        }

        private static bool ShadersWereEvicted()
        {
            for (int i = 0; i < pinnedShaders.Count; i++)
            {
                if (!pinnedShaders[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static void ForgetShaderWarmup(bool releaseHolds)
        {
            if (releaseHolds)
            {
                ReleaseShaderHolds();
            }

            shaderVariantKeys.Clear();
            shaderInfo.Clear();
            pinnedShaders.Clear();
            batchKeys.Clear();
            variantMarks.Clear();
            shaderTraceLines.Clear();
            shaderCursor = 0;
            shaderVariants = 0;
            shaderBatch = null;
            shaderBatchCount = 0;
            shaderWarmCalls = 0;
            shaderWarmMicroseconds = 0;
            traceMisses = 0;
            traceRecorded = 0;
            traceCompiled = 0;
            traceEmpty = 0;
            traceUnloaded = 0;
            traceNotPooled = 0;
            traceRepeats = 0;
            traceFogStamp = int.MinValue;
            if (effectTrace.Length > 0)
            {
                System.Array.Clear(effectTrace, 0, effectTrace.Length);
            }
        }

        private static void CollectShaderVariants(GameObject prefab, int index)
        {
            bool any = false;
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                bool instancing = false;
                Material? trail = null;
                if (renderer is ParticleSystemRenderer particles)
                {
                    instancing = particles.enableGPUInstancing;
                    trail = particles.trailMaterial;
                }

                Material[] materials = renderer.sharedMaterials ?? System.Array.Empty<Material>();

                for (int j = 0; j < materials.Length; j++)
                {
                    any |= TryAddMaterial(materials[j], instancing);
                }

                if (trail && !MaterialsContain(materials, trail!))
                {
                    any |= TryAddMaterial(trail, instancing);
                }
            }

            OmniEffect[] omnis = prefab.GetComponentsInChildren<OmniEffect>(true);
            for (int i = 0; i < omnis.Length; i++)
            {
                OmniEffect.OmniEffectGroup[] groups = omnis[i].omniEffectGroups;
                if (groups == null)
                {
                    continue;
                }

                for (int g = 0; g < groups.Length; g++)
                {
                    OmniEffect.OmniEffectElement[] elements = groups[g].omniEffectElements;
                    if (elements == null)
                    {
                        continue;
                    }

                    for (int e = 0; e < elements.Length; e++)
                    {
                        OmniEffect.OmniEffectElement element = elements[e];
                        if (element == null)
                        {
                            continue;
                        }

                        bool instancing = false;
                        ParticleSystem system = element.particleSystem;
                        if (system && system.TryGetComponent(out ParticleSystemRenderer particles))
                        {
                            instancing = particles.enableGPUInstancing;
                        }

                        any |= TryAddMaterial(element.particleSystemOverrideMaterial, instancing);
                    }
                }
            }

            AssignRandomMaterial[] picks = prefab.GetComponentsInChildren<AssignRandomMaterial>(true);
            for (int i = 0; i < picks.Length; i++)
            {
                Material[] choices = picks[i].materials;
                if (choices == null)
                {
                    continue;
                }

                bool instancing = picks[i].rend is ParticleSystemRenderer particles && particles.enableGPUInstancing;
                for (int j = 0; j < choices.Length; j++)
                {
                    any |= TryAddMaterial(choices[j], instancing);
                }
            }

            if (any)
            {
                HoldPrefab(index);
            }
        }

        private static bool MaterialsContain(Material[] materials, Material trail)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == trail)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryAddMaterial(Material? material, bool instancing)
        {
            if (!material)
            {
                return false;
            }

            Shader shader = material!.shader;
            if (!shader)
            {
                return false;
            }
            ShaderWarmInfo info = InfoFor(shader);
            FillScratch(material.shaderKeywords, info, instancing);
            bool any = false;
            PassType[] passes = info.Passes;
            for (int i = 0; i < passes.Length; i++)
            {
                any |= SubmitSets(shader, info, passes[i]);
            }

            return any;
        }

        private static void FillScratch(string[] keywords, ShaderWarmInfo info, bool instancing)
        {
            FillKeywordList(keywordScratch, keywords, info, instancing);
        }

        private static void FillKeywordList(List<string> dest, string[]? keywords, ShaderWarmInfo info, bool instancing)
        {
            dest.Clear();
            if (keywords != null)
            {
                for (int i = 0; i < keywords.Length; i++)
                {
                    string keyword = keywords[i];
                    if (!string.IsNullOrEmpty(keyword) && !ListHas(dest, keyword))
                    {
                        dest.Add(keyword);
                    }
                }
            }

            if (instancing && info.Instancing && !ListHas(dest, "INSTANCING_ON"))
            {
                dest.Add("INSTANCING_ON");
            }

            if (QualitySettings.softParticles && info.SoftParticles && !ListHas(dest, "SOFTPARTICLES_ON"))
            {
                dest.Add("SOFTPARTICLES_ON");
            }
        }

        private static bool ScratchHas(string keyword)
        {
            return ListHas(keywordScratch, keyword);
        }

        private static bool ListHas(List<string> list, string keyword)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == keyword)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SubmitSets(Shader shader, ShaderWarmInfo info, PassType pass)
        {
            bool directional = pass == PassType.ForwardBase && info.Directional && !ScratchHas("DIRECTIONAL");
            bool any = SubmitOne(shader, info, pass, false, null);
            if (directional)
            {
                any |= SubmitOne(shader, info, pass, true, null);
            }

            string[] fog = info.Fog;
            for (int i = 0; i < fog.Length; i++)
            {
                if (ScratchHas(fog[i]))
                {
                    continue;
                }

                any |= SubmitOne(shader, info, pass, false, fog[i]);
                if (directional)
                {
                    any |= SubmitOne(shader, info, pass, true, fog[i]);
                }
            }

            return any;
        }

        private static bool SubmitOne(Shader shader, ShaderWarmInfo info, PassType pass, bool directional, string? fog)
        {
            keywordCombo.Clear();
            for (int i = 0; i < keywordScratch.Count; i++)
            {
                keywordCombo.Add(keywordScratch[i]);
            }

            if (directional)
            {
                keywordCombo.Add("DIRECTIONAL");
                if (info.LightProbe && !ScratchHas("LIGHTPROBE_SH"))
                {
                    keywordCombo.Add("LIGHTPROBE_SH");
                }
            }

            if (!string.IsNullOrEmpty(fog))
            {
                keywordCombo.Add(fog!);
            }

            keywordCombo.Sort(System.StringComparer.Ordinal);
            string key = WriteVariantKey(variantKey, shader, pass, keywordCombo);
            if (!shaderVariantKeys.Add(key))
            {
                return false;
            }

            shaderBatch ??= new ShaderVariantCollection();
            int before = shaderBatch.variantCount;
            shaderBatch.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, keywordCombo.ToArray()));
            if (shaderBatch.variantCount <= before)
            {
                return false;
            }

            batchKeys.Add(key);
            shaderBatchCount++;
            shaderVariants++;
            RememberShader(shader);
            return true;
        }

        private static string WriteVariantKey(StringBuilder key, Shader shader, PassType pass, List<string> keywords)
        {
            key.Clear();
            key.Append(shader.GetInstanceID());
            key.Append('|');
            key.Append((int)pass);
            for (int i = 0; i < keywords.Count; i++)
            {
                key.Append('|');
                key.Append(keywords[i]);
            }

            return key.ToString();
        }

        private static void RememberShader(Shader shader)
        {
            for (int i = 0; i < pinnedShaders.Count; i++)
            {
                Shader pinned = pinnedShaders[i];
                if (pinned && pinned.GetInstanceID() == shader.GetInstanceID())
                {
                    return;
                }
            }

            pinnedShaders.Add(shader);
        }

        private static ShaderWarmInfo InfoFor(Shader shader)
        {
            int id = shader.GetInstanceID();
            if (shaderInfo.TryGetValue(id, out ShaderWarmInfo? info) && info != null)
            {
                return info;
            }

            info = BuildShaderWarmInfo(shader);
            shaderInfo[id] = info;
            return info;
        }

        private static ShaderWarmInfo BuildShaderWarmInfo(Shader shader)
        {
            string[] names = shader.keywordSpace.keywordNames;
            HashSet<string> declared = names == null
                ? new HashSet<string>(System.StringComparer.Ordinal)
                : new HashSet<string>(names, System.StringComparer.Ordinal);
            passScratch.Clear();
            int passCount = shader.passCount;
            if (!lightModeTagReady)
            {
                lightModeTag = new ShaderTagId("LightMode");
                lightModeTagReady = true;
            }

            for (int i = 0; i < passCount; i++)
            {
                string mode = shader.FindPassTagValue(i, lightModeTag).name;
                if (!TryMapPass(mode, out PassType pass) || passScratch.Contains(pass))
                {
                    continue;
                }

                passScratch.Add(pass);
            }

            if (passScratch.Count == 0)
            {
                passScratch.Add(PassType.Normal);
            }

            int fogCount = 0;
            for (int i = 0; i < FogKeywordNames.Length; i++)
            {
                if (declared.Contains(FogKeywordNames[i]))
                {
                    fogCount++;
                }
            }

            string[] fog = new string[fogCount];
            int fogIndex = 0;
            for (int i = 0; i < FogKeywordNames.Length; i++)
            {
                if (declared.Contains(FogKeywordNames[i]))
                {
                    fog[fogIndex++] = FogKeywordNames[i];
                }
            }

            return new ShaderWarmInfo
            {
                Passes = passScratch.ToArray(),
                SoftParticles = declared.Contains("SOFTPARTICLES_ON"),
                Instancing = declared.Contains("INSTANCING_ON"),
                Directional = declared.Contains("DIRECTIONAL"),
                LightProbe = declared.Contains("LIGHTPROBE_SH"),
                Fog = fog
            };
        }

        private static bool TryMapPass(string mode, out PassType pass)
        {
            if (string.IsNullOrEmpty(mode) || mode == "Always" || mode == "ALWAYS")
            {
                pass = PassType.Normal;
                return true;
            }

            if (mode == "ForwardBase")
            {
                pass = PassType.ForwardBase;
                return true;
            }

            pass = PassType.Normal;
            return false;
        }

        // One extra ref per prefab that introduced a shader. The run pin is released on run end
        // (immediately for direct loads, 12 seconds later for OnRunEnd). This ref is what keeps
        // the shader object, and the GPU programs on it, loaded for the rest of the process.
        private static void HoldPrefab(int index)
        {
            if (index < 0 || index >= shaderHeld.Length || shaderHeld[index])
            {
                return;
            }

            AssetReferenceT<GameObject>? address = addresses[index];
            if (address != null && address.RuntimeKeyIsValid())
            {
                try
                {
                    AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(address);
                    if (!handle.IsValid())
                    {
                        return;
                    }

                    shaderHolds.Add(handle);
                    shaderHeld[index] = true;
                    return;
                }
                catch (System.Exception exception)
                {
                    RORO.GlobalLogger?.LogWarning($"Effect shader hold did not start: {exception.Message}");
                    return;
                }
            }

            GameObject? prefab = prefabs[index];
            if (!prefab)
            {
                return;
            }

            shaderHoldPrefabs.Add(prefab!);
            shaderHeld[index] = true;
        }

        private static void ReleaseShaderHolds()
        {
            for (int i = 0; i < shaderHolds.Count; i++)
            {
                AsyncOperationHandle<GameObject> handle = shaderHolds[i];
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            shaderHolds.Clear();
            shaderHoldPrefabs.Clear();
            if (shaderHeld.Length > 0)
            {
                System.Array.Clear(shaderHeld, 0, shaderHeld.Length);
            }
        }

        private sealed class ShaderWarmInfo
        {
            internal PassType[] Passes = System.Array.Empty<PassType>();
            internal bool SoftParticles;
            internal bool Instancing;
            internal bool Directional;
            internal bool LightProbe;
            internal string[] Fog = System.Array.Empty<string>();
        }

        private struct WarmBatchMark
        {
            internal int Microseconds;
            internal int Variants;
        }

        private static void TraceSpawn(int index)
        {
            int stamp = FogStamp();
            if (stamp != traceFogStamp)
            {
                // Fog and soft particles pick the variant the next draw uses. Counts are for
                // the current pair only, so a stage with different fog does not keep the old verdict.
                traceFogStamp = stamp;
                traceMisses = 0;
                traceRecorded = 0;
                traceCompiled = 0;
                traceEmpty = 0;
                traceUnloaded = 0;
                traceNotPooled = 0;
                traceRepeats = 0;
                shaderTraceLines.Clear();
                if (effectTrace.Length > 0)
                {
                    System.Array.Clear(effectTrace, 0, effectTrace.Length);
                }
            }

            if (index < 0 || index >= effectTrace.Length)
            {
                return;
            }

            if (effectTrace[index] != 0)
            {
                traceRepeats++;
                return;
            }

            EffectDef? def = EffectCatalog.GetEffectDef((EffectIndex)index);
            string name = def != null && !string.IsNullOrEmpty(def.prefabName) ? def.prefabName : index.ToString();
            if (def == null || !def.shouldPool)
            {
                effectTrace[index] = 5;
                traceNotPooled++;
                return;
            }

            GameObject? prefab = prefabs[index];
            if (!prefab)
            {
                effectTrace[index] = 4;
                traceUnloaded++;
                PushTraceLine("нет префаба  " + name);
                return;
            }

            int worst = 3;
            bool any = false;
            RankPrefab(prefab!, name, ref worst, ref any);
            if (!any)
            {
                effectTrace[index] = 6;
                traceEmpty++;
                return;
            }

            if (worst == 1)
            {
                effectTrace[index] = 1;
                traceMisses++;
                return;
            }

            if (worst == 2)
            {
                effectTrace[index] = 2;
                traceRecorded++;
                return;
            }

            effectTrace[index] = 3;
            traceCompiled++;
        }

        private static int FogStamp()
        {
            int stamp = RenderSettings.fog ? 1 : 0;
            stamp |= ((int)RenderSettings.fogMode) << 1;
            if (QualitySettings.softParticles)
            {
                stamp |= 32;
            }

            return stamp;
        }

        private static void RankPrefab(GameObject prefab, string effectName, ref int worst, ref bool any)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                bool instancing = false;
                Material? trail = null;
                if (renderer is ParticleSystemRenderer particles)
                {
                    instancing = particles.enableGPUInstancing;
                    trail = particles.trailMaterial;
                }

                Material[] materials = renderer.sharedMaterials ?? System.Array.Empty<Material>();
                for (int j = 0; j < materials.Length; j++)
                {
                    RankMaterial(materials[j], instancing, effectName, ref worst, ref any);
                }

                if (trail && !MaterialsContain(materials, trail!))
                {
                    RankMaterial(trail, instancing, effectName, ref worst, ref any);
                }
            }

            OmniEffect[] omnis = prefab.GetComponentsInChildren<OmniEffect>(true);
            for (int i = 0; i < omnis.Length; i++)
            {
                OmniEffect.OmniEffectGroup[] groups = omnis[i].omniEffectGroups;
                if (groups == null)
                {
                    continue;
                }

                for (int g = 0; g < groups.Length; g++)
                {
                    OmniEffect.OmniEffectElement[] elements = groups[g].omniEffectElements;
                    if (elements == null)
                    {
                        continue;
                    }

                    for (int e = 0; e < elements.Length; e++)
                    {
                        OmniEffect.OmniEffectElement element = elements[e];
                        if (element == null)
                        {
                            continue;
                        }

                        bool instancing = false;
                        ParticleSystem system = element.particleSystem;
                        if (system && system.TryGetComponent(out ParticleSystemRenderer particles))
                        {
                            instancing = particles.enableGPUInstancing;
                        }

                        RankMaterial(element.particleSystemOverrideMaterial, instancing, effectName, ref worst, ref any);
                    }
                }
            }

            AssignRandomMaterial[] picks = prefab.GetComponentsInChildren<AssignRandomMaterial>(true);
            for (int i = 0; i < picks.Length; i++)
            {
                Material[] choices = picks[i].materials;
                if (choices == null)
                {
                    continue;
                }

                bool instancing = picks[i].rend is ParticleSystemRenderer particles && particles.enableGPUInstancing;
                for (int j = 0; j < choices.Length; j++)
                {
                    RankMaterial(choices[j], instancing, effectName, ref worst, ref any);
                }
            }
        }

        private static void RankMaterial(Material? material, bool instancing, string effectName, ref int worst, ref bool any)
        {
            if (!material)
            {
                return;
            }

            Shader shader = material!.shader;
            if (!shader)
            {
                return;
            }

            any = true;
            ShaderWarmInfo info = InfoFor(shader);
            FillKeywordList(traceScratch, material.shaderKeywords, info, instancing);
            PassType[] passes = info.Passes;
            for (int i = 0; i < passes.Length; i++)
            {
                int rank = RankPass(shader, info, passes[i], effectName);
                if (rank < worst)
                {
                    worst = rank;
                }
            }
        }

        // 1 = the draw's variant is not in a warmed collection.
        // 2 = it is in a collection, but this batch's WarmUp was not a single measured compile.
        // 3 = the collection held only this variant, and WarmUp took at least a millisecond.
        private static int RankPass(Shader shader, ShaderWarmInfo info, PassType pass, string effectName)
        {
            CopyTraceCombo(info, pass);
            traceCombo.Sort(System.StringComparer.Ordinal);
            string key = WriteVariantKey(traceKey, shader, pass, traceCombo);
            if (!CollectionContains(shader, pass))
            {
                PushTraceLine("промах  " + effectName + "  " + shader.name + "  " + pass + "  " + JoinCombo());
                return 1;
            }

            if (variantMarks.TryGetValue(key, out WarmBatchMark mark) && mark.Variants == 1 && mark.Microseconds >= 1000)
            {
                PushTraceLine("сборка  " + effectName + "  " + shader.name + "  " + mark.Microseconds + " мкс");
                return 3;
            }

            return 2;
        }

        private static void CopyTraceCombo(ShaderWarmInfo info, PassType pass)
        {
            traceCombo.Clear();
            for (int i = 0; i < traceScratch.Count; i++)
            {
                traceCombo.Add(traceScratch[i]);
            }

            if (pass == PassType.ForwardBase && info.Directional && !ListHas(traceScratch, "DIRECTIONAL"))
            {
                traceCombo.Add("DIRECTIONAL");
                if (info.LightProbe && !ListHas(traceScratch, "LIGHTPROBE_SH"))
                {
                    traceCombo.Add("LIGHTPROBE_SH");
                }
            }

            string? fog = ActiveFogKeyword(info);
            if (!string.IsNullOrEmpty(fog) && !ListHas(traceScratch, fog!))
            {
                traceCombo.Add(fog!);
            }
        }

        private static string? ActiveFogKeyword(ShaderWarmInfo info)
        {
            if (!RenderSettings.fog)
            {
                return null;
            }

            string? name = null;
            if (RenderSettings.fogMode == FogMode.Linear)
            {
                name = "FOG_LINEAR";
            }
            else if (RenderSettings.fogMode == FogMode.Exponential)
            {
                name = "FOG_EXP";
            }
            else if (RenderSettings.fogMode == FogMode.ExponentialSquared)
            {
                name = "FOG_EXP2";
            }

            if (name == null)
            {
                return null;
            }

            for (int i = 0; i < info.Fog.Length; i++)
            {
                if (info.Fog[i] == name)
                {
                    return name;
                }
            }

            return null;
        }

        private static bool CollectionContains(Shader shader, PassType pass)
        {
            string[] keywords = traceCombo.ToArray();
            ShaderVariantCollection.ShaderVariant variant = new ShaderVariantCollection.ShaderVariant(shader, pass, keywords);
            for (int i = 0; i < shaderCollections.Count; i++)
            {
                ShaderVariantCollection collection = shaderCollections[i];
                if (collection && collection.Contains(variant))
                {
                    return true;
                }
            }

            return false;
        }

        private static string JoinCombo()
        {
            if (traceCombo.Count == 0)
            {
                return "без слов";
            }

            traceKey.Clear();
            for (int i = 0; i < traceCombo.Count; i++)
            {
                if (i > 0)
                {
                    traceKey.Append(' ');
                }

                traceKey.Append(traceCombo[i]);
            }

            return traceKey.ToString();
        }

        private static void PushTraceLine(string line)
        {
            shaderTraceLines.Insert(0, line);
            if (shaderTraceLines.Count > 6)
            {
                shaderTraceLines.RemoveAt(6);
            }
        }

        private static void FlushShaderBatch()
        {
            if (shaderBatch == null || shaderBatchCount == 0)
            {
                shaderBatch = null;
                shaderBatchCount = 0;
                return;
            }

            int variants = shaderBatchCount;
            bool warmed = false;
            int microseconds = 0;
            try
            {
                // WarmUp runs on this thread and does not return until the call is done.
                // Unity 2021.3 has no background compile, and this call is not safe off the main thread.
                // A return in tens of microseconds is a lookup, not a driver compile.
                long started = Stopwatch.GetTimestamp();
                shaderBatch.WarmUp();
                microseconds = (int)((Stopwatch.GetTimestamp() - started) * 1000000d / Stopwatch.Frequency);
                shaderCollections.Add(shaderBatch);
                warmed = true;
            }
            catch (System.Exception exception)
            {
                RORO.GlobalLogger?.LogWarning($"Effect shader warmup batch failed: {exception.Message}");
            }

            if (warmed)
            {
                shaderWarmCalls++;
                shaderWarmMicroseconds += microseconds;
                WarmBatchMark mark = new WarmBatchMark
                {
                    Microseconds = microseconds,
                    Variants = variants
                };
                for (int i = 0; i < batchKeys.Count; i++)
                {
                    variantMarks[batchKeys[i]] = mark;
                }
            }

            batchKeys.Clear();
            shaderBatch = null;
            shaderBatchCount = 0;
        }

        private static bool TryWarm(int index)
        {
            if (settled[index])
            {
                return false;
            }

            int target = PeakTarget(index);
            if (target == 0)
            {
                return false;
            }

            GameObject? prefab = prefabs[index];
            if (!prefab)
            {
                AsyncOperationHandle<GameObject> handle = handles[index];
                if (!handle.IsValid())
                {
                    // The warm cursor can lap the pin cursor. An invalid handle before Pin ran is not a failure.
                    if (index < pinCursor)
                    {
                        settled[index] = true;
                    }

                    return false;
                }

                if (!handle.IsDone)
                {
                    return false;
                }

                prefab = handle.Result;
                if (!prefab)
                {
                    settled[index] = true;
                    return false;
                }

                prefabs[index] = prefab;
            }

            if (warmed[index] >= target)
            {
                return false;
            }

            EffectDef? def = EffectCatalog.GetEffectDef((EffectIndex)index);
            if (def == null || !def.shouldPool || !EffectManager.UsePools)
            {
                settled[index] = true;
                return false;
            }

            string prefabName = prefab!.name;

            // A combat spawn may already have left enough idle copies in this pool. Checking one
            // out and returning it again builds nothing, so do not spend the checkout or count it.
            int idleBefore = EffectPools.PoolIdleCount(prefab);
            if (idleBefore >= target)
            {
                if (warmed[index] == 0)
                {
                    warmedPrefabs++;
                }

                warmed[index] = (byte)target;
                return false;
            }

            // GetObject pops an idle copy when one exists, so a checkout returned at once reuses
            // the same copy every time. Holding every checkout until the target is reached is
            // what forces the pool to instantiate distinct copies for Warm Instances above one.
            int need = target - idleBefore;
            int totalBefore = EffectPools.PoolTotalCount(prefab);
            int checkoutBefore = EffectPools.CheckoutCount(prefab);
            bool wasActive = prefab.activeSelf;
            bool checkoutStarted = false;
            heldHelpers.Clear();
            long warmStart = Stopwatch.GetTimestamp();
            try
            {
                if (wasActive)
                {
                    // An active instantiate runs Start immediately. Orb effects read effect data there and
                    // throw, and the copy stays checked out of the pool.
                    prefab.SetActive(false);
                }

                checkoutStarted = true;
                for (int i = 0; i < need; i++)
                {
                    EffectManagerHelper? helper = EffectManager.GetPooledEffect(prefab, Park, Quaternion.identity);
                    if (!helper)
                    {
                        settled[index] = true;
                        ReturnHeld(prefabName);
                        return false;
                    }

                    heldHelpers.Add(helper);
                }
            }
            catch (System.Exception exception)
            {
                settled[index] = true;
                ReturnHeld(prefabName);
                if (checkoutStarted)
                {
                    EffectPools.DiscardCheckoutGrowth(prefab, checkoutBefore);
                }

                RORO.GlobalLogger?.LogWarning($"Effect warmup skipped {prefabName}: {exception.Message}");
                return false;
            }
            finally
            {
                if (wasActive && prefab)
                {
                    prefab.SetActive(true);
                }
            }

            int builtCopies = EffectPools.PoolTotalCount(prefab) - totalBefore;
            if (builtCopies < 0)
            {
                builtCopies = 0;
            }

            if (!ReturnHeld(prefabName))
            {
                settled[index] = true;
                return false;
            }

            if (warmed[index] == 0)
            {
                warmedPrefabs++;
            }

            warmed[index] = (byte)target;
            instancesBuilt += builtCopies;

            // One instantiate cannot be split, so a costly prefab earns a cooldown instead of
            // letting the next frame pay for another one right away.
            float warmMs = (float)((Stopwatch.GetTimestamp() - warmStart) * 1000d / Stopwatch.Frequency);
            if (warmMs > ExpensiveWarmMs)
            {
                float cooldown = warmMs * 0.008f;
                warmNotBefore = Time.unscaledTime + (cooldown > 1f ? 1f : cooldown);
            }

            return true;
        }

        private static bool ReturnHeld(string prefabName)
        {
            bool parked = true;
            for (int i = 0; i < heldHelpers.Count; i++)
            {
                EffectManagerHelper helper = heldHelpers[i];
                try
                {
                    helper.ReturnToPool();
                }
                catch (System.Exception exception)
                {
                    if (helper)
                    {
                        helper.gameObject.SetActive(false);
                    }

                    EffectPools.Discard(helper);
                    parked = false;
                    RORO.GlobalLogger?.LogWarning($"Effect warmup could not park {prefabName}: {exception.Message}");
                }
            }

            heldHelpers.Clear();
            return parked;
        }

        private static int PeakCap()
        {
            // 20 is the game's own default pool capacity: a return past it destroys the copy,
            // so building more than that ahead of time cannot be held anyway.
            if (warmFullPeak?.Value == true)
            {
                return 20;
            }

            return Clamp(warmPeakCopies?.Value ?? 4, 0, 20);
        }

        private static int PeakTarget(int index)
        {
            int cap = PeakCap();
            int peak = index >= 0 && index < peakInUse.Length ? peakInUse[index] : 0;
            return peak < cap ? peak : cap;
        }

        private static void Consider(VFXAttributes attributes, Vector3 cameraPosition, float stopSqr, float resumeSqr, bool offScreen)
        {
            ParticleSystem[]? systems = attributes.secondaryParticleSystem;
            Light[]? lights = attributes.optionalLights;
            bool hasSecondary = systems != null && systems.Length > 0;
            bool hasLights = lights != null && lights.Length > 0;
            if (!hasSecondary && !hasLights)
            {
                return;
            }

            if (!gates.TryGetValue(attributes, out EffectCullState? gate) || gate == null)
            {
                gate = new EffectCullState();
                gates[attributes] = gate;
            }

            Vector3 delta = attributes.transform.position - cameraPosition;
            float distanceSqr = delta.sqrMagnitude;
            bool tooFar = distanceSqr > (gate.secondaryStopped ? resumeSqr : stopSqr);
            bool hidden = false;
            if (!tooFar && offScreen)
            {
                // Distance alone already stops this effect, so the six-plane test is only
                // paid for effects close enough to stay visible on distance grounds.
                if (!frustumReady)
                {
                    Camera? camera = gameplayCamera;
                    if (camera)
                    {
                        GeometryUtility.CalculateFrustumPlanes(camera, Frustum);
                        frustumReady = true;
                    }
                }

                if (frustumReady)
                {
                    hidden = !GeometryUtility.TestPlanesAABB(Frustum, new Bounds(attributes.transform.position, CullBounds));
                }
            }

            bool stop = tooFar || hidden;
            if (stop == gate.secondaryStopped)
            {
                return;
            }

            if (stop)
            {
                StopSecondary(systems, lights, gate);
                gate.secondaryStopped = true;
                culledEffects++;
                return;
            }

            PlaySecondary(systems, lights, gate);
            gate.secondaryStopped = false;
            if (culledEffects > 0)
            {
                culledEffects--;
            }
        }

        private static void StopSecondary(ParticleSystem[]? systems, Light[]? lights, EffectCullState gate)
        {
            if (systems != null)
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem? system = systems[i];
                    if (system)
                    {
                        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }

            if (lights == null)
            {
                return;
            }

            if (gate.lightWasEnabled == null || gate.lightWasEnabled.Length != lights.Length)
            {
                gate.lightWasEnabled = new bool[lights.Length];
            }

            for (int i = 0; i < lights.Length; i++)
            {
                Light? light = lights[i];
                if (!light)
                {
                    continue;
                }

                gate.lightWasEnabled[i] = light.enabled;
                light.enabled = false;
            }
        }

        private static void PlaySecondary(ParticleSystem[]? systems, Light[]? lights, EffectCullState gate)
        {
            if (systems != null)
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem? system = systems[i];
                    if (system)
                    {
                        system.Play(false);
                    }
                }
            }

            RestoreLights(lights, gate);
        }

        private static void RestoreLights(Light[]? lights, EffectCullState gate)
        {
            if (lights == null || gate.lightWasEnabled == null)
            {
                return;
            }

            int count = lights.Length < gate.lightWasEnabled.Length ? lights.Length : gate.lightWasEnabled.Length;
            for (int i = 0; i < count; i++)
            {
                Light? light = lights[i];
                if (light)
                {
                    light.enabled = gate.lightWasEnabled[i];
                }
            }
        }

        private static void ResumeAll()
        {
            foreach (KeyValuePair<VFXAttributes, EffectCullState> pair in gates)
            {
                EffectCullState? gate = pair.Value;
                VFXAttributes? attributes = pair.Key;
                if (gate == null || !gate.secondaryStopped || !attributes)
                {
                    continue;
                }

                PlaySecondary(attributes.secondaryParticleSystem, attributes.optionalLights, gate);
                gate.secondaryStopped = false;
            }

            culledEffects = 0;
        }

        private static void RefreshStatus()
        {
            int loaded = 0;
            int pinned = 0;
            for (int i = 0; i < handles.Length; i++)
            {
                if (prefabs[i] || handles[i].IsValid())
                {
                    pinned++;
                }

                if (prefabs[i] || (handles[i].IsValid() && handles[i].Status == AsyncOperationStatus.Succeeded))
                {
                    loaded++;
                }
            }

            EffectPools.CountPools(out int pools, out int idle, out int inUse);
            int cap = PeakCap();

            statusBuilder.Clear();
            statusBuilder.Append("Effects ").Append(handles.Length);
            statusBuilder.Append("\nPinned ").Append(pinned).Append("  loaded ").Append(loaded);
            statusBuilder.Append("\nWarmed prefabs ").Append(warmedPrefabs).Append("  copies built ").Append(instancesBuilt).Append("  peak cap ").Append(cap).Append("  queued ").Append(warmQueue.Count);
            statusBuilder.Append("\nPools ").Append(pools).Append("  idle ").Append(idle).Append("  in use ").Append(inUse);
            statusBuilder.Append("\nLive ").Append(VFXAttributes.readonlyVFXList.Count).Append("  secondary culled ").Append(culledEffects);
            statusText = statusBuilder.ToString();
        }

        private static void RefreshShaderStatus()
        {
            int live = 0;
            for (int i = 0; i < pinnedShaders.Count; i++)
            {
                if (pinnedShaders[i])
                {
                    live++;
                }
            }

            int stored = 0;
            for (int i = 0; i < shaderCollections.Count; i++)
            {
                ShaderVariantCollection collection = shaderCollections[i];
                if (collection)
                {
                    stored += collection.variantCount;
                }
            }

            statusBuilder.Clear();
            statusBuilder.Append(warmShaders?.Value == true ? "Шейдеры эффектов: прогрев включён" : "Шейдеры эффектов: прогрев выключен");
            statusBuilder.Append("\nКурсор ").Append(shaderCursor).Append('/').Append(prefabs.Length);
            statusBuilder.Append("  в коллекциях ").Append(stored);
            statusBuilder.Append("  WarmUp ").Append(shaderWarmCalls).Append(" вызовов, ");
            AppendMicros(statusBuilder, shaderWarmMicroseconds);
            statusBuilder.Append("\nЖивые шейдеры ").Append(live).Append('/').Append(pinnedShaders.Count);
            statusBuilder.Append("  удержано префабов ").Append(shaderHolds.Count + shaderHoldPrefabs.Count);
            statusBuilder.Append("\nСпавны: промах ").Append(traceMisses);
            statusBuilder.Append("  записан ").Append(traceRecorded);
            statusBuilder.Append("  сборка одного варианта ").Append(traceCompiled);
            statusBuilder.Append("  повтор ").Append(traceRepeats);
            statusBuilder.Append("\nБез рендера ").Append(traceEmpty);
            statusBuilder.Append("  нет префаба ").Append(traceUnloaded);
            statusBuilder.Append("  не из пула ").Append(traceNotPooled);
            statusBuilder.Append("\nПопадание — вариант текущего тумана, мягких частиц и инстансинга есть в коллекции после WarmUp.");
            statusBuilder.Append("\nРелизный DX11 не пишет Shader.CreateGPUProgram. Короткий WarmUp — не сборка драйвера.");
            for (int i = 0; i < shaderTraceLines.Count; i++)
            {
                statusBuilder.Append('\n').Append(shaderTraceLines[i]);
            }

            shaderStatusText = statusBuilder.ToString();
        }

        private static void AppendMicros(StringBuilder builder, long microseconds)
        {
            if (microseconds >= 1000)
            {
                builder.Append((microseconds / 1000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                builder.Append(" мс");
                return;
            }

            builder.Append(microseconds);
            builder.Append(" мкс");
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }

    internal sealed class EffectCullState
    {
        internal bool secondaryStopped;
        internal bool[]? lightWasEnabled;
    }
}
