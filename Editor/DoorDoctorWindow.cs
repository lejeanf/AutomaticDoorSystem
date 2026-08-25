#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AutomaticDoorSystem.Utilities;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Scenes;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Play-mode diagnostics for the whole door runtime, one tab per concern:
    ///
    /// - Overview: the managers, the baked-world census (duplicate ids, doors invisible to the
    ///   bridge, disabled doors) and the selected door's live state + authoring locator. The
    ///   generic checks every other tab depends on.
    /// - Colliders: the pooled BoxCollider chain, fleet-wide. Surfaces the runtime's two SILENT
    ///   failure paths: an empty bridge cache (it retries forever without logging) and a door
    ///   whose panel buffer cannot be resolved (the pool skips it without a warning).
    /// - Audio: the pooled AudioSource chain for one door, link by link, with test events and
    ///   output-stage bypasses (formerly the Door Audio Doctor).
    ///
    /// The static helpers back the audio section on the DoorAuthoring inspector, so the same
    /// checks are available right on the door.
    /// </summary>
    public class DoorDoctorWindow : EditorWindow
    {
        private enum Tab { Overview, Colliders, Audio }

        private static readonly string[] TabLabels = { "Overview", "Colliders", "Audio" };

        private Tab _tab;
        private int _doorId;
        private Vector2 _scroll;
        private bool _collidersOnlyProblems;

        // Locator scan results (button-triggered - reading every closed subscene file each GUI
        // frame would be far too slow).
        private int _scannedDoorId = int.MinValue;
        private List<(string sceneName, SubScene subScene)> _subScenesWithDoor;

        [MenuItem("Tools/Jeanf/AutomaticDoorSystem/Door Doctor")]
        public static void Open()
        {
            var window = GetWindow<DoorDoctorWindow>("Door Doctor");
            window.minSize = new Vector2(460f, 340f);
            window.Show();
        }

        /// <summary>Inspector entry point ("Doctor..." on DoorAuthoring) - lands on the Audio tab.</summary>
        public static void Open(int doorId)
        {
            Open();
            var window = GetWindow<DoorDoctorWindow>();
            window._doorId = doorId;
            window._tab = Tab.Audio;
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
            // Live values (state, distance, slot assignments) change while playing - keep the view fresh.
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
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _doorId = EditorGUILayout.IntField("Door Id", _doorId);
                if (GUILayout.Button("From selection", GUILayout.Width(110f)))
                {
                    TryAdoptSelection();
                }
            }

            _tab = (Tab)GUILayout.Toolbar((int)_tab, TabLabels);
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Overview: DrawOverviewTab(); break;
                case Tab.Colliders: DrawCollidersTab(); break;
                case Tab.Audio: DrawAudioTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------------------------------
        // Overview tab - managers, world census, selected door state, authoring locator.

        private void DrawOverviewTab()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Live checks need play mode - the pools, the bridges and the door entities only exist there. " +
                    "Static asset checks live in Tools > Jeanf > AutomaticDoorSystem > Setup Validator.",
                    MessageType.Info);
                DrawAuthoringLocator();
                return;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                EditorGUILayout.HelpBox("No DefaultGameObjectInjectionWorld - ECS is not running at all.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Managers", EditorStyles.boldLabel);
            var dataBridge = DoorDataBridge.Instance;
            Row(dataBridge != null, "DoorDataBridge present in the scene",
                "No DoorDataBridge instance - nothing reads the door entities. Run the Setup Validator.");
            Row(BoxColliderPoolManager.Instance != null, "BoxColliderPoolManager present in the scene",
                "No BoxColliderPoolManager - doors never get physical colliders.");
            Row(AudioSourcePoolManager.Instance != null, "AudioSourcePoolManager present in the scene",
                "No AudioSourcePoolManager - no pooled AudioSources exist at all.");
            Row(DoorAudioBridge.Instance != null, "DoorAudioBridge present in the scene",
                "No DoorAudioBridge - audio events are never routed to an AudioSource.");
            Row(Camera.main != null, "Camera.main found (both pools cull by distance to it)",
                "No enabled MainCamera - neither pool ever activates anything.");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baked world census", EditorStyles.boldLabel);
            DrawWorldCensus(world.EntityManager, dataBridge);

            EditorGUILayout.Space();
            if (dataBridge != null && dataBridge.TryGetDoorInfo(_doorId, out var info))
            {
                EditorGUILayout.LabelField($"Door {_doorId}", EditorStyles.boldLabel);
                DrawDoorState(info);
            }
            else
            {
                EditorGUILayout.HelpBox($"Door id {_doorId} is not in the bridge cache - see the census above " +
                                        "for duplicates / invisible doors, or locate its authoring below.", MessageType.Warning);
            }

            DrawAuthoringLocator();
        }

        /// <summary>
        /// Fleet-wide integrity of the baked doors: duplicate ids (all but one lose colliders AND
        /// audio), doors missing one of the components the bridge's query requires (they never
        /// reach the cache - silently), and disabled doors.
        /// </summary>
        private void DrawWorldCensus(EntityManager em, DoorDataBridge dataBridge)
        {
            var idsSeen = new Dictionary<int, int>();
            var invisibleRows = new List<string>();
            int enabledCount;

            using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<DoorComponent>()))
            {
                var entities = query.ToEntityArray(Allocator.Temp);
                enabledCount = entities.Length;
                for (var i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    var id = em.GetComponentData<DoorComponent>(e).DoorId;
                    idsSeen[id] = idsSeen.TryGetValue(id, out var n) ? n + 1 : 1;

                    // The bridge's query needs ALL of these; a door missing one never reaches the
                    // cache - and therefore never gets a collider or audio - without any log.
                    var missing = new List<string>();
                    if (!em.HasComponent<DoorStateComponent>(e)) missing.Add("DoorStateComponent");
                    if (!em.HasComponent<DoorTransformData>(e)) missing.Add("DoorTransformData");
                    if (!em.HasComponent<LocalToWorld>(e)) missing.Add("LocalToWorld");
                    if (!em.HasComponent<DoorTriggerVolume>(e)) missing.Add("DoorTriggerVolume");
                    if (missing.Count > 0) invisibleRows.Add($"Door {id} (e{e.Index}) missing: {string.Join(", ", missing)}");
                }
                entities.Dispose();
            }

            int disabledCount;
            var allDesc = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<DoorComponent>() },
                Options = EntityQueryOptions.IncludeDisabledEntities
            };
            using (var query = em.CreateEntityQuery(allDesc))
            {
                disabledCount = query.CalculateEntityCount() - enabledCount;
            }

            var cacheCount = dataBridge != null ? dataBridge.GetAllDoorInfo()?.Count ?? 0 : 0;
            EditorGUILayout.LabelField(
                $"Door entities: {enabledCount} enabled, {disabledCount} disabled   |   distinct ids: {idsSeen.Count}   |   bridge cache: {cacheCount}");

            if (enabledCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "ZERO enabled door entities in the world. Door subscenes are not streamed in, or their entity " +
                    "bakes are stale/empty.", MessageType.Error);
            }

            foreach (var d in idsSeen.Where(kv => kv.Value > 1))
            {
                EditorGUILayout.HelpBox(
                    $"Door Id {d.Key} exists on {d.Value} baked doors - all but one lose their collider AND their " +
                    "audio. Stale subscene bake or missed renumbering; renumber with the Setup Validator.",
                    MessageType.Error);
            }

            foreach (var row in invisibleRows)
            {
                EditorGUILayout.HelpBox(row + "  -> invisible to DoorDataBridge (silently gets no collider and no audio).",
                    MessageType.Error);
            }

            if (dataBridge != null)
            {
                EditorGUILayout.HelpBox(LoadedIdsSummary(dataBridge), MessageType.Info);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Colliders tab - the pooled BoxCollider chain, fleet-wide.

        private void DrawCollidersTab()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode and walk near the doors you are testing.", MessageType.Info);
                return;
            }

            var dataBridge = DoorDataBridge.Instance;
            var pool = BoxColliderPoolManager.Instance;
            var cam = Camera.main;
            var world = World.DefaultGameObjectInjectionWorld;

            if (!Row(dataBridge != null, "DoorDataBridge present", "No DoorDataBridge - the pool has nothing to read.")) return;
            if (!Row(pool != null, "BoxColliderPoolManager present", "No BoxColliderPoolManager - no pooled colliders exist.")) return;
            if (!Row(cam != null, "Camera.main found (pool culls by distance to it)", "No enabled MainCamera - the pool never activates any collider.")) return;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var camPos = cam.transform.position;
            var slots = ReadColliderSlots(pool);
            var slotsByDoor = slots.Where(s => s.DoorId >= 0)
                .GroupBy(s => s.DoorId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var cache = dataBridge.GetAllDoorInfo();
            if (cache == null || cache.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "The bridge cache is EMPTY - the pool's update returns early without any console message. " +
                    "See the Overview tab's census: either no door entities are loaded, or they are missing " +
                    "components the bridge's query requires.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"{cache.Count} door(s) cached  |  range {pool.cullingDistance:0}m  |  " +
                    $"{pool.GetActiveColliderCount()}/{pool.maxPoolSize} colliders active (a double door uses 2)",
                    EditorStyles.boldLabel);
                _collidersOnlyProblems = GUILayout.Toggle(_collidersOnlyProblems, "problems only", GUILayout.Width(110f));
            }

            foreach (var door in cache.OrderBy(d => Vector3.Distance(d.position, camPos)))
            {
                var dist = Vector3.Distance(door.position, camPos);
                var inRange = dist <= pool.cullingDistance;
                slotsByDoor.TryGetValue(door.doorId, out var doorSlots);
                var activeSlots = doorSlots?.Count(s => s.ColliderEnabled) ?? 0;

                var panelsOk = dataBridge.TryGetDoorPanels(door.doorId, out var panels, out var panelCount);
                var panelText = "no panel buffer";
                if (panelsOk)
                {
                    var withCollider = 0;
                    for (var p = 0; p < panelCount; p++)
                        if (panels[p].hasColliderData) withCollider++;
                    panelText = $"{panelCount} panel(s), {withCollider} with baked collider size";
                }

                // A door root within Minimum Spacing of a nearer door is silently dropped by
                // RemoveSpatialDuplicates - name the conflicting door instead of guessing.
                var spatialConflicts = cache
                    .Where(d => d.doorId != door.doorId &&
                                Vector3.Distance(d.position, door.position) < pool.minimumSpacing)
                    .Select(d => d.doorId).ToList();

                string verdict;
                var type = MessageType.None;
                if (!inRange)
                {
                    verdict = "out of range - no collider expected";
                }
                else if (!panelsOk)
                {
                    verdict = "IN RANGE but the panel buffer cannot be resolved (no DoubleDoorBuffer on the baked " +
                              "entity, or a dead cached entity) - the pool skips this door with NO console message. " +
                              "Re-bake this door's subscene.";
                    type = MessageType.Error;
                }
                else if (activeSlots == 0 && spatialConflicts.Count > 0)
                {
                    verdict = $"in range, panels fine, but NO slot - door root within {pool.minimumSpacing:0.00}m of " +
                              $"door(s) {string.Join(", ", spatialConflicts)}: the pool keeps only the nearest of " +
                              "spatially overlapping doors. Separate the door roots or lower Minimum Spacing.";
                    type = MessageType.Warning;
                }
                else if (activeSlots == 0)
                {
                    verdict = "in range, panels fine, but NO slot - nearer doors may exhaust the pool " +
                              $"({pool.GetActiveColliderCount()}/{pool.maxPoolSize} in use; raise Max Pool Size to test).";
                    type = MessageType.Warning;
                }
                else
                {
                    verdict = $"OK - {activeSlots} pooled collider(s) tracking it" +
                              (door.isLocked ? " (door locked: colliders hold position by design)" : "");
                }

                // Alignment: the assigned colliders must sit ON the rendered panels. Compares each
                // enabled slot's world bounds against the panel subtree's WorldRenderBounds - an
                // independent source of truth, so it catches stale bakes (child-local centers baked
                // raw by package < 2.14.0 land mirrored across the hinge) and any future pose bug.
                var misalignments = new List<string>();
                if (panelsOk && doorSlots != null)
                {
                    foreach (var s in doorSlots)
                    {
                        if (!s.ColliderEnabled || s.Collider == null) continue;
                        if (s.PanelIndex < 0 || s.PanelIndex >= panelCount) continue;
                        if (!TryGetPanelRenderBounds(em, panels[s.PanelIndex].panelEntity, out var meshBounds)) continue;

                        var delta = s.Collider.bounds.center - meshBounds.center;
                        if (delta.magnitude > ColliderAlignmentTolerance)
                            misalignments.Add($"panel {s.PanelIndex} (slot {s.PoolIndex}): collider is {delta.magnitude:0.00}m " +
                                              $"from the rendered mesh (delta {delta})");
                    }
                }
                if (misalignments.Count > 0)
                {
                    verdict = "collider(s) MISALIGNED with the rendered panels - " + string.Join("; ", misalignments) +
                              ". Most likely a stale bake: the panel's BoxCollider lives on a child node and this " +
                              "subscene was baked before the panel-frame fix (package 2.14.0). Re-import (re-bake) " +
                              "the door's subscene; if it persists, run the Setup Validator on the door prefab.";
                    type = MessageType.Error;
                }

                if (_collidersOnlyProblems && type == MessageType.None) continue;

                var marker = door.doorId == _doorId ? "> " : "   ";
                EditorGUILayout.LabelField($"{marker}Door {door.doorId}   {dist:0.0}m   {panelText}");
                if (type == MessageType.None) EditorGUILayout.LabelField("      " + verdict, EditorStyles.miniLabel);
                else EditorGUILayout.HelpBox(verdict, type);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pool slots", EditorStyles.boldLabel);
            foreach (var s in slots)
            {
                var label = s.DoorId < 0
                    ? $"slot {s.PoolIndex:D2}: free"
                    : $"slot {s.PoolIndex:D2}: door {s.DoorId} panel {s.PanelIndex} {(s.ColliderEnabled ? "ENABLED" : "disabled")} at {s.Position}";
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            }
        }

        private struct ColliderSlotInfo
        {
            public int PoolIndex;
            public int DoorId;
            public int PanelIndex;
            public bool ColliderEnabled;
            public Vector3 Position;
            public BoxCollider Collider;
        }

        private static List<ColliderSlotInfo> ReadColliderSlots(BoxColliderPoolManager pool)
        {
            var result = new List<ColliderSlotInfo>(pool.maxPoolSize);
            for (var i = 0; i < pool.maxPoolSize; i++)
            {
                if (!pool.TryGetSlotInfo(i, out var doorId, out var panelIndex, out var collider)) break;
                result.Add(new ColliderSlotInfo
                {
                    PoolIndex = i,
                    DoorId = doorId,
                    PanelIndex = panelIndex,
                    ColliderEnabled = collider != null && collider.enabled,
                    Position = collider != null ? collider.transform.position : Vector3.zero,
                    Collider = collider,
                });
            }
            return result;
        }

        /// <summary>
        /// A pooled collider farther than this from the panel's rendered bounds is reported as
        /// misaligned. Generous enough to swallow the one-physics-tick MovePosition lag of an
        /// animating door; the historical failure this catches (child-local center baked raw,
        /// mirroring the box across the hinge) is a full door width, ~1 m.
        /// </summary>
        private const float ColliderAlignmentTolerance = 0.25f;

        /// <summary>
        /// World AABB of everything the panel entity renders (its own WorldRenderBounds plus all
        /// descendants'). This is ground truth for "where the door visually is", independent of
        /// the collider math being checked.
        /// </summary>
        private static bool TryGetPanelRenderBounds(EntityManager em, Entity panel, out Bounds bounds)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var found = false;
            AccumulateRenderBounds(em, panel, ref min, ref max, ref found, 0);
            bounds = found ? new Bounds((min + max) * 0.5f, max - min) : default;
            return found;
        }

        private static void AccumulateRenderBounds(EntityManager em, Entity entity, ref Vector3 min, ref Vector3 max, ref bool found, int depth)
        {
            if (depth > 8 || entity == Entity.Null || !em.Exists(entity)) return;

            if (em.HasComponent<WorldRenderBounds>(entity))
            {
                var aabb = em.GetComponentData<WorldRenderBounds>(entity).Value;
                min = Vector3.Min(min, aabb.Min);
                max = Vector3.Max(max, aabb.Max);
                found = true;
            }

            if (!em.HasBuffer<Child>(entity)) return;
            // Copy before recursing: nested GetBuffer calls invalidate the handle.
            var buffer = em.GetBuffer<Child>(entity, true);
            var children = new List<Entity>(buffer.Length);
            for (var i = 0; i < buffer.Length; i++) children.Add(buffer[i].Value);
            foreach (var child in children)
                AccumulateRenderBounds(em, child, ref min, ref max, ref found, depth + 1);
        }

        // ---------------------------------------------------------------------------------------
        // Audio tab - the pooled AudioSource chain for one door (formerly the Door Audio Doctor).

        private void DrawAudioTab()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Live checks need play mode - the pool, the bridges and the door entities only exist there. " +
                    "Static asset checks live in Tools > Jeanf > AutomaticDoorSystem > Setup Validator.",
                    MessageType.Info);
                DrawAuthoringLocator();
                return;
            }

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
                CountLiveEntities(_doorId, out int liveEnabled, out int liveDisabled);
                if (liveEnabled >= 2)
                {
                    EditorGUILayout.HelpBox(
                        $"{liveEnabled} live door entities share id {_doorId}! The bridge's id-keyed lookup can " +
                        "only serve one of them - the others get no colliders and no audio. Find and renumber " +
                        "the duplicates (Setup Validator > Subscene bake lists which subscenes author this id).",
                        MessageType.Error);
                }
                else if (liveEnabled == 1)
                {
                    EditorGUILayout.HelpBox(
                        $"A live entity with id {_doorId} DOES exist - the bridge's cached handle was stale " +
                        "(its subscene streamed out and back in). The bridge self-heals on lookup as of 2.12.0; " +
                        "if this message persists, report it.",
                        MessageType.Warning);
                }
                else if (liveDisabled > 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Door {_doorId} IS baked - but DISABLED: its authoring GameObject is inactive, so the " +
                        "entity carries the Disabled tag and is invisible to detection, animation and audio. " +
                        "If this door should work, activate its GameObject in the subscene and save.",
                        MessageType.Warning);
                }
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

            // --- output stage: listener, routing, spatial processing ---
            if (hasSlot && slotSource != null)
            {
                DrawOutputStage(slotSource);
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

        // Original routing of the source while a debug bypass is active, so it can be restored.
        // Two independent toggles: mixer-only and spatial-only, so the silent layer can be
        // pinpointed instead of lumping both into one switch.
        private AudioSource _bypassedSource;
        private UnityEngine.Audio.AudioMixerGroup _bypassedGroup;
        private float _bypassedSpatialBlend;
        private bool _bypassedSpatialize;
        private bool _mixerBypassOn;
        private bool _spatialBypassOn;

        /// <summary>
        /// The stage the chain checks cannot see: everything between a playing AudioSource and the
        /// ear. Listener state (paused / volume 0 silences the whole game), where the source is
        /// routed, and whether spatial processing is involved - plus a bypass toggle that pulls the
        /// source out of the mixer and into 2D. Bypass audible + normal path silent = routing or
        /// 3D processing; both silent = listener or global audio state.
        /// </summary>
        private void DrawOutputStage(AudioSource source)
        {
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None)
                .Where(l => l.isActiveAndEnabled).ToList();
            if (Row(listeners.Count == 1,
                    listeners.Count == 1
                        ? $"One active AudioListener, {Vector3.Distance(listeners[0].transform.position, source.transform.position):0.0}m from the source"
                        : $"{listeners.Count} active AudioListener(s) in the scene",
                    listeners.Count == 0
                        ? "No active AudioListener - nothing in the game is audible."
                        : "Multiple listeners - Unity uses an arbitrary one, so 3D audio positions are unreliable.")
                && listeners.Count == 1)
            {
                Row(!AudioListener.pause && AudioListener.volume > 0.01f,
                    $"Listener state ok (pause {AudioListener.pause}, global volume {AudioListener.volume:0.00})",
                    "AudioListener.pause / AudioListener.volume is silencing the ENTIRE game - some system " +
                    "(pause menu, scenario) set it and did not restore it.");
            }

            // A spatialized voice without a SteamAudioSource has no component pushing per-source
            // parameters into the Steam Audio Spatializer - the classic result is a source that
            // "plays" at full volume yet renders silence.
            if (source.spatialize)
            {
                bool hasSteamSource = source.GetComponent<SteamAudio.SteamAudioSource>() != null;
                Row(hasSteamSource,
                    "Spatialize is ON and a SteamAudioSource feeds the spatializer",
                    "Spatialize is ON but the source has NO SteamAudioSource component - the Steam Audio " +
                    "Spatializer gets no per-source parameters and typically renders SILENCE. Add a " +
                    "SteamAudioSource to the pooled AudioSource prefab, or uncheck Spatialize on it to fall " +
                    "back to plain Unity 3D panning.");
            }

            string groupLabel = source.outputAudioMixerGroup != null
                ? $"mixer group '{source.outputAudioMixerGroup.name}' ({source.outputAudioMixerGroup.audioMixer.name})"
                : "no mixer group (direct out)";
            string spatializer = string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName())
                ? "none" : AudioSettings.GetSpatializerPluginName();
            EditorGUILayout.HelpBox(
                $"Output: {groupLabel}  |  spatialBlend {source.spatialBlend:0.00}  |  " +
                $"spatialize {source.spatialize} (plugin: {spatializer})\n" +
                "Snapshot transitions or an attenuated parent group can silence this route even though the " +
                "source itself is playing at full volume.",
                MessageType.None);

            // The mixer-wide mute: MixerManager parks the whole mixer on the Muted snapshot
            // (Master at -80 dB) during loads and region changes. If the unmute signal is missed,
            // EVERY mixer-routed sound is silent while every per-source check stays green - the
            // exact "all green but silent" case.
            var mixerManager = FindFirstObjectByType<MixerManager>();
            if (mixerManager != null)
            {
                Row(!mixerManager.IsCurrentlyMuted,
                    "MixerManager: mixer is unmuted",
                    "MixerManager reports the mixer is MUTED (snapshot with Master at -80 dB) - every sound " +
                    "routed through the mixer is silent, doors included. The unmute normally fires after " +
                    "region loading completes; if this persists, the load-complete signal was missed " +
                    "(audiosystems >= 0.5.1 unmutes on a fallback timeout).");
            }

            var steamManager = FindFirstObjectByType<SteamAudio.SteamAudioManager>();
            Row(steamManager != null || !source.spatialize,
                steamManager != null
                    ? "SteamAudioManager present (Steam Audio simulation running)"
                    : "No SteamAudioManager needed (source not spatialized)",
                "The source is spatialized through Steam Audio but no SteamAudioManager exists - the " +
                "simulation (occlusion, HRTF) never runs and spatialized voices can render silence.");

            // A source that changed hands invalidates any stored bypass state.
            if (_bypassedSource != source && _bypassedSource != null)
            {
                _mixerBypassOn = false;
                _spatialBypassOn = false;
                _bypassedSource = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool newMixer = GUILayout.Toggle(_mixerBypassOn, "Bypass mixer group", "Button");
                bool newSpatial = GUILayout.Toggle(_spatialBypassOn, "Bypass 3D + spatializer", "Button");

                if (newMixer != _mixerBypassOn)
                {
                    if (newMixer)
                    {
                        CaptureOriginals(source);
                        source.outputAudioMixerGroup = null;
                    }
                    else
                    {
                        source.outputAudioMixerGroup = _bypassedGroup;
                    }
                    _mixerBypassOn = newMixer;
                    ReleaseIfRestored(source);
                }

                if (newSpatial != _spatialBypassOn)
                {
                    if (newSpatial)
                    {
                        CaptureOriginals(source);
                        source.spatialBlend = 0f;
                        source.spatialize = false;
                    }
                    else
                    {
                        source.spatialBlend = _bypassedSpatialBlend;
                        source.spatialize = _bypassedSpatialize;
                    }
                    _spatialBypassOn = newSpatial;
                    ReleaseIfRestored(source);
                }
            }

            if (_mixerBypassOn || _spatialBypassOn)
            {
                EditorGUILayout.HelpBox(
                    (_mixerBypassOn && _spatialBypassOn
                        ? "Both bypasses active - the source plays 2D straight to the output."
                        : _mixerBypassOn
                            ? "Mixer bypass active - 3D/spatializer processing still applies."
                            : "Spatial bypass active (2D) - the mixer route still applies.") +
                    " Fire a test event and toggle one at a time: the bypass that brings the sound back names " +
                    "the layer that eats it. Spatializer-side silence usually means Steam Audio occlusion - the " +
                    "source parks at the trigger-volume centre, INSIDE the doorway geometry, when the door has " +
                    "no Audio Anchor; assign an anchor just outside the door plane or disable occlusion in the " +
                    "DoorAudioConfiguration.",
                    MessageType.Warning);
            }
        }

        private void CaptureOriginals(AudioSource source)
        {
            if (_bypassedSource == source) return;
            _bypassedSource = source;
            _bypassedGroup = source.outputAudioMixerGroup;
            _bypassedSpatialBlend = source.spatialBlend;
            _bypassedSpatialize = source.spatialize;
        }

        private void ReleaseIfRestored(AudioSource source)
        {
            if (!_mixerBypassOn && !_spatialBypassOn && _bypassedSource == source)
            {
                _bypassedSource = null;
            }
        }

        private void DrawDoorState(DoorDataBridge.DoorInfo info)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.EntityManager.HasComponent<DoorStateComponent>(info.rootEntity)) return;

            var state = world.EntityManager.GetComponentData<DoorStateComponent>(info.rootEntity);
            EditorGUILayout.HelpBox(
                $"Live state: {state.CurrentState} (was {state.PreviousState})  |  timer {state.StateTimer:0.00}s  |  " +
                $"entities in trigger: {state.EntitiesInTrigger}  |  locked: {(state.IsLocked == 1 ? "yes" : "no")}\n" +
                "No sound on approach but a working test event means the problem is detection/state, not audio.",
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
                error = "[DoorDoctor] Test events only work in play mode.";
                return false;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                error = "[DoorDoctor] No default ECS world - cannot fire a test event.";
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

            Debug.Log($"[DoorDoctor] Fired test {eventType} event for door {doorId}.");
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
                SubSceneDoorScanner.BakedDoorIds(out _, out var disabledIds);
                if (disabledIds.Contains(doorId))
                {
                    return $"Door {doorId} is baked but DISABLED (inactive GameObject) - activate it in the " +
                           "subscene if it should work.";
                }
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

        /// <summary>Live entity counts for one door id, straight from the world - no caches.</summary>
        private static void CountLiveEntities(int doorId, out int enabled, out int disabled)
        {
            enabled = 0;
            disabled = 0;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            using (var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DoorComponent>()))
            using (var doors = query.ToComponentDataArray<DoorComponent>(Allocator.Temp))
            {
                foreach (var door in doors)
                {
                    if (door.DoorId == doorId) enabled++;
                }
            }

            var allDesc = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<DoorComponent>() },
                Options = EntityQueryOptions.IncludeDisabledEntities
            };
            using (var query = world.EntityManager.CreateEntityQuery(allDesc))
            using (var doors = query.ToComponentDataArray<DoorComponent>(Allocator.Temp))
            {
                int total = 0;
                foreach (var door in doors)
                {
                    if (door.DoorId == doorId) total++;
                }
                disabled = total - enabled;
            }
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
