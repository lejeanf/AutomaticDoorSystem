#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Edit-mode open/close preview for a DoorAuthoring, driven from its inspector. Replicates the
    /// EXACT math DoorAnimationSystem runs on the baked entities — including the parts that make a
    /// mis-pivoted door erratic (rotating doors treat quaternion.identity as the closed pose, slide
    /// offsets apply in the parent's frame) — so what you see here is what play mode will do, not
    /// what the gizmos suggest. One door previews at a time; the authored pose is restored on
    /// Reset, on deselection, and before play mode or a domain reload.
    /// </summary>
    public static class DoorPreviewDriver
    {
        private class PanelSnapshot
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public bool isLeft;
        }

        private static DoorAuthoring _door;
        private static readonly List<PanelSnapshot> _panels = new List<PanelSnapshot>();
        private static double _animationStartTime;
        private static bool _animating;
        private static bool _isOpening;
        private static bool _directionForward = true;
        private static bool _poseIsOpen;

        static DoorPreviewDriver()
        {
            EditorApplication.playModeStateChanged += _ => EndPreview();
            AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
            // The swing-arc gizmos must stay put while a panel is being previewed: hand them the
            // authored (closed) rotation instead of the live one.
            DoorAuthoring.AuthoredWorldRotationProvider = AuthoredWorldRotation;
            DoorAuthoring.AuthoredWorldPositionProvider = AuthoredWorldPosition;
        }

        private static Vector3? AuthoredWorldPosition(Transform panel)
        {
            foreach (var snapshot in _panels)
            {
                if (snapshot.transform != panel) continue;
                return panel.parent != null
                    ? panel.parent.TransformPoint(snapshot.localPosition)
                    : snapshot.localPosition;
            }
            return null;
        }

        private static Quaternion? AuthoredWorldRotation(Transform panel)
        {
            foreach (var snapshot in _panels)
            {
                if (snapshot.transform != panel) continue;
                var parentRotation = panel.parent != null ? panel.parent.rotation : Quaternion.identity;
                return parentRotation * snapshot.localRotation;
            }
            return null;
        }

        public static bool IsPreviewing(DoorAuthoring door) => _door == door && _door != null;
        public static bool PoseIsOpen => _poseIsOpen;

        public static void PreviewOpen(DoorAuthoring door, bool directionForward = true)
        {
            if (Application.isPlaying || door == null || door.doorConfig == null) return;
            BeginSession(door);
            _directionForward = directionForward;
            _isOpening = true;
            _animationStartTime = EditorApplication.timeSinceStartup;
            _animating = true;
        }

        public static void PreviewClose(DoorAuthoring door)
        {
            if (Application.isPlaying || door == null || door.doorConfig == null) return;
            // Closing lerps from the OPEN pose (as the runtime does), so from a closed pose it
            // would snap the door open first — skip instead.
            if (IsPreviewing(door) && !_poseIsOpen && !_animating) return;
            BeginSession(door);
            _isOpening = false;
            _animationStartTime = EditorApplication.timeSinceStartup;
            _animating = true;
        }

        /// <summary>Restores the authored pose and ends the session.</summary>
        public static void EndPreview()
        {
            EditorApplication.update -= OnEditorUpdate;
            _animating = false;
            _poseIsOpen = false;

            foreach (var panel in _panels)
            {
                if (panel.transform == null) continue;
                panel.transform.localPosition = panel.localPosition;
                panel.transform.localRotation = panel.localRotation;
            }
            _panels.Clear();
            _door = null;
            SceneView.RepaintAll();
        }

        private static void BeginSession(DoorAuthoring door)
        {
            if (_door == door) return;
            EndPreview();

            _door = door;
            var config = door.doorConfig;
            var isDouble = config.doorCount == DoorConfig.DoorCountEnum.Double;

            if (isDouble)
            {
                Snapshot(door.leftDoorMesh, true);
                Snapshot(door.rightDoorMesh, false);
            }
            else
            {
                Snapshot(door.doorMesh, false);
            }

            foreach (var panel in _panels)
            {
                Undo.RegisterCompleteObjectUndo(panel.transform, "Door Preview");
            }

            EditorApplication.update += OnEditorUpdate;
        }

        private static void Snapshot(Transform panel, bool isLeft)
        {
            if (panel == null) return;
            _panels.Add(new PanelSnapshot
            {
                transform = panel,
                localPosition = panel.localPosition,
                localRotation = panel.localRotation,
                isLeft = isLeft
            });
        }

        private static void OnEditorUpdate()
        {
            if (!_animating || _door == null || _door.doorConfig == null)
            {
                if (_door == null) EndPreview();
                return;
            }

            var duration = Mathf.Max(_door.doorConfig.animationDuration, 0.01f);
            var progress = Mathf.Clamp01((float)(EditorApplication.timeSinceStartup - _animationStartTime) / duration);
            // Same cosine ease as DoorAnimationSystem.CalculateEasedProgress.
            var eased = -(Mathf.Cos(Mathf.PI * progress) - 1f) / 2f;

            ApplyPose(eased);

            if (progress >= 1f)
            {
                _animating = false;
                _poseIsOpen = _isOpening;
                // A finished Close stays where the runtime closes to (identity local rotation /
                // authored slide position). On a door whose authored rotation is off identity that
                // differs from the authored pose - restoring it here read as "the door jumped open".
                // The authored pose comes back when the door is deselected.
            }
            SceneView.RepaintAll();
        }

        private static void ApplyPose(float easedProgress)
        {
            var config = _door.doorConfig;
            var isDouble = config.doorCount == DoorConfig.DoorCountEnum.Double;
            var isRotating = config.doorMovement == DoorConfig.DoorMovementEnum.Rotating;

            if (isRotating)
            {
                if (isDouble) ApplyRotatingDouble(config, easedProgress);
                else ApplyRotatingSingle(config, easedProgress);
            }
            else
            {
                if (isDouble && config.slidingStyle == DoorConfig.SlidingStyleEnum.Telescopic)
                    ApplyTelescopicSliding(config, easedProgress);
                else
                    ApplySliding(config, isDouble, easedProgress);
            }
        }

        // Mirrors DoorAnimationSystem.GetTargetRotation with DirectionForward from the preview toggle.
        private static void ApplyRotatingSingle(DoorConfig config, float easedProgress)
        {
            var forward = Quaternion.Euler(0f, config.openForwardAngle, 0f);
            var backward = Quaternion.Euler(0f, config.openBackwardAngle, 0f);

            Quaternion target;
            switch (config.openingStyle)
            {
                case DoorConfig.OpeningStyle.OneWay:
                    var oneWayDir = config.oneWayDirection.sqrMagnitude > 0.0001f ? config.oneWayDirection.normalized : Vector3.forward;
                    target = oneWayDir.z >= 0 ? backward : forward;
                    break;
                default: // Forward and BothWay both pick by approach direction at runtime.
                    target = _directionForward ? forward : backward;
                    break;
            }

            foreach (var panel in _panels)
            {
                if (panel.transform == null) continue;
                panel.transform.localRotation = _isOpening
                    ? Quaternion.Slerp(Quaternion.identity, target, easedProgress)
                    : Quaternion.Slerp(target, Quaternion.identity, easedProgress);
            }
        }

        // Mirrors DoorAnimationSystem.GetDoubleRotations (note the runtime's forward/backward inversion
        // for the Forward style, and the mirrored right panel).
        private static void ApplyRotatingDouble(DoorConfig config, float easedProgress)
        {
            var forward = Quaternion.Euler(0f, config.openForwardAngle, 0f);
            var backward = Quaternion.Euler(0f, config.openBackwardAngle, 0f);

            Quaternion leftTarget;
            Quaternion rightTarget;
            switch (config.openingStyle)
            {
                case DoorConfig.OpeningStyle.BothWay:
                    leftTarget = forward;
                    rightTarget = forward;
                    break;
                case DoorConfig.OpeningStyle.OneWay:
                    var oneWayDir = config.oneWayDirection.sqrMagnitude > 0.0001f ? config.oneWayDirection.normalized : Vector3.forward;
                    leftTarget = oneWayDir.z >= 0 ? backward : forward;
                    rightTarget = Mirror(leftTarget);
                    break;
                default: // Forward: runtime uses the BACKWARD rotation when approaching forward.
                    leftTarget = _directionForward ? backward : forward;
                    rightTarget = Mirror(leftTarget);
                    break;
            }

            foreach (var panel in _panels)
            {
                if (panel.transform == null) continue;
                var target = panel.isLeft ? leftTarget : rightTarget;
                panel.transform.localRotation = _isOpening
                    ? Quaternion.Slerp(Quaternion.identity, target, easedProgress)
                    : Quaternion.Slerp(target, Quaternion.identity, easedProgress);
            }
        }

        private static Quaternion Mirror(Quaternion rotation)
        {
            var euler = rotation.eulerAngles;
            return Quaternion.Euler(euler.x, -euler.y, euler.z);
        }

        private static void ApplySliding(DoorConfig config, bool isDouble, float easedProgress)
        {
            foreach (var panel in _panels)
            {
                if (panel.transform == null) continue;

                var direction = !isDouble ? 1f : (panel.isLeft ? 1f : -1f);
                var offset = new Vector3(
                    config.slideOpenOffset.x * direction,
                    config.slideOpenOffset.y,
                    config.slideOpenOffset.z);

                var closedPos = panel.localPosition;
                var openPos = closedPos + offset;
                panel.transform.localPosition = _isOpening
                    ? Vector3.Lerp(closedPos, openPos, easedProgress)
                    : Vector3.Lerp(openPos, closedPos, easedProgress);
            }
        }

        private static void ApplyTelescopicSliding(DoorConfig config, float easedProgress)
        {
            var leftSpan = config.slideOpenOffset.magnitude;
            var rightSpan = config.rightSlideOpenOffset.magnitude;

            DoorAnimationSystem.ComputeTelescopicTravel(
                rightSpan, leftSpan, config.openRightDoorOnly, easedProgress, _isOpening,
                out var rightTravel, out var leftTravel);

            foreach (var panel in _panels)
            {
                if (panel.transform == null) continue;

                Vector3 direction;
                float travel;
                if (panel.isLeft)
                {
                    direction = leftSpan > 1e-4f ? config.slideOpenOffset / leftSpan : Vector3.zero;
                    travel = leftTravel;
                }
                else
                {
                    direction = rightSpan > 1e-4f ? config.rightSlideOpenOffset / rightSpan : Vector3.zero;
                    travel = rightTravel;
                }

                panel.transform.localPosition = panel.localPosition + direction * travel;
            }
        }
    }
}
#endif
