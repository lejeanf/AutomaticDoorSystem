using Unity.Mathematics;
using UnityEngine;

namespace AutomaticDoorSystem
{
    /// <summary>
    /// Pure math for baking a panel's BoxCollider into the frame the collider pool reproduces at
    /// runtime: a proxy GameObject parked at the panel entity's LocalToWorld position+rotation,
    /// at localScale 1, whose BoxCollider gets the baked center/size verbatim.
    ///
    /// The authored collider often does NOT live on the panel pivot itself - door models keep the
    /// BoxCollider on the mesh child (e.g. 'DoubleDoors_Door_L' under the animated CTRL node), so
    /// its center is child-local. Baking that raw mirrors the collider to the wrong side of the
    /// hinge at runtime (off by a full door width) while edit-mode physics, which uses the child's
    /// real transform, looks perfect. This helper folds the whole child chain - offset, rotation,
    /// scale, mirror - into panel-frame center/size, so the runtime proxy reproduces the authored
    /// box exactly. Static and UnityEngine-only so the baker, the Setup Validator and the tests
    /// share one definition.
    /// </summary>
    public static class DoorColliderBake
    {
        /// <summary>
        /// A baked box more than this factor larger (by volume) than the authored one gets a
        /// warning. Right-angle rotations (a right panel authored as the left one turned 180°)
        /// reproduce exactly and stay silent; only oblique rotations actually bloat the AABB.
        /// </summary>
        public const float BloatWarnRatio = 1.05f;

        /// <summary>
        /// How much bigger (by volume) the baked panel-frame AABB is than the authored world box.
        /// 1 = exact reproduction; 2 = a box rotated 45° around one axis.
        /// </summary>
        public static float BloatRatio(float3 bakedSize, Vector3 authoredWorldSize)
        {
            var authoredVolume = authoredWorldSize.x * authoredWorldSize.y * authoredWorldSize.z;
            if (authoredVolume <= 1e-12f) return 1f;
            return bakedSize.x * bakedSize.y * bakedSize.z / authoredVolume;
        }

        /// <summary>The authored box's world-space center (child transform applied).</summary>
        public static Vector3 WorldCenter(BoxCollider box) => box.transform.TransformPoint(box.center);

        /// <summary>The authored box's world-space size (|lossyScale| applied per axis).</summary>
        public static Vector3 WorldSize(BoxCollider box)
        {
            var s = box.transform.lossyScale;
            return Vector3.Scale(box.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        /// <summary>
        /// Expresses a world-space oriented box in the panel pivot's rotation frame (scale-free,
        /// matching the runtime proxy). When the box is rotated relative to the panel
        /// (<paramref name="relativeAngleDegrees"/> &gt; 0) the returned size is the enclosing
        /// AABB in that frame - exact for aligned boxes, conservative otherwise.
        /// </summary>
        public static void DescribeInPanelFrame(
            Vector3 panelPosition, Quaternion panelRotation,
            Vector3 boxWorldCenter, Quaternion boxWorldRotation, Vector3 boxWorldSize,
            out float3 center, out float3 size, out float relativeAngleDegrees)
        {
            var panelRotationInverse = Quaternion.Inverse(panelRotation);
            center = panelRotationInverse * (boxWorldCenter - panelPosition);

            var relativeRotation = panelRotationInverse * boxWorldRotation;
            relativeAngleDegrees = Quaternion.Angle(Quaternion.identity, relativeRotation);

            var m = Matrix4x4.Rotate(relativeRotation);
            var extents = 0.5f * boxWorldSize;
            size = 2f * new float3(
                Mathf.Abs(m.m00) * extents.x + Mathf.Abs(m.m01) * extents.y + Mathf.Abs(m.m02) * extents.z,
                Mathf.Abs(m.m10) * extents.x + Mathf.Abs(m.m11) * extents.y + Mathf.Abs(m.m12) * extents.z,
                Mathf.Abs(m.m20) * extents.x + Mathf.Abs(m.m21) * extents.y + Mathf.Abs(m.m22) * extents.z);
        }
    }
}
