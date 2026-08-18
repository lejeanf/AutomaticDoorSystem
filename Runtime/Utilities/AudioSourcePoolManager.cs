using System.Collections;
using AutomaticDoorSystem.Utilities;
using SteamAudio;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace AutomaticDoorSystem
{
    /// <summary>
    /// Keeps a small pool of AudioSources parked on the doors nearest the player.
    /// Doors, their positions and their <see cref="DoorAudioConfiguration"/> all come from
    /// <see cref="DoorDataBridge"/> (i.e. straight off the baked entities), so no per-door
    /// companion object is needed in the main scene.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class AudioSourcePoolManager : MonoBehaviour
    {
        public static AudioSourcePoolManager Instance { get; private set; }

        #region Configuration

        [Header("Prefab Configuration")]
        [Tooltip("Prefab to spawn for each pooled AudioSource (must have an AudioSource, optionally a SteamAudioSource).\n" +
            "If null, a basic AudioSource GameObject will be created.")]
        public GameObject audioSourcePrefab;

        [Header("Pool Configuration")]
        [Tooltip("Maximum number of AudioSources in the pool (default: 25)")]
        [Range(2, 100)]
        public int maxPoolSize = 25;

        [Tooltip("Distance from player (Camera.main) at which AudioSources are activated (default: 25m)")]
        [Range(5f, 100f)]
        public float cullingDistance = 25f;

        [Tooltip("Frequency of distance checks in seconds (default: 0.5s)")]
        [Range(0.1f, 2f)]
        public float distanceCheckInterval = 0.5f;

        [Tooltip("Minimum distance between AudioSources to prevent overlap (meters)")]
        [Range(0f, 5f)]
        public float minimumSourceSpacing = 0.5f;

        [Tooltip("How much closer a new door must be to steal an AudioSource from an assigned door (multiplier). Higher = more stable assignments.")]
        [Range(0.5f, 2f)]
        public float reassignmentThreshold = 1.3f;

        [Tooltip("Keep AudioSources assigned to out-of-range doors until needed. Reduces reassignments when player moves back and forth.")]
        public bool keepOutOfRangeAssignments = true;

        [Header("Audio Fade Settings")]
        [Tooltip("Duration of fade in/out when activating/deactivating AudioSources (default: 0.1s)")]
        [Range(0.01f, 1f)]
        public float fadeDuration = 0.1f;

        #endregion

        #region Private Fields

        private Transform _cameraTransform;
        private AudioSource[] _audioSourcePool;
        private Transform _poolContainer;
        private PooledAudioSourceState[] _poolStates;
        private DoorSelectionStrategy _selectionStrategy;
        private Coroutine _distanceCheckCoroutine;
        private WaitForSeconds _distanceCheckWait;
        private bool _duplicateIdWarningLogged;

        #endregion

        #region Structs

        private struct PooledAudioSourceState
        {
            public AudioSource audioSource;
            public SteamAudioSource steamAudioSource;
            public int assignedDoorNumber;
            public float targetVolume;
            public Coroutine fadeCoroutine;
            public bool isPlayingAudio;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            if (DoorDataBridge.Instance == null && GetComponent<DoorDataBridge>() == null)
            {
                gameObject.AddComponent<DoorDataBridge>();
            }

            InitializePool();
            _selectionStrategy = new DoorSelectionStrategy(500, maxPoolSize, Allocator.Persistent);
            _distanceCheckWait = new WaitForSeconds(distanceCheckInterval);
            CacheCameraReference();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_selectionStrategy.IsCreated)
            {
                _selectionStrategy.Dispose();
            }
        }

        private void OnEnable()
        {
            StartDistanceChecks();
        }

        private void OnDisable()
        {
            StopDistanceChecks();

            if (_poolStates == null) return;

            for (int i = 0; i < _poolStates.Length; i++)
            {
                if (_poolStates[i].fadeCoroutine != null)
                {
                    StopCoroutine(_poolStates[i].fadeCoroutine);
                }
            }
        }

        #endregion

        #region Initialization

        private void InitializePool()
        {
            _poolContainer = new GameObject("AudioSource Pool").transform;
            _poolContainer.SetParent(transform);
            _poolContainer.localPosition = Vector3.zero;

            _audioSourcePool = new AudioSource[maxPoolSize];
            _poolStates = new PooledAudioSourceState[maxPoolSize];

            bool usingPrefab = audioSourcePrefab != null;

            for (int i = 0; i < maxPoolSize; i++)
            {
                GameObject audioSourceObj;
                AudioSource audioSource;

                if (usingPrefab)
                {
                    audioSourceObj = Instantiate(audioSourcePrefab, _poolContainer);
                    audioSourceObj.name = $"PooledAudioSource_{i:D2}";
                    audioSourceObj.transform.localPosition = Vector3.zero;

                    audioSource = audioSourceObj.GetComponent<AudioSource>();

                    if (audioSource == null)
                    {
                        audioSource = audioSourceObj.AddComponent<AudioSource>();
                        ConfigureAudioSource(audioSource);
                    }
                }
                else
                {
                    audioSourceObj = new GameObject($"PooledAudioSource_{i:D2}");
                    audioSourceObj.transform.SetParent(_poolContainer);
                    audioSourceObj.transform.localPosition = Vector3.zero;

                    audioSource = audioSourceObj.AddComponent<AudioSource>();
                    ConfigureAudioSource(audioSource);
                }

                audioSource.playOnAwake = false;

                _audioSourcePool[i] = audioSource;

                _poolStates[i] = new PooledAudioSourceState
                {
                    audioSource = audioSource,
                    steamAudioSource = audioSourceObj.GetComponent<SteamAudioSource>(),
                    assignedDoorNumber = -1,
                    targetVolume = 1f,
                    fadeCoroutine = null,
                    isPlayingAudio = false
                };

                audioSourceObj.SetActive(true);
                audioSource.volume = 0f;
                audioSource.mute = true;
            }
        }

        private void ConfigureAudioSource(AudioSource audioSource)
        {
            audioSource.spatialBlend = 1f; // 3D sound
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 25f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
        }

        private void CacheCameraReference()
        {
            if (Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
        }

        #endregion

        #region Distance-Based Activation

        private void StartDistanceChecks()
        {
            if (_distanceCheckCoroutine != null)
            {
                StopCoroutine(_distanceCheckCoroutine);
            }

            UpdateAudioSourceActivation();

            _distanceCheckCoroutine = StartCoroutine(DistanceCheckRoutine());
        }

        private void StopDistanceChecks()
        {
            if (_distanceCheckCoroutine != null)
            {
                StopCoroutine(_distanceCheckCoroutine);
                _distanceCheckCoroutine = null;
            }
        }

        private IEnumerator DistanceCheckRoutine()
        {
            while (true)
            {
                yield return _distanceCheckWait;
                UpdateAudioSourceActivation();
            }
        }

        private void UpdateAudioSourceActivation()
        {
            if (_cameraTransform == null)
            {
                CacheCameraReference();
                if (_cameraTransform == null) return;
            }

            var bridge = DoorDataBridge.Instance;
            if (bridge == null || !_selectionStrategy.IsCreated) return;

            var allDoors = bridge.GetAllDoorInfo();
            if (allDoors == null || allDoors.Count == 0) return;

            float3 playerPosition = _cameraTransform.position;

            _selectionStrategy.BeginSelection();

            for (int i = 0; i < allDoors.Count; i++)
            {
                var doorInfo = allDoors[i];

                // A door with no audio config has nothing to play - don't let it hold a slot.
                if (doorInfo.audioConfig == null) continue;

                _selectionStrategy.AddCandidate(doorInfo.doorId, doorInfo.audioPosition, playerPosition);
            }

            _selectionStrategy.FilterByDistance(cullingDistance);
            _selectionStrategy.SortByDistance();

            int duplicateIds = _selectionStrategy.RemoveDuplicateIds();
            if (duplicateIds > 0 && !_duplicateIdWarningLogged)
            {
                _duplicateIdWarningLogged = true;
                Debug.LogError(
                    $"[AudioSourcePoolManager] {duplicateIds} door(s) in range share a Door Id with another door. " +
                    "AudioSources are assigned per Door Id, so the duplicates stay silent. " +
                    "Run Tools > AutomaticDoorSystem > Setup Validator to find and renumber them.", this);
            }

            _selectionStrategy.RemoveSpatialDuplicates(minimumSourceSpacing);

            // A source that is mid-clip must not be stolen out from under the sound it is playing.
            for (int i = 0; i < maxPoolSize; i++)
            {
                var state = _poolStates[i];
                _selectionStrategy.SetSlotLocked(i,
                    state.isPlayingAudio && state.audioSource != null && state.audioSource.isPlaying);
            }

            _selectionStrategy.AssignPoolSlots(maxPoolSize, keepOutOfRangeAssignments, reassignmentThreshold);

            for (int i = 0; i < maxPoolSize; i++)
            {
                int doorId = _selectionStrategy.GetDoorIdForSlot(i);

                if (doorId == -1)
                {
                    DeactivateAudioSource(i);
                    continue;
                }

                if (bridge.TryGetDoorInfo(doorId, out var doorInfo) && doorInfo.audioConfig != null)
                {
                    ActivateAudioSourceForDoor(i, doorInfo);
                }
                else
                {
                    // The door went away (subscene unloaded) - release the slot.
                    _selectionStrategy.UnassignSlot(i);
                    DeactivateAudioSource(i);
                }
            }
        }

        private void ActivateAudioSourceForDoor(int poolIndex, DoorDataBridge.DoorInfo doorInfo)
        {
            PooledAudioSourceState state = _poolStates[poolIndex];
            if (state.audioSource == null) return;

            if (state.isPlayingAudio && state.audioSource.isPlaying)
            {
                state.audioSource.transform.position = doorInfo.audioPosition;
                return;
            }

            if (state.assignedDoorNumber == doorInfo.doorId)
            {
                state.audioSource.transform.position = doorInfo.audioPosition;

                if (DoorAudioBridge.Instance != null)
                {
                    DoorAudioBridge.Instance.RegisterAudioSource(doorInfo.doorId, state.audioSource, doorInfo.audioConfig);
                }

                return;
            }

            if (state.assignedDoorNumber != -1 && DoorAudioBridge.Instance != null)
            {
                DoorAudioBridge.Instance.UnregisterAudioSource(state.assignedDoorNumber, state.audioSource);
            }

            var config = doorInfo.audioConfig;
            state.targetVolume = config.volume;
            config.ApplyToAudioSource(state.audioSource);
            if (state.steamAudioSource != null)
            {
                config.ApplyToSteamAudioSource(state.steamAudioSource);
            }

            state.audioSource.transform.position = doorInfo.audioPosition;

            if (state.fadeCoroutine != null)
            {
                StopCoroutine(state.fadeCoroutine);
            }

            if (state.audioSource.mute || state.audioSource.volume < 0.01f)
            {
                state.audioSource.mute = false;
                state.audioSource.volume = 0f;
                state.fadeCoroutine = StartCoroutine(FadeVolume(poolIndex, state.targetVolume, fadeDuration));
            }
            else
            {
                state.audioSource.mute = false;
                state.audioSource.volume = state.targetVolume;
            }

            if (DoorAudioBridge.Instance != null)
            {
                DoorAudioBridge.Instance.RegisterAudioSource(doorInfo.doorId, state.audioSource, config);
            }

            state.assignedDoorNumber = doorInfo.doorId;
            _poolStates[poolIndex] = state;
        }

        private void DeactivateAudioSource(int poolIndex)
        {
            PooledAudioSourceState state = _poolStates[poolIndex];

            if (state.assignedDoorNumber == -1 || state.audioSource == null)
            {
                return;
            }

            if (state.isPlayingAudio && state.audioSource.isPlaying)
            {
                return;
            }

            if (state.fadeCoroutine != null)
            {
                StopCoroutine(state.fadeCoroutine);
            }

            state.fadeCoroutine = StartCoroutine(FadeOutAndDeactivate(poolIndex));

            _poolStates[poolIndex] = state;
        }

        private IEnumerator FadeVolume(int poolIndex, float targetVolume, float duration)
        {
            PooledAudioSourceState state = _poolStates[poolIndex];
            if (state.audioSource == null) yield break;

            float startVolume = state.audioSource.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                state.audioSource.volume = Mathf.Lerp(startVolume, targetVolume, t);
                yield return null;
            }

            state.audioSource.volume = targetVolume;

            state = _poolStates[poolIndex];
            state.fadeCoroutine = null;
            _poolStates[poolIndex] = state;
        }

        private IEnumerator FadeOutAndDeactivate(int poolIndex)
        {
            PooledAudioSourceState state = _poolStates[poolIndex];
            if (state.audioSource == null) yield break;

            int previousDoorNumber = state.assignedDoorNumber;
            float startVolume = state.audioSource.volume;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                state.audioSource.volume = Mathf.Lerp(startVolume, 0f, t);
                yield return null;
            }

            state.audioSource.volume = 0f;
            state.audioSource.mute = true;

            if (previousDoorNumber != -1 && DoorAudioBridge.Instance != null)
            {
                DoorAudioBridge.Instance.UnregisterAudioSource(previousDoorNumber, state.audioSource);
            }

            state = _poolStates[poolIndex];
            state.fadeCoroutine = null;
            state.assignedDoorNumber = -1;
            _poolStates[poolIndex] = state;
        }

        #endregion

        #region Public API

        public void NotifyAudioPlayback(int doorNumber, AudioSource audioSource, float clipLength)
        {
            if (_poolStates == null) return;

            for (int i = 0; i < _poolStates.Length; i++)
            {
                if (_poolStates[i].audioSource != audioSource || _poolStates[i].assignedDoorNumber != doorNumber)
                    continue;

                var state = _poolStates[i];
                state.isPlayingAudio = true;
                _poolStates[i] = state;

                StartCoroutine(ClearPlaybackFlag(i, clipLength));
                break;
            }
        }

        private IEnumerator ClearPlaybackFlag(int poolIndex, float duration)
        {
            yield return new WaitForSeconds(duration);

            if (poolIndex >= 0 && poolIndex < _poolStates.Length)
            {
                var state = _poolStates[poolIndex];
                state.isPlayingAudio = false;
                _poolStates[poolIndex] = state;
            }
        }

        [ContextMenu("Force Update Audio Sources")]
        public void ForceUpdateAudioSources()
        {
            UpdateAudioSourceActivation();
        }

        public int GetActiveAudioSourceCount()
        {
            int count = 0;
            for (int i = 0; i < maxPoolSize; i++)
            {
                if (_poolStates[i].assignedDoorNumber != -1)
                {
                    count++;
                }
            }
            return count;
        }

        #endregion

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (_cameraTransform == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_cameraTransform.position, cullingDistance);

            if (_poolStates == null) return;

            Gizmos.color = Color.green;
            for (int i = 0; i < maxPoolSize; i++)
            {
                if (_poolStates[i].assignedDoorNumber != -1 && _poolStates[i].audioSource != null)
                {
                    Gizmos.DrawWireSphere(_poolStates[i].audioSource.transform.position, 1f);
                }
            }
        }

        #endregion
    }
}
