using NUnit.Framework;
using UnityEngine;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// Slide offsets live on a SHARED DoorConfig, so they only work if they are interpreted in each
    /// door's own local space. They used to be treated as world vectors - the baker converted world
    /// to local and the animation system applied the result locally, which cancelled out - so every
    /// door slid along the same world axis and a door rotated 90 degrees slid across its doorway.
    /// The gizmo hid it: TransformVector(InverseTransformVector(v)) is an exact no-op.
    /// </summary>
    public class DoorSlideDirectionTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("DoorRoot");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        private static void AssertVectorsEqual(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f),
                $"expected {expected} but was {actual}");
        }

        [Test]
        public void UnrotatedDoorSlidesAlongTheAuthoredAxis()
        {
            var world = DoorAuthoring.SlideVectorToWorld(_root.transform, new Vector3(0f, 0f, 1.5f));
            AssertVectorsEqual(new Vector3(0f, 0f, 1.5f), world);
        }

        [Test]
        public void RotatedDoorSlidesAlongItsOwnAxis()
        {
            _root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // Local +Z becomes world +X for a door turned a quarter turn - the case that was broken.
            var world = DoorAuthoring.SlideVectorToWorld(_root.transform, new Vector3(0f, 0f, 1.5f));
            AssertVectorsEqual(new Vector3(1.5f, 0f, 0f), world);
        }

        [Test]
        public void TwoDoorsSharingAConfigSlideAlongDifferentWorldAxes()
        {
            var offset = new Vector3(0f, 0f, 1.5f);
            var other = new GameObject("OtherDoor");
            try
            {
                other.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

                var a = DoorAuthoring.SlideVectorToWorld(_root.transform, offset);
                var b = DoorAuthoring.SlideVectorToWorld(other.transform, offset);

                Assert.That(Vector3.Distance(a, b), Is.GreaterThan(0.1f),
                    "Doors at different rotations must not slide along the same world axis.");
                AssertVectorsEqual(a.normalized, Vector3.forward);
                AssertVectorsEqual(b.normalized, Vector3.right);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void ScaledDoorScalesItsTravel()
        {
            _root.transform.localScale = new Vector3(2f, 2f, 2f);

            var world = DoorAuthoring.SlideVectorToWorld(_root.transform, new Vector3(0f, 0f, 1.5f));
            AssertVectorsEqual(new Vector3(0f, 0f, 3f), world);
        }

        [Test]
        public void RightPanelOfAMirroredDoubleNegatesLocalXOnly()
        {
            var mirrored = DoorAuthoring.MirrorForRightPanel(new Vector3(1.5f, 0.25f, 0.75f));
            AssertVectorsEqual(new Vector3(-1.5f, 0.25f, 0.75f), mirrored);
        }

        [Test]
        public void MirroringHappensBeforeTheRotationIsApplied()
        {
            _root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            var world = DoorAuthoring.SlideVectorToWorld(
                _root.transform, DoorAuthoring.MirrorForRightPanel(new Vector3(1.5f, 0f, 0f)));

            // Local -X under a 90 degree turn points to world +Z.
            AssertVectorsEqual(new Vector3(0f, 0f, 1.5f), world);
        }
    }
}
