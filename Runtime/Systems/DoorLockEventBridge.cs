using jeanf.EventSystem;
using Unity.Entities;
using UnityEngine;

namespace AutomaticDoorSystem
{
    public class DoorLockEventBridge : MonoBehaviour
    {
        [Header("Event Channel Integration")]
        [Tooltip("IntEventChannelSO asset for door lock events (assign LockDoors.asset)")]
        public IntEventChannelSO lockEventChannel;

        [Tooltip("IntEventChannelSO asset for door unlock events (assign UnlockDoors.asset)")]
        public IntEventChannelSO unlockEventChannel;

        private EntityManager _entityManager;

        private void OnEnable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _entityManager = world.EntityManager;
            }

            SubscribeToEventChannels();
        }

        private void OnDisable()
        {
            UnsubscribeFromEventChannels();
        }

        public void LockDoor(int doorId)
        {
            CreateLockEvent(doorId, shouldLock: true);
        }

        public void UnlockDoor(int doorId)
        {
            CreateLockEvent(doorId, shouldLock: false);
        }

        public void LockDoorRange(int startId, int endId)
        {
            for (int i = startId; i <= endId; i++)
            {
                LockDoor(i);
            }
        }

        public void UnlockDoorRange(int startId, int endId)
        {
            for (int i = startId; i <= endId; i++)
            {
                UnlockDoor(i);
            }
        }

        private void CreateLockEvent(int doorId, bool shouldLock)
        {
            var eventEntity = _entityManager.CreateEntity();
            _entityManager.AddComponentData(eventEntity, new DoorLockEventComponent
            {
                DoorId = doorId,
                ShouldLock = (byte)(shouldLock ? 1 : 0)
            });
        }

        private void SubscribeToEventChannels()
        {
            if (lockEventChannel != null)
            {
                lockEventChannel.OnEventRaised += LockDoor;
            }

            if (unlockEventChannel != null)
            {
                unlockEventChannel.OnEventRaised += UnlockDoor;
            }
        }

        private void UnsubscribeFromEventChannels()
        {
            if (lockEventChannel != null)
            {
                lockEventChannel.OnEventRaised -= LockDoor;
            }

            if (unlockEventChannel != null)
            {
                unlockEventChannel.OnEventRaised -= UnlockDoor;
            }
        }
    }
}
