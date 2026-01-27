using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

namespace AutomaticDoorSystem
{
    public struct DoorComponent : IComponentData
    {
        public int DoorId;
        public DoorType Type;
        public DoorAxis Axis;
        public float AnimationDuration;
        public float AutoCloseDelay;
        public FixedString128Bytes RegionId;
    }

    public struct DoorStateComponent : IComponentData
    {
        public DoorState CurrentState;
        public DoorState PreviousState;
        public float StateTimer;
        public int EntitiesInTrigger;
        public byte IsLocked; // 0 = unlocked, 1 = locked
        public byte ShouldPlayOpenSound; // 0 = no, 1 = yes
        public byte ShouldPlayCloseSound; // 0 = no, 1 = yes
        public byte DirectionForward; // 0 = backward, 1 = forward (for rotating doors)
    }

    public struct DoorTransformData : IComponentData
    {
        public float3 SlideOffset;
        public float3 InitialPosition;
        public quaternion ClosedRotation;
        public quaternion OpenRotationForward;
        public quaternion OpenRotationBackward;
        public OpeningStyle OpeningStyle;
        public float3 OneWayDirection;
    }

    public struct DoorTriggerVolume : IComponentData
    {
        public float3 Size;
        public float3 Center;
        public int LayerMask; // Bitmask for entities that can trigger door
    }

    public struct DoorAudioConfig : IComponentData
    {
        public int OpenSoundId;
        public int CloseSoundId;
        public float SoundDelayOffset; // Offset for closing sound timing
    }

    public struct DoorTriggerableTag : IComponentData { }

    public struct DoorInitialPositionComponent : IComponentData
    {
        public float3 InitialPosition;
    }

    public struct DoorDebugComponent : IComponentData { }

    public struct DoubleDoorBuffer : IBufferElementData
    {
        public Entity DoorEntity;
        public byte IsLeftDoor; // 0 = right, 1 = left
        public float3 ColliderSize;   // BoxCollider size from prefab
        public float3 ColliderCenter; // BoxCollider center from prefab
        public byte HasColliderData;  // 1 if collider data was extracted from prefab
    }

    public struct DoorAudioEventComponent : IComponentData
    {
        public int DoorId;
        public AudioEventType EventType;
        public int SoundId;
        public FixedString64Bytes ClipName;
        public float3 Position; // World position of the door for 3D audio (legacy, may be removed)
    }

    public struct DoorLockEventComponent : IComponentData
    {
        public int DoorId;
        public byte ShouldLock; // 0 = unlock, 1 = lock
    }

    public struct EntityLayerComponent : IComponentData
    {
        public int Layer; // GameObject layer (0-31)
    }

    public enum DoorType : byte
    {
        RotatingSingle = 0,
        RotatingDouble = 1,
        SlidingSingle = 2,
        SlidingDouble = 3 
    }

    public enum DoorState : byte
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
        Closing = 3
    }

    public enum DoorAxis : byte
    {
        X = 0,
        Z = 1,
        NegX = 2,
        NegZ = 3
    }

    public enum AudioEventType : byte
    {
        Open = 0,
        Close = 1,
        Lock = 2,
        Unlock = 3
    }

    public enum OpeningStyle : byte
    {
        Forward = 0,   // Both doors open away from player (default behavior)
        BothWay = 1,   // Right door opens forward, left door opens backward
        OneWay = 2     // Both doors always open in a specific direction
    }
}
