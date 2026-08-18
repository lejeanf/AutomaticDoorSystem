#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// The single migration notice for this deprecated component. DoorIdentifier carries no
    /// [Obsolete] attribute precisely so Unity does not stack its own deprecation box above
    /// this one — everything the user needs to retire the component is spelled out here.
    /// </summary>
    [CustomEditor(typeof(DoorIdentifier))]
    public class DoorIdentifierEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "DoorIdentifier is deprecated: it does nothing at runtime and will be removed in a " +
                "future release.\n\n" +
                "Audio configuration now lives on DoorAuthoring in the subscene and is baked into the " +
                "door entity, so nothing in the main scene needs to mirror it.\n\n" +
                "To migrate:\n" +
                "1.  Open the subscene containing the matching door.\n" +
                "2.  Open the Setup Validator with the button below, or via " +
                "Tools > AutomaticDoorSystem > Setup Validator.\n" +
                "3.  Run the migration - it copies Audio Configuration onto the DoorAuthoring with the " +
                "same door number, then deletes these objects.",
                MessageType.Warning);

            if (GUILayout.Button("Open Setup Validator"))
            {
                DoorSetupValidatorWindow.Open();
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        private void OnSceneGUI()
        {
            var identifier = (DoorIdentifier)target;

            Handles.Label(identifier.transform.position + Vector3.up * 2.5f,
                $"Door {identifier.doorNumber} (legacy)",
                new GUIStyle
                {
                    normal = new GUIStyleState { textColor = Color.yellow },
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                });
        }
    }
}
#endif
