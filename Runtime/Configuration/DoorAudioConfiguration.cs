using UnityEngine;
using SteamAudio;
using jeanf.propertyDrawer;

namespace AutomaticDoorSystem
{
    [ScriptableObjectDrawer]
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

        [Header("Steam Audio - HRTF Settings")]
        [Tooltip("Enable Steam Audio spatialization")]
        public bool useSteamAudio = true;

        [Tooltip("Enable binaural rendering using HRTF")]
        public bool directBinaural = true;

        [Tooltip("HRTF interpolation method")]
        public HRTFInterpolation hrtfInterpolation = HRTFInterpolation.Nearest;

        [Header("Steam Audio - Attenuation Settings")]
        [Tooltip("Enable distance attenuation")]
        public bool distanceAttenuation = true;

        [Tooltip("Distance attenuation input mode")]
        public SteamAudio.DistanceAttenuationInput distanceAttenuationInput = SteamAudio.DistanceAttenuationInput.CurveDriven;

        [Tooltip("Enable air absorption")]
        public bool airAbsorption = true;

        [Tooltip("Air absorption input mode")]
        public SteamAudio.AirAbsorptionInput airAbsorptionInput = SteamAudio.AirAbsorptionInput.SimulationDefined;

        [Header("Steam Audio - Directivity Settings")]
        [Tooltip("Enable source directivity")]
        public bool directivity = true;

        [Tooltip("Directivity input mode")]
        public SteamAudio.DirectivityInput directivityInput = SteamAudio.DirectivityInput.SimulationDefined;

        [Tooltip("Dipole weight (0 = omnidirectional, 1 = dipole)")]
        [Range(0f, 1f)]
        public float dipoleWeight = 0f;

        [Tooltip("Dipole power (sharpness of directivity pattern)")]
        [Range(0f, 4f)]
        public float dipolePower = 0f;

        [Header("Steam Audio - Occlusion Settings")]
        [Tooltip("Enable occlusion")]
        public bool occlusion = true;

        [Tooltip("Occlusion input mode")]
        public SteamAudio.OcclusionInput occlusionInput = SteamAudio.OcclusionInput.SimulationDefined;

        [Tooltip("Occlusion calculation type")]
        public SteamAudio.OcclusionType occlusionType = SteamAudio.OcclusionType.Raycast;

        [Tooltip("Enable sound transmission through occluders")]
        public bool transmission = false;

        [Header("Steam Audio - Direct Mix Settings")]
        [Tooltip("Direct sound mix level")]
        [Range(0f, 1f)]
        public float directMixLevel = 1f;

        [Header("Steam Audio - Reflections Settings")]
        [Tooltip("Enable reflections")]
        public bool reflections = true;

        [Tooltip("Reflections calculation type")]
        public SteamAudio.ReflectionsType reflectionsType = SteamAudio.ReflectionsType.Realtime;

        [Tooltip("Use distance curve for reflections attenuation")]
        public bool useDistanceCurveForReflections = false;

        [Tooltip("Apply HRTF to reflections")]
        public bool applyHRTFToReflections = false;

        [Tooltip("Reflections mix level")]
        [Range(0f, 1f)]
        public float reflectionsMixLevel = 1f;

        [Header("Steam Audio - Pathing Settings")]
        [Tooltip("Enable pathing simulation (requires Pathing Probe Batch to be assigned)")]
        public bool pathing = false;

        [Tooltip("Reference to Steam Audio Probe Batch for pathing")]
        public SteamAudioProbeBatch pathingProbeBatch;

        [Tooltip("Enable path validation")]
        public bool pathValidation = true;

        [Tooltip("Find alternate paths")]
        public bool findAlternatePaths = true;

        [Tooltip("Apply HRTF to pathing")]
        public bool applyHRTFToPathing = false;

        [Tooltip("Pathing mix level")]
        [Range(0f, 1f)]
        public float pathingMixLevel = 1f;

        public void ApplyToAudioSource(AudioSource audioSource)
        {
            if (audioSource == null) return;

            audioSource.volume = volume;
            audioSource.spatialBlend = spatialBlend;
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
            audioSource.rolloffMode = rolloffMode;
        }

        public void ApplyToSteamAudioSource(SteamAudioSource steamAudioSource)
        {
            if (steamAudioSource == null || !useSteamAudio) return;

            steamAudioSource.directBinaural = directBinaural;
            steamAudioSource.interpolation = hrtfInterpolation;

            steamAudioSource.distanceAttenuation = distanceAttenuation;
            steamAudioSource.distanceAttenuationInput = distanceAttenuationInput;
            steamAudioSource.airAbsorption = airAbsorption;
            steamAudioSource.airAbsorptionInput = airAbsorptionInput;

            steamAudioSource.directivity = directivity;
            steamAudioSource.directivityInput = directivityInput;
            steamAudioSource.dipoleWeight = dipoleWeight;
            steamAudioSource.dipolePower = dipolePower;

            steamAudioSource.occlusion = occlusion;
            steamAudioSource.occlusionInput = occlusionInput;
            steamAudioSource.occlusionType = occlusionType;
            steamAudioSource.transmission = transmission;

            steamAudioSource.directMixLevel = directMixLevel;

            steamAudioSource.reflections = reflections;
            steamAudioSource.reflectionsType = reflectionsType;
            steamAudioSource.useDistanceCurveForReflections = useDistanceCurveForReflections;
            steamAudioSource.applyHRTFToReflections = applyHRTFToReflections;
            steamAudioSource.reflectionsMixLevel = reflectionsMixLevel;

            bool enablePathing = pathing && pathingProbeBatch != null;
            steamAudioSource.pathing = enablePathing;
            steamAudioSource.pathingProbeBatch = pathingProbeBatch;
            steamAudioSource.pathValidation = pathValidation;
            steamAudioSource.findAlternatePaths = findAlternatePaths;
            steamAudioSource.applyHRTFToPathing = applyHRTFToPathing;
            steamAudioSource.pathingMixLevel = pathingMixLevel;
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
}
