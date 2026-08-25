#if UNITY_EDITOR
using System.Collections.Generic;
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

        private List<string> _pivotWarnings;

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

            RefreshPivotWarnings(logToConsole: true);
        }

        private void OnDisable()
        {
            // Never leave a previewed door in a non-authored pose when the inspector goes away.
            if (DoorPreviewDriver.IsPreviewing(target as DoorAuthoring))
            {
                DoorPreviewDriver.EndPreview();
            }
        }

        /// <summary>
        /// Heuristic pivot/orientation findings. Logged once per selection so the door "notifies",
        /// but nothing is ever auto-fixed â€” the inspector asks for human confirmation via the
        /// edit-mode test buttons instead.
        /// </summary>
        private void RefreshPivotWarnings(bool logToConsole)
        {
            var door = target as DoorAuthoring;
            _pivotWarnings = DoorPivotAnalysis.Analyze(door);

            // Panel-collider checks (same helper the Setup Validator runs, so the two surfaces
            // never disagree): missing boxes, obliquely-rotated boxes, stale-bake tells.
            _colliderFindings.Clear();
            if (door != null) DoorColliderChecks.Analyze(door, _colliderFindings);

            if (logToConsole && door != null && _pivotWarnings.Count > 0)
            {
                Debug.LogWarning(
                    $"[DoorAuthoring] '{door.gameObject.name}' pivot/orientation check found {_pivotWarnings.Count} " +
                    $"possible issue(s):\n- {string.Join("\n- ", _pivotWarnings)}\n" +
                    "These are heuristics â€” confirm with the edit-mode Open/Close test buttons on the DoorAuthoring " +
                    "inspector before changing the setup.", door.gameObject);
            }
        }

        private readonly List<DoorColliderChecks.Finding> _colliderFindings = new List<DoorColliderChecks.Finding>();

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
            // when it is unset â€” the help boxes below only add the how-to.
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

            DrawAudioDiagnostics((DoorAuthoring)target);

            EditorGUILayout.Space();

            bool isDouble = doorConfig.doorCount == DoorConfig.DoorCountEnum.Double;

            // Conditionally required (depends on the config's door count), so no [Validation]
            // attribute â€” the shared helper gives the missing ones the same orange treatment.
            if (isDouble)
            {
                ValidationUi.DrawRequiredField(leftDoorMeshProp,
                    "A Double config needs BOTH panels â€” without the left panel nothing animates.");
                ValidationUi.DrawRequiredField(rightDoorMeshProp,
                    "A Double config needs BOTH panels â€” without the right panel nothing animates.");
            }
            else
            {
                ValidationUi.DrawRequiredField(doorMeshProp,
                    "A Single config needs the door mesh â€” without it nothing animates.");
            }

            EditorGUILayout.PropertyField(triggerVolumeObjectProp);

            EditorGUILayout.Space();

            DrawPivotCheckSection();
            DrawEditModeTestSection(doorConfig, isDouble);

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

        /// <summary>
        /// The audio checks, right on the door. Edit mode: the assigned config run through the
        /// same validator as the Setup Validator window. Play mode: the door's live audio-chain
        /// status plus the doctor's test buttons - fire a real open/close audio event and select
        /// the pooled AudioSource serving this door - so a silent door is debugged where it is
        /// configured.
        /// </summary>
        private void DrawAudioDiagnostics(DoorAuthoring door)
        {
            var config = door.doorAudioConfig;
            if (config != null)
            {
                var issues = DoorAudioConfigurationValidator.Validate(config);
                int shown = 0;
                foreach (var issue in issues)
                {
                    if (shown++ == 4)
                    {
                        EditorGUILayout.HelpBox(
                            $"...and {issues.Count - 4} more issue(s) - run Tools > Jeanf > AutomaticDoorSystem > " +
                            "Setup Validator for the full list.", MessageType.Info);
                        break;
                    }
                    EditorGUILayout.HelpBox($"Audio config '{config.name}': {issue.Message}",
                        issue.IsError ? MessageType.Error : MessageType.Warning);
                }
            }

            if (!Application.isPlaying) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live Audio (play mode)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(DoorDoctorWindow.LiveStatusSummary(door.doorId), MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                var source = DoorDoctorWindow.FindAssignedSource(door.doorId);
                using (new EditorGUI.DisabledScope(source == null))
                {
                    if (GUILayout.Button("Select AudioSource"))
                    {
                        Selection.activeGameObject = source.gameObject;
                        EditorGUIUtility.PingObject(source.gameObject);
                    }
                }

                if (GUILayout.Button("Fire Open"))
                {
                    if (!DoorDoctorWindow.TryFireTestEvent(door.doorId, AudioEventType.Open, out var error))
                        Debug.LogError(error);
                }

                if (GUILayout.Button("Fire Close"))
                {
                    if (!DoorDoctorWindow.TryFireTestEvent(door.doorId, AudioEventType.Close, out var error))
                        Debug.LogError(error);
                }

                if (GUILayout.Button("Doctor..."))
                {
                    DoorDoctorWindow.Open(door.doorId);
                }
            }
        }

        private void DrawPivotCheckSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Pivot, Orientation & Collider Check", EditorStyles.boldLabel);
            if (GUILayout.Button("Re-run", GUILayout.Width(60)))
            {
                RefreshPivotWarnings(logToConsole: false);
            }
            EditorGUILayout.EndHorizontal();

            var pivotClean = _pivotWarnings == null || _pivotWarnings.Count == 0;
            var collidersClean = _colliderFindings.Count == 0;

            if (pivotClean && collidersClean)
            {
                EditorGUILayout.HelpBox("No pivot, orientation or panel-collider issues detected.", MessageType.None);
                return;
            }

            if (!pivotClean)
            {
                EditorGUILayout.HelpBox(
                    "Possible pivot/orientation issues (heuristics â€” confirm with the test buttons below " +
                    "before changing anything):\n\nâ€¢ " + string.Join("\n\nâ€¢ ", _pivotWarnings),
                    MessageType.Warning);
            }

            foreach (var finding in _colliderFindings)
            {
                EditorGUILayout.HelpBox(finding.Message,
                    finding.Level == DoorColliderChecks.Level.Warning ? MessageType.Warning : MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (finding.Fix != null && GUILayout.Button(finding.FixLabel, GUILayout.Width(220)))
                    {
                        finding.Fix();
                        RefreshPivotWarnings(logToConsole: false);
                        GUIUtility.ExitGUI(); // findings list just changed - stop drawing stale rows
                    }
                    if (finding.Context != null && GUILayout.Button("Select", GUILayout.Width(80)))
                    {
                        Selection.activeObject = finding.Context;
                        EditorGUIUtility.PingObject(finding.Context);
                    }
                }
            }
        }

        private void DrawEditModeTestSection(DoorConfig doorConfig, bool isDouble)
        {
            EditorGUILayout.LabelField("Edit-Mode Door Test", EditorStyles.boldLabel);

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("The edit-mode test is unavailable in play mode â€” the door systems are live.", MessageType.Info);
                return;
            }

            var door = (DoorAuthoring)target;
            var panelsAssigned = isDouble
                ? door.leftDoorMesh != null && door.rightDoorMesh != null
                : door.doorMesh != null;

            if (!panelsAssigned)
            {
                EditorGUILayout.HelpBox("Assign the door panel(s) above to test the door.", MessageType.Info);
                return;
            }

            // The preview replays DoorAnimationSystem's math on the authored transforms, so an
            // erratic pivot misbehaves here exactly as it would in play mode.
            var isRotating = doorConfig.doorMovement == DoorConfig.DoorMovementEnum.Rotating;
            var directionMatters = isRotating &&
                (doorConfig.openingStyle == DoorConfig.OpeningStyle.Forward ||
                 (isDouble && doorConfig.openingStyle == DoorConfig.OpeningStyle.BothWay));

            EditorGUILayout.BeginHorizontal();
            if (directionMatters)
            {
                // "Front" is the side the DETECTION system treats as DirectionForward = 1 â€” the
                // quantized world axis, exactly what a player walking up in game triggers. Testing
                // both sides here reproduces the roaming behavior, not just the animation.
                if (GUILayout.Button("Open From Front")) DoorPreviewDriver.PreviewOpen(door, directionForward: true);
                if (GUILayout.Button("Open From Back")) DoorPreviewDriver.PreviewOpen(door, directionForward: false);
            }
            else
            {
                if (GUILayout.Button("Open")) DoorPreviewDriver.PreviewOpen(door);
            }
            if (GUILayout.Button("Close")) DoorPreviewDriver.PreviewClose(door);

            GUI.enabled = DoorPreviewDriver.IsPreviewing(door);
            if (GUILayout.Button("Reset")) DoorPreviewDriver.EndPreview();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (directionMatters)
            {
                var front = DoorPivotAnalysis.QuantizedFrontAxis(door.transform);
                EditorGUILayout.LabelField(
                    $"Front = {DoorPivotAnalysis.AxisLabel(front)} (the side the detection system reads as forward)",
                    EditorStyles.miniLabel);
            }

            if (DoorPreviewDriver.IsPreviewing(door))
            {
                EditorGUILayout.HelpBox(
                    "Preview active â€” Reset (or deselecting this door) restores the authored pose. " +
                    "Don't save the scene while the door is posed open.",
                    MessageType.Info);
            }
        }
    }
    #endif
}
