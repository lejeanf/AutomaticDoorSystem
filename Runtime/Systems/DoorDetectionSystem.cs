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

            _doorQuery = SystemAPI.QueryBuilder()
                .WithAll<DoorComponent, DoorStateComponent, DoorTriggerVolume, LocalToWorld>()
                .Build();

            _triggerableEntitiesQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalToWorld, EntityLayerComponent>()
                .WithNone<DoorComponent>()
                .Build();

        }

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

            var triggerableTransforms = _triggerableEntitiesQuery.ToComponentDataArray<LocalToWorld>(Allocator.TempJob);
            var triggerableLayerComponents = _triggerableEntitiesQuery.ToComponentDataArray<EntityLayerComponent>(Allocator.TempJob);

            if (triggerableTransforms.Length == 0)
            {
                triggerableTransforms.Dispose();
                triggerableLayerComponents.Dispose();
                return;
            }

            // The per-door evaluation (DoorSideMath.Evaluate) only needs positions and layers, and
            // keeping it free of ECS types is what lets the editor tools and tests run the exact
            // same code.
            var triggerablePositions = new NativeArray<float3>(triggerableTransforms.Length, Allocator.TempJob);
            var triggerableLayers = new NativeArray<int>(triggerableTransforms.Length, Allocator.TempJob);
            for (var i = 0; i < triggerableTransforms.Length; i++)
            {
                triggerablePositions[i] = triggerableTransforms[i].Position;
                triggerableLayers[i] = triggerableLayerComponents[i].Layer;
            }
            triggerableTransforms.Dispose();
            triggerableLayerComponents.Dispose();

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
            [ReadOnly] public NativeArray<float3> TriggerablePositions;
            [ReadOnly] public NativeArray<int> TriggerableLayers;
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

                DoorSideMath.Evaluate(
                    in doorTransform.Value,
                    in door.SidePlaneLocalOrigin,
                    door.InvertForwardSide == 1,
                    in trigger.Center,
                    in trigger.Size,
                    trigger.LayerMask,
                    in TriggerablePositions,
                    in TriggerableLayers,
                    out var insideCount,
                    out var nearestDirectionForward);

                state.EntitiesInTrigger = insideCount;

                var isRotating = door.Type == DoorType.RotatingSingle || door.Type == DoorType.RotatingDouble;

                if (insideCount > 0 && state.CurrentState == DoorState.Closed)
                {
                    // Decided once, from the closed pose, by whoever is nearest the doorway. The
                    // closing animation reads the same value, so it must not change mid-swing.
                    if (isRotating)
                        state.DirectionForward = nearestDirectionForward;

                    state.PreviousState = state.CurrentState;
                    state.CurrentState = DoorState.Opening;
                    state.StateTimer = 0f;
                    state.ShouldPlayOpenSound = 1;
                }
                else if (insideCount == 0 && state.CurrentState == DoorState.Open)
                {
                    if (isRotating)
                    {
                        state.PreviousState = state.CurrentState;
                        state.CurrentState = DoorState.Closing;
                        state.StateTimer = 0f;
                        state.ShouldPlayCloseSound = 1;
                    }
                }
            }
        }
    }
}
