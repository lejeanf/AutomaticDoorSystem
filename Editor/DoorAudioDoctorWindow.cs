#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using AutomaticDoorSystem.Utilities;
using Unity.Entities;
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
    /// the culprit.
    /// </summary>
    public class DoorAudioDoctorWindow : EditorWindow
    {
        private int _doorId;
        private Vector2 _scroll;

        [MenuItem("Tools/Jeanf/AutomaticDoorSystem/Door Audio Doctor")]
        public static void Open()
        {
            var window = GetWindow<DoorAudioDoctorWindow>("Door Audio Doctor");
            window.minSize = new Vector2(420f, 320f);
            window.Show();
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
                    "Static asset checks live in Tools > AutomaticDoorSystem > Setup Validator.",
                    MessageType.Info);
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
                    $"No baked door entity has id {_doorId}. Either the subscene is not loaded yet, the id is " +
                    "different at runtime, or baking skipped the door (missing DoorConfig)."))
                return;

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
            Row(hasSlot, hasSlot
                    ? $"Pool slot assigned: slot {slot} -> '{slotSource?.name}'{(playingFlag ? " (mid-playback)" : "")}"
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

            DrawActions(hasSlot ? slotSource : registeredSource, info);
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

        private void DrawActions(AudioSource source, DoorDataBridge.DoorInfo info)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions (play mode)", EditorStyles.boldLabel);

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
                    FireTestEvent(AudioEventType.Open, info);
                }

                if (GUILayout.Button("Fire Close event"))
                {
                    FireTestEvent(AudioEventType.Close, info);
                }
            }

            EditorGUILayout.LabelField(
                "Test events go through the real DoorAudioBridge path (event entity -> registered source -> config).",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// Injects a real DoorAudioEventComponent entity, exactly like DoorAudioSystem does when a
        /// door changes state - so the whole bridge/pool/config chain runs without touching the door.
        /// </summary>
        private void FireTestEvent(AudioEventType eventType, DoorDataBridge.DoorInfo info)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[DoorAudioDoctor] No default ECS world - cannot fire a test event.");
                return;
            }

            var entityManager = world.EntityManager;
            var eventEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(eventEntity, new DoorAudioEventComponent
            {
                DoorId = _doorId,
                EventType = eventType,
                SoundId = 0,
                ClipName = default,
                Position = info.audioPosition
            });

            Debug.Log($"[DoorAudioDoctor] Fired test {eventType} event for door {_doorId}.");
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
