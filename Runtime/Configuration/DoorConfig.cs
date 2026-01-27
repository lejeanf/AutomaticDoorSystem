using jeanf.scenemanagement;
using UnityEngine;

namespace AutomaticDoorSystem
{
    [CreateAssetMenu(fileName = "DoorConfig", menuName = "AutomaticDoorSystem/DoorConfig", order = 1)]
    public class DoorConfig : ScriptableObject
    {
        public enum DoorCountEnum
        {
            Single,
            Double
        }

        public enum DoorMovementEnum
        {
            Rotating,
            Sliding
        }
        
        public enum OpeningStyle
        {
            Forward,
            BothWay,
            OneWay
        }

        [Header("Door Type Configuration")]
        [Tooltip("Movement type of the door - shared by all doors using this config")]
        public DoorMovementEnum doorMovement = DoorMovementEnum.Rotating;
        [Tooltip("Number of door panels - shared by all doors using this config")]
        public DoorCountEnum doorCount = DoorCountEnum.Single;

        [Header("Opening Style (Rotating Double Doors Only)")]
        [Tooltip("Controls how double rotating doors open: Forward (both away from player), BothWay (right forward/left backward), OneWay (always same direction)")]
        public OpeningStyle openingStyle = OpeningStyle.Forward;

        [Tooltip("Direction for OneWay style in local space (e.g., Vector3.forward opens doors forward)")]
        public Vector3 oneWayDirection = Vector3.forward;


        [Header("Rotating Door Settings")]
        [Tooltip("Angle in degrees when door opens forward (typically 90°)")]
        [Range(0f, 180f)]
        public float openForwardAngle = 90f;

        [Tooltip("Angle in degrees when door opens backward (typically -90°)")]
        [Range(-180f, 0f)]
        public float openBackwardAngle = -90f;

        [Header("Sliding Door Settings")]
        [Tooltip("Distance and direction to slide when opening (e.g., 1.5 units right = (1.5, 0, 0))")]
        public Vector3 slideOpenOffset = new Vector3(1.5f, 0, 0);

        [Header("Animation Configuration")]
        [Range(0.1f, 5f)]
        [Tooltip("Duration for door animation in seconds")]
        public float animationDuration = 1.5f;

        [Range(0f, 10f)]
        [Tooltip("Time in seconds before door automatically closes")]
        public float autoCloseDelay = 3f;

        [Header("Door Behavior")]
        [Tooltip("Layer mask for entities that can open the door (Player, NPC, etc.)")]
        public LayerMask canOpenLayerMask = -1;

        [Tooltip("Whether doors using this config start locked")]
        public bool startLocked = false;
    }
}

