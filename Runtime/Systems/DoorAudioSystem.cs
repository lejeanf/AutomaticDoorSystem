using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace AutomaticDoorSystem
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DoorStateSystem))]
    public partial struct DoorAudioSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DoorComponent>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (stateRef, door, transform, entity) in
                SystemAPI.Query<RefRW<DoorStateComponent>, RefRO<DoorComponent>, RefRO<LocalToWorld>>()
                .WithEntityAccess())
            {
                var doorState = stateRef.ValueRW;
                var doorPosition = transform.ValueRO.Position;

                if (doorState.ShouldPlayOpenSound == 1)
                {
                    var eventEntity = ecb.CreateEntity();
                    ecb.AddComponent(eventEntity, new DoorAudioEventComponent
                    {
                        DoorId = door.ValueRO.DoorId,
                        EventType = AudioEventType.Open,
                        SoundId = 0,
                        ClipName = new Unity.Collections.FixedString64Bytes(),
                        Position = doorPosition
                    });

                    doorState.ShouldPlayOpenSound = 0;
                }

                if (doorState.ShouldPlayCloseSound == 1)
                {
                    var eventEntity = ecb.CreateEntity();
                    ecb.AddComponent(eventEntity, new DoorAudioEventComponent
                    {
                        DoorId = door.ValueRO.DoorId,
                        EventType = AudioEventType.Close,
                        SoundId = 0,
                        ClipName = new Unity.Collections.FixedString64Bytes(),
                        Position = doorPosition
                    });

                    doorState.ShouldPlayCloseSound = 0;
                }

                stateRef.ValueRW = doorState;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
