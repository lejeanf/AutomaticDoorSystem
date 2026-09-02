using jeanf.propertyDrawer;
using jeanf.scenemanagement;
using UnityEngine;

namespace AutomaticDoorSystem
{
    [ScriptableObjectDrawer]
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

        public enum SlidingStyleEnum
        {
            Mirrored,
            Telescopic
        }

        // Literal values for the [ShowWhen] conditions below (attribute arguments must be constants).
        private const int Rotating = (int)DoorMovementEnum.Rotating;
        private const int Sliding = (int)DoorMovementEnum.Sliding;
        private const int Double = (int)DoorCountEnum.Double;
        private const int OneWay = (int)OpeningStyle.OneWay;
        private const int Telescopic = (int)SlidingStyleEnum.Telescopic;

        [Header("Door Type Configuration")]
        [Tooltip("Movement type of the door - shared by all doors using this config")]
        public DoorMovementEnum doorMovement = DoorMovementEnum.Rotating;
        [Tooltip("Number of door panels - shared by all doors using this config")]
        public DoorCountEnum doorCount = DoorCountEnum.Single;

        // ---- Rotating doors only (hidden while Sliding is selected) ----

        [ShowWhen(nameof(doorMovement), Rotating, Header = "Opening Style (Rotating Doors)")]
        [Tooltip("Controls how rotating doors open: Forward (away from whoever approaches), BothWay (double doors: right forward/left backward), OneWay (always the same direction)")]
        public OpeningStyle openingStyle = OpeningStyle.Forward;

        [ShowWhen(nameof(doorMovement), Rotating, nameof(openingStyle), OneWay)]
        [Tooltip("Direction for OneWay style in local space (e.g., Vector3.forward opens doors forward)")]
        public Vector3 oneWayDirection = Vector3.forward;

        [ShowWhen(nameof(doorMovement), Rotating, Header = "Rotating Door Settings")]
        [Tooltip("Angle in degrees when door opens forward (typically 90°)")]
        [Range(0f, 180f)]
        public float openForwardAngle = 90f;

        [ShowWhen(nameof(doorMovement), Rotating)]
        [Tooltip("Angle in degrees when door opens backward (typically -90°)")]
        [Range(-180f, 0f)]
        public float openBackwardAngle = -90f;

        [ShowWhen(nameof(doorMovement), Rotating)]
        [Tooltip("Swap which side of the door counts as FRONT. By default the front is the door root's local +Z " +
                 "side, split at the panel pivots. Turn this on when a door model's pivot, rotation or scale puts " +
                 "local +Z on the wrong side and Forward-style doors swing TOWARD the player - it fixes the " +
                 "direction without touching the source file. Check the FRONT/BACK gizmo on a DoorAuthoring " +
                 "before and after. A single door can override this on its DoorAuthoring.")]
        public bool invertForwardSide = false;

        // ---- Sliding doors only (hidden while Rotating is selected) ----

        [ShowWhen(nameof(doorMovement), Sliding, Header = "Sliding Door Settings")]
        [Tooltip("Distance and direction to slide when opening (e.g., 1.5 units right = (1.5, 0, 0)). " +
                 "For Telescopic style this is the LEFT panel's travel.")]
        public Vector3 slideOpenOffset = new Vector3(1.5f, 0, 0);

        [ShowWhen(nameof(doorMovement), Sliding, nameof(doorCount), Double)]
        [Tooltip("Sliding Double only. Mirrored: panels part in opposite directions (default). " +
                 "Telescopic: both panels slide in the SAME direction with different spans and stack " +
                 "into one pocket - the right door leads, catches the left door, then both finish together.")]
        public SlidingStyleEnum slidingStyle = SlidingStyleEnum.Mirrored;

        [ShowWhen(nameof(doorMovement), Sliding, nameof(doorCount), Double, nameof(slidingStyle), Telescopic)]
        [Tooltip("Telescopic only: travel of the RIGHT (leading) panel. Must point in the same " +
                 "direction as Slide Open Offset and be longer.")]
        public Vector3 rightSlideOpenOffset = new Vector3(3f, 0, 0);

        [ShowWhen(nameof(doorMovement), Sliding, nameof(doorCount), Double, nameof(slidingStyle), Telescopic)]
        [Tooltip("Telescopic only: the right door stops where the left door sits (the left door " +
                 "never moves) - a partial opening.")]
        public bool openRightDoorOnly = false;

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
