#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem
{
    [CustomEditor(typeof(DoorIdentifier))]
    public class DoorIdentifierEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            DoorIdentifier identifier = (DoorIdentifier)target;

            // Show door number label
            Handles.Label(identifier.transform.position + Vector3.up * 2.5f,
                $"Door {identifier.doorNumber}",
                new GUIStyle()
                {
                    normal = new GUIStyleState() { textColor = Color.cyan },
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                });
        }
    }
}
#endif
