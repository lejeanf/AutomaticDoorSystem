#if UNITY_EDITOR
using jeanf.validationTools;
using UnityEditor;
using UnityEngine;
#endif

namespace AutomaticDoorSystem.Editor
{
    #if UNITY_EDITOR
    [CustomEditor(typeof(DoorAuthoring))]
    public class DoorAuthoringEditor : UnityEditor.Editor
    {
        
        private SerializedProperty doorConfigProp;
        private SerializedProperty doorAudioConfigProp;
        private SerializedProperty audioAnchorProp;
        private SerializedProperty doorMeshProp;
        private SerializedProperty leftDoorMeshProp;
        private SerializedProperty rightDoorMeshProp;
        private SerializedProperty triggerVolumeObjectProp;
        private SerializedProperty doorIdProp;
        private SerializedProperty enableDebugProp;

        private SerializedObject doorConfigSerializedObject;

        private void OnEnable()
        {
            doorConfigProp = serializedObject.FindProperty("doorConfig");
            doorAudioConfigProp = serializedObject.FindProperty("doorAudioConfig");
            audioAnchorProp = serializedObject.FindProperty("audioAnchor");
            doorMeshProp = serializedObject.FindProperty("doorMesh");
            leftDoorMeshProp = serializedObject.FindProperty("leftDoorMesh");
            rightDoorMeshProp = serializedObject.FindProperty("rightDoorMesh");
            triggerVolumeObjectProp = serializedObject.FindProperty("triggerVolumeObject");
            doorIdProp = serializedObject.FindProperty("doorId");
            enableDebugProp = serializedObject.FindProperty("enableDebug");
        }

