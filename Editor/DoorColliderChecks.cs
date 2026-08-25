#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// The panel-collider authoring checks, shared by the DoorAuthoring inspector and the Setup
    /// Validator so both surfaces always agree. Mirrors the baker's collider handling
    /// (DoorColliderBake): the bake is agnostic to where the BoxCollider lives - child offsets,
    /// right-angle rotations, scale and mirrored bake-time transforms all fold into the panel
    /// frame - so these checks only flag what genuinely cannot be reproduced (oblique rotations
    /// bake bloated, mirrored panel nodes break the runtime pose) plus the setups that tell on
    /// stale pre-2.14 bakes. Nothing is silent, and findings that can be repaired locally carry
    /// a one-click, undoable <see cref="Finding.Fix"/>.
    /// </summary>
    internal static class DoorColliderChecks
    {
        internal enum Level { Info, Warning }

        internal struct Finding
        {
            public Level Level;
            public string Message;
            public Object Context;
            /// <summary>Optional one-click repair (undoable). Null when only re-authoring can fix it.</summary>
            public System.Action Fix;
            public string FixLabel;
        }

        /// <summary>Runs every panel-collider check for one door, appending to <paramref name="results"/>.</summary>
        internal static void Analyze(DoorAuthoring door, List<Finding> results)
        {
            if (door == null || door.doorConfig == null) return;

            if (door.doorConfig.doorCount == DoorConfig.DoorCountEnum.Double)
            {
                AnalyzePanel(door.leftDoorMesh, "left", results);
                AnalyzePanel(door.rightDoorMesh, "right", results);
            }
            else
            {
                AnalyzePanel(door.doorMesh, "door", results);
            }
        }

        private static void AnalyzePanel(Transform panel, string which, List<Finding> results)
        {
            if (panel == null) return;

            // A mirrored panel node breaks the runtime pose itself: the pooled proxy takes the
            // panel entity's LocalToWorld rotation, which is undefined for a negative-scale
            // matrix - no bake math can compensate. Authoring change required, so no auto-fix.
            var panelScale = panel.lossyScale;
            if (panelScale.x < 0f || panelScale.y < 0f || panelScale.z < 0f)
            {
                results.Add(new Finding
                {
                    Level = Level.Warning,
                    Message = $"The {which} panel '{panel.name}' has a NEGATIVE scale ({panelScale}) - the runtime " +
                              "collider proxy derives its pose from the panel's LocalToWorld rotation, which cannot " +
                              "represent a mirror, so the pooled collider will sit wrong. Re-author the panel without " +
                              "negative scale (mirror via a 180° rotation instead).",
                    Context = panel
                });
            }

            var box = panel.GetComponent<BoxCollider>();
            if (box == null) box = panel.GetComponentInChildren<BoxCollider>();
            if (box == null)
            {
                var fixablePanel = panel;
                var hasRenderers = panel.GetComponentInChildren<Renderer>() != null;
                results.Add(new Finding
                {
                    Level = Level.Warning,
                    Message = $"The {which} panel '{panel.name}' has no BoxCollider. Baking copies its size onto " +
                              "the pooled collider, so without one the panel falls back to a generic 1 x 2.5 x 0.1 box.",
                    Context = panel,
                    Fix = hasRenderers ? () => AddBoxFromRendererBounds(fixablePanel) : (System.Action)null,
                    FixLabel = "Add BoxCollider from mesh bounds"
                });
                return;
            }

            var worldSize = DoorColliderBake.WorldSize(box);
            DoorColliderBake.DescribeInPanelFrame(
                panel.position, panel.rotation,
                DoorColliderBake.WorldCenter(box), box.transform.rotation, worldSize,
                out _, out var bakedSize, out var relativeAngle);

            // Right-angle rotations reproduce exactly and stay silent; only oblique ones bloat.
            if (DoorColliderBake.BloatRatio(bakedSize, worldSize) > DoorColliderBake.BloatWarnRatio)
            {
                var fixablePanel = panel;
                var fixableBox = box;
                results.Add(new Finding
                {
                    Level = Level.Warning,
                    Message = $"The {which} panel's BoxCollider (on '{box.name}') is rotated {relativeAngle:0}° " +
                              "relative to the panel pivot. The pooled runtime collider is an axis-aligned box in " +
                              "the panel's frame, so it bakes as the enclosing (larger) box. The fix moves an " +
                              "equivalent box onto the panel pivot (same enclosing size the bake would use, but " +
                              "then edit mode and runtime match exactly).",
                    Context = box,
                    Fix = () => MoveBoxOntoPanelPivot(fixablePanel, fixableBox),
                    FixLabel = "Move box onto panel pivot"
                });
            }

            if (box.transform != panel)
            {
                var childOffset = panel.InverseTransformPoint(box.transform.position);
                if (childOffset.magnitude > 0.05f)
                {
                    results.Add(new Finding
                    {
                        Level = Level.Info,
                        Message = $"The {which} panel's BoxCollider lives on child '{box.name}' at local offset " +
                                  $"{childOffset}. Handled correctly since package 2.14.0 - but a subscene baked " +
                                  "with an older version places this collider on the wrong side of the hinge. If " +
                                  "the Door Doctor reports a misaligned collider for this door, re-import (re-bake) " +
                                  "its subscene.",
                        Context = box
                    });
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // One-click fixes. Both express the box in the panel node's own local space (position via
        // InverseTransformPoint, size divided by |lossyScale|), so the authored component matches
        // what the bake computes - edit mode and runtime become identical by construction.

        /// <summary>Adds a BoxCollider on the panel pivot enclosing everything the panel renders.</summary>
        private static void AddBoxFromRendererBounds(Transform panel)
        {
            var renderers = panel.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var renderer in renderers)
            {
                var b = renderer.bounds;
                // All 8 corners of the world AABB into panel space - conservative but correct
                // for any panel orientation.
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = panel.InverseTransformPoint(corner);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }

            var box = Undo.AddComponent<BoxCollider>(panel.gameObject);
            box.center = (min + max) * 0.5f;
            box.size = max - min;
        }

        /// <summary>
        /// Replaces an off-pivot (rotated) BoxCollider with an equivalent one on the panel pivot:
        /// the same enclosing box the bake computes, so nothing changes at runtime - but edit-mode
        /// physics now matches it exactly and the warning goes away.
        /// </summary>
        private static void MoveBoxOntoPanelPivot(Transform panel, BoxCollider oldBox)
        {
            var worldSize = DoorColliderBake.WorldSize(oldBox);
            var worldCenter = DoorColliderBake.WorldCenter(oldBox);
            DoorColliderBake.DescribeInPanelFrame(
                panel.position, panel.rotation, worldCenter, oldBox.transform.rotation, worldSize,
                out _, out var bakedSize, out _);

            var lossy = panel.lossyScale;
            var localSize = new Vector3(
                bakedSize.x / Mathf.Max(Mathf.Abs(lossy.x), 1e-6f),
                bakedSize.y / Mathf.Max(Mathf.Abs(lossy.y), 1e-6f),
                bakedSize.z / Mathf.Max(Mathf.Abs(lossy.z), 1e-6f));

            Undo.DestroyObjectImmediate(oldBox);
            var box = Undo.AddComponent<BoxCollider>(panel.gameObject);
            box.center = panel.InverseTransformPoint(worldCenter);
            box.size = localSize;
        }
    }
}
#endif
