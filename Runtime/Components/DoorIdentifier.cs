using UnityEngine;

namespace AutomaticDoorSystem
{
    public class DoorIdentifier : MonoBehaviour
    {
        [Tooltip("The door number this AudioSource is associated with (must match door's DoorId)")]
        public int doorNumber;

        [Header("Audio Configuration")]
        [Tooltip("Audio configuration ScriptableObject for this door (optional - uses default settings if not set)")]
        public DoorAudioConfiguration audioConfiguration;

        public DoorAudioConfiguration GetAudioConfiguration()
        {
            return audioConfiguration;
        }

        private bool _isRegistered = false;

        private void OnEnable()
        {
            RegisterWithPoolManager();
        }

        private void Start()
        {
            if (!_isRegistered)
            {
                RegisterWithPoolManager();
            }
        }

        private void OnDisable()
        {
            UnregisterFromPoolManager();
        }

        private void OnDestroy()
        {
            UnregisterFromPoolManager();
        }

        private void RegisterWithPoolManager()
        {
            if (_isRegistered)
            {
                return;
            }

            if (AudioSourcePoolManager.Instance != null)
            {
                AudioSourcePoolManager.Instance.RegisterDoor(this);
                _isRegistered = true;
            }
        }

        private void UnregisterFromPoolManager()
        {
            if (AudioSourcePoolManager.Instance != null && _isRegistered)
            {
                AudioSourcePoolManager.Instance.UnregisterDoor(this);
                _isRegistered = false;
            }
        }
    }
}
