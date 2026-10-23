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
    // for the run, builds the pool copy while frames are short, and leaves the copy alive until the stage unloads.
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
        private static ConfigEntry<int>? warmInstances;
        private static ConfigEntry<int>? warmPrefabLimit;
        private static ConfigEntry<int>? loadsPerFrame;
        private static ConfigEntry<int>? warmsPerFrame;
        private static ConfigEntry<bool>? cullSecondary;
        private static ConfigEntry<float>? cullDistance;
        private static ConfigEntry<bool>? cullOffScreen;
        private static ConfigEntry<int>? cullInterval;
        private static ConfigEntry<bool>? showStatus;

        private static readonly Dictionary<VFXAttributes, EffectCullState> gates = new Dictionary<VFXAttributes, EffectCullState>();
        private static readonly StringBuilder statusBuilder = new StringBuilder(384);
        // Reused across warms: checking out several helpers at once is the only way to make a pool
        // grow past the copies it already holds. Returning each one immediately re-pops the same copy.
        private static readonly List<EffectManagerHelper> heldHelpers = new List<EffectManagerHelper>(4);

        private static AsyncOperationHandle<GameObject>[] handles = System.Array.Empty<AsyncOperationHandle<GameObject>>();
        private static AssetReferenceT<GameObject>?[] addresses = System.Array.Empty<AssetReferenceT<GameObject>?>();
        private static GameObject?[] prefabs = System.Array.Empty<GameObject?>();
        private static byte[] warmed = System.Array.Empty<byte>();
        private static bool[] settled = System.Array.Empty<bool>();

        private static int pinCursor;
        private static int warmCursor;
        private static int warmedPrefabs;
        private static int instancesBuilt;
        private static int culledEffects;
        private static int frame;
        private static float lastWarmTime;
        private static float warmNotBefore;
        private static bool frustumReady;
        private static float sceneReadyTime;
        private static Camera? gameplayCamera;
        private static bool reportedReady;
        private static int readyTarget = int.MinValue;
        private static int readyLimit = int.MinValue;
        private static bool cullWasEnabled;
        private static string statusText = "Effect status is not ready yet.";
        private static GUIStyle? statusStyle;

        internal static ConfigEntry<SettingsProfiles.Profile>? Profile { get; private set; }
        internal static ConfigEntry<bool>? WarmEffects => warmEffects;
        internal static ConfigEntry<bool>? KeepPoolsEntry => keepPools;
        internal static ConfigEntry<int>? WarmInstancesEntry => warmInstances;
        internal static ConfigEntry<int>? WarmPrefabLimitEntry => warmPrefabLimit;
        internal static ConfigEntry<int>? LoadsPerFrameEntry => loadsPerFrame;
        internal static ConfigEntry<int>? WarmsPerFrameEntry => warmsPerFrame;
        internal static ConfigEntry<bool>? CullSecondary => cullSecondary;
        internal static ConfigEntry<float>? CullDistanceEntry => cullDistance;
        internal static ConfigEntry<bool>? CullOffScreenEntry => cullOffScreen;
        internal static ConfigEntry<int>? CullIntervalEntry => cullInterval;
        internal static ConfigEntry<bool>? ShowStatusEntry => showStatus;

        internal static bool KeepPools => keepPools?.Value ?? true;

        internal static void Bind(ConfigFile config)
        {
            const string section = "Effects";

            warmEffects = config.Bind(
                section,
                "Warm Effect Prefabs",
                true,
                "Loads effect prefabs in the background and builds inactive pool copies while frames are short. The first combat spawn otherwise waits on the load and instantiates a new copy.");

            keepPools = config.Bind(
                section,
                "Keep Effect Pools",
                true,
                "Keeps idle effect copies until the stage unloads. The game otherwise destroys a pool 45 seconds after its last use, and the next spawn instantiates a new copy.");

            warmInstances = config.Bind(
                section,
                "Warm Instances",
                1,
                "Inactive copies to build for each poolable effect. Zero only loads the prefab and leaves the first spawn to instantiate.");

            warmPrefabLimit = config.Bind(
                section,
                "Prefab Warm Limit",
                0,
                "Maximum number of effect prefabs to instantiate ahead of time. Zero warms the whole catalog.");

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
                "Draws the live effect-pool counts on screen. The log button writes the same counts to the BepInEx log.");

            Profile = SettingsProfiles.Begin(config, section);
            SettingsProfiles.Add(warmEffects, true, true, true);
            SettingsProfiles.Add(keepPools, true, true, true);
            SettingsProfiles.Add(warmInstances, 1, 1, 1);
            SettingsProfiles.Add(warmPrefabLimit, 0, 0, 0);
            SettingsProfiles.Add(loadsPerFrame, 2, 4, 4);
            SettingsProfiles.Add(warmsPerFrame, 1, 1, 1);
            SettingsProfiles.Add(cullSecondary, false, true, true);
            SettingsProfiles.Add(cullDistance, 80f, 55f, 28f);
            SettingsProfiles.Add(cullOffScreen, false, true, true);
            SettingsProfiles.Add(cullInterval, 8, 4, 2);
            SettingsProfiles.Add(showStatus, false, false, false);
            SettingsProfiles.End();
        }

        internal static void LogStatus()
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
            // because they are held until the run ends, so only the instances need to be built again.
            pinCursor = 0;
            warmCursor = 0;
            warmedPrefabs = 0;
            instancesBuilt = 0;
            reportedReady = false;
            readyTarget = int.MinValue;
            readyLimit = int.MinValue;
            warmNotBefore = 0f;
            sceneReadyTime = Time.unscaledTime;
            System.Array.Clear(warmed, 0, warmed.Length);
            System.Array.Clear(settled, 0, settled.Length);
            System.Array.Clear(prefabs, 0, prefabs.Length);
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            gates.Clear();
            culledEffects = 0;
            cullWasEnabled = false;
        }

        private void Update()
        {
            if (warmEffects?.Value != true || !EffectCatalog.availability.available)
            {
                return;
            }

            if (NetworkManager.IsSceneLoadingAsynchronously || EffectManager.DisableAllEffectSpawning)
            {
                return;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName.StartsWith("loading", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int count = EnsureCatalog();
            if (count == 0)
            {
                return;
            }

            // Starting an Addressables load and instantiating a copy are both main-thread work.
            // A frame that already ran long gets neither; the old overdue rule did the opposite and
            // forced a warm onto exactly those frames once two seconds had passed.
            float frameSeconds = Time.unscaledDeltaTime;
            if (frameSeconds > HitchFrameSeconds || Time.unscaledTime < warmNotBefore)
            {
                return;
            }

            int loads = Clamp(loadsPerFrame?.Value ?? 4, 1, 8);
            int started = 0;
            while (pinCursor < count && started < loads)
            {
                Pin(pinCursor);
                pinCursor++;
                started++;
            }

            // Copies are built only after a run exists. Doing it in the menu instantiates effects
            // into the lobby scene, and the lobby's own effect is one of those prefabs.
            if (!Run.instance)
            {
                return;
            }

            bool busy = CharacterBody.readOnlyInstancesList.Count > 1;
            bool opening = Time.unscaledTime - sceneReadyTime < 15f;
            bool calm = frameSeconds <= CalmFrameSeconds;
            bool throttled = Time.unscaledTime - lastWarmTime < BusyWarmIntervalSeconds;
            if (!opening && busy)
            {
                if (throttled)
                {
                    return;
                }
            }
            else if (!calm && throttled)
            {
                return;
            }

            int targetNow = Clamp(warmInstances?.Value ?? 1, 0, 4);
            int limitNow = Mathf.Max(0, warmPrefabLimit?.Value ?? 0);
            if (reportedReady)
            {
                if (readyTarget == targetNow && readyLimit == limitNow)
                {
                    return;
                }

                if (!AnyPending(targetNow, limitNow))
                {
                    readyTarget = targetNow;
                    readyLimit = limitNow;
                    return;
                }

                reportedReady = false;
            }

            int budget = Clamp(warmsPerFrame?.Value ?? 1, 1, 2);
            int built = 0;
            int checks = 0;
            while (built < budget && checks < 48)
            {
                if (warmCursor >= count)
                {
                    warmCursor = 0;
                }

                if (TryWarm(warmCursor))
                {
                    built++;
                    lastWarmTime = Time.unscaledTime;
                }

                warmCursor++;
                checks++;
            }

            if (!reportedReady && pinCursor >= count && !AnyPending(targetNow, limitNow))
            {
                reportedReady = true;
                readyTarget = targetNow;
                readyLimit = limitNow;
                RORO.GlobalLogger?.LogInfo($"Effect warmup finished: {warmedPrefabs} prefabs, {instancesBuilt} copies built, catalog {handles.Length}.");
            }
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

            Camera? camera = gameplayCamera;
            if (!camera)
            {
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

            GUI.Label(new Rect(16f, 16f, 720f, 180f), statusText, statusStyle);
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
            pinCursor = 0;
            warmCursor = 0;
            warmedPrefabs = 0;
            instancesBuilt = 0;
            reportedReady = false;
            readyTarget = int.MinValue;
            readyLimit = int.MinValue;
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

            int target = Clamp(warmInstances?.Value ?? 1, 0, 4);
            if (target == 0)
            {
                return false;
            }

            int limit = Mathf.Max(0, warmPrefabLimit?.Value ?? 0);
            if (limit > 0 && warmedPrefabs >= limit && warmed[index] == 0)
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

        private static bool AnyPending(int target, int limit)
        {
            for (int i = 0; i < settled.Length; i++)
            {
                if (settled[i] || warmed[i] >= target)
                {
                    continue;
                }

                if (limit > 0 && warmedPrefabs >= limit && warmed[i] == 0)
                {
                    continue;
                }

                return true;
            }

            return false;
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
            int target = Clamp(warmInstances?.Value ?? 1, 0, 4);
            int live = VFXAttributes.readonlyVFXList.Count;

            statusBuilder.Clear();
            statusBuilder.Append("Effects ").Append(handles.Length);
            statusBuilder.Append("\nPinned ").Append(pinned).Append("  loaded ").Append(loaded);
            statusBuilder.Append("\nWarmed prefabs ").Append(warmedPrefabs).Append("  copies built ").Append(instancesBuilt).Append("  target ").Append(target);
            statusBuilder.Append("\nPools ").Append(pools).Append("  idle ").Append(idle).Append("  in use ").Append(inUse);
            statusBuilder.Append("\nLive ").Append(live).Append("  secondary culled ").Append(culledEffects);
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
