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
        private static ConfigEntry<int>? warmPeakCopies;
        private static ConfigEntry<int>? loadsPerFrame;
        private static ConfigEntry<int>? warmsPerFrame;
        private static ConfigEntry<bool>? cullSecondary;
        private static ConfigEntry<float>? cullDistance;
        private static ConfigEntry<bool>? cullOffScreen;
        private static ConfigEntry<int>? cullInterval;
        private static ConfigEntry<bool>? showStatus;
        private static ConfigEntry<bool>? logStatus;
        private static float nextLogTime;

        private static readonly StringBuilder statusBuilder = new StringBuilder(384);
        private static readonly Dictionary<VFXAttributes, EffectCullState> gates = new Dictionary<VFXAttributes, EffectCullState>();
        private static readonly Queue<int> warmQueue = new Queue<int>();
        // Reused across warms: checking out several helpers at once is the only way to make a pool
        // grow past the copies it already holds. Returning each one immediately re-pops the same copy.
        private static readonly List<EffectManagerHelper> heldHelpers = new List<EffectManagerHelper>(4);

        private static AsyncOperationHandle<GameObject>[] handles = System.Array.Empty<AsyncOperationHandle<GameObject>>();
        private static AssetReferenceT<GameObject>?[] addresses = System.Array.Empty<AssetReferenceT<GameObject>?>();
        private static GameObject?[] prefabs = System.Array.Empty<GameObject?>();
        private static byte[] warmed = System.Array.Empty<byte>();
        private static bool[] settled = System.Array.Empty<bool>();
        private static int[] peakInUse = System.Array.Empty<int>();
        private static bool[] queued = System.Array.Empty<bool>();

        private static int pinCursor;
        private static int warmedPrefabs;
        private static int instancesBuilt;
        private static int culledEffects;
        private static int frame;
        private static float lastWarmTime;
        private static float warmNotBefore;
        private static bool frustumReady;
        private static float sceneReadyTime;
        private static Camera? gameplayCamera;
        private static bool cullWasEnabled;
        private static Run? observedRun;
        private static string statusText = "Effect status is not ready yet.";
        private static GUIStyle? statusStyle;

        internal static ConfigEntry<SettingsProfiles.Profile>? Profile { get; private set; }
        internal static ConfigEntry<bool>? WarmEffects => warmEffects;
        internal static ConfigEntry<bool>? KeepPoolsEntry => keepPools;
        internal static ConfigEntry<bool>? KeepCopiesActiveEntry => keepCopiesActive;
        internal static ConfigEntry<int>? WarmPeakCopiesEntry => warmPeakCopies;
        internal static ConfigEntry<int>? LoadsPerFrameEntry => loadsPerFrame;
        internal static ConfigEntry<int>? WarmsPerFrameEntry => warmsPerFrame;
        internal static ConfigEntry<bool>? CullSecondary => cullSecondary;
        internal static ConfigEntry<float>? CullDistanceEntry => cullDistance;
        internal static ConfigEntry<bool>? CullOffScreenEntry => cullOffScreen;
        internal static ConfigEntry<int>? CullIntervalEntry => cullInterval;
        internal static ConfigEntry<bool>? ShowStatusEntry => showStatus;
        internal static ConfigEntry<bool>? LogStatusEntry => logStatus;

        internal static bool KeepPools => keepPools?.Value ?? true;
        internal static bool KeepCopiesActive => keepCopiesActive?.Value == true;

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
                "<color=#FFDD44>[Experimental]</color> Parked pool copies stay active instead of being deactivated. The next spawn skips SetActive. Particles are stopped, renderers are hidden, and the copy leaves the live effect list the same way a deactivate would. A script on the prefab that keeps ticking while it is parked still runs.");

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

            logStatus = config.Bind(
                section,
                "Log Effect Status",
                false,
                "Writes the effect-pool counts to the BepInEx log once a second while this is on.");

            Profile = SettingsProfiles.Begin(config, section);
            SettingsProfiles.Add(warmEffects, true, true, true);
            SettingsProfiles.Add(keepPools, true, true, true);
            SettingsProfiles.Add(keepCopiesActive, false, false, false);
            SettingsProfiles.Add(warmPeakCopies, 2, 4, 6);
            // SettingsProfiles.Add(warmAllCopies, false, false, false);
            SettingsProfiles.Add(loadsPerFrame, 2, 4, 4);
            SettingsProfiles.Add(warmsPerFrame, 1, 1, 1);
            SettingsProfiles.Add(cullSecondary, false, true, true);
            SettingsProfiles.Add(cullDistance, 80f, 55f, 28f);
            SettingsProfiles.Add(cullOffScreen, false, true, true);
            SettingsProfiles.Add(cullInterval, 8, 4, 2);
            SettingsProfiles.Add(showStatus, false, false, false);
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
            if (warmEffects?.Value != true)
            {
                return;
            }

            int index = (int)effectIndex;
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
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
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
            if (sceneName.StartsWith("loading", System.StringComparison.OrdinalIgnoreCase) || LoadingPhase.IsStartupSplash(sceneName))
            {
                return;
            }

            int count = EnsureCatalog();
            if (count == 0)
            {
                return;
            }

            // The loading window is for asset loads only. Pins are Addressables load starts,
            // which survive scene activation, so they keep running under FadeToBlack. Pool
            // copies are scene objects and would be destroyed at activation, so building
            // them still waits for the new stage below.
            bool loading = LoadingPhase.IsLoading;
            float frameSeconds = Time.unscaledDeltaTime;
            bool frameBlocked = !loading && (frameSeconds > HitchFrameSeconds || Time.unscaledTime < warmNotBefore);
            if (!frameBlocked)
            {
                long pinStart = HitchProbes.Armed ? Stopwatch.GetTimestamp() : 0L;
                int loads = Clamp(loadsPerFrame?.Value ?? 4, 1, 8);
                int started = 0;
                while (pinCursor < count && started < loads)
                {
                    Pin(pinCursor);
                    pinCursor++;
                    started++;
                }

                if (started > 0)
                {
                    HitchProbes.Note(HitchProbes.Id.EffectPin, pinStart);
                }
            }

            LoadingProgress.Set("Effect prefabs", pinCursor, count, pinCursor < count);
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
            if (showStatus?.Value != true)
            {
                return;
            }

            if ((Time.frameCount & 15) == 0)
            {
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
            if (count == handles.Length)
            {
                return count;
            }

            handles = new AsyncOperationHandle<GameObject>[count];
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
            return count;
        }

        private static void Pin(int index)
        {
            if (settled[index] || prefabs[index] || handles[index].IsValid())
            {
                return;
            }

            AssetReferenceT<GameObject>? knownAddress = addresses[index];
            if (knownAddress != null)
            {
                handles[index] = EffectPools.PinAddress(knownAddress);
                return;
            }

            EffectDef? def = EffectCatalog.GetEffectDef((EffectIndex)index);
            if (def == null)
            {
                settled[index] = true;
                return;
            }

            AssetOrDirectReference<GameObject>? reference = EffectPools.PrefabReference(def);
            if (reference == null)
            {
                settled[index] = true;
                return;
            }

            AssetReferenceT<GameObject>? address = reference.address;
            if (address != null && address.RuntimeKeyIsValid())
            {
                addresses[index] = address;
                handles[index] = EffectPools.PinAddress(address);
                return;
            }

            if (reference.directRef)
            {
                prefabs[index] = reference.directRef;
                return;
            }

            settled[index] = true;
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
            return Clamp(warmPeakCopies?.Value ?? 4, 0, 8);
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
            int cap = Clamp(warmPeakCopies?.Value ?? 4, 0, 8);

            statusBuilder.Clear();
            statusBuilder.Append("Effects ").Append(handles.Length);
            statusBuilder.Append("\nPinned ").Append(pinned).Append("  loaded ").Append(loaded);
            statusBuilder.Append("\nWarmed prefabs ").Append(warmedPrefabs).Append("  copies built ").Append(instancesBuilt).Append("  peak cap ").Append(cap).Append("  queued ").Append(warmQueue.Count);
            statusBuilder.Append("\nPools ").Append(pools).Append("  idle ").Append(idle).Append("  in use ").Append(inUse);
            statusBuilder.Append("\nLive ").Append(VFXAttributes.readonlyVFXList.Count).Append("  secondary culled ").Append(culledEffects);
            statusText = statusBuilder.ToString();
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
