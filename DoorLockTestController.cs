using jeanf.EventSystem;
using UnityEngine;

namespace AutomaticDoorSystem
{
    /// <summary>
    /// Example test controller demonstrating how to send lock/unlock events to doors.
    /// This script should be placed in the Main scene (NOT in subscenes).
    ///
    /// USAGE EXAMPLES:
    /// 1. Attach this to a GameObject in your Main scene
    /// 2. Assign the LockDoors and UnlockDoors IntEventChannelSO assets
    /// 3. Use keyboard shortcuts or inspector buttons to test locking/unlocking
    /// 4. Set the targetDoorId to match the door ID you want to control
    ///
    /// KEYBOARD SHORTCUTS (in Play mode):
    /// - L: Lock door with targetDoorId
    /// - U: Unlock door with targetDoorId
    /// - K: Lock all doors (IDs 1-10)
    /// - I: Unlock all doors (IDs 1-10)
    /// </summary>
    public class DoorLockTestController : MonoBehaviour
    {
        [Header("Event Channels")]
        [Tooltip("Reference to LockDoors IntEventChannelSO asset")]
        public IntEventChannelSO lockDoorsChannel;

        [Tooltip("Reference to UnlockDoors IntEventChannelSO asset")]
        public IntEventChannelSO unlockDoorsChannel;

        [Header("Test Settings")]
        [Tooltip("Target door ID for lock/unlock tests")]
        public int targetDoorId = 1;

        [Tooltip("Enable keyboard shortcuts for testing")]
        public bool enableKeyboardShortcuts = true;

        [Header("Debug Info")]
        [SerializeField]
        private string lastAction = "None";

        private void Update()
        {
            if (!enableKeyboardShortcuts)
                return;

            // L = Lock target door
            if (Input.GetKeyDown(KeyCode.L))
            {
                LockDoor(targetDoorId);
            }

            // U = Unlock target door
            if (Input.GetKeyDown(KeyCode.U))
            {
                UnlockDoor(targetDoorId);
            }

            // K = Lock all doors (example: IDs 1-10)
            if (Input.GetKeyDown(KeyCode.K))
            {
                LockAllDoors();
            }

            // I = Unlock all doors (example: IDs 1-10)
            if (Input.GetKeyDown(KeyCode.I))
            {
                UnlockAllDoors();
            }
        }

        /// <summary>
        /// Lock a specific door by ID.
        /// This sends an event that crosses the Main scene -> Subscene boundary.
        /// </summary>
        public void LockDoor(int doorId)
        {
            if (lockDoorsChannel == null)
            {
                Debug.LogError("[DoorLockTestController] LockDoors channel is not assigned!");
                return;
            }

            lockDoorsChannel.RaiseEvent(doorId);
            lastAction = $"Locked door {doorId}";
            Debug.Log($"[DoorLockTestController] Sent LOCK event for door ID: {doorId}");
        }

        /// <summary>
        /// Unlock a specific door by ID.
        /// This sends an event that crosses the Main scene -> Subscene boundary.
        /// </summary>
        public void UnlockDoor(int doorId)
        {
            if (unlockDoorsChannel == null)
            {
                Debug.LogError("[DoorLockTestController] UnlockDoors channel is not assigned!");
                return;
            }

            unlockDoorsChannel.RaiseEvent(doorId);
            lastAction = $"Unlocked door {doorId}";
            Debug.Log($"[DoorLockTestController] Sent UNLOCK event for door ID: {doorId}");
        }

        /// <summary>
        /// Lock all doors in a range (example: doors 1-10).
        /// Useful for zone-based locking (e.g., "lock all doors on floor 1").
        /// </summary>
        public void LockAllDoors()
        {
            if (lockDoorsChannel == null)
            {
                Debug.LogError("[DoorLockTestController] LockDoors channel is not assigned!");
                return;
            }

            // Lock doors with IDs 1-10
            for (int i = 1; i <= 10; i++)
            {
                lockDoorsChannel.RaiseEvent(i);
            }

            lastAction = "Locked all doors (1-10)";
            Debug.Log("[DoorLockTestController] Sent LOCK events for all doors (IDs 1-10)");
        }

        /// <summary>
        /// Unlock all doors in a range (example: doors 1-10).
        /// </summary>
        public void UnlockAllDoors()
        {
            if (unlockDoorsChannel == null)
            {
                Debug.LogError("[DoorLockTestController] UnlockDoors channel is not assigned!");
                return;
            }

            // Unlock doors with IDs 1-10
            for (int i = 1; i <= 10; i++)
            {
                unlockDoorsChannel.RaiseEvent(i);
            }

            lastAction = "Unlocked all doors (1-10)";
            Debug.Log("[DoorLockTestController] Sent UNLOCK events for all doors (IDs 1-10)");
        }

        /// <summary>
        /// Example: Lock doors providing access to a specific room/zone.
        /// Multiple doors can share the same ID for zone-based control.
        /// </summary>
        public void LockZone(int zoneId)
        {
            if (lockDoorsChannel == null)
            {
                Debug.LogError("[DoorLockTestController] LockDoors channel is not assigned!");
                return;
            }

            // Send lock event with zone ID
            // All doors with this ID will be locked (supports multiple doors per zone)
            lockDoorsChannel.RaiseEvent(zoneId);
            lastAction = $"Locked zone {zoneId}";
            Debug.Log($"[DoorLockTestController] Sent LOCK event for zone ID: {zoneId} (affects all doors with this ID)");
        }

        /// <summary>
        /// Example: Unlock doors providing access to a specific room/zone.
        /// </summary>
        public void UnlockZone(int zoneId)
        {
            if (unlockDoorsChannel == null)
            {
                Debug.LogError("[DoorLockTestController] UnlockDoors channel is not assigned!");
                return;
            }

            unlockDoorsChannel.RaiseEvent(zoneId);
            lastAction = $"Unlocked zone {zoneId}";
            Debug.Log($"[DoorLockTestController] Sent UNLOCK event for zone ID: {zoneId} (affects all doors with this ID)");
        }

        private void OnValidate()
        {
            // Ensure targetDoorId is positive
            if (targetDoorId < 0)
                targetDoorId = 0;
        }
    }
}