        private void UpdateDoorConfigSerializedObject()
        {
            if (doorConfigProp.objectReferenceValue != null)
            {
                if (doorConfigSerializedObject == null ||
                    doorConfigSerializedObject.targetObject != doorConfigProp.objectReferenceValue)
                {
                    doorConfigSerializedObject = new SerializedObject(doorConfigProp.objectReferenceValue);
                }
                doorConfigSerializedObject.Update();
            }
            else
            {
                doorConfigSerializedObject = null;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            UpdateDoorConfigSerializedObject();

            // This CustomEditor replaces ValidationInspectorBanner, so draw the banner ourselves:
            // missing [Validation] fields and the IValidatable panel-wiring rule land here.
            ValidationUi.DrawIssuesBanner(target as Component);

            GUI.enabled = false;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            GUI.enabled = true;

            EditorGUILayout.HelpBox(
                "Door configuration is set via the DoorConfig ScriptableObject. " +
                "Gizmos in Scene View show door opening visualization.",
                MessageType.Info);

            EditorGUILayout.Space();

            // Draws its [Header("Debug Settings")] decorator with it, so no LabelField needed.
            EditorGUILayout.PropertyField(enableDebugProp);

            EditorGUILayout.Space();

            // Both config assets first: behavior, then audio
            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(doorConfigProp, new GUIContent("Behavior Config"));
            EditorGUILayout.PropertyField(doorAudioConfigProp, new GUIContent("Audio Config"));
            EditorGUILayout.PropertyField(audioAnchorProp, new GUIContent("Audio Anchor (Optional)"));

            // ValidationDrawer already washes the Behavior Config field orange and states why
            // when it is unset — nothing to add here, just stop before the null config below.
            if (doorConfigProp.objectReferenceValue == null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (doorAudioConfigProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "No audio configuration - this door will be silent. Assign a DoorAudioConfiguration " +
                    "to give it open/close sounds.",
                    MessageType.Info);
            }
            else if (((DoorAuthoring)target).audioAnchor == null && ((DoorAuthoring)target).triggerVolumeObject == null)
            {
                EditorGUILayout.HelpBox(
                    "No Audio Anchor or Trigger Volume Object assigned - the pooled AudioSource will fall " +
                    "back to the door root (usually a hinge edge) instead of the middle of the doorway.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            var doorCountProp = doorConfigSerializedObject.FindProperty("doorCount");
            var doorMovementProp = doorConfigSerializedObject.FindProperty("doorMovement");

            var doorCount = (DoorConfig.DoorCountEnum)doorCountProp.enumValueIndex;
            bool isDouble = doorCount == DoorConfig.DoorCountEnum.Double;

            if (isDouble)
            {
                EditorGUILayout.PropertyField(leftDoorMeshProp);
                EditorGUILayout.PropertyField(rightDoorMeshProp);
            }
            else
            {
                EditorGUILayout.PropertyField(doorMeshProp);
            }

            EditorGUILayout.PropertyField(triggerVolumeObjectProp);

            EditorGUILayout.Space();

            GUI.enabled = false;
            EditorGUILayout.PropertyField(doorCountProp, new GUIContent("Door Count"));
            EditorGUILayout.PropertyField(doorMovementProp, new GUIContent("Movement Type"));
            GUI.enabled = true;

            EditorGUILayout.Space();

            var doorMovement = (DoorConfig.DoorMovementEnum)doorMovementProp.enumValueIndex;
            bool isRotating = doorMovement == DoorConfig.DoorMovementEnum.Rotating;

            if (isRotating)
            {
                GUI.enabled = false;
                var openForwardAngleProp = doorConfigSerializedObject.FindProperty("openForwardAngle");
                var openBackwardAngleProp = doorConfigSerializedObject.FindProperty("openBackwardAngle");
                EditorGUILayout.PropertyField(openForwardAngleProp, new GUIContent("Open Forward Angle"));
                EditorGUILayout.PropertyField(openBackwardAngleProp, new GUIContent("Open Backward Angle"));

                var openingStyleProp = doorConfigSerializedObject.FindProperty("openingStyle");
                EditorGUILayout.PropertyField(openingStyleProp, new GUIContent("Opening Style"));

                if (!isDouble && (DoorConfig.OpeningStyle)openingStyleProp.enumValueIndex == DoorConfig.OpeningStyle.BothWay)
                {
                    EditorGUILayout.HelpBox("BothWay style only applies to double doors. This door will use Forward behavior.", MessageType.Warning);
                }

                if ((DoorConfig.OpeningStyle)openingStyleProp.enumValueIndex == DoorConfig.OpeningStyle.OneWay)
                {
                    var oneWayDirectionProp = doorConfigSerializedObject.FindProperty("oneWayDirection");
                    EditorGUILayout.PropertyField(oneWayDirectionProp, new GUIContent("One Way Direction"));
                }

                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = false;
                var slideOpenOffsetProp = doorConfigSerializedObject.FindProperty("slideOpenOffset");
                EditorGUILayout.PropertyField(slideOpenOffsetProp, new GUIContent("Slide Open Offset"));
                GUI.enabled = true;
            }

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(doorIdProp);

            EditorGUILayout.Space();

            GUI.enabled = false;
            var animationDurationProp = doorConfigSerializedObject.FindProperty("animationDuration");
            var autoCloseDelayProp = doorConfigSerializedObject.FindProperty("autoCloseDelay");
            var canOpenLayerMaskProp = doorConfigSerializedObject.FindProperty("canOpenLayerMask");
            var startLockedProp = doorConfigSerializedObject.FindProperty("startLocked");

            EditorGUILayout.PropertyField(animationDurationProp);
            EditorGUILayout.PropertyField(autoCloseDelayProp);
            EditorGUILayout.PropertyField(canOpenLayerMaskProp);
            EditorGUILayout.PropertyField(startLockedProp);
            GUI.enabled = true;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "To edit Door Type, Animation, and Behavior settings, select the DoorConfig asset directly.",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
            if (doorConfigSerializedObject != null)
            {
                doorConfigSerializedObject.ApplyModifiedProperties();
            }
        }
    }
    #endif
}