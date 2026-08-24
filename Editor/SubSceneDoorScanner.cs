#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Answers "which door ids does each subscene AUTHOR, and which ids actually EXIST as baked
    /// entities right now" - the two sides whose difference is a stale bake, a bake-skipped door,
    /// or a leftover entity. Loaded subscenes are read from their live objects; closed ones are
    /// text-scanned (serialized DoorAuthoring blocks plus prefab instances, resolving prefab
    /// default ids and per-instance overrides) so nothing has to be opened to be checked.
    /// </summary>
    public static class SubSceneDoorScanner
    {
        public class SubSceneDoors
        {
            public SubScene SubScene;
            public string ScenePath;
            public bool IsLoaded;
            public List<int> AuthoringIds = new();
            /// <summary>Ids whose authoring GameObject is inactive (only known for loaded
            /// subscenes - text scans cannot cheaply resolve activity).</summary>
            public HashSet<int> InactiveAuthoringIds = new();
        }

        private static readonly Regex PlainDoorId = new(@"^  doorId: (\d+)\r?$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex OverrideDoorId = new(@"propertyPath: doorId\s*\r?\n\s*value: (\d+)\r?$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex SourcePrefabGuid = new(@"m_SourcePrefab: \{fileID: \d+, guid: ([0-9a-f]{32})", RegexOptions.Compiled);

        // Prefab guid -> default door ids of the DoorAuthorings inside it (empty when none).
        private static readonly Dictionary<string, int[]> PrefabDoorIdCache = new();

        /// <summary>Authored door ids for every SubScene component in the open hierarchy.</summary>
        public static List<SubSceneDoors> ScanAll()
        {
            var results = new List<SubSceneDoors>();

            foreach (var subScene in Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (subScene.SceneAsset == null) continue;

                var entry = new SubSceneDoors
                {
                    SubScene = subScene,
                    ScenePath = AssetDatabase.GetAssetPath(subScene.SceneAsset),
                    IsLoaded = subScene.IsLoaded
                };

                if (subScene.IsLoaded && subScene.EditingScene.IsValid())
                {
                    foreach (var root in subScene.EditingScene.GetRootGameObjects())
                    {
                        foreach (var door in root.GetComponentsInChildren<DoorAuthoring>(true))
                        {
                            entry.AuthoringIds.Add(door.doorId);
                            if (!door.gameObject.activeInHierarchy)
                            {
                                entry.InactiveAuthoringIds.Add(door.doorId);
                            }
                        }
                    }
                }
                else
                {
                    entry.AuthoringIds.AddRange(ScanSceneFile(entry.ScenePath));
                }

                results.Add(entry);
            }

            return results;
        }

        /// <summary>
        /// Door ids authored in a scene file without opening it: plain serialized DoorAuthoring
        /// blocks, plus prefab instances of prefabs that contain DoorAuthoring (per-instance
        /// doorId overrides when present, the prefab's default ids otherwise).
        /// </summary>
        public static List<int> ScanSceneFile(string scenePath)
        {
            var ids = new List<int>();
            if (string.IsNullOrEmpty(scenePath)) return ids;

            string fullPath = Path.GetFullPath(scenePath);
            if (!File.Exists(fullPath)) return ids;

            var text = File.ReadAllText(fullPath);

            // Plain (non-prefab) DoorAuthoring components serialized straight into the scene.
            foreach (Match m in PlainDoorId.Matches(text))
            {
                ids.Add(int.Parse(m.Groups[1].Value));
            }

            // Prefab instances: "--- !u!1001" blocks. Within a block, doorId overrides win; doors
            // in the prefab beyond the overridden count contribute their prefab-default id.
            var blocks = text.Split(new[] { "--- !u!1001 " }, System.StringSplitOptions.None);
            for (int i = 1; i < blocks.Length; i++)
            {
                var block = blocks[i];
                var guidMatch = SourcePrefabGuid.Match(block);
                if (!guidMatch.Success) continue;

                var defaults = GetPrefabDoorIds(guidMatch.Groups[1].Value);
                if (defaults.Length == 0) continue;

                var overrides = OverrideDoorId.Matches(block).Select(m2 => int.Parse(m2.Groups[1].Value)).ToList();
                ids.AddRange(overrides);

                // Remaining doors in the prefab keep their default id. (Matching each override to
                // a specific door inside a multi-door prefab is not attempted - door prefabs here
                // carry one DoorAuthoring each, and the count-based fallback stays correct for those.)
                for (int d = overrides.Count; d < defaults.Length; d++)
                {
                    ids.Add(defaults[d]);
                }
            }

            return ids;
        }

        private static int[] GetPrefabDoorIds(string guid)
        {
            if (PrefabDoorIdCache.TryGetValue(guid, out var cached)) return cached;

            int[] result = System.Array.Empty<int>();
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    result = prefab.GetComponentsInChildren<DoorAuthoring>(true).Select(d => d.doorId).ToArray();
                }
            }

            PrefabDoorIdCache[guid] = result;
            return result;
        }

        /// <summary>
        /// Door ids that exist as baked entities in the default world right now - closed subscenes
        /// stream their baked entities into the editor world, open ones are live-baked, so in both
        /// edit and play mode this is "what the runtime would actually see". Doors baked from
        /// inactive GameObjects carry the Disabled tag and land in <paramref name="disabled"/>:
        /// they exist in the bake but are invisible to detection, animation and audio until
        /// something activates them - the correct state for a deliberately disabled door variant,
        /// and the explanation when a "configured" door does nothing at runtime.
        /// </summary>
        public static void BakedDoorIds(out HashSet<int> enabled, out HashSet<int> disabled)
        {
            enabled = new HashSet<int>();
            disabled = new HashSet<int>();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            using (var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DoorComponent>()))
            using (var doors = query.ToComponentDataArray<DoorComponent>(Unity.Collections.Allocator.Temp))
            {
                foreach (var door in doors) enabled.Add(door.DoorId);
            }

            var allDesc = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<DoorComponent>() },
                Options = EntityQueryOptions.IncludeDisabledEntities
            };
            using (var query = world.EntityManager.CreateEntityQuery(allDesc))
            using (var doors = query.ToComponentDataArray<DoorComponent>(Unity.Collections.Allocator.Temp))
            {
                foreach (var door in doors)
                {
                    if (!enabled.Contains(door.DoorId)) disabled.Add(door.DoorId);
                }
            }
        }

        /// <summary>Call when assets may have changed (the cache holds prefab door ids).</summary>
        public static void ClearCache() => PrefabDoorIdCache.Clear();
    }
}
#endif
