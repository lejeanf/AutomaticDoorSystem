using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AutomaticDoorSystem
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DoorStateSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct DoorAnimationSystem : ISystem
    {

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DoorComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {

            foreach (var (door, doorState, transformData, buffer, localTransform, entity) in
                SystemAPI.Query<
                    RefRO<DoorComponent>,
                    RefRO<DoorStateComponent>,
                    RefRO<DoorTransformData>,
                    DynamicBuffer<DoubleDoorBuffer>,
                    RefRO<LocalTransform>>()
                .WithEntityAccess())
            {
                if (door.ValueRO.Type == DoorType.SlidingSingle || door.ValueRO.Type == DoorType.SlidingDouble)
                {
                    AnimateSlidingDoor(
                        ref state,
                        door.ValueRO,
                        doorState.ValueRO,
                        transformData.ValueRO,
                        buffer,
                        localTransform.ValueRO,
                        entity);
                }
                else if (door.ValueRO.Type == DoorType.RotatingSingle)
                {
                    RotatingSingleAnimation(
                        ref state,
                        door.ValueRO,
                        doorState.ValueRO,
                        transformData.ValueRO,
                        buffer,
                        entity);
                }
                else if (door.ValueRO.Type == DoorType.RotatingDouble)
                {
                    RotatingDoubleAnimation(
                        ref state,
                        door.ValueRO,
                        doorState.ValueRO,
                        transformData.ValueRO,
                        buffer,
                        entity);
                }
            }
        }

        [BurstCompile]
        private static float CalculateEasedProgress(float stateTimer, float animationDuration)
        {
            var progress = math.clamp(stateTimer / animationDuration, 0f, 1f);
            return -(math.cos(math.PI * progress) - 1f) / 2f; 
        }

        [BurstCompile]
        private static void GetTargetRotation(
            in DoorTransformData transformData,
            in DoorStateComponent doorState,
            out quaternion result,
            bool isLeftDoor = false)
        {
            switch (transformData.OpeningStyle)
            {
                case OpeningStyle.Forward:
                    result = doorState.DirectionForward == 1
                        ? transformData.OpenRotationForward
                        : transformData.OpenRotationBackward;
                    break;

                case OpeningStyle.OneWay:
                    bool useForwardRotation = transformData.OneWayDirection.z >= 0;
                    result = useForwardRotation
                        ? transformData.OpenRotationBackward
                        : transformData.OpenRotationForward;
                    break;

                case OpeningStyle.BothWay:
                    result = doorState.DirectionForward == 1
                        ? transformData.OpenRotationForward
                        : transformData.OpenRotationBackward;
                    break;

                default:
                    result = doorState.DirectionForward == 1
                        ? transformData.OpenRotationForward
                        : transformData.OpenRotationBackward;
                    break;
            }
        }

        [BurstCompile]
        private static void AnimateRotation(
            ref LocalTransform doorTransform,
            in quaternion closedRotation,
            in quaternion targetRotation,
            float easedProgress,
            bool isOpening)
        {
            if (isOpening)
            {
                doorTransform.Rotation = math.slerp(closedRotation, targetRotation, easedProgress);
            }
            else
            {
                doorTransform.Rotation = math.slerp(targetRotation, closedRotation, easedProgress);
            }
        }

        [BurstCompile]
        private static void AnimatePosition(
            ref LocalTransform doorTransform,
            in float3 closedPosition,
            in float3 openPosition,
            float easedProgress,
            bool isOpening)
        {
            if (isOpening)
            {
                doorTransform.Position = math.lerp(closedPosition, openPosition, easedProgress);
            }
            else
            {
                doorTransform.Position = math.lerp(openPosition, closedPosition, easedProgress);
            }
        }

        [BurstCompile]
        private static void GetMirroredRotation(in quaternion rotation, out quaternion result)
        {
            var euler = ((Quaternion)rotation).eulerAngles;
            result = Quaternion.Euler(euler.x, -euler.y, euler.z);
        }

        [BurstCompile]
        private static void GetDoubleRotations(
            in DoorTransformData transformData,
            in DoorStateComponent doorState,
            out quaternion leftRotation,
            out quaternion rightRotation)
        {
            switch (transformData.OpeningStyle)
            {
                case OpeningStyle.Forward:
                    quaternion baseRotation = doorState.DirectionForward == 1
                        ? transformData.OpenRotationBackward
                        : transformData.OpenRotationForward;
                    leftRotation = baseRotation;
                    GetMirroredRotation(in baseRotation, out rightRotation);
                    break;

                case OpeningStyle.BothWay:
                    leftRotation = transformData.OpenRotationForward;
                    rightRotation = transformData.OpenRotationForward;
                    break;

                case OpeningStyle.OneWay:
                    bool useForwardRotation = transformData.OneWayDirection.z >= 0;
                    quaternion oneWayRotation = useForwardRotation
                        ? transformData.OpenRotationBackward
                        : transformData.OpenRotationForward;
                    leftRotation = oneWayRotation;
                    GetMirroredRotation(in oneWayRotation, out rightRotation);
                    break;

                default:
                    quaternion defaultRotation = doorState.DirectionForward == 1
                        ? transformData.OpenRotationBackward
                        : transformData.OpenRotationForward;
                    leftRotation = defaultRotation;
                    GetMirroredRotation(in defaultRotation, out rightRotation);
                    break;
            }
        }

        private void RotatingSingleAnimation(
            ref SystemState state,
            in DoorComponent door,
            in DoorStateComponent doorState,
            in DoorTransformData transformData,
            DynamicBuffer<DoubleDoorBuffer> doorBuffer,
            Entity doorEntity)
        {
            if (doorState.CurrentState != DoorState.Opening &&
                doorState.CurrentState != DoorState.Closing)
                return;

            var easedProgress = CalculateEasedProgress(doorState.StateTimer, door.AnimationDuration);
            GetTargetRotation(in transformData, in doorState, out quaternion targetRotation);
            var isOpening = doorState.CurrentState == DoorState.Opening;

            for (var i = 0; i < doorBuffer.Length; i++)
            {
                var doorData = doorBuffer[i];
                if (!state.EntityManager.Exists(doorData.DoorEntity))
                    continue;

                var doorTransform = SystemAPI.GetComponentRW<LocalTransform>(doorData.DoorEntity);
                AnimateRotation(
                    ref doorTransform.ValueRW,
                    in transformData.ClosedRotation,
                    in targetRotation,
                    easedProgress,
                    isOpening);
            }
        }
        
        private void AnimateSlidingDoor(
            ref SystemState state,
            in DoorComponent door,
            in DoorStateComponent doorState,
            in DoorTransformData transformData,
            DynamicBuffer<DoubleDoorBuffer> doorBuffer,
            in LocalTransform doorRootTransform,
            Entity doorEntity)
        {
            if (doorState.CurrentState != DoorState.Opening &&
                doorState.CurrentState != DoorState.Closing)
                return;

            var easedProgress = CalculateEasedProgress(doorState.StateTimer, door.AnimationDuration);
            var isOpening = doorState.CurrentState == DoorState.Opening;

            if (door.Type == DoorType.SlidingDouble && transformData.SlidingStyle == SlidingStyle.Telescopic)
            {
                AnimateTelescopicSlidingDoor(ref state, in transformData, doorBuffer, easedProgress, isOpening);
                return;
            }

            for (var i = 0; i < doorBuffer.Length; i++)
            {
                var doorData = doorBuffer[i];
                if (!state.EntityManager.Exists(doorData.DoorEntity))
                    continue;

                var doorTransform = SystemAPI.GetComponentRW<LocalTransform>(doorData.DoorEntity);

                var direction = door.Type == DoorType.SlidingSingle ? 1f : (doorData.IsLeftDoor == 1 ? 1f : -1f);
                var localSlideOffset = new float3(
                    transformData.SlideOffset.x * direction,
                    transformData.SlideOffset.y,
                    transformData.SlideOffset.z);

                // Each panel slides from its OWN authored position - using the root's InitialPosition
                // here made every off-origin panel pop on the first animation frame.
                var closedPos = doorData.InitialLocalPosition;
                var openPos = closedPos + localSlideOffset;

                AnimatePosition(
                    ref doorTransform.ValueRW,
                    in closedPos,
                    in openPos,
                    easedProgress,
                    isOpening);
            }
        }

        /// <summary>
        /// Telescopic travel along the shared slide direction. The right (leading) panel owns the
        /// timeline; the left panel waits until the right one has covered the catch-up distance,
        /// then follows in lockstep. Closing is the exact reverse: they return together until the
        /// left panel is home, then the right panel finishes alone. With rightDoorOnly the right
        /// panel's target is the catch-up point itself, so the left panel never moves.
        /// </summary>
        [BurstCompile]
        public static void ComputeTelescopicTravel(
            float rightSpan, float leftSpan, bool rightDoorOnly, float easedProgress, bool isOpening,
            out float rightTravel, out float leftTravel)
        {
            var catchUp = math.max(rightSpan - leftSpan, 0f);
            var targetSpan = rightDoorOnly ? catchUp : rightSpan;
            var openFraction = isOpening ? easedProgress : 1f - easedProgress;
            rightTravel = targetSpan * openFraction;
            leftTravel = math.clamp(rightTravel - catchUp, 0f, leftSpan);
        }

        private void AnimateTelescopicSlidingDoor(
            ref SystemState state,
            in DoorTransformData transformData,
            DynamicBuffer<DoubleDoorBuffer> doorBuffer,
            float easedProgress,
            bool isOpening)
        {
            var leftSpan = math.length(transformData.SlideOffset);
            var rightSpan = math.length(transformData.RightSlideOffset);

            ComputeTelescopicTravel(rightSpan, leftSpan, transformData.OpenRightDoorOnly == 1,
                easedProgress, isOpening, out var rightTravel, out var leftTravel);

            for (var i = 0; i < doorBuffer.Length; i++)
            {
                var doorData = doorBuffer[i];
                if (!state.EntityManager.Exists(doorData.DoorEntity))
                    continue;

                var doorTransform = SystemAPI.GetComponentRW<LocalTransform>(doorData.DoorEntity);

                float3 direction;
                float travel;
                if (doorData.IsLeftDoor == 1)
                {
                    direction = leftSpan > 1e-4f ? transformData.SlideOffset / leftSpan : float3.zero;
                    travel = leftTravel;
                }
                else
                {
                    direction = rightSpan > 1e-4f ? transformData.RightSlideOffset / rightSpan : float3.zero;
                    travel = rightTravel;
                }

                // Travel is applied along the slide direction only, on top of the panel's own
                // authored closed position - the other axes (like parallel-rail Z offsets) stay put.
                doorTransform.ValueRW.Position = doorData.InitialLocalPosition + direction * travel;
            }
        }

        private void RotatingDoubleAnimation(
            ref SystemState state,
            in DoorComponent door,
            in DoorStateComponent doorState,
            in DoorTransformData transformData,
            DynamicBuffer<DoubleDoorBuffer> doorBuffer,
            Entity doorEntity)
        {
            if (doorState.CurrentState != DoorState.Opening &&
                doorState.CurrentState != DoorState.Closing)
                return;

            var easedProgress = CalculateEasedProgress(doorState.StateTimer, door.AnimationDuration);
            GetDoubleRotations(in transformData, in doorState, out var leftRotation, out var rightRotation);
            var isOpening = doorState.CurrentState == DoorState.Opening;

            for (var i = 0; i < doorBuffer.Length; i++)
            {
                var doorData = doorBuffer[i];
                if (!state.EntityManager.Exists(doorData.DoorEntity))
                    continue;

                var doorTransform = SystemAPI.GetComponentRW<LocalTransform>(doorData.DoorEntity);
                var targetRotation = doorData.IsLeftDoor == 1 ? leftRotation : rightRotation;

                AnimateRotation(
                    ref doorTransform.ValueRW,
                    in transformData.ClosedRotation,
                    in targetRotation,
                    easedProgress,
                    isOpening);
            }
        }
    }
}