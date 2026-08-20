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
                CheckAxisQuantizationBoundary(door, warnings);
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
                        "Mirrored sliding double with Slide Open Offset X = 0: mirroring negates X only, " +
                        "so both panels will slide the SAME way instead of apart.");
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
                    $"{label} '{panel.name}': its parent '{panel.parent.name}' is rotated " +
                    $"{Quaternion.Angle(panel.parent.rotation, door.transform.rotation):F1}° relative to the door root. " +
                    "Animation happens in the parent's frame, so the travel/rotation axis will not match the gizmos.");
            }

            if (!isRotating) return;

            // Runtime treats quaternion.identity as the CLOSED pose. A panel authored with any other
            // local rotation snaps on the first animation frame and swings from the wrong start.
            var closedError = Quaternion.Angle(panel.localRotation, Quaternion.identity);
            if (closedError > RotationToleranceDegrees)
            {
                warnings.Add(
                    $"{label} '{panel.name}': local rotation is {closedError:F1}° away from identity. " +
                    "The runtime uses identity as the closed pose, so this door will SNAP on its first frame " +
                    "and swing differently than the gizmos suggest. Bake the rotation into the mesh or " +
                    "re-parent the panel so its closed local rotation is identity.");
            }

            // A rotating panel spins around its own pivot: the pivot must sit on the hinge edge,
            // with the mesh extending to one side. A centred pivot spins the panel in place.
            if (TryGetPanelXProfile(panel, out var centerOffsetX, out var halfExtentX) && halfExtentX > 0.01f)
            {
                var ratio = Mathf.Abs(centerOffsetX) / halfExtentX;
                if (ratio < CentredPivotRatio)
                {
                    warnings.Add(
                        $"{label} '{panel.name}': the mesh is roughly centred on the pivot " +
                        $"(offset {centerOffsetX:F2}m over a {halfExtentX:F2}m half-width). A rotating panel " +
                        "should pivot on its hinge edge — a centred pivot makes the door spin in place and " +
                        "clip through the wall.");
                }
            }
        }

        /// <summary>
        /// The world-space side the detection system treats as "front" (DirectionForward = 1):
        /// the baker quantizes the root's world yaw into a cardinal axis (DoorBaker.CalculateDoorAxis)
        /// and CalculateApproachDirection compares player positions along it.
        /// </summary>
        public static Vector3 QuantizedFrontAxis(Transform doorRoot)
        {
            var yaw = ((doorRoot.eulerAngles.y % 360f) + 360f) % 360f;
            if (yaw < 45f || yaw >= 315f) return Vector3.forward;   // DoorAxis.Z
            if (yaw < 135f) return Vector3.right;                    // DoorAxis.X
            if (yaw < 225f) return Vector3.back;                     // DoorAxis.NegZ
            return Vector3.left;                                     // DoorAxis.NegX
        }

        public static string AxisLabel(Vector3 axis)
        {
            if (axis == Vector3.forward) return "world +Z";
            if (axis == Vector3.right) return "world +X";
            if (axis == Vector3.back) return "world -Z";
            return "world -X";
        }

        /// <summary>
        /// A yaw near a 45° bucket edge means a tiny rotation flips which world axis the detection
        /// system reads approach direction along — and with it, forward/backward.
        /// </summary>
        private static void CheckAxisQuantizationBoundary(DoorAuthoring door, List<string> warnings)
        {
            var yaw = ((door.transform.eulerAngles.y % 360f) + 360f) % 360f;
            // Boundaries sit at 45° + 90°k; distance is measured to the nearest one.
            var wrapped = Mathf.Repeat(yaw - 45f, 90f);
            var distanceToBoundary = Mathf.Min(wrapped, 90f - wrapped);
            if (distanceToBoundary < 5f)
            {
                warnings.Add(
                    $"Door root yaw is {yaw:F1}° — within {distanceToBoundary:F1}° of a 45° detection-axis " +
                    "boundary. The runtime quantizes yaw to a cardinal axis to decide which side counts as " +
                    "'front', so doors this close to the boundary can flip forward/backward. Rotate the root " +
                    "to a cardinal-ish angle.");
            }
        }

        /// <summary>
        /// Replays the full runtime chain for a player approaching each side of the door —
        /// CalculateApproachDirection (via the quantized axis) picking DirectionForward, the
        /// animation system's target rotation for that direction, and the panel's hinge-extent
        /// swing — and flags any side where the door would open TOWARD the player. Forward-style
        /// doors (and singles with BothWay, which the runtime treats as Forward) are expected to
        /// open away from whichever side you approach; OneWay and double BothWay open a fixed way
        /// by design and are skipped.
        /// </summary>
        private static void CheckSwingDirection(DoorAuthoring door, List<string> warnings)
        {
            var config = door.doorConfig;
            var isDouble = config.doorCount == DoorConfig.DoorCountEnum.Double;

            if (config.openingStyle == DoorConfig.OpeningStyle.OneWay) return;
            if (isDouble && config.openingStyle == DoorConfig.OpeningStyle.BothWay) return;

            var front = QuantizedFrontAxis(door.transform);
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
                    $"{label} '{panel.name}': approaching from the {sideName} ({AxisLabel(playerSide.normalized)} side), " +
                    "the runtime would swing this panel TOWARD the player instead of away. The panel's hinge side " +
                    "(or a left/right mesh swap on a double) is inverted for this door's orientation — verify with " +
                    "'Open From Front' / 'Open From Back' below.");
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
                    "Left and right panels both extend the SAME way along the door's X axis. Double-door " +
                    "panels should extend toward each other from opposite hinges — the meshes are likely " +
                    "swapped (left/right assigned backwards) or one panel is flipped.");
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
