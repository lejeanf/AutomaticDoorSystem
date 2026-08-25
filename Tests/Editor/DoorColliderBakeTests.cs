using NUnit.Framework;
using UnityEngine;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// The panel-frame collider bake (DoorColliderBake.DescribeInPanelFrame) - the math that keeps
    /// the pooled runtime collider on the authored box even when the BoxCollider lives on an
    /// offset/rotated/scaled child of the animated panel pivot (the DoubleDoors_2024 layout:
    /// collider on the mesh grand-child, pivot on the CTRL hinge node).
    /// </summary>
    public class DoorColliderBakeTests
    {
        private const float Epsilon = 1e-4f;

        private static void AssertNear(Vector3 expected, Vector3 actual, string label)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Epsilon),
                $"{label}: expected {expected}, got {actual}");
        }

        [Test]
        public void ColliderOnPanelItself_BakesCenterAndSizeVerbatim()
        {
            var panelPosition = new Vector3(3f, 0f, -2f);
            var panelRotation = Quaternion.Euler(0f, 90f, 0f);
            var authoredCenter = new Vector3(0.5f, 1.25f, 0f);
            var authoredSize = new Vector3(1f, 2.5f, 0.1f);

            // Box directly on the panel: world pose = panel pose applied to authored center.
            var worldCenter = panelPosition + panelRotation * authoredCenter;

            DoorColliderBake.DescribeInPanelFrame(
                panelPosition, panelRotation, worldCenter, panelRotation, authoredSize,
                out var center, out var size, out var angle);

            AssertNear(authoredCenter, center, "center");
            AssertNear(authoredSize, size, "size");
            Assert.That(angle, Is.LessThan(0.01f));
        }

        [Test]
        public void ColliderOnOffsetChild_FoldsChildOffsetIntoCenter()
        {
            // The regression this whole class exists for: mesh child at local (-0.951, 0, -0.031)
            // under the hinge pivot, box center (+0.473, 1.10, 0) child-local. Baking the center
            // raw put the runtime box a full door width across the hinge.
            var panelPosition = new Vector3(11f, 0f, 1.5f);
            var panelRotation = Quaternion.identity;
            var childOffset = new Vector3(-0.951f, 0f, -0.031f);
            var boxCenterInChild = new Vector3(0.473f, 1.1f, 0f);
            var authoredSize = new Vector3(0.944f, 2.194f, 0.06f);

            var worldCenter = panelPosition + panelRotation * (childOffset + boxCenterInChild);

            DoorColliderBake.DescribeInPanelFrame(
                panelPosition, panelRotation, worldCenter, panelRotation, authoredSize,
                out var center, out var size, out var angle);

            AssertNear(childOffset + boxCenterInChild, center, "center");   // (-0.478, 1.1, -0.031)
            AssertNear(authoredSize, size, "size");
            Assert.That(angle, Is.LessThan(0.01f));
        }

        [Test]
        public void RotatedChild_SwapsSizeAxesAndReportsAngle()
        {
            var panelPosition = Vector3.zero;
            var panelRotation = Quaternion.identity;
            var boxRotation = Quaternion.Euler(0f, 90f, 0f); // box X axis -> panel -Z
            var authoredSize = new Vector3(1f, 2.5f, 0.1f);

            DoorColliderBake.DescribeInPanelFrame(
                panelPosition, panelRotation, Vector3.zero, boxRotation, authoredSize,
                out _, out var size, out var angle);

            AssertNear(new Vector3(0.1f, 2.5f, 1f), size, "size (axes swapped)");
            Assert.That(angle, Is.EqualTo(90f).Within(0.01f));
            Assert.That(angle, Is.GreaterThan(DoorColliderBake.RotationBloatWarnDegrees));
        }

        [Test]
        public void DiagonallyRotatedChild_BakesConservativeEnclosingBox()
        {
            var authoredSize = new Vector3(1f, 1f, 1f);
            DoorColliderBake.DescribeInPanelFrame(
                Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.Euler(0f, 45f, 0f), authoredSize,
                out _, out var size, out _);

            // A unit cube rotated 45 degrees spans sqrt(2) on X and Z - never smaller than authored.
            Assert.That(size.x, Is.EqualTo(Mathf.Sqrt(2f)).Within(Epsilon));
            Assert.That(size.z, Is.EqualTo(Mathf.Sqrt(2f)).Within(Epsilon));
            Assert.That(size.y, Is.EqualTo(1f).Within(Epsilon));
        }

        [Test]
        public void RotatedPanel_CenterIsExpressedInPanelFrame()
        {
            // Panel rotated 90 degrees around Y (a door mid-animation): a box 1m down world -X
            // from the pivot must come back as a purely panel-local offset.
            var panelPosition = new Vector3(5f, 0f, 5f);
            var panelRotation = Quaternion.Euler(0f, 90f, 0f);
            var worldCenter = panelPosition + new Vector3(-1f, 1f, 0f);

            DoorColliderBake.DescribeInPanelFrame(
                panelPosition, panelRotation, worldCenter, panelRotation, Vector3.one,
                out var center, out _, out _);

            // panel X axis = world -Z, panel Z axis = world +X -> world (-1, 1, 0) = panel (0, 1, -1).
            AssertNear(new Vector3(0f, 1f, -1f), center, "center");
        }
    }
}
