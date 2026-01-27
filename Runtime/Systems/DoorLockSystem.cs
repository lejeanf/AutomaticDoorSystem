using Unity.Burst;
using Unity.Entities;

namespace AutomaticDoorSystem
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DoorDetectionSystem))]
    public partial struct DoorLockSystem : ISystem
    {
        private EntityQuery _lockEventQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DoorComponent>();
            _lockEventQuery = SystemAPI.QueryBuilder()
                .WithAll<DoorLockEventComponent>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_lockEventQuery.IsEmpty)
                return;

            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (lockEvent, eventEntity) in
                SystemAPI.Query<RefRO<DoorLockEventComponent>>()
                .WithEntityAccess())
            {
                var doorId = lockEvent.ValueRO.DoorId;
                var shouldLock = lockEvent.ValueRO.ShouldLock;

                foreach (var (door, stateRef, entity) in
                    SystemAPI.Query<RefRO<DoorComponent>, RefRW<DoorStateComponent>>()
                    .WithEntityAccess())
                {
                    if (door.ValueRO.DoorId == doorId)
                    {
                        var doorState = stateRef.ValueRW;

                        if (shouldLock == 1)
                        {
                            doorState.IsLocked = 1;

                            if (doorState.CurrentState != DoorState.Closed &&
                                doorState.CurrentState != DoorState.Closing)
                            {
                                doorState.PreviousState = doorState.CurrentState;
                                doorState.CurrentState = DoorState.Closing;
                                doorState.StateTimer = 0f;
                                doorState.ShouldPlayCloseSound = 1;
                            }
                            else if (doorState.CurrentState == DoorState.Closed)
                            {
                                var audioEvent = ecb.CreateEntity();
                                ecb.AddComponent(audioEvent, new DoorAudioEventComponent
                                {
                                    DoorId = doorId,
                                    EventType = AudioEventType.Lock,
                                    SoundId = 0,
                                    ClipName = new Unity.Collections.FixedString64Bytes(),
                                    Position = new Unity.Mathematics.float3()
                                });
                            }
                        }
                        else
                        {
                            doorState.IsLocked = 0;

                            var audioEvent = ecb.CreateEntity();
                            ecb.AddComponent(audioEvent, new DoorAudioEventComponent
                            {
                                DoorId = doorId,
                                EventType = AudioEventType.Unlock,
                                SoundId = 0,
                                ClipName = new Unity.Collections.FixedString64Bytes(),
                                Position = new Unity.Mathematics.float3()
                            });
                        }

                        stateRef.ValueRW = doorState;
                        break; 
                    }
                }

                ecb.DestroyEntity(eventEntity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
