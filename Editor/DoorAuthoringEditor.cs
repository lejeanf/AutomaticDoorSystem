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

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // This CustomEditor replaces ValidationInspectorBanner, so draw the banner ourselves:
            // missing [Validation] fields and the IValidatable panel-wiring rule land here.
            ValidationUi.DrawIssuesBanner(target as Component);

            GUI.enabled = false;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            GUI.enabled = true;

            EditorGUILayout.Space();

            // Draws its [Header("Debug Settings")] decorator with it, so no LabelField needed.
            EditorGUILayout.PropertyField(enableDebugProp);

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(doorIdProp);

            EditorGUILayout.Space();

            // Both config assets first: behavior, then audio
            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(doorConfigProp, new GUIContent("Behavior Config"));
            EditorGUILayout.PropertyField(doorAudioConfigProp, new GUIContent("Audio Config"));
            EditorGUILayout.PropertyField(audioAnchorProp, new GUIContent("Audio Anchor (Optional)"));

            // ValidationDrawer already washes the Behavior Config field orange and states why
            // when it is unset — the help boxes below only add the how-to.
            var doorConfig = doorConfigProp.objectReferenceValue as DoorConfig;
            if (doorConfig == null)
            {
                EditorGUILayout.HelpBox(
                    "Door configuration is set via the DoorConfig ScriptableObject. " +
                    "Gizmos in Scene View show door opening visualization.",
                    MessageType.Info);
                EditorGUILayout.HelpBox(
                    "To edit Door Type, Animation, and Behavior settings, select the DoorConfig asset directly.",
                    MessageType.Info);
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

            bool isDouble = doorConfig.doorCount == DoorConfig.DoorCountEnum.Double;

            // Conditionally required (depends on the config's door count), so no [Validation]
            // attribute — the shared helper gives the missing ones the same orange treatment.
            if (isDouble)
            {
                ValidationUi.DrawRequiredField(leftDoorMeshProp,
                    "A Double config needs BOTH panels — without the left panel nothing animates.");
                ValidationUi.DrawRequiredField(rightDoorMeshProp,
                    "A Double config needs BOTH panels — without the right panel nothing animates.");
            }
            else
            {
                ValidationUi.DrawRequiredField(doorMeshProp,
                    "A Single config needs the door mesh — without it nothing animates.");
            }

            EditorGUILayout.PropertyField(triggerVolumeObjectProp);

            EditorGUILayout.Space();

            // Read-only mirror of the DoorConfig asset. Typed fields instead of PropertyField so the
            // asset's [Header] decorators are not drawn again here (they stay on the asset inspector).
            GUI.enabled = false;
            EditorGUILayout.EnumPopup("Door Count", doorConfig.doorCount);
            EditorGUILayout.EnumPopup("Movement Type", doorConfig.doorMovement);
            GUI.enabled = true;

            EditorGUILayout.Space();

            bool isRotating = doorConfig.doorMovement == DoorConfig.DoorMovementEnum.Rotating;

            if (isRotating)
            {
                GUI.enabled = false;
                EditorGUILayout.Slider(new GUIContent("Open Forward Angle"), doorConfig.openForwardAngle, 0f, 180f);
                EditorGUILayout.Slider(new GUIContent("Open Backward Angle"), doorConfig.openBackwardAngle, -180f, 0f);
                EditorGUILayout.EnumPopup("Opening Style", doorConfig.openingStyle);

                if (!isDouble && doorConfig.openingStyle == DoorConfig.OpeningStyle.BothWay)
                {
                    EditorGUILayout.HelpBox("BothWay style only applies to double doors. This door will use Forward behavior.", MessageType.Warning);
                }

                if (doorConfig.openingStyle == DoorConfig.OpeningStyle.OneWay)
                {
                    EditorGUILayout.Vector3Field("One Way Direction", doorConfig.oneWayDirection);
                }

                GUI.enabled = true;
            }
            else
            {
                bool isTelescopic = doorConfig.slidingStyle == DoorConfig.SlidingStyleEnum.Telescopic;

                GUI.enabled = false;
                EditorGUILayout.EnumPopup("Sliding Style", doorConfig.slidingStyle);
                EditorGUILayout.Vector3Field(isTelescopic ? "Slide Open Offset (Left)" : "Slide Open Offset",
                    doorConfig.slideOpenOffset);
                if (isTelescopic)
                {
                    EditorGUILayout.Vector3Field("Right Slide Open Offset", doorConfig.rightSlideOpenOffset);
                    EditorGUILayout.Toggle("Open Right Door Only", doorConfig.openRightDoorOnly);
                }
                GUI.enabled = true;

                if (isTelescopic)
                {
                    if (!isDouble)
                    {
                        EditorGUILayout.HelpBox(
                            "Telescopic sliding only applies to Double doors - this Single door will use " +
                            "standard sliding.",
                            MessageType.Warning);
                    }
                    else if (doorConfig.rightSlideOpenOffset.magnitude <= doorConfig.slideOpenOffset.magnitude)
                    {
                        EditorGUILayout.HelpBox(
                            "Right Slide Open Offset must be LONGER than Slide Open Offset - the right door " +
                            "leads and travels further.",
                            MessageType.Warning);
                    }
                    else if (Vector3.Dot(doorConfig.rightSlideOpenOffset.normalized,
                                 doorConfig.slideOpenOffset.normalized) < 0.999f)
                    {
                        EditorGUILayout.HelpBox(
                            "Both offsets must point in the SAME direction - telescopic panels slide the same " +
                            "way and stack into one pocket.",
                            MessageType.Warning);
                    }
                }
            }

            EditorGUILayout.Space();

            GUI.enabled = false;
            EditorGUILayout.Slider(new GUIContent("Animation Duration"), doorConfig.animationDuration, 0.1f, 5f);
            EditorGUILayout.Slider(new GUIContent("Auto Close Delay"), doorConfig.autoCloseDelay, 0f, 10f);
            EditorGUILayout.MaskField("Can Open Layer Mask",
                UnityEditorInternal.InternalEditorUtility.LayerMaskToConcatenatedLayersMask(doorConfig.canOpenLayerMask),
                UnityEditorInternal.InternalEditorUtility.layers);
            EditorGUILayout.Toggle("Start Locked", doorConfig.startLocked);
            GUI.enabled = true;

            serializedObject.ApplyModifiedProperties();
        }
    }
    #endif
}
