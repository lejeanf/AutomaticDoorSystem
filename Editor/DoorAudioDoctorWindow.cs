#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutomaticDoorSystem.Utilities;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Play-mode diagnostics for one door's audio chain, link by link: door entity found ->
    /// audio config baked -> within pool culling range -> no id/spacing conflict -> pool slot
    /// assigned -> AudioSource registered on the bridge. Each link reports its live state, and
    /// the test buttons fire a real DoorAudioEventComponent so the chain can be exercised
    /// without walking an avatar into the trigger - if a test event plays, the audio chain is
    /// healthy and the problem is detection/state; if it stays silent, the first red row is
    /// the culprit. The static helpers also back the audio section on the DoorAuthoring
    /// inspector, so the same checks are available right on the door.
    /// </summary>
    public class DoorAudioDoctorWindow : EditorWindow
    {
        private int _doorId;
        private Vector2 _scroll;

        // Locator scan results (button-triggered - reading every closed subscene file each GUI
        // frame would be far too slow).
        private int _scannedDoorId = int.MinValue;
        private List<(string sceneName, SubScene subScene)> _subScenesWithDoor;

        [MenuItem("Tools/Jeanf/AutomaticDoorSystem/Door Audio Doctor")]
        public static void Open()
        {
            var window = GetWindow<DoorAudioDoctorWindow>("Door Audio Doctor");
            window.minSize = new Vector2(420f, 320f);
            window.Show();
        }

        public static void Open(int doorId)
        {
            Open();
            GetWindow<DoorAudioDoctorWindow>()._doorId = doorId;
        }

        private void OnEnable()
        {
            TryAdoptSelection();
        }

        private void OnSelectionChange()
        {
            TryAdoptSelection();
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            // Live values (state, distance, isPlaying) change while playing - keep the view fresh.
            if (Application.isPlaying) Repaint();
        }

        private void TryAdoptSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;

            var authoring = go.GetComponentInParent<DoorAuthoring>();
            if (authoring != null) _doorId = authoring.doorId;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _doorId = EditorGUILayout.IntField("Door Id", _doorId);
                if (GUILayout.Button("From selection", GUILayout.Width(110f)))
                {
                    TryAdoptSelection();
                }
            }

            EditorGUILayout.Space();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Live checks need play mode - the pool, the bridges and the door entities only exist there. " +
                    "Static asset checks live in Tools > Jeanf > AutomaticDoorSystem > Setup Validator.",
                    MessageType.Info);
                DrawAuthoringLocator();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawLiveDiagnostics();

            EditorGUILayout.EndScrollView();
        }

        private void DrawLiveDiagnostics()
        {
            var dataBridge = DoorDataBridge.Instance;
            var pool = AudioSourcePoolManager.Instance;
            var audioBridge = DoorAudioBridge.Instance;

            if (!Row(dataBridge != null, "DoorDataBridge present in the scene",
                    "No DoorDataBridge instance - nothing reads the door entities. Run the Setup Validator."))
                return;
            if (!Row(pool != null, "AudioSourcePoolManager present in the scene",
                    "No AudioSourcePoolManager - no pooled AudioSources exist at all."))
                return;
            if (!Row(audioBridge != null, "DoorAudioBridge present in the scene",
                    "No DoorAudioBridge - audio events are never routed to an AudioSource."))
                return;

            // --- door entity ---
            bool found = dataBridge.TryGetDoorInfo(_doorId, out var info);
            if (!Row(found, $"Door entity with id {_doorId} found",
                    $"No baked door entity has id {_doorId}. Either the subscene is not entity-loaded, the baked " +
                    "id differs from the authoring (stale bake - re-bake the subscene), or baking skipped the " +
                    "door (missing DoorConfig)."))
            {
                EditorGUILayout.HelpBox(LoadedIdsSummary(dataBridge), MessageType.Info);
                DrawAuthoringLocator();
                return;
            }

            DrawDoorState(info);

            // --- audio config ---
            if (!Row(info.audioConfig != null, $"Audio config baked: {(info.audioConfig != null ? info.audioConfig.name : "none")}",
                    "The entity carries no DoorAudioConfiguration - the DoorAuthoring had none assigned when the " +
                    "subscene was baked. Assign one and re-bake; the pool skips config-less doors entirely."))
                return;

            var configIssues = DoorAudioConfigurationValidator.Validate(info.audioConfig);
            int configErrors = configIssues.Count(i => i.IsError);
            Row(configErrors == 0,
                $"Audio config valid ({configIssues.Count} issue(s), {configErrors} error(s))",
                "The config has authoring errors - open it and check the console, or run the Setup Validator.");

            // --- distance / culling ---
            var cam = Camera.main;
            if (Row(cam != null, "Camera.main found (pool culls by distance to it)",
                    "No enabled MainCamera - the pool never activates any AudioSource.") && cam != null)
            {
                float distance = Vector3.Distance(cam.transform.position, info.audioPosition);
                Row(distance <= pool.cullingDistance,
                    $"Within pool culling range ({distance:0.0}m of {pool.cullingDistance:0.0}m)",
                    "The door's audio position is beyond the pool's culling distance, so it holds no AudioSource " +
                    "until the player gets closer.");
            }

            // --- id / spacing conflicts ---
            var allDoors = dataBridge.GetAllDoorInfo();
            int sameId = allDoors.Count(d => d.doorId == _doorId);
            Row(sameId <= 1, $"Door id unique among loaded doors ({sameId} door(s) use id {_doorId})",
                "Several loaded doors share this id - pool slots and audio registrations are keyed by id, so all " +
                "but one of them stay silent. Renumber with the Setup Validator.");

            var tooClose = allDoors
                .Where(d => d.doorId != _doorId && d.audioConfig != null &&
                            Vector3.Distance(d.audioPosition, info.audioPosition) < pool.minimumSourceSpacing)
                .ToList();
            Row(tooClose.Count == 0,
                tooClose.Count == 0
                    ? $"No other door's audio position within {pool.minimumSourceSpacing:0.00}m"
                    : $"Audio position conflicts with door(s) {string.Join(", ", tooClose.Select(d => d.doorId))} " +
                      $"(closer than {pool.minimumSourceSpacing:0.00}m)",
                "The pool drops all but the nearest of spatially overlapping doors, so this door may never get an " +
                "AudioSource while the conflicting door is in range. Give one of them an Audio Anchor to separate " +
                "them, or lower Minimum Source Spacing on the pool.");

            // --- pool slot ---
            bool hasSlot = pool.TryGetPoolSlot(_doorId, out int slot, out var slotSource, out bool playingFlag);
            string parkedInfo = "";
            if (hasSlot && slotSource != null)
            {
                // The source is parked ON the door when assigned - a large distance here means it
                // is actually serving a different position than expected.
                float parkedDistance = Vector3.Distance(slotSource.transform.position, info.audioPosition);
                parkedInfo = $", parked {parkedDistance:0.00}m from this door's audio position";
            }
            Row(hasSlot, hasSlot
                    ? $"Pool slot assigned: slot {slot} -> '{slotSource?.name}'{parkedInfo}{(playingFlag ? " (mid-playback)" : "")}"
                    : "No pool slot assigned to this door",
                "The pool has not parked an AudioSource on this door. Causes: out of culling range, an id/spacing " +
                "conflict above, or all slots taken by closer doors (pool size " +
                $"{pool.maxPoolSize}, {pool.GetActiveAudioSourceCount()} in use).");

            if (hasSlot && slotSource != null)
            {
                bool audible = !slotSource.mute && slotSource.volume > 0.01f && slotSource.isActiveAndEnabled;
                Row(audible,
                    $"AudioSource audible (volume {slotSource.volume:0.00}, mute {slotSource.mute}, " +
                    $"enabled {slotSource.isActiveAndEnabled}, playing {slotSource.isPlaying})",
                    "The pooled source is muted, at volume 0 or disabled - it may still be fading in, or was " +
                    "deactivated by the pool.");
            }

            // --- bridge registration ---
            bool registered = audioBridge.TryGetRegistration(_doorId, out var registeredSource, out var registeredConfig);
            Row(registered, registered
                    ? $"DoorAudioBridge registration: '{registeredSource.name}' with config '{registeredConfig?.name}'"
                    : "DoorAudioBridge has no AudioSource registered for this door",
                "Audio events for this id are dropped at the bridge. The pool registers a source when it assigns " +
                "a slot - fix the pool rows above first.");

            if (registered && hasSlot && registeredSource != slotSource)
            {
                EditorGUILayout.HelpBox(
                    "The bridge's registered AudioSource differs from the pool's slot source - a stale " +
                    "registration. Report this; it should not happen.", MessageType.Error);
            }

            DrawActions();
        }

        private void DrawDoorState(DoorDataBridge.DoorInfo info)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.EntityManager.HasComponent<DoorStateComponent>(info.rootEntity)) return;

            var state = world.EntityManager.GetComponentData<DoorStateComponent>(info.rootEntity);
            EditorGUILayout.HelpBox(
                $"Live state: {state.CurrentState} (was {state.PreviousState})  |  timer {state.StateTimer:0.00}s  |  " +
                $"entities in trigger: {state.EntitiesInTrigger}  |  locked: {(state.IsLocked == 1 ? "yes" : "no")}\n" +
                "No sound on approach but a working test event below means the problem is detection/state, not audio.",
                MessageType.None);
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions (play mode)", EditorStyles.boldLabel);

            var source = FindAssignedSource(_doorId);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(source == null))
                {
                    if (GUILayout.Button("Select AudioSource"))
                    {
                        Selection.activeGameObject = source.gameObject;
                        EditorGUIUtility.PingObject(source.gameObject);
                    }
                }

                if (GUILayout.Button("Fire Open event"))
                {
                    if (!TryFireTestEvent(_doorId, AudioEventType.Open, out var error)) Debug.LogError(error);
                }

                if (GUILayout.Button("Fire Close event"))
                {
                    if (!TryFireTestEvent(_doorId, AudioEventType.Close, out var error)) Debug.LogError(error);
                }
            }

            EditorGUILayout.LabelField(
                "Test events go through the real DoorAudioBridge path (event entity -> registered source -> config). " +
                "Assigned pooled sources are named 'PooledAudioSource_NN [door id]' in the hierarchy.",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ---------------------------------------------------------------------------------------
        // Authoring locator: where does door N live, and how do I see its config?

        /// <summary>
        /// Finds the DoorAuthoring for the current id in the open scenes (select it directly), or
        /// - via a button-triggered text scan - the closed subscene that contains it, with a
        /// one-click open. This is how "the door exists in some subscene I can't see" turns into
        /// an inspectable object.
        /// </summary>
        private void DrawAuthoringLocator()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Find the door's authoring", EditorStyles.boldLabel);

            var authorings = FindObjectsByType<DoorAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var match = authorings.FirstOrDefault(a => a.doorId == _doorId);

            if (match != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"'{match.gameObject.name}' in '{match.gameObject.scene.name}'");
                    if (GUILayout.Button("Select", GUILayout.Width(80f)))
                    {
                        Selection.activeGameObject = match.gameObject;
                        EditorGUIUtility.PingObject(match.gameObject);
                    }
                }
                return;
            }

            EditorGUILayout.LabelField(
                $"No DoorAuthoring with id {_doorId} in the open scenes. It may live in a closed subscene:",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button($"Scan closed subscenes for door {_doorId}"))
            {
                ScanClosedSubScenes();
            }

            if (_scannedDoorId != _doorId || _subScenesWithDoor == null) return;

            if (_subScenesWithDoor.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No closed subscene's file mentions door id {_doorId}. The id may only exist as a stale " +
                    "baked value, or the door is in a scene that is not currently in the hierarchy.",
                    MessageType.Warning);
                return;
            }

            foreach (var (sceneName, subScene) in _subScenesWithDoor)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Subscene '{sceneName}' mentions door {_doorId}");
                    if (subScene != null && GUILayout.Button("Open subscene", GUILayout.Width(110f)))
                    {
                        Unity.Scenes.Editor.SubSceneUtility.EditScene(subScene);
                    }
                }
            }
        }

        /// <summary>
        /// Text-scans the scene files behind closed SubScenes for this door id - both the
        /// prefab-override form ("propertyPath: doorId" + "value: N") and the plain serialized
        /// form ("doorId: N"). A text scan is approximate but instant compared to opening scenes.
        /// </summary>
        private void ScanClosedSubScenes()
        {
            _scannedDoorId = _doorId;
            _subScenesWithDoor = new List<(string, SubScene)>();

            var overridePattern = new Regex($@"propertyPath: doorId\s*\r?\n\s*value: {_doorId}\r?$", RegexOptions.Multiline);
            var plainPattern = new Regex($@"doorId: {_doorId}\r?$", RegexOptions.Multiline);

            foreach (var subScene in FindObjectsByType<SubScene>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (subScene.SceneAsset == null || subScene.IsLoaded) continue;

                var assetPath = AssetDatabase.GetAssetPath(subScene.SceneAsset);
                if (string.IsNullOrEmpty(assetPath)) continue;

                string fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath)) continue;

                var text = File.ReadAllText(fullPath);
                if (overridePattern.IsMatch(text) || plainPattern.IsMatch(text))
                {
                    _subScenesWithDoor.Add((subScene.SceneName, subScene));
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Static helpers - also used by the DoorAuthoring inspector's audio section.

        /// <summary>The pooled/registered AudioSource currently serving a door, or null.</summary>
        public static AudioSource FindAssignedSource(int doorId)
        {
            if (AudioSourcePoolManager.Instance != null &&
                AudioSourcePoolManager.Instance.TryGetPoolSlot(doorId, out _, out var slotSource, out _) &&
                slotSource != null)
            {
                return slotSource;
            }

            if (DoorAudioBridge.Instance != null &&
                DoorAudioBridge.Instance.TryGetRegistration(doorId, out var registered, out _))
            {
                return registered;
            }

            return null;
        }

        /// <summary>
        /// Injects a real DoorAudioEventComponent entity, exactly like DoorAudioSystem does when a
        /// door changes state - so the whole bridge/pool/config chain runs without touching the door.
        /// </summary>
        public static bool TryFireTestEvent(int doorId, AudioEventType eventType, out string error)
        {
            error = null;

            if (!Application.isPlaying)
            {
                error = "[DoorAudioDoctor] Test events only work in play mode.";
                return false;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                error = "[DoorAudioDoctor] No default ECS world - cannot fire a test event.";
                return false;
            }

            Vector3 position = Vector3.zero;
            if (DoorDataBridge.Instance != null && DoorDataBridge.Instance.TryGetDoorInfo(doorId, out var info))
            {
                position = info.audioPosition;
            }

            var entityManager = world.EntityManager;
            var eventEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(eventEntity, new DoorAudioEventComponent
            {
                DoorId = doorId,
                EventType = eventType,
                SoundId = 0,
                ClipName = default,
                Position = position
            });

            Debug.Log($"[DoorAudioDoctor] Fired test {eventType} event for door {doorId}.");
            return true;
        }

        /// <summary>One-paragraph live status of a door's audio chain, for inspector embedding.</summary>
        public static string LiveStatusSummary(int doorId)
        {
            var dataBridge = DoorDataBridge.Instance;
            var pool = AudioSourcePoolManager.Instance;
            var audioBridge = DoorAudioBridge.Instance;

            if (dataBridge == null || pool == null || audioBridge == null)
            {
                return "Door audio systems not running (DoorDataBridge / AudioSourcePoolManager / DoorAudioBridge " +
                       "missing) - run the Setup Validator.";
            }

            if (!dataBridge.TryGetDoorInfo(doorId, out var info))
            {
                return $"No baked door entity with id {doorId} - stale bake or subscene not entity-loaded. " +
                       LoadedIdsSummary(dataBridge);
            }

            string config = info.audioConfig != null ? $"config '{info.audioConfig.name}'" : "NO audio config baked";
            string slot = pool.TryGetPoolSlot(doorId, out int index, out var source, out _)
                ? $"pool slot {index} ('{source?.name}')"
                : "no pool slot assigned";
            string registration = audioBridge.TryGetRegistration(doorId, out _, out _)
                ? "bridge registered"
                : "not registered on the bridge";

            return $"Entity found, {config}, {slot}, {registration}.";
        }

        /// <summary>What door ids ARE loaded - the fastest way to spot a stale-bake id mismatch.</summary>
        public static string LoadedIdsSummary(DoorDataBridge dataBridge, int max = 60)
        {
            var ids = dataBridge.GetAllDoorInfo().Select(d => d.doorId).Distinct().OrderBy(i => i).ToList();
            if (ids.Count == 0)
            {
                return "No door entities are loaded at all - the door subscenes have not been entity-loaded yet.";
            }

            string list = string.Join(", ", ids.Take(max));
            if (ids.Count > max) list += $", ... (+{ids.Count - max} more)";
            return $"{ids.Count} door id(s) currently loaded: {list}";
        }

        /// <summary>Draws one check row; returns the check's result so callers can early-out.</summary>
        private static bool Row(bool ok, string okMessage, string failHint)
        {
            if (ok)
            {
                EditorGUILayout.HelpBox(okMessage, MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox($"{okMessage}\n\n{failHint}", MessageType.Error);
            }

            return ok;
        }
    }
}
#endif
