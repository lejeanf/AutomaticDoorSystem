using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace AutomaticDoorSystem
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DoorStateSystem))]
    public partial struct DoorDetectionSystem : ISystem
    {
        private NativeHashSet<int> _checkableDoorIds;
        private NativeHashMap<int, FixedString128Bytes> _doorToRegionMap;
        private NativeHashSet<int> _globalDoorIds;

        private EntityQuery _doorQuery;
        private EntityQuery _triggerableEntitiesQuery;

        private float _detectionAccumulator;
        private const float DETECTION_INTERVAL = 1f / 30f; // 30Hz update rate

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DoorComponent>();

            _checkableDoorIds = new NativeHashSet<int>(100, Allocator.Persistent);
            _doorToRegionMap = new NativeHashMap<int, FixedString128Bytes>(100, Allocator.Persistent);
            _globalDoorIds = new NativeHashSet<int>(50, Allocator.Persistent);
            _detectionAccumulator = 0f;
            _firstFrameLogged = false;

            _doorQuery = SystemAPI.QueryBuilder()
                .WithAll<DoorComponent, DoorStateComponent, DoorTriggerVolume, LocalToWorld>()
                .Build();

            _triggerableEntitiesQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalToWorld, EntityLayerComponent>()
                .WithNone<DoorComponent>()
                .Build();

        }

        private bool _firstFrameLogged;

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_checkableDoorIds.IsCreated) _checkableDoorIds.Dispose();
            if (_doorToRegionMap.IsCreated) _doorToRegionMap.Dispose();
            if (_globalDoorIds.IsCreated) _globalDoorIds.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _detectionAccumulator += SystemAPI.Time.DeltaTime;
            if (_detectionAccumulator < DETECTION_INTERVAL)
                return;
            _detectionAccumulator = 0f;

            var triggerablePositions = _triggerableEntitiesQuery.ToComponentDataArray<LocalToWorld>(Allocator.TempJob);
            var triggerableLayers = _triggerableEntitiesQuery.ToComponentDataArray<EntityLayerComponent>(Allocator.TempJob);


            if (triggerablePositions.Length == 0)
            {
                triggerablePositions.Dispose();
                triggerableLayers.Dispose();
                return;
            }

            SetCheckableDoors(ref state);

            var resetJob = new ResetEntityCountJob();
            state.Dependency = resetJob.ScheduleParallel(state.Dependency);

            var detectionJob = new DoorDetectionJob
            {
                TriggerablePositions = triggerablePositions,
                TriggerableLayers = triggerableLayers,
                CheckableDoorIds = _checkableDoorIds
            };
            state.Dependency = detectionJob.ScheduleParallel(state.Dependency);

            state.Dependency.Complete();
            triggerablePositions.Dispose();
            triggerableLayers.Dispose();
        }

        private void SetCheckableDoors(ref SystemState state)
        {
            _checkableDoorIds.Clear();

            foreach (var door in SystemAPI.Query<RefRO<DoorComponent>>())
            {
                _checkableDoorIds.Add(door.ValueRO.DoorId);
            }
        }

        [BurstCompile]
        partial struct ResetEntityCountJob : IJobEntity
        {
            void Execute(ref DoorStateComponent state)
            {
                state.EntitiesInTrigger = 0;
            }
        }

        [BurstCompile]
        private partial struct DoorDetectionJob : IJobEntity
        {
            [ReadOnly] public NativeArray<LocalToWorld> TriggerablePositions;
            [ReadOnly] public NativeArray<EntityLayerComponent> TriggerableLayers;
            [ReadOnly] public NativeHashSet<int> CheckableDoorIds;

            private void Execute(
                ref DoorStateComponent state,
                in DoorComponent door,
                in DoorTriggerVolume trigger,
                in LocalToWorld doorTransform)
            {
                if (state.IsLocked == 1)
                    return;

                if (!CheckableDoorIds.Contains(door.DoorId))
                    return;

                var triggerWorldCenter = doorTransform.Position + trigger.Center;

                for (var i = 0; i < TriggerablePositions.Length; i++)
                {
                    var entityPos = TriggerablePositions[i].Position;
                    var entityLayer = TriggerableLayers[i].Layer;

                    int layerBit = 1 << entityLayer;
                    if ((trigger.LayerMask & layerBit) == 0)
                        continue;

                    if (IsInsideVolume(entityPos, triggerWorldCenter, trigger.Size))
                    {
                        state.EntitiesInTrigger++;

                        if ((door.Type == DoorType.RotatingSingle || door.Type == DoorType.RotatingDouble) &&
                            state.CurrentState == DoorState.Closed)
                        {
                            var directionForward = CalculateApproachDirection(
                                entityPos,
                                doorTransform.Position,
                                door.Axis);

                            state.DirectionForward = directionForward;
                        }
                    }
                }

                if (state.EntitiesInTrigger > 0 && state.CurrentState == DoorState.Closed)
                {
                    state.PreviousState = state.CurrentState;
                    state.CurrentState = DoorState.Opening;
                    state.StateTimer = 0f;
                    state.ShouldPlayOpenSound = 1;
                }
                else if (state.EntitiesInTrigger == 0 && state.CurrentState == DoorState.Open)
                {
                    if (door.Type == DoorType.RotatingSingle || door.Type == DoorType.RotatingDouble)
                    {
                        state.PreviousState = state.CurrentState;
                        state.CurrentState = DoorState.Closing;
                        state.StateTimer = 0f;
                        state.ShouldPlayCloseSound = 1;
                    }
                }
            }

            [BurstCompile]
            private bool IsInsideVolume(float3 point, float3 volumeCenter, float3 volumeSize)
            {
                var distance = math.abs(point - volumeCenter);
                var halfSize = volumeSize * 0.5f;
                return math.all(distance < halfSize);
            }

            [BurstCompile]
            private byte CalculateApproachDirection(float3 entityPos, float3 doorPos, DoorAxis axis)
            {
                switch (axis)
                {
                    case DoorAxis.X:
                        return (byte)(entityPos.x > doorPos.x ? 1 : 0);
                    case DoorAxis.Z:
                        return (byte)(entityPos.z > doorPos.z ? 1 : 0);
                    case DoorAxis.NegX:
                        return (byte)(entityPos.x < doorPos.x ? 1 : 0);
                    case DoorAxis.NegZ:
                        return (byte)(entityPos.z < doorPos.z ? 1 : 0);
                    default:
                        return 1;
                }
            }
        }
    }
}
