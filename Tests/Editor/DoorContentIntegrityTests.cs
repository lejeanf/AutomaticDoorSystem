using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// Guards the package's own serialized content (sample scenes and door prefabs) against the
    /// two ways it can rot silently:
    ///  1. A MonoBehaviour class is deleted or renamed while scenes still reference it - Unity
    ///     turns those into "missing script" slots that only show up when someone opens the scene.
    ///     This is what retiring DoorIdentifier risked, so the check now lives in CI instead of
    ///     in someone's memory.
    ///  2. A sample door loses its audio profile, making it silent at runtime with no error.
    /// Scoped to this package's folder on purpose: a consuming project's unrelated missing scripts
    /// are not this package's failure to report.
    /// </summary>
    public class DoorContentIntegrityTests
    {
        /// <summary>Matches every serialized component reference: m_Script: {fileID: n, guid: h, type: 3}.</summary>
        private static readonly Regex ScriptReference =
            new Regex(@"m_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}", RegexOptions.Compiled);

        private static string _packageRoot;

        [OneTimeSetUp]
        public void LocatePackage()
        {
            _packageRoot = FindPackageRoot();
            Assert.IsNotNull(_packageRoot,
                "Could not locate the AutomaticDoorSystem package root (no package.json above DoorAuthoring.cs).");
        }

        [Test]
        public void EverySerializedScriptReferenceResolves()
        {
            var dangling = new List<string>();

            foreach (var file in SerializedFiles())
            {
                var text = File.ReadAllText(file);
                foreach (Match match in ScriptReference.Matches(text))
                {
                    var guid = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                    {
                        dangling.Add($"{ToAssetPath(file)} references missing script guid {guid}");
                    }
                }
            }

            Assert.IsEmpty(dangling,
                "Serialized content points at scripts that no longer exist. Either restore the class or " +
                "strip the leftover components from the listed files:\n  " + string.Join("\n  ", dangling));
        }

        [Test]
        public void EverySampleDoorHasAnAudioConfig()
        {
            var silent = new List<string>();
            var checkedDoors = 0;

            foreach (var file in SerializedFiles())
            {
                if (!file.EndsWith(".prefab")) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ToAssetPath(file));
                if (prefab == null) continue;

                foreach (var door in prefab.GetComponentsInChildren<DoorAuthoring>(true))
                {
                    checkedDoors++;
                    if (door.doorAudioConfig == null)
                    {
                        silent.Add($"{ToAssetPath(file)} -> '{door.name}' (door id {door.doorId})");
                    }
                }
            }

            Assert.Greater(checkedDoors, 0, "No DoorAuthoring found in the package's prefabs - the scan is not looking where it should.");
            Assert.IsEmpty(silent,
                "These sample doors have no DoorAudioConfiguration and will be silent at runtime:\n  " +
                string.Join("\n  ", silent));
        }

        private static IEnumerable<string> SerializedFiles()
        {
            foreach (var pattern in new[] { "*.unity", "*.prefab" })
            {
                foreach (var file in Directory.GetFiles(_packageRoot, pattern, SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }
        }

        /// <summary>Absolute path back to the "Assets/..." or "Packages/..." form AssetDatabase speaks.</summary>
        private static string ToAssetPath(string absolute)
        {
            var normalized = absolute.Replace('\\', '/');
            var projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/') + "/";
            return normalized.StartsWith(projectRoot) ? normalized.Substring(projectRoot.Length) : normalized;
        }

        /// <summary>
        /// Walks up from DoorAuthoring.cs to the folder holding package.json, so the test works
        /// both when the package is embedded under Assets and when it is installed from the registry.
        /// </summary>
        private static string FindPackageRoot()
        {
            foreach (var guid in AssetDatabase.FindAssets("DoorAuthoring t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/DoorAuthoring.cs")) continue;

                var directory = Directory.GetParent(Path.GetFullPath(path));
                while (directory != null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "package.json")))
                    {
                        return directory.FullName;
                    }
                    directory = directory.Parent;
                }
            }

            return null;
        }
    }
}
