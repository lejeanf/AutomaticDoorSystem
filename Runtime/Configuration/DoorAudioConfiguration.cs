using UnityEngine;

namespace AutomaticDoorSystem
{
    [CreateAssetMenu(fileName = "DoorAudioConfig", menuName = "AutomaticDoorSystem/DoorAudioConfiguration", order = 1)]
    public class DoorAudioConfiguration : ScriptableObject
    {
        [Header("AudioSource Settings")]
        [Tooltip("Volume of the audio source")]
        [Range(0f, 1f)]
        public float volume = 1f;

        [Tooltip("Spatial blend (0 = 2D, 1 = 3D)")]
        [Range(0f, 1f)]
        public float spatialBlend = 1f;

        [Tooltip("Minimum distance for 3D audio")]
        public float minDistance = 1f;

        [Tooltip("Maximum distance for 3D audio")]
        public float maxDistance = 25f;

        [Tooltip("Audio rolloff mode")]
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

        [Header("Audio Clips")]
        [Tooltip("Sound to play when door opens (can be multiple for variation)")]
        public AudioClip[] openSoundClips;

        [Tooltip("Sound to play when door closes (can be multiple for variation)")]
        public AudioClip[] closeSoundClips;

        [Tooltip("Sound to play when door locks (can be multiple for variation)")]
        public AudioClip[] lockSoundClips;

        [Tooltip("Sound to play when door unlocks (can be multiple for variation)")]
        public AudioClip[] unlockSoundClips;

        [Header("Steam Audio Settings")]
        [Tooltip("Enable Steam Audio spatialization")]
        public bool useSteamAudio = true;

        [Tooltip("Enable HRTF (Head-Related Transfer Function) for realistic 3D audio")]
        public bool enableHRTF = true;

        [Tooltip("Enable occlusion detection")]
        public bool enableOcclusion = true;

        [Tooltip("Enable reverb")]
        public bool enableReverb = true;

        [Tooltip("Occlusion detection method")]
        public SteamAudioOcclusionMode occlusionMode = SteamAudioOcclusionMode.Raycast;

        [Tooltip("Number of transmission rays for occlusion (higher = more accurate but slower)")]
        [Range(1, 128)]
        public int transmissionRays = 16;

        [Tooltip("Direct sound mix level")]
        [Range(0f, 1f)]
        public float directMixLevel = 1f;

        public void ApplyToAudioSource(AudioSource audioSource)
        {
            if (audioSource == null) return;

            audioSource.volume = volume;
            audioSource.spatialBlend = spatialBlend;
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
            audioSource.rolloffMode = rolloffMode;
        }

        public AudioClip GetRandomOpenClip()
        {
            return GetRandomClip(openSoundClips);
        }

        public AudioClip GetRandomCloseClip()
        {
            return GetRandomClip(closeSoundClips);
        }

        public AudioClip GetRandomLockClip()
        {
            return GetRandomClip(lockSoundClips);
        }

        public AudioClip GetRandomUnlockClip()
        {
            return GetRandomClip(unlockSoundClips);
        }

        public AudioClip GetClipForEventType(AudioEventType eventType)
        {
            return eventType switch
            {
                AudioEventType.Open => GetRandomOpenClip(),
                AudioEventType.Close => GetRandomCloseClip(),
                AudioEventType.Lock => GetRandomLockClip(),
                AudioEventType.Unlock => GetRandomUnlockClip(),
                _ => null
            };
        }

        private AudioClip GetRandomClip(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
                return null;

            if (clips.Length == 1)
                return clips[0];

            return clips[Random.Range(0, clips.Length)];
        }
    }

    public enum SteamAudioOcclusionMode
    {
        Off,
        Raycast,
        PartialRaycast
    }
}
