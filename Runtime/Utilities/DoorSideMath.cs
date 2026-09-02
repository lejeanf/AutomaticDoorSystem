using Unity.Collections;
using Unity.Mathematics;

namespace AutomaticDoorSystem
{
    /// <summary>
    /// The ONE definition of which side of a door a point is on. Used by the runtime detection
    /// job and by every edit-mode tool (gizmos, pivot heuristics, the Scene-camera probe), so what
    /// the editor tells you before play mode is exactly what the entity system computes.
    ///
    /// Rule: the door root's local +Z half-space is the FRONT (DirectionForward = 1); the local -Z
    /// half-space is the BACK. The split plane passes through the panel pivots (the doorway), not
    /// necessarily the root origin. The test goes through the root's full world-to-local matrix, so
    /// rotation at any angle, scale (including negative/mirrored scale) and nested parents are all
    /// honoured. Detection used to quantize the root's world yaw to a cardinal world axis and
    /// compare raw world coordinates against the root position - a door turned 30°, one whose root
    /// sits off the doorway, or one placed next to another door's trigger volume then read the
    /// approach side wrong and opened toward the player or always the same way.
    /// </summary>
    // No [BurstCompile] here on purpose: these are plain static helpers inlined into the Burst-
    // compiled detection job (and called from editor code). Marking them as direct-call Burst
    // entry points would trip over the bool parameters, which are not blittable.
    public static class DoorSideMath
    {
        /// <summary>
        /// Signed depth of a world point in front of the doorway plane, in the door root's local
        /// units. Positive = FRONT (local +Z side), negative = BACK.
        /// </summary>
        public static float FrontDepth(in float4x4 worldToDoor, in float3 planeLocalOrigin, in float3 worldPoint)
        {
            return math.transform(worldToDoor, worldPoint).z - planeLocalOrigin.z;
        }

        /// <summary>
        /// DirectionForward for a point at the given front depth. <paramref name="invert"/> is the
        /// baked "Invert Forward Side" switch: it flips the answer for door models whose pivot,
        /// rotation or scale puts local +Z on the wrong side, so the asset does not have to be fixed.
        /// A point exactly on the plane counts as front.
        /// </summary>
        public static byte DirectionForward(float frontDepth, bool invert)
        {
            var front = frontDepth >= 0f;
            if (invert) front = !front;
            return (byte)(front ? 1 : 0);
        }

        public static byte DirectionForward(in float4x4 worldToDoor, in float3 planeLocalOrigin, bool invert, in float3 worldPoint)
        {
            return DirectionForward(FrontDepth(in worldToDoor, in planeLocalOrigin, in worldPoint), invert);
        }

        /// <summary>
        /// The trigger test is a world-axis-aligned box around the transformed centre, matching how
        /// Size is authored (see DoorDetectionSystem).
        /// </summary>
        public static bool IsInsideVolume(in float3 point, in float3 volumeWorldCenter, in float3 volumeSize)
        {
            var distance = math.abs(point - volumeWorldCenter);
            var halfSize = volumeSize * 0.5f;
            return math.all(distance < halfSize);
        }

        /// <summary>
        /// One detection tick for one door: counts the triggerables inside its volume and reports
        /// the approach side of the NEAREST one (distance to the doorway plane origin). The nearest
        /// entity is the one about to walk through, so it decides - previously the last entity in
        /// iteration order won, which with overlapping trigger volumes (a corridor of doors, NPCs on
        /// the far side) made the swing direction depend on array order.
        /// </summary>
        /// <returns>True when at least one triggerable is inside the volume.</returns>
        public static bool Evaluate(
            in float4x4 doorToWorld,
            in float3 planeLocalOrigin,
            bool invertForwardSide,
            in float3 triggerLocalCenter,
            in float3 triggerSize,
            int triggerLayerMask,
            in NativeArray<float3> positions,
            in NativeArray<int> layers,
            out int insideCount,
            out byte nearestDirectionForward)
        {
            insideCount = 0;
            nearestDirectionForward = 1;

            var worldToDoor = math.inverse(doorToWorld);
            // Centre is stored in the door root's local space, so it has to go through the full
            // transform - adding it to the root position ignores rotation and scale and puts the
            // volume in the wrong place on any rotated door.
            var triggerWorldCenter = math.transform(doorToWorld, triggerLocalCenter);
            var planeWorldOrigin = math.transform(doorToWorld, planeLocalOrigin);

            var nearestDistanceSq = float.MaxValue;

            for (var i = 0; i < positions.Length; i++)
            {
                var layerBit = 1 << layers[i];
                if ((triggerLayerMask & layerBit) == 0)
                    continue;

                var position = positions[i];
                if (!IsInsideVolume(in position, in triggerWorldCenter, in triggerSize))
                    continue;

                insideCount++;

                var distanceSq = math.distancesq(position, planeWorldOrigin);
                if (distanceSq >= nearestDistanceSq)
                    continue;

                nearestDistanceSq = distanceSq;
                nearestDirectionForward = DirectionForward(in worldToDoor, in planeLocalOrigin, invertForwardSide, in position);
            }

            return insideCount > 0;
        }
    }
}
