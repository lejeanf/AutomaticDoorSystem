using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// Approach-side detection (DoorSideMath) - the rule that decides which way a Forward-style
    /// rotating door swings. It used to quantize the door root's world yaw to a cardinal world axis
    /// and compare raw world coordinates against the root position, so a door turned 30°, a root
    /// sitting off the doorway, or a second entity in an overlapping trigger volume made doors open
    /// toward the player or always the same way. These pin the replacement: the split is the
    /// root's local +Z plane through the panel pivots, and the nearest entity decides.
    /// </summary>
    public class DoorSideDetectionTests
    {
        private static float4x4 Trs(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            return float4x4.TRS(position, rotation, scale);
        }

        private static byte Side(float4x4 doorToWorld, Vector3 planeLocalOrigin, Vector3 worldPoint, bool invert = false)
        {
            var worldToDoor = math.inverse(doorToWorld);
            float3 origin = planeLocalOrigin;
            float3 point = worldPoint;
            return DoorSideMath.DirectionForward(in worldToDoor, in origin, invert, in point);
        }

        // ---- Which side is front ----

        [Test]
        public void UnrotatedDoor_PlusZIsFront_MinusZIsBack()
        {
            var door = Trs(Vector3.zero, Quaternion.identity, Vector3.one);
            Assert.That(Side(door, Vector3.zero, new Vector3(0f, 1f, 2f)), Is.EqualTo(1), "+Z should be FRONT");
            Assert.That(Side(door, Vector3.zero, new Vector3(0f, 1f, -2f)), Is.EqualTo(0), "-Z should be BACK");
        }

        [Test]
        public void DoorTurnedAQuarterTurn_FrontFollowsItsLocalZ()
        {
            // Local +Z becomes world +X - the cardinal case the old quantization also handled.
            var door = Trs(new Vector3(5f, 0f, 5f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            Assert.That(Side(door, Vector3.zero, new Vector3(7f, 1f, 5f)), Is.EqualTo(1));
            Assert.That(Side(door, Vector3.zero, new Vector3(3f, 1f, 5f)), Is.EqualTo(0));
        }

        [Test]
        public void DoorAtAnObliqueAngle_SplitsAlongItsOwnPlane_NotAWorldAxis()
        {
            // The clinic case: a door at 30° yaw. The old code snapped it to world +Z and compared
            // world z, so a player walking the corridor diagonally could be read on the wrong side.
            var rotation = Quaternion.Euler(0f, 30f, 0f);
            var door = Trs(Vector3.zero, rotation, Vector3.one);

            var inFront = rotation * new Vector3(0f, 1f, 1.5f);
            var behind = rotation * new Vector3(0f, 1f, -1.5f);
            Assert.That(Side(door, Vector3.zero, inFront), Is.EqualTo(1));
            Assert.That(Side(door, Vector3.zero, behind), Is.EqualTo(0));

            // A point in the door's own front half-space but with world z < 0 - the old rule got this wrong.
            var frontButNegativeWorldZ = rotation * new Vector3(3f, 1f, 0.5f);
            Assert.That(frontButNegativeWorldZ.z, Is.LessThan(0f), "test setup: world z must be negative");
            Assert.That(Side(door, Vector3.zero, frontButNegativeWorldZ), Is.EqualTo(1));
        }

        [Test]
        public void RootOffTheDoorway_SplitsAtThePanelPivotPlane()
        {
            // Root 1 m in front of the doorway (source-file pivot). A player between the doorway
            // and the root is in FRONT of the door but BEHIND the root - the old rule compared
            // against the root and read them as behind, so the door swung toward them.
            var door = Trs(Vector3.zero, Quaternion.identity, Vector3.one);
            var planeOrigin = new Vector3(0f, 0f, -1f); // hinge, root-local

            Assert.That(Side(door, planeOrigin, new Vector3(0f, 1f, -0.5f)), Is.EqualTo(1), "between doorway and root = FRONT");
            Assert.That(Side(door, planeOrigin, new Vector3(0f, 1f, -1.5f)), Is.EqualTo(0), "past the doorway = BACK");
        }

        [Test]
        public void MirroredDoor_NegativeZScale_FlipsFrontWithTheGeometry()
        {
            // A door instance mirrored with scale.z = -1 has its geometry's front on world -Z, and
            // local space follows: the runtime reads local +Z, which is now world -Z.
            var door = Trs(Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, -1f));
            Assert.That(Side(door, Vector3.zero, new Vector3(0f, 1f, -2f)), Is.EqualTo(1));
            Assert.That(Side(door, Vector3.zero, new Vector3(0f, 1f, 2f)), Is.EqualTo(0));
        }

        [Test]
        public void InvertForwardSide_SwapsTheAnswerOnly()
        {
            var door = Trs(Vector3.zero, Quaternion.Euler(0f, 45f, 0f), Vector3.one);
            var point = Quaternion.Euler(0f, 45f, 0f) * new Vector3(0.3f, 1f, 2f);

            Assert.That(Side(door, Vector3.zero, point, invert: false), Is.EqualTo(1));
            Assert.That(Side(door, Vector3.zero, point, invert: true), Is.EqualTo(0));
        }

        [Test]
        public void FrontDepth_IsMeasuredFromThePlane_InLocalUnits()
        {
            var worldToDoor = math.inverse(Trs(new Vector3(2f, 0f, 3f), Quaternion.identity, new Vector3(1f, 1f, 2f)));
            float3 origin = new float3(0f, 0f, 0.5f);
            float3 point = new float3(2f, 0f, 3f + 2f * 1.5f); // local z = 1.5 after the scale
            Assert.That(DoorSideMath.FrontDepth(in worldToDoor, in origin, in point), Is.EqualTo(1f).Within(1e-4f));
        }

        // ---- Who decides, and the trigger count ----

        private static void Evaluate(float4x4 doorToWorld, Vector3 triggerCenter, Vector3 triggerSize,
            Vector3[] positions, int[] layers, int layerMask, out int inside, out byte direction, bool invert = false,
            Vector3 planeOrigin = default)
        {
            var nativePositions = new NativeArray<float3>(positions.Length, Allocator.Temp);
            var nativeLayers = new NativeArray<int>(positions.Length, Allocator.Temp);
            try
            {
                for (var i = 0; i < positions.Length; i++)
                {
                    nativePositions[i] = positions[i];
                    nativeLayers[i] = layers[i];
                }

                float3 origin = planeOrigin;
                float3 center = triggerCenter;
                float3 size = triggerSize;
                DoorSideMath.Evaluate(in doorToWorld, in origin, invert, in center, in size, layerMask,
                    nativePositions, nativeLayers, out inside, out direction);
            }
            finally
            {
                nativePositions.Dispose();
                nativeLayers.Dispose();
            }
        }

        [Test]
        public void NearestEntityToTheDoorwayDecidesTheSide_NotTheLastOneInTheArray()
        {
            // Overlapping trigger volumes in a corridor: an NPC far away on the BACK side is listed
            // AFTER the player who is right at the door on the FRONT side. Last-one-wins swung the
            // door toward the player; nearest-wins must not.
            var door = Trs(Vector3.zero, Quaternion.identity, Vector3.one);
            var positions = new[] { new Vector3(0f, 1f, 0.4f), new Vector3(0.5f, 1f, -1.4f) };
            var layers = new[] { 3, 3 };

            Evaluate(door, Vector3.zero, new Vector3(3f, 3f, 3f), positions, layers, 1 << 3, out var inside, out var direction);

            Assert.That(inside, Is.EqualTo(2));
            Assert.That(direction, Is.EqualTo(1), "the player at 0.4 m in front must decide");

            // Same two entities in the other order must give the same answer.
            Evaluate(door, Vector3.zero, new Vector3(3f, 3f, 3f), new[] { positions[1], positions[0] }, layers, 1 << 3,
                out inside, out direction);
            Assert.That(direction, Is.EqualTo(1));
        }

        [Test]
        public void EntitiesOutsideTheVolumeOrOnOtherLayers_DoNotCountOrDecide()
        {
            var door = Trs(Vector3.zero, Quaternion.identity, Vector3.one);
            var positions = new[]
            {
                new Vector3(0f, 1f, -0.5f),  // inside, BACK, wrong layer
                new Vector3(0f, 1f, 5f),     // right layer, outside the 3 m box
                new Vector3(0f, 1f, 1f),     // inside, FRONT, right layer
            };
            var layers = new[] { 4, 3, 3 };

            Evaluate(door, Vector3.zero, new Vector3(3f, 3f, 3f), positions, layers, 1 << 3, out var inside, out var direction);

            Assert.That(inside, Is.EqualTo(1));
            Assert.That(direction, Is.EqualTo(1));
        }

        [Test]
        public void EmptyVolume_ReportsNobodyAndKeepsForwardAsDefault()
        {
            var door = Trs(Vector3.zero, Quaternion.identity, Vector3.one);
            var found = false;
            using (var positions = new NativeArray<float3>(0, Allocator.Temp))
            using (var layers = new NativeArray<int>(0, Allocator.Temp))
            {
                float3 zero = float3.zero;
                float3 size = new float3(3f, 3f, 3f);
                found = DoorSideMath.Evaluate(in door, in zero, false, in zero, in size, ~0,
                    positions, layers, out var inside, out var direction);
                Assert.That(inside, Is.EqualTo(0));
                Assert.That(direction, Is.EqualTo(1));
            }
            Assert.That(found, Is.False);
        }

        [Test]
        public void TriggerVolumeCentre_GoesThroughTheRootTransform()
        {
            // Rotated door: a centre authored 1 m along local +X lands on world -Z... unless the
            // centre is added to the position without rotating it, which is what this guards.
            var door = Trs(new Vector3(10f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
            var localCenter = new Vector3(1f, 1f, 0f);
            var worldCenter = new Vector3(10f, 1f, -1f);

            Evaluate(door, localCenter, new Vector3(1f, 1f, 1f), new[] { worldCenter }, new[] { 0 }, 1,
                out var inside, out _);
            Assert.That(inside, Is.EqualTo(1));

            Evaluate(door, localCenter, new Vector3(1f, 1f, 1f), new[] { new Vector3(11f, 1f, 0f) }, new[] { 0 }, 1,
                out inside, out _);
            Assert.That(inside, Is.EqualTo(0), "the unrotated centre must not detect anything");
        }

        // ---- The authoring-side helper the editor tools and the baker share ----

        [Test]
        public void DoorAuthoring_SidePlaneOrigin_IsTheMeanOfThePanelPivots_RootLocal()
        {
            var root = new GameObject("Door");
            var config = ScriptableObject.CreateInstance<DoorConfig>();
            try
            {
                config.doorMovement = DoorConfig.DoorMovementEnum.Rotating;
                config.doorCount = DoorConfig.DoorCountEnum.Double;

                root.transform.SetPositionAndRotation(new Vector3(4f, 0f, 4f), Quaternion.Euler(0f, 90f, 0f));
                var left = new GameObject("Left").transform;
                var right = new GameObject("Right").transform;
                left.SetParent(root.transform, false);
                right.SetParent(root.transform, false);
                left.localPosition = new Vector3(-1f, 0f, 0.5f);
                right.localPosition = new Vector3(1f, 0f, 0.5f);

                var door = root.AddComponent<DoorAuthoring>();
                door.doorConfig = config;
                door.leftDoorMesh = left;
                door.rightDoorMesh = right;

                var origin = door.SidePlaneLocalOrigin;
                Assert.That(Vector3.Distance(origin, new Vector3(0f, 0f, 0.5f)), Is.LessThan(1e-4f));

                // FRONT is local +Z = world +X for this yaw; the split sits at local z = 0.5.
                Assert.That(door.DirectionForwardFor(root.transform.TransformPoint(new Vector3(0f, 1f, 0.6f)), out _), Is.EqualTo(1));
                Assert.That(door.DirectionForwardFor(root.transform.TransformPoint(new Vector3(0f, 1f, 0.4f)), out _), Is.EqualTo(0));
                Assert.That(Vector3.Distance(door.FrontDirectionWorld, Vector3.right), Is.LessThan(1e-4f));

                // Config flag and per-door override combine: override wins.
                config.invertForwardSide = true;
                Assert.That(door.EffectiveInvertForwardSide, Is.True);
                Assert.That(door.DirectionForwardFor(root.transform.TransformPoint(new Vector3(0f, 1f, 0.6f)), out _), Is.EqualTo(0));
                door.invertForwardSideOverride = DoorAuthoring.ConfigBoolOverride.ForceOff;
                Assert.That(door.EffectiveInvertForwardSide, Is.False);

                // Start-locked follows the same override rule.
                config.startLocked = true;
                Assert.That(door.EffectiveStartLocked, Is.True);
                door.startLockedOverride = DoorAuthoring.ConfigBoolOverride.ForceOff;
                Assert.That(door.EffectiveStartLocked, Is.False);
                config.startLocked = false;
                door.startLockedOverride = DoorAuthoring.ConfigBoolOverride.ForceOn;
                Assert.That(door.EffectiveStartLocked, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(config);
            }
        }
    }
}
