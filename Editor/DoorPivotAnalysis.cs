#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Heuristic pivot/orientation checks for a DoorAuthoring setup. The gizmos draw arcs from the
    /// panel's CURRENT world pose, but the runtime animates the panel's LocalTransform from
    /// quaternion.identity (rotating) or its authored localPosition (sliding) — so a setup can look
    /// fine in the scene view yet behave erratically in play mode. These checks compare the authored
    /// data against what the animation system will actually do and surface the mismatches.
    /// Heuristics only: every finding needs human confirmation (use the edit-mode test buttons on
    /// the DoorAuthoring inspector) — never auto-fix from here.
    /// </summary>
    public static class DoorPivotAnalysis
    {
        private const float RotationToleranceDegrees = 1f;
        /// <summary>Below this |bounds-centre offset| / half-width ratio the pivot reads as centred
        /// rather than sitting on a hinge edge (a proper hinge pivot has a ratio near 1).</summary>
        private const float CentredPivotRatio = 0.5f;

        public static List<string> Analyze(DoorAuthoring door)
        {
            var warnings = new List<string>();
            if (door == null || door.doorConfig == null) return warnings;

            var config = door.doorConfig;
            var isDouble = config.doorCount == DoorConfig.DoorCountEnum.Double;
            var isRotating = config.doorMovement == DoorConfig.DoorMovementEnum.Rotating;

            if (isRotating)
            {
                CheckSwingDirection(door, warnings);
            }

            if (isDouble)
            {
                CheckPanel(door, door.leftDoorMesh, "Left panel", isRotating, warnings);
                CheckPanel(door, door.rightDoorMesh, "Right panel", isRotating, warnings);
                if (isRotating) CheckPanelsOpposition(door, warnings);
                if (!isRotating && config.slidingStyle == DoorConfig.SlidingStyleEnum.Mirrored &&
                    Mathf.Abs(config.slideOpenOffset.x) < 1e-4f)
                {
                    warnings.Add(
                        "Both sliding panels will slide the SAME way (Mirrored style negates X only, and Slide Open " +
                        "Offset X is 0). Fix: put the travel on the config's Slide Open Offset X.");
                }
            }
            else
            {
                CheckPanel(door, door.doorMesh, "Door panel", isRotating, warnings);
            }

            return warnings;
        }

        private static void CheckPanel(DoorAuthoring door, Transform panel, string label, bool isRotating, List<string> warnings)
        {
            if (panel == null) return;

            // The animation system writes the panel's LocalTransform, i.e. in its PARENT's frame,
            // while the config offsets/angles are authored in the door root's frame. A parent whose
            // rotation differs from the root silently re-aims every slide vector and rotation axis.
            if (panel.parent != null && Quaternion.Angle(panel.parent.rotation, door.transform.rotation) > RotationToleranceDegrees)
            {
                warnings.Add(
                    $"{label} '{panel.name}' animates in a frame rotated {Quaternion.Angle(panel.parent.rotation, door.transform.rotation):F0}° " +
                    $"from the door root (parent '{panel.parent.name}'), so its travel/rotation axis will not match the gizmos. " +
                    "Fix: give the parent the same rotation as the door root.");
            }

            if (!isRotating) return;

            // Runtime treats quaternion.identity as the CLOSED pose. A panel authored with any other
            // local rotation snaps on the first animation frame and swings from the wrong start.
            var closedError = Quaternion.Angle(panel.localRotation, Quaternion.identity);
            if (closedError > RotationToleranceDegrees)
            {
                warnings.Add(
                    $"{label} '{panel.name}' has a closed local rotation of {closedError:F0}° (the runtime expects 0°), " +
                    "so it will SNAP on the first frame and swing from the wrong pose. " +
                    "Fix: bake the rotation into the mesh, or re-parent the panel under a node that carries the rotation.");
            }

            // A rotating panel spins around its own pivot: the pivot must sit on the hinge edge,
            // with the mesh extending to one side. A centred pivot spins the panel in place.
            if (TryGetPanelXProfile(panel, out var centerOffsetX, out var halfExtentX) && halfExtentX > 0.01f)
            {
                var ratio = Mathf.Abs(centerOffsetX) / halfExtentX;
                if (ratio < CentredPivotRatio)
                {
                    warnings.Add(
                        $"{label} '{panel.name}' pivots near its centre (offset {centerOffsetX:F2} m over a {halfExtentX:F2} m half-width), " +
                        "so it will spin in place and clip through the wall. " +
                        "Fix: move the pivot to the hinge edge (re-export, or parent the mesh under an offset hinge node).");
                }
            }
        }

        /// <summary>
        /// The world-space direction into the side the detection system treats as "front"
        /// (DirectionForward = 1): the root's local +Z through its full transform, negated when
        /// Invert Forward Side is in effect. Split at the panel pivots - see DoorSideMath.
        /// </summary>
        public static Vector3 FrontAxis(DoorAuthoring door) => door.FrontDirectionWorld;

        public static string AxisLabel(Vector3 axis)
        {
            axis.Normalize();
            if (Vector3.Dot(axis, Vector3.forward) > 0.999f) return "world +Z";
            if (Vector3.Dot(axis, Vector3.right) > 0.999f) return "world +X";
            if (Vector3.Dot(axis, Vector3.back) > 0.999f) return "world -Z";
            if (Vector3.Dot(axis, Vector3.left) > 0.999f) return "world -X";
            return $"world ({axis.x:F2}, {axis.y:F2}, {axis.z:F2})";
        }

        /// <summary>
        /// Replays the full runtime chain for a player approaching each side of the door —
        /// DoorSideMath picking DirectionForward (root local +Z through the panel pivots, honouring
        /// Invert Forward Side), the animation system's target rotation for that direction, and
        /// the panel's hinge-extent swing — and flags any side where the door would open TOWARD
        /// the player. Forward-style doors (and singles with BothWay, which the runtime treats as
        /// Forward) are expected to open away from whichever side you approach; OneWay and double
        /// BothWay open a fixed way by design and are skipped. When this fires on BOTH sides the
        /// fix is Invert Forward Side (config or per-door override), not the model.
        /// </summary>
        private static void CheckSwingDirection(DoorAuthoring door, List<string> warnings)
        {
            var config = door.doorConfig;
            var isDouble = config.doorCount == DoorConfig.DoorCountEnum.Double;

            if (config.openingStyle == DoorConfig.OpeningStyle.OneWay) return;
            if (isDouble && config.openingStyle == DoorConfig.OpeningStyle.BothWay) return;

            var front = FrontAxis(door);
            var forward = Quaternion.Euler(0f, config.openForwardAngle, 0f);
            var backward = Quaternion.Euler(0f, config.openBackwardAngle, 0f);

            // side, DirectionForward for a player on that side, and the target rotations the
            // animation system would use (doubles invert forward/backward and mirror the right panel).
            foreach (var fromFront in new[] { true, false })
            {
                var playerSide = fromFront ? front : -front;
                var sideName = fromFront ? "front" : "back";

                Quaternion leftTarget, rightTarget;
                if (isDouble)
                {
                    var baseRotation = fromFront ? backward : forward;
                    leftTarget = baseRotation;
                    var euler = baseRotation.eulerAngles;
                    rightTarget = Quaternion.Euler(euler.x, -euler.y, euler.z);
                }
                else
                {
                    leftTarget = rightTarget = fromFront ? forward : backward;
                }

                if (isDouble)
                {
                    CheckPanelSwing(door, door.leftDoorMesh, "Left panel", leftTarget, playerSide, sideName, warnings);
                    CheckPanelSwing(door, door.rightDoorMesh, "Right panel", rightTarget, playerSide, sideName, warnings);
                }
                else
                {
                    CheckPanelSwing(door, door.doorMesh, "Door panel", leftTarget, playerSide, sideName, warnings);
                }
            }
        }

        private static void CheckPanelSwing(DoorAuthoring door, Transform panel, string label,
            Quaternion targetLocalRotation, Vector3 playerSide, string sideName, List<string> warnings)
        {
            if (panel == null) return;
            if (!TryGetPanelXProfile(panel, out var centerOffsetX, out var halfExtentX)) return;
            if (halfExtentX < 0.01f || Mathf.Abs(centerOffsetX) < halfExtentX * 0.25f) return; // centred pivot: separate warning

            // The runtime overwrites the panel's local rotation, so the open-pose free-edge
            // direction is parentRotation * targetRotation * (hinge-extent in panel space).
            var extentLocal = new Vector3(Mathf.Sign(centerOffsetX), 0f, 0f);
            var parentRotation = panel.parent != null ? panel.parent.rotation : Quaternion.identity;
            var openEdgeWorld = parentRotation * targetLocalRotation * extentLocal;

            if (Vector3.Dot(openEdgeWorld, playerSide) > 0.3f)
            {
                warnings.Add(
                    $"{label} '{panel.name}' swings TOWARD a player coming from the {sideName} ({AxisLabel(playerSide.normalized)}). " +
                    "Fix: if both sides are reported, turn on Invert Forward Side (config or per-door override); " +
                    "if only one side, the hinge side is off (or left/right panels are swapped on a double).");
            }
        }

        /// <summary>
        /// Both panels of a rotating double hinge on opposite jambs and extend toward the middle of
        /// the doorway. If both extend the same way along the root's X the meshes are swapped
        /// (left/right assigned backwards) or one panel is flipped.
        /// </summary>
        private static void CheckPanelsOpposition(DoorAuthoring door, List<string> warnings)
        {
            if (door.leftDoorMesh == null || door.rightDoorMesh == null) return;
            if (!TryGetWorldBoundsCenter(door.leftDoorMesh, out var leftCenter)) return;
            if (!TryGetWorldBoundsCenter(door.rightDoorMesh, out var rightCenter)) return;

            var rootRight = door.transform.right;
            var leftDir = Vector3.Dot(leftCenter - door.leftDoorMesh.position, rootRight);
            var rightDir = Vector3.Dot(rightCenter - door.rightDoorMesh.position, rootRight);

            if (Mathf.Abs(leftDir) > 0.01f && Mathf.Abs(rightDir) > 0.01f && leftDir * rightDir > 0f)
            {
                warnings.Add(
                    "Left and right panels both extend the SAME way along the door's X axis instead of toward each other. " +
                    "Fix: swap the Left/Right Door Mesh assignments, or un-flip the mirrored panel.");
            }
        }

        /// <summary>
        /// The panel's extent along its own right axis, measured from its pivot: bounds-centre offset
        /// and half-width. Prefers the BoxCollider (authoring source for the collider pool), falls
        /// back to renderer bounds. Corners are projected so rotated/parented colliders measure right.
        /// </summary>
        private static bool TryGetPanelXProfile(Transform panel, out float centerOffsetX, out float halfExtentX)
        {
            centerOffsetX = 0f;
            halfExtentX = 0f;

            var axis = panel.right;
            var pivot = panel.position;

            var box = panel.GetComponent<BoxCollider>();
            if (box == null) box = panel.GetComponentInChildren<BoxCollider>();
            if (box != null)
            {
                var min = float.MaxValue;
                var max = float.MinValue;
                var half = box.size * 0.5f;
                for (var i = 0; i < 8; i++)
                {
                    var corner = box.center + new Vector3(
                        ((i & 1) == 0 ? -1 : 1) * half.x,
                        ((i & 2) == 0 ? -1 : 1) * half.y,
                        ((i & 4) == 0 ? -1 : 1) * half.z);
                    var projected = Vector3.Dot(box.transform.TransformPoint(corner) - pivot, axis);
                    min = Mathf.Min(min, projected);
                    max = Mathf.Max(max, projected);
                }
                centerOffsetX = (min + max) * 0.5f;
                halfExtentX = (max - min) * 0.5f;
                return true;
            }

            var renderer = panel.GetComponent<Renderer>();
            if (renderer == null) renderer = panel.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                var bounds = renderer.bounds;
                var min = float.MaxValue;
                var max = float.MinValue;
                for (var i = 0; i < 8; i++)
                {
                    var corner = bounds.center + new Vector3(
                        ((i & 1) == 0 ? -1 : 1) * bounds.extents.x,
                        ((i & 2) == 0 ? -1 : 1) * bounds.extents.y,
                        ((i & 4) == 0 ? -1 : 1) * bounds.extents.z);
                    var projected = Vector3.Dot(corner - pivot, axis);
                    min = Mathf.Min(min, projected);
                    max = Mathf.Max(max, projected);
                }
                centerOffsetX = (min + max) * 0.5f;
                halfExtentX = (max - min) * 0.5f;
                return true;
            }

            return false;
        }

        private static bool TryGetWorldBoundsCenter(Transform panel, out Vector3 center)
        {
            center = default;

            var box = panel.GetComponent<BoxCollider>();
            if (box == null) box = panel.GetComponentInChildren<BoxCollider>();
            if (box != null)
            {
                center = box.transform.TransformPoint(box.center);
                return true;
            }

            var renderer = panel.GetComponent<Renderer>();
            if (renderer == null) renderer = panel.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                center = renderer.bounds.center;
                return true;
            }

            return false;
        }
    }
}
#endif
