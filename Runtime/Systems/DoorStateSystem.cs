using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace AutomaticDoorSystem
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DoorDetectionSystem))]
    [UpdateBefore(typeof(DoorAnimationSystem))]
    public partial struct DoorStateSystem : ISystem
    {
        private NativeList<StateTransitionLog> _stateTransitions;

        private struct StateTransitionLog
        {
            public int DoorId;
            public DoorState OldState;
            public DoorState NewState;
            public FixedString128Bytes Reason;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DoorComponent>();
            _stateTransitions = new NativeList<StateTransitionLog>(16, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_stateTransitions.IsCreated)
                _stateTransitions.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            _stateTransitions.Clear();

            foreach (var (stateRef, door, entity) in SystemAPI.Query<RefRW<DoorStateComponent>, RefRO<DoorComponent>>().WithEntityAccess())
            {
                var doorState = stateRef.ValueRW;
                var oldState = doorState.CurrentState;

                if (doorState.IsLocked == 1 && doorState.CurrentState != DoorState.Closing)
                {
                    if (doorState.CurrentState != DoorState.Closed)
                    {
                        doorState.PreviousState = doorState.CurrentState;
                        doorState.CurrentState = DoorState.Closed;
                        doorState.StateTimer = 0f;

                        if (state.EntityManager.HasComponent<DoorDebugComponent>(entity))
                        {
                            _stateTransitions.Add(new StateTransitionLog
                            {
                                DoorId = door.ValueRO.DoorId,
                                OldState = oldState,
                                NewState = DoorState.Closed,
                                Reason = new FixedString128Bytes("Door is locked")
                            });
                        }
                    }
                    stateRef.ValueRW = doorState;
                    continue;
                }

                doorState.StateTimer += deltaTime;

                switch (doorState.CurrentState)
                {
                    case DoorState.Opening:
                        ProcessOpening(ref doorState, door.ValueRO, entity, ref state);
                        break;

                    case DoorState.Open:
                        ProcessOpen(ref doorState, door.ValueRO, entity, ref state);
                        break;

                    case DoorState.Closing:
                        ProcessClosing(ref doorState, door.ValueRO, entity, ref state);
                        break;

                    case DoorState.Closed:
                        break;
                }

                if (doorState.CurrentState != oldState && state.EntityManager.HasComponent<DoorDebugComponent>(entity))
                {
                    var reason = GetTransitionReason(oldState, doorState.CurrentState, doorState, door.ValueRO);
                    _stateTransitions.Add(new StateTransitionLog
                    {
                        DoorId = door.ValueRO.DoorId,
                        OldState = oldState,
                        NewState = doorState.CurrentState,
                        Reason = reason
                    });
                }

                stateRef.ValueRW = doorState;
            }
        }


        [BurstCompile]
        private FixedString128Bytes GetTransitionReason(DoorState oldState, DoorState newState, in DoorStateComponent state, in DoorComponent door)
        {
            if (oldState == DoorState.Opening && newState == DoorState.Open)
                return new FixedString128Bytes("Animation completed");
            if (oldState == DoorState.Closing && newState == DoorState.Closed)
                return new FixedString128Bytes("Animation completed");
            if (oldState == DoorState.Open && newState == DoorState.Closing)
            {
                if (state.EntitiesInTrigger == 0)
                    return new FixedString128Bytes("Auto-close timer expired");
                return new FixedString128Bytes("Entities left trigger");
            }
            return new FixedString128Bytes("State machine transition");
        }

        [BurstCompile]
        private void ProcessOpening(ref DoorStateComponent state, in DoorComponent door, Entity entity, ref SystemState systemState)
        {
            if (state.StateTimer >= door.AnimationDuration)
            {
                state.PreviousState = state.CurrentState;
                state.CurrentState = DoorState.Open;
                state.StateTimer = 0f;
            }
        }

        [BurstCompile]
        private void ProcessOpen(ref DoorStateComponent state, in DoorComponent door, Entity entity, ref SystemState systemState)
        {
            if (door.Type == DoorType.SlidingSingle || door.Type == DoorType.SlidingDouble)
            {
                if (state.EntitiesInTrigger == 0 && state.StateTimer >= door.AutoCloseDelay)
                {
                    state.PreviousState = state.CurrentState;
                    state.CurrentState = DoorState.Closing;
                    state.StateTimer = 0f;
                    state.ShouldPlayCloseSound = 1;
                }
            }
        }

        [BurstCompile]
        private void ProcessClosing(ref DoorStateComponent state, in DoorComponent door, Entity entity, ref SystemState systemState)
        {
            if (state.StateTimer >= door.AnimationDuration)
            {
                state.PreviousState = state.CurrentState;
                state.CurrentState = DoorState.Closed;
                state.StateTimer = 0f;
            }
        }
    }
}
