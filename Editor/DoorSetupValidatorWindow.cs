#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using AutomaticDoorSystem.Utilities;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Checks that the two halves of a door setup line up: the main scene (pool managers + bridges,
    /// which are plain MonoBehaviours) and the world subscene (DoorAuthoring + the player's
    /// DoorTriggerableAuthoring, which only exist as entities once baked).
    /// Also creates the ScriptableObjects and the pooled AudioSource prefab a project needs.
    /// </summary>
    public class DoorSetupValidatorWindow : EditorWindow
    {
        private enum Severity { Ok, Info, Warning, Error }

        private class Check
        {
            public Severity Severity;
            public string Message;
            public Object Context;
            public string FixLabel;
            public Action Fix;
        }

        private readonly List<(string Title, List<Check> Checks)> _sections = new();
        private Vector2 _scroll;
        private bool _hasRun;

        [MenuItem("Tools/Jeanf/AutomaticDoorSystem/Setup Validator")]
        public static void Open()
        {
            var window = GetWindow<DoorSetupValidatorWindow>("Door Setup");
            window.minSize = new Vector2(460f, 400f);
            window.RunChecks();
            window.Show();
        }

        private void OnFocus()
        {
            if (_hasRun) RunChecks();
        }

        #region GUI

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Re-run checks", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                {
                    RunChecks();
                }

                GUILayout.FlexibleSpace();

                if (_hasRun)
                {
                    int errors = CountOf(Severity.Error);
                    int warnings = CountOf(Severity.Warning);
                    GUILayout.Label(errors == 0 && warnings == 0
                            ? "No problems found"
                            : $"{errors} error(s), {warnings} warning(s)",
                        EditorStyles.miniLabel);
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawAssetCreation();

            foreach (var (title, checks) in _sections)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                foreach (var check in checks)
                {
                    DrawCheck(check);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.EndScrollView();
        }

        private void DrawCheck(Check check)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.HelpBox(check.Message, ToMessageType(check.Severity));

                bool hasContext = check.Context != null;
                bool hasFix = check.Fix != null;
                if (!hasContext && !hasFix) return;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (hasContext && GUILayout.Button("Select", GUILayout.Width(60f)))
                    {
                        Selection.activeObject = check.Context;
                        EditorGUIUtility.PingObject(check.Context);
                    }

                    GUILayout.FlexibleSpace();

                    if (hasFix && GUILayout.Button(check.FixLabel ?? "Fix", GUILayout.Width(220f)))
                    {
                        check.Fix();
                        RunChecks();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawAssetCreation()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Create configuration assets", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Each button asks where to save. Skip the ones you already have set up in this project.",
                    EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("Create DoorConfig assets (all 4 door types)..."))
                {
                    CreateDoorConfigSet();
                    RunChecks();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Create DoorAudioConfiguration asset..."))
                {
                    CreateDoorAudioConfiguration();
                    RunChecks();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Create pooled AudioSource prefab..."))
                {
                    CreateAudioSourcePrefab();
                    RunChecks();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static MessageType ToMessageType(Severity severity)
        {
            return severity switch
            {
                Severity.Error => MessageType.Error,
                Severity.Warning => MessageType.Warning,
                Severity.Info => MessageType.Info,
                _ => MessageType.None
            };
        }

        private int CountOf(Severity severity)
        {
            return _sections.Sum(section => section.Checks.Count(c => c.Severity == severity));
        }

        #endregion

        #region Checks

        private void RunChecks()
        {
            _sections.Clear();
            _hasRun = true;

            var subScenePaths = GetOpenSubScenePaths();
            var doors = FindAll<DoorAuthoring>();

            CheckManagers(subScenePaths);
            CheckPlayer(doors, subScenePaths);
            CheckDoors(doors, subScenePaths);
            CheckSubSceneBakes();
            CheckAudioConfigurations(doors);
            CheckLegacyIdentifiers(doors);

            Repaint();
        }

        private void CheckManagers(HashSet<string> subScenePaths)
        {
            var checks = new List<Check>();

            var colliderPools = FindAll<BoxColliderPoolManager>();
            var audioPools = FindAll<AudioSourcePoolManager>();
            var dataBridges = FindAll<DoorDataBridge>();
            var audioBridges = FindAll<DoorAudioBridge>();

            bool nothingAtAll = colliderPools.Count == 0 && audioPools.Count == 0 &&
                                dataBridges.Count == 0 && audioBridges.Count == 0;

            if (nothingAtAll)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = "No door management components in the loaded scenes. The pooled colliders, " +
                              "pooled AudioSources and the ECS<->MonoBehaviour bridges all live in the main scene.",
                    FixLabel = "Create DoorManagement GameObject",
                    Fix = CreateDoorManagement
                });
            }
            else
            {
                var host = colliderPools.FirstOrDefault()?.gameObject
                           ?? audioPools.FirstOrDefault()?.gameObject
                           ?? dataBridges.FirstOrDefault()?.gameObject
                           ?? audioBridges.FirstOrDefault()?.gameObject;

                AddManagerCheck<BoxColliderPoolManager>(checks, colliderPools, host, subScenePaths,
                    "pooled BoxColliders that follow the animated door panels");
                AddManagerCheck<AudioSourcePoolManager>(checks, audioPools, host, subScenePaths,
                    "pooled AudioSources for door sounds");
                AddManagerCheck<DoorDataBridge>(checks, dataBridges, host, subScenePaths,
                    "door positions and states read from the entities");
                AddManagerCheck<DoorAudioBridge>(checks, audioBridges, host, subScenePaths,
                    "audio events routed from the entities to the pooled AudioSources");
            }

            var audioPool = audioPools.FirstOrDefault();
            if (audioPool != null && audioPool.audioSourcePrefab == null)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Info,
                    Message = "AudioSourcePoolManager has no AudioSource prefab. Plain AudioSources will be " +
                              "created instead, which means no Steam Audio spatialization on door sounds.",
                    Context = audioPool,
                    FixLabel = "Create and assign prefab...",
                    Fix = () => CreateAudioSourcePrefab(audioPool)
                });
            }

            if (Camera.main == null)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Warning,
                    Message = "No enabled camera tagged MainCamera in the loaded scenes. Both pools cull by " +
                              "distance to Camera.main, and the player follower entity tracks it too - without " +
                              "it no door will ever activate. (Fine if your player rig is spawned at runtime.)"
                });
            }

            AddSection("Main scene", checks);
        }

        private void AddManagerCheck<T>(List<Check> checks, List<T> found, GameObject host,
            HashSet<string> subScenePaths, string purpose) where T : MonoBehaviour
        {
            string typeName = typeof(T).Name;

            if (found.Count == 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"{typeName} is missing - no {purpose}.",
                    Context = host,
                    FixLabel = host != null ? $"Add to '{host.name}'" : null,
                    Fix = host != null
                        ? () =>
                        {
                            Undo.AddComponent<T>(host);
                            EditorSceneManager.MarkSceneDirty(host.scene);
                        }
                        : null
                });
                return;
            }

            if (found.Count > 1)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"{found.Count} {typeName} components in the loaded scenes " +
                              $"({string.Join(", ", found.Select(f => f.gameObject.name))}). " +
                              "These are singletons - every instance after the first destroys itself at Awake, " +
                              "so which one survives is load order dependent. Keep exactly one.",
                    Context = found[0]
                });
                return;
            }

            var instance = found[0];
            if (subScenePaths.Contains(instance.gameObject.scene.path))
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"{typeName} on '{instance.gameObject.name}' is inside the subscene " +
                              $"'{instance.gameObject.scene.name}'. MonoBehaviours in a subscene are baked away " +
                              "and never run. Move it to the main scene.",
                    Context = instance
                });
            }
        }

        private void CheckPlayer(List<DoorAuthoring> doors, HashSet<string> subScenePaths)
        {
            var checks = new List<Check>();

            var triggerables = FindAll<DoorTriggerableAuthoring>();
            var playerAuthorings = FindAll<jeanf.scenemanagement.PlayerAuthoring>();

            if (triggerables.Count == 0)
            {
                var candidate = playerAuthorings.FirstOrDefault();
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = "No DoorTriggerableAuthoring in the loaded scenes. Door detection only reacts to " +
                              "entities carrying it, so no door will ever open. It belongs on the PlayerAuthoring " +
                              "object inside your world subscene - that object bakes into the entity that follows " +
                              "Camera.main.",
                    Context = candidate,
                    FixLabel = candidate != null ? $"Add to '{candidate.gameObject.name}'" : null,
                    Fix = candidate != null
                        ? () =>
                        {
                            Undo.AddComponent<DoorTriggerableAuthoring>(candidate.gameObject);
                            EditorSceneManager.MarkSceneDirty(candidate.gameObject.scene);
                        }
                        : null
                });
            }

            foreach (var triggerable in triggerables)
            {
                var go = triggerable.gameObject;

                if (!subScenePaths.Contains(go.scene.path))
                {
                    checks.Add(new Check
                    {
                        Severity = Severity.Error,
                        Message = $"DoorTriggerableAuthoring on '{go.name}' is in '{go.scene.name}', which is not " +
                                  "a subscene. Authoring components only become entities when a subscene is baked, " +
                                  "so this one is inert and doors will not detect the player. Put it on the " +
                                  "PlayerAuthoring object inside the world subscene instead.",
                        Context = triggerable
                    });
                }
                else if (go.GetComponent<jeanf.scenemanagement.PlayerAuthoring>() == null)
                {
                    checks.Add(new Check
                    {
                        Severity = Severity.Info,
                        Message = $"'{go.name}' is door-triggerable but has no PlayerAuthoring, so its entity stays " +
                                  "where it was baked. That is correct for static NPCs or props; the player object " +
                                  "needs PlayerAuthoring to follow Camera.main.",
                        Context = triggerable
                    });
                }

                CheckTriggerableLayer(checks, triggerable, doors);
            }

            if (playerAuthorings.Count == 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Warning,
                    Message = "No PlayerAuthoring found in the open scenes. The entity that represents the player " +
                              "for door detection comes from it. If your world subscene is closed, open it and " +
                              "re-run these checks."
                });
            }

            AddSection("Player", checks);
        }

        private void CheckTriggerableLayer(List<Check> checks, DoorTriggerableAuthoring triggerable, List<DoorAuthoring> doors)
        {
            int layer = triggerable.gameObject.layer;
            int layerBit = 1 << layer;

            var blockingConfigs = doors
                .Select(d => d.doorConfig)
                .Where(c => c != null && (c.canOpenLayerMask.value & layerBit) == 0)
                .Distinct()
                .ToList();

            if (blockingConfigs.Count == 0) return;

            checks.Add(new Check
            {
                Severity = Severity.Error,
                Message = $"'{triggerable.gameObject.name}' is on layer '{LayerMask.LayerToName(layer)}', which is " +
                          $"excluded by Can Open Layer Mask on {blockingConfigs.Count} DoorConfig(s): " +
                          $"{string.Join(", ", blockingConfigs.Select(c => c.name))}. Those doors will ignore it.",
                Context = blockingConfigs[0],
                FixLabel = $"Allow layer '{LayerMask.LayerToName(layer)}'",
                Fix = () =>
                {
                    foreach (var config in blockingConfigs)
                    {
                        Undo.RecordObject(config, "Allow door trigger layer");
                        config.canOpenLayerMask = config.canOpenLayerMask.value | layerBit;
                        EditorUtility.SetDirty(config);
                    }
                    AssetDatabase.SaveAssets();
                }
            });
        }

        private void CheckDoors(List<DoorAuthoring> doors, HashSet<string> subScenePaths)
        {
            var checks = new List<Check>();

            var closedSubScenes = FindAll<SubScene>().Where(s => s.SceneAsset != null && !s.IsLoaded).ToList();
            if (closedSubScenes.Count > 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Info,
                    Message = $"{closedSubScenes.Count} subscene(s) are closed " +
                              $"({string.Join(", ", closedSubScenes.Select(s => s.SceneName))}). " +
                              "Their doors cannot be inspected, so the checks below - including the duplicate " +
                              "Door Id check - only cover what is open.",
                    Context = closedSubScenes[0],
                    FixLabel = "Open all subscenes",
                    Fix = () =>
                    {
                        Unity.Scenes.Editor.SubSceneUtility.EditScene(closedSubScenes.ToArray());
                    }
                });
            }

            if (doors.Count == 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Warning,
                    Message = "No DoorAuthoring components in the open scenes."
                });
                AddSection("Doors", checks);
                return;
            }

            CheckDuplicateDoorIds(checks, doors);

            foreach (var door in doors)
            {
                CheckSingleDoor(checks, door, subScenePaths);
            }

            if (checks.Count == 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Ok,
                    Message = $"{doors.Count} door(s) checked - all correctly configured."
                });
            }

            AddSection("Doors", checks);
        }

        private void CheckDuplicateDoorIds(List<Check> checks, List<DoorAuthoring> doors)
        {
            var duplicateGroups = doors
                .GroupBy(d => d.doorId)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in duplicateGroups)
            {
                var members = group.ToList();
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"Door Id {group.Key} is used by {members.Count} doors " +
                              $"({string.Join(", ", members.Select(d => d.gameObject.name))}). " +
                              "Door Ids key the collider pool, the audio pool and the lock events, so duplicates " +
                              "make all but one of these doors lose their collider and their sound - and older " +
                              "builds threw \"An item with the same key has already been added\" here.",
                    Context = members[0],
                    FixLabel = "Assign unique Door Ids",
                    Fix = () => AssignUniqueDoorIds(members.Skip(1).ToList(), doors)
                });
            }
        }

        private void CheckSingleDoor(List<Check> checks, DoorAuthoring door, HashSet<string> subScenePaths)
        {
            var go = door.gameObject;
            string label = $"Door '{go.name}' (id {door.doorId})";

            if (!subScenePaths.Contains(go.scene.path))
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"{label} is in '{go.scene.name}', which is not a subscene. DoorAuthoring only " +
                              "becomes a door entity when a subscene is baked, so this door will not work.",
                    Context = door
                });
            }

            if (door.doorConfig == null)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"{label} has no DoorConfig - baking skips it entirely. Assign one, or create a set " +
                              "with the button at the top of this window.",
                    Context = door
                });
                return; // every remaining check reads the config
            }

            if (door.doorAudioConfig == null)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Info,
                    Message = $"{label} has no DoorAudioConfiguration and will be silent.",
                    Context = door
                });
            }

            if (door.triggerVolumeObject == null)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Warning,
                    Message = $"{label} has no Trigger Volume Object. Baking falls back to a 3x3x3 box at the door " +
                              "root, and the AudioSource is parked on the root instead of mid-doorway.",
                    Context = door
                });
            }
            else if (door.triggerVolumeObject.GetComponent<DoorTriggerVolumeAuthoring>() == null)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"{label} points at '{door.triggerVolumeObject.name}' as its trigger volume, but that " +
                              "object has no DoorTriggerVolumeAuthoring, so the authored size is ignored.",
                    Context = door.triggerVolumeObject,
                    FixLabel = "Add DoorTriggerVolumeAuthoring",
                    Fix = () =>
                    {
                        Undo.AddComponent<DoorTriggerVolumeAuthoring>(door.triggerVolumeObject.gameObject);
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
                });
            }

            bool isDouble = door.doorConfig.doorCount == DoorConfig.DoorCountEnum.Double;

            if (isDouble)
            {
                if (door.leftDoorMesh == null || door.rightDoorMesh == null)
                {
                    checks.Add(new Check
                    {
                        Severity = Severity.Error,
                        Message = $"{label} uses a Double config but " +
                                  (door.leftDoorMesh == null && door.rightDoorMesh == null
                                      ? "neither panel is assigned."
                                      : $"the {(door.leftDoorMesh == null ? "left" : "right")} panel is not assigned."),
                        Context = door
                    });
                }

                CheckPanelColliders(checks, door);
            }
            else
            {
                if (door.doorMesh == null)
                {
                    checks.Add(new Check
                    {
                        Severity = Severity.Error,
                        Message = $"{label} uses a Single config but Door Mesh is not assigned - nothing will animate.",
                        Context = door
                    });
                }

                CheckPanelColliders(checks, door);
            }
        }

        /// <summary>
        /// Shared with the DoorAuthoring inspector's check section (DoorColliderChecks), so the
        /// validator and the inspector always report the same panel-collider findings.
        /// </summary>
        private void CheckPanelColliders(List<Check> checks, DoorAuthoring door)
        {
            var findings = new List<DoorColliderChecks.Finding>();
            DoorColliderChecks.Analyze(door, findings);
            foreach (var finding in findings)
            {
                checks.Add(new Check
                {
                    Severity = finding.Level == DoorColliderChecks.Level.Warning ? Severity.Warning : Severity.Info,
                    Message = $"Door '{door.gameObject.name}' (id {door.doorId}): {finding.Message}",
                    Context = finding.Context != null ? finding.Context : door
                });
            }
        }

        /// <summary>
        /// Compares what each subscene AUTHORS (door ids, read live or text-scanned from the scene
        /// file - no need to open anything) against the door ids that actually EXIST as baked
        /// entities in the world. A door authored but not baked is exactly the "looks configured
        /// but never makes a sound or moves" failure: stale bake, still importing, or skipped at
        /// bake (missing DoorConfig). A baked id no subscene authors is a stale leftover.
        /// </summary>
        private void CheckSubSceneBakes()
        {
            var checks = new List<Check>();

            SubSceneDoorScanner.ClearCache();
            var scanned = SubSceneDoorScanner.ScanAll();
            if (scanned.Count == 0) return;

            SubSceneDoorScanner.BakedDoorIds(out var baked, out var bakedDisabled);
            int authoredTotal = scanned.Sum(s => s.AuthoringIds.Count);

            if (baked.Count == 0 && bakedDisabled.Count == 0 && authoredTotal > 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Warning,
                    Message = $"{authoredTotal} door(s) authored across {scanned.Count} subscene(s) but no baked " +
                              "door entities exist in the world yet. Subscenes may still be importing - wait for " +
                              "the import to finish and re-run, or open the subscenes to trigger baking."
                });
                AddSection("Subscene bake", checks);
                return;
            }

            // Duplicate ids across ALL subscenes, open or closed - the open-scenes duplicate check
            // above cannot see into closed ones.
            var duplicates = scanned
                .SelectMany(s => s.AuthoringIds.Select(id => (id, s)))
                .GroupBy(t => t.id)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in duplicates)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"Door id {group.Key} is authored {group.Count()} times across subscene(s) " +
                              $"{string.Join(", ", group.Select(t => t.s.SubScene.SceneName).Distinct())}. " +
                              "Ids key the pools and audio registrations, so all but one of these doors go silent.",
                    Context = group.First().s.SubScene
                });
            }

            foreach (var entry in scanned)
            {
                if (entry.AuthoringIds.Count == 0) continue;

                var subScene = entry.SubScene;
                var notEnabled = entry.AuthoringIds.Where(id => !baked.Contains(id)).Distinct().ToList();
                if (notEnabled.Count == 0) continue;

                // Baked, but from an inactive GameObject: the entity exists with a Disabled tag,
                // so the bake is fine - the door is just switched off. Deliberate for door
                // variants; a mistake when the door was supposed to work.
                var disabledOnes = notEnabled.Where(id => bakedDisabled.Contains(id)).ToList();
                if (disabledOnes.Count > 0)
                {
                    bool knownInactive = disabledOnes.All(id => entry.InactiveAuthoringIds.Contains(id));
                    checks.Add(new Check
                    {
                        Severity = knownInactive ? Severity.Info : Severity.Warning,
                        Message = $"Subscene '{subScene.SceneName}': door id(s) {string.Join(", ", disabledOnes)} " +
                                  "are baked but DISABLED (their GameObject is inactive). A disabled door does " +
                                  "not detect, move, or make a sound until something activates it at runtime. " +
                                  "Fine for intentionally switched-off door variants - if one of these should " +
                                  "work, activate its GameObject and save the subscene.",
                        Context = subScene
                    });
                }

                var missing = notEnabled.Except(disabledOnes).ToList();
                if (missing.Count == 0) continue;

                checks.Add(new Check
                {
                    Severity = Severity.Error,
                    Message = $"Subscene '{subScene.SceneName}': door id(s) {string.Join(", ", missing)} are " +
                              "authored but have NO baked entity - those doors will not move or make a sound. " +
                              "Stale bake, import still running, or the door was skipped at bake (missing " +
                              "DoorConfig). Opening the subscene re-bakes it live.",
                    Context = subScene,
                    FixLabel = entry.IsLoaded ? null : $"Open '{subScene.SceneName}'",
                    Fix = entry.IsLoaded
                        ? null
                        : () => Unity.Scenes.Editor.SubSceneUtility.EditScene(subScene)
                });
            }

            var authoredEverywhere = new HashSet<int>(scanned.SelectMany(s => s.AuthoringIds));
            var stale = baked.Concat(bakedDisabled)
                .Where(id => !authoredEverywhere.Contains(id)).Distinct().OrderBy(id => id).ToList();
            if (stale.Count > 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Warning,
                    Message = $"Baked door entity id(s) {string.Join(", ", stale)} exist in the world but no " +
                              "subscene in the hierarchy authors them - stale bakes (an id was changed after " +
                              "baking) or doors from a scene outside this hierarchy. If a door recently got a " +
                              "new id, its old baked entity may still be answering events meant for nobody."
                });
            }

            if (checks.Count == 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Ok,
                    Message = $"{authoredTotal} authored door(s) across {scanned.Count} subscene(s) - every " +
                              "authored id has a baked entity, and no stale baked ids found."
                });
            }

            AddSection("Subscene bake", checks);
        }

        /// <summary>
        /// Sweeps every DoorAudioConfiguration asset in the project (not just those on open doors)
        /// through DoorAudioConfigurationValidator - the same rules DoorAudioBridge lives by at
        /// runtime - so a half-authored or broken config is caught here instead of as a door that
        /// plays its open sound but not its close sound in playmode.
        /// </summary>
        private void CheckAudioConfigurations(List<DoorAuthoring> doors)
        {
            var checks = new List<Check>();
            int total = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:DoorAudioConfiguration"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<DoorAudioConfiguration>(path);
                if (config == null) continue;

                total++;

                var issues = DoorAudioConfigurationValidator.Validate(config);
                if (issues.Count == 0) continue;

                int usedBy = doors.Count(d => d.doorAudioConfig == config);
                string usage = usedBy > 0 ? $" (used by {usedBy} open door(s))" : "";

                foreach (var issue in issues)
                {
                    checks.Add(new Check
                    {
                        Severity = issue.IsError ? Severity.Error : Severity.Warning,
                        Message = $"'{config.name}'{usage}: {issue.Message}",
                        Context = issue.Context != null ? issue.Context : config
                    });
                }
            }

            if (checks.Count == 0 && total > 0)
            {
                checks.Add(new Check
                {
                    Severity = Severity.Ok,
                    Message = $"{total} DoorAudioConfiguration asset(s) checked - all correctly authored."
                });
            }

            AddSection("Audio configurations", checks);
        }

        private void CheckLegacyIdentifiers(List<DoorAuthoring> doors)
        {
            var identifiers = FindAll<DoorIdentifier>();
            if (identifiers.Count == 0) return;

            int migratable = identifiers.Count(i =>
                i.audioConfiguration != null &&
                doors.Any(d => d.doorId == i.doorNumber && d.doorAudioConfig == null));

            AddSection("Legacy DoorIdentifier objects", new List<Check>
            {
                new()
                {
                    Severity = Severity.Warning,
                    Message = $"{identifiers.Count} DoorIdentifier object(s) are still in the loaded scenes. " +
                              "They no longer do anything - audio configuration now lives on DoorAuthoring and is " +
                              "baked into the door entity. " +
                              (migratable > 0
                                  ? $"{migratable} of them hold a configuration that the matching door is missing."
                                  : "Their configurations are already on the matching doors, or no matching door is open."),
                    Context = identifiers[0],
                    FixLabel = "Migrate and delete",
                    Fix = () => MigrateLegacyIdentifiers(identifiers, doors)
                }
            });
        }

        #endregion

        #region Fixes

        private void CreateDoorManagement()
        {
            var go = new GameObject("DoorManagement");
            Undo.RegisterCreatedObjectUndo(go, "Create DoorManagement");

            go.AddComponent<DoorDataBridge>();
            go.AddComponent<DoorAudioBridge>();
            go.AddComponent<BoxColliderPoolManager>();
            go.AddComponent<AudioSourcePoolManager>();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }

        private static void AssignUniqueDoorIds(List<DoorAuthoring> toRenumber, List<DoorAuthoring> allDoors)
        {
            var usedIds = new HashSet<int>(allDoors.Select(d => d.doorId));
            int nextId = usedIds.Count == 0 ? 1 : usedIds.Max() + 1;

            foreach (var door in toRenumber)
            {
                while (usedIds.Contains(nextId)) nextId++;

                Undo.RecordObject(door, "Assign unique Door Id");
                door.doorId = nextId;
                usedIds.Add(nextId);
                EditorUtility.SetDirty(door);
                EditorSceneManager.MarkSceneDirty(door.gameObject.scene);
            }
        }

        private static void MigrateLegacyIdentifiers(List<DoorIdentifier> identifiers, List<DoorAuthoring> doors)
        {
            int migrated = 0;
            int skipped = 0;

            foreach (var identifier in identifiers)
            {
                var door = doors.FirstOrDefault(d => d.doorId == identifier.doorNumber);

                if (identifier.audioConfiguration != null && door != null && door.doorAudioConfig == null)
                {
                    Undo.RecordObject(door, "Migrate door audio configuration");
                    door.doorAudioConfig = identifier.audioConfiguration;
                    EditorUtility.SetDirty(door);
                    EditorSceneManager.MarkSceneDirty(door.gameObject.scene);
                    migrated++;
                }
                else if (identifier.audioConfiguration != null && door == null)
                {
                    // The matching door lives in a closed subscene - deleting now would lose the reference.
                    skipped++;
                    continue;
                }

                var go = identifier.gameObject;
                bool isBareHolder = go.transform.childCount == 0 && go.GetComponents<Component>().Length == 2;

                if (isBareHolder)
                {
                    Undo.DestroyObjectImmediate(go);
                }
                else
                {
                    Undo.DestroyObjectImmediate(identifier);
                }
            }

            string message = $"Moved {migrated} audio configuration(s) onto the matching DoorAuthoring.";
            if (skipped > 0)
            {
                message += $"\n\n{skipped} DoorIdentifier(s) were left alone because no open door matches their " +
                           "Door Number. Open the subscene that holds those doors and run this again.";
            }

            EditorUtility.DisplayDialog("Migrate DoorIdentifier", message, "OK");
        }

        #endregion

        #region Asset creation

        private static void CreateDoorConfigSet()
        {
            string folder = PromptForProjectFolder("Where should the DoorConfig assets go?");
            if (folder == null) return;

            // Telescopic is a style of Sliding Double rather than its own movement type, so the set
            // carries both variants - otherwise the only way to get one is to hand-edit a config.
            var variants = new (string name, DoorConfig.DoorCountEnum count, DoorConfig.DoorMovementEnum movement, DoorConfig.SlidingStyleEnum slidingStyle)[]
            {
                ("DoorConfig_SingleRotating", DoorConfig.DoorCountEnum.Single, DoorConfig.DoorMovementEnum.Rotating, DoorConfig.SlidingStyleEnum.Mirrored),
                ("DoorConfig_DoubleRotating", DoorConfig.DoorCountEnum.Double, DoorConfig.DoorMovementEnum.Rotating, DoorConfig.SlidingStyleEnum.Mirrored),
                ("DoorConfig_SingleSliding", DoorConfig.DoorCountEnum.Single, DoorConfig.DoorMovementEnum.Sliding, DoorConfig.SlidingStyleEnum.Mirrored),
                ("DoorConfig_DoubleSliding", DoorConfig.DoorCountEnum.Double, DoorConfig.DoorMovementEnum.Sliding, DoorConfig.SlidingStyleEnum.Mirrored),
                ("DoorConfig_DoubleSlidingTelescopic", DoorConfig.DoorCountEnum.Double, DoorConfig.DoorMovementEnum.Sliding, DoorConfig.SlidingStyleEnum.Telescopic),
            };

            var created = new List<string>();

            foreach (var (name, count, movement, slidingStyle) in variants)
            {
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset");

                var config = CreateInstance<DoorConfig>();
                config.doorCount = count;
                config.doorMovement = movement;
                config.slidingStyle = slidingStyle;

                AssetDatabase.CreateAsset(config, path);
                created.Add(System.IO.Path.GetFileName(path));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("DoorConfig assets created",
                $"Created in {folder}:\n\n{string.Join("\n", created)}\n\n" +
                "Adjust angles, slide offset, timings and Can Open Layer Mask on each, then assign them to your doors.",
                "OK");

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<DoorConfig>($"{folder}/{created[0]}");
        }

        private static void CreateDoorAudioConfiguration()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create DoorAudioConfiguration",
                "DoorAudioConfig",
                "asset",
                "Where should the door audio configuration be saved?");

            if (string.IsNullOrEmpty(path)) return;

            var config = CreateInstance<DoorAudioConfiguration>();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        private static void CreateAudioSourcePrefab(AudioSourcePoolManager assignTo = null)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create pooled AudioSource prefab",
                "PooledDoorAudioSource",
                "prefab",
                "Where should the pooled AudioSource prefab be saved?");

            if (string.IsNullOrEmpty(path)) return;

            var temp = new GameObject("PooledDoorAudioSource");

            try
            {
                var audioSource = temp.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 1f;
                audioSource.maxDistance = 25f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;

                // Per-source Steam Audio settings are overwritten from the DoorAudioConfiguration at runtime;
                // the component only has to be present for that to have somewhere to land.
                temp.AddComponent<SteamAudio.SteamAudioSource>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);

                if (assignTo != null && prefab != null)
                {
                    Undo.RecordObject(assignTo, "Assign pooled AudioSource prefab");
                    assignTo.audioSourcePrefab = prefab;
                    EditorUtility.SetDirty(assignTo);
                    EditorSceneManager.MarkSceneDirty(assignTo.gameObject.scene);
                }

                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
            finally
            {
                DestroyImmediate(temp);
            }
        }

        private static string PromptForProjectFolder(string title)
        {
            string absolute = EditorUtility.SaveFolderPanel(title, "Assets", "");
            if (string.IsNullOrEmpty(absolute)) return null;

            absolute = absolute.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');

            if (absolute == dataPath) return "Assets";
            if (absolute.StartsWith(dataPath + "/")) return "Assets" + absolute.Substring(dataPath.Length);

            EditorUtility.DisplayDialog("Folder outside the project",
                "Assets have to live under the project's Assets folder. Pick a folder inside it.", "OK");
            return null;
        }

        #endregion

        #region Helpers

        private void AddSection(string title, List<Check> checks)
        {
            if (checks.Count == 0) return;

            // Worst first, so a long list of per-door notes never buries a blocking error.
            checks.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            _sections.Add((title, checks));
        }

        private static List<T> FindAll<T>() where T : Object
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include).ToList();
        }

        /// <summary>Paths of the scenes backing every currently open SubScene.</summary>
        private static HashSet<string> GetOpenSubScenePaths()
        {
            var paths = new HashSet<string>();

            foreach (var subScene in FindAll<SubScene>())
            {
                if (subScene.SceneAsset == null) continue;

                string path = AssetDatabase.GetAssetPath(subScene.SceneAsset);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        #endregion
    }
}
#endif
