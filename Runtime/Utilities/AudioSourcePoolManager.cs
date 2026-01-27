using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AutomaticDoorSystem
{
    public class AudioSourcePoolManager : MonoBehaviour
    {
        public static AudioSourcePoolManager Instance { get; private set; }

        #region Configuration

        [Header("Prefab Configuration")]
        [Tooltip("Prefab to spawn for each pooled AudioSource (must have AudioSource and DoorAudioSourceIdentifier components).\n" +
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
        private Vector3 _playerPosition;
        private List<DoorIdentifier> _currentDoorList;
        private AudioSource[] _audioSourcePool;
        private Transform _poolContainer;
        private DoorDistanceInfo[] _doorDistances;
        private int _doorDistanceCount;
        private PooledAudioSourceState[] _poolStates;
        private DoorDistanceComparer _distanceComparer;
        private Coroutine _distanceCheckCoroutine;
        private WaitForSeconds _distanceCheckWait;

        #endregion

        #region Structs and Classes

        private struct DoorDistanceInfo
        {
            public int doorNumber;
            public Vector3 position;
            public float sqrDistance;
            public DoorIdentifier identifier; 
        }

        private struct PooledAudioSourceState
        {
            public AudioSource audioSource;
            public int assignedDoorNumber;
            public float targetVolume;
            public Coroutine fadeCoroutine;
            public bool isPlayingAudio;
        }

        private class DoorDistanceComparer : IComparer<DoorDistanceInfo>
        {
            public int Compare(DoorDistanceInfo a, DoorDistanceInfo b)
            {
                return a.sqrDistance.CompareTo(b.sqrDistance);
            }
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

            InitializePool();
            InitializeDataStructures();
            CacheCameraReference();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            StartDistanceChecks();
        }

        private void LateUpdate()
        {
            if (_poolStates == null || _currentDoorList == null) return;

            for (int i = 0; i < _poolStates.Length; i++)
            {
                var state = _poolStates[i];
                if (state.assignedDoorNumber == -1 || state.audioSource == null) continue;

                for (int j = 0; j < _currentDoorList.Count; j++)
                {
                    var identifier = _currentDoorList[j];
                    if (identifier != null && identifier.doorNumber == state.assignedDoorNumber)
                    {
                        state.audioSource.transform.position = identifier.transform.position;
                        break;
                    }
                }
            }
        }

        private void OnDisable()
        {
            StopDistanceChecks();

            if (_poolStates != null)
            {
                for (int i = 0; i < _poolStates.Length; i++)
                {
                    if (_poolStates[i].fadeCoroutine != null)
                    {
                        StopCoroutine(_poolStates[i].fadeCoroutine);
                    }
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

        private void InitializeDataStructures()
        {
            _currentDoorList = new List<DoorIdentifier>(500);
            _doorDistances = new DoorDistanceInfo[500];
            _doorDistanceCount = 0;
            _distanceComparer = new DoorDistanceComparer();
            _distanceCheckWait = new WaitForSeconds(distanceCheckInterval);

            if (DoorAudioBridge.Instance != null)
            {
                for (int i = 0; i < maxPoolSize; i++)
                {
                    DoorAudioBridge.Instance.RegisterAudioSource(-(i + 1), _audioSourcePool[i], null);
                }
            }
        }

        private void Start()
        {
            FindAndRegisterMissingDoors();
        }

        private void FindAndRegisterMissingDoors()
        {
            var allDoorIdentifiers = FindObjectsByType<DoorIdentifier>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var identifier in allDoorIdentifiers)
            {
                if (!_currentDoorList.Contains(identifier))
                {
                    RegisterDoor(identifier);
                }
            }
        }

        private void CacheCameraReference()
        {
            if (Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
            else
            {
                Invoke(nameof(RetryCameraReference), 1f);
            }
        }

        private void RetryCameraReference()
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
                return;
            }

            if (_currentDoorList == null || _currentDoorList.Count == 0)
            {
                return;
            }

            _playerPosition = _cameraTransform.position;

            float sqrCullingDistance = cullingDistance * cullingDistance;

            _doorDistanceCount = 0;

            for (int i = 0; i < _currentDoorList.Count; i++)
            {
                DoorIdentifier identifier = _currentDoorList[i];
                if (identifier == null) continue;

                Vector3 doorPosition = identifier.transform.position;
                float sqrDist = (doorPosition - _playerPosition).sqrMagnitude;

                if (sqrDist <= sqrCullingDistance)
                {
                    _doorDistances[_doorDistanceCount] = new DoorDistanceInfo
                    {
                        doorNumber = identifier.doorNumber,
                        position = doorPosition,
                        sqrDistance = sqrDist,
                        identifier = identifier
                    };

                    _doorDistanceCount++;

                    if (_doorDistanceCount >= _doorDistances.Length)
                    {
                        break;
                    }
                }
            }

            if (_doorDistanceCount > 0)
            {
                Array.Sort(_doorDistances, 0, _doorDistanceCount, _distanceComparer);
            }

            int beforeDedup = _doorDistanceCount;
            _doorDistanceCount = RemoveDuplicatePositions(_doorDistances, _doorDistanceCount);

            int doorsToActivate = Mathf.Min(_doorDistanceCount, maxPoolSize);

            AssignAudioSourcesWithHysteresis(doorsToActivate);

            if (!keepOutOfRangeAssignments)
            {
                for (int i = doorsToActivate; i < maxPoolSize; i++)
                {
                    DeactivateAudioSource(i);
                }
            }
            else
            {
                DeactivateUnusedSlotsOnly();
            }

            int inRangeSlots = 0;
            int stickySlots = 0;
            var inRangeDoorSet = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < doorsToActivate; i++)
            {
                inRangeDoorSet.Add(_doorDistances[i].doorNumber);
            }

            for (int i = 0; i < maxPoolSize; i++)
            {
                if (_poolStates[i].assignedDoorNumber != -1)
                {
                    if (inRangeDoorSet.Contains(_poolStates[i].assignedDoorNumber))
                    {
                        inRangeSlots++;
                    }
                    else
                    {
                        stickySlots++;
                    }
                }
            }
        }

        private void ActivateAudioSourceForDoor(int poolIndex, DoorDistanceInfo doorInfo)
        {
            PooledAudioSourceState state = _poolStates[poolIndex];

            if (state.isPlayingAudio && state.audioSource.isPlaying)
            {
                state.audioSource.transform.position = doorInfo.position;
                return;
            }

            if (state.assignedDoorNumber == doorInfo.doorNumber)
            {
                state.audioSource.transform.position = doorInfo.position;
                return;
            }

            if (state.assignedDoorNumber != -1 && DoorAudioBridge.Instance != null)
            {
                DoorAudioBridge.Instance.UnregisterAudioSource(state.assignedDoorNumber, state.audioSource);
            }

            DoorAudioConfiguration config = null;
            if (doorInfo.identifier != null)
            {
                config = doorInfo.identifier.GetAudioConfiguration();
            }

            if (config != null)
            {
                state.targetVolume = config.volume;
                config.ApplyToAudioSource(state.audioSource);
            }
            else
            {
                state.targetVolume = 1f;
            }

            state.audioSource.transform.position = doorInfo.position;

            if (state.fadeCoroutine != null)
            {
                StopCoroutine(state.fadeCoroutine);
            }

            bool wasMuted = state.audioSource.mute;
            if (wasMuted || state.audioSource.volume < 0.01f)
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
                DoorAudioBridge.Instance.RegisterAudioSource(doorInfo.doorNumber, state.audioSource, config);
            }

            state.assignedDoorNumber = doorInfo.doorNumber;
            _poolStates[poolIndex] = state;
        }

        private void DeactivateAudioSource(int poolIndex)
        {
            PooledAudioSourceState state = _poolStates[poolIndex];

            if (state.assignedDoorNumber == -1)
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

        private void DeactivateUnusedSlotsOnly()
        {
            for (int i = 0; i < maxPoolSize; i++)
            {
                if (_poolStates[i].assignedDoorNumber == -1)
                {
                    DeactivateAudioSource(i);
                }
            }
        }

        private void DeactivateAllAudioSources()
        {
            for (int i = 0; i < maxPoolSize; i++)
            {
                DeactivateAudioSource(i);
            }
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
            state.fadeCoroutine = null;

            if (previousDoorNumber != -1 && DoorAudioBridge.Instance != null)
            {
                DoorAudioBridge.Instance.UnregisterAudioSource(previousDoorNumber, state.audioSource);
            }

            state.assignedDoorNumber = -1;
            _poolStates[poolIndex] = state;
        }

        private int RemoveDuplicatePositions(DoorDistanceInfo[] distances, int count)
        {
            if (count <= 1) return count;

            int writeIndex = 0;
            float sqrMinSpacing = minimumSourceSpacing * minimumSourceSpacing;

            for (int i = 0; i < count; i++)
            {
                bool isDuplicate = false;

                for (int j = 0; j < writeIndex; j++)
                {
                    float sqrDist = (distances[i].position - distances[j].position).sqrMagnitude;
                    if (sqrDist < sqrMinSpacing)
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    if (writeIndex != i)
                    {
                        distances[writeIndex] = distances[i];
                    }
                    writeIndex++;
                }
            }

            return writeIndex;
        }

        private void AssignAudioSourcesWithHysteresis(int doorsToActivate)
        {
            var currentAssignments = new System.Collections.Generic.Dictionary<int, int>();
            var inRangeDoors = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < maxPoolSize; i++)
            {
                if (_poolStates[i].assignedDoorNumber != -1)
                {
                    currentAssignments[_poolStates[i].assignedDoorNumber] = i;
                }
            }

            for (int i = 0; i < doorsToActivate; i++)
            {
                inRangeDoors.Add(_doorDistances[i].doorNumber);
            }

            bool[] poolSlotUsed = new bool[maxPoolSize];

            for (int i = 0; i < doorsToActivate; i++)
            {
                DoorDistanceInfo doorInfo = _doorDistances[i];

                if (currentAssignments.TryGetValue(doorInfo.doorNumber, out int existingPoolIndex))
                {
                    ActivateAudioSourceForDoor(existingPoolIndex, doorInfo);
                    poolSlotUsed[existingPoolIndex] = true;
                }
            }

            if (keepOutOfRangeAssignments)
            {
                for (int i = 0; i < maxPoolSize; i++)
                {
                    int assignedDoorNumber = _poolStates[i].assignedDoorNumber;
                    if (assignedDoorNumber != -1 && !poolSlotUsed[i] && !inRangeDoors.Contains(assignedDoorNumber))
                    {
                        poolSlotUsed[i] = true;
                    }
                }
            }

            int nextFreeSlot = 0;
            for (int i = 0; i < doorsToActivate; i++)
            {
                DoorDistanceInfo doorInfo = _doorDistances[i];

                if (currentAssignments.ContainsKey(doorInfo.doorNumber))
                {
                    continue;
                }

                while (nextFreeSlot < maxPoolSize && _poolStates[nextFreeSlot].assignedDoorNumber != -1)
                {
                    nextFreeSlot++;
                }

                if (nextFreeSlot < maxPoolSize)
                {
                    ActivateAudioSourceForDoor(nextFreeSlot, doorInfo);
                    poolSlotUsed[nextFreeSlot] = true;
                    nextFreeSlot++;
                }
                else
                {
                    int victimSlot = FindSlotToReassign(doorInfo, inRangeDoors);
                    if (victimSlot != -1)
                    {
                        ActivateAudioSourceForDoor(victimSlot, doorInfo);
                        poolSlotUsed[victimSlot] = true;
                    }
                }
            }
        }

        private int FindSlotToReassign(DoorDistanceInfo newDoor, System.Collections.Generic.HashSet<int> inRangeDoors)
        {
            int outOfRangeSlot = -1;
            int bestInRangeSlot = -1;
            float bestRatio = reassignmentThreshold;

            for (int i = 0; i < maxPoolSize; i++)
            {
                var state = _poolStates[i];
                if (state.assignedDoorNumber == -1) continue;

                if (state.isPlayingAudio && state.audioSource.isPlaying) continue;

                bool isInRange = inRangeDoors.Contains(state.assignedDoorNumber);

                if (!isInRange)
                {
                    if (outOfRangeSlot == -1)
                    {
                        outOfRangeSlot = i;
                    }
                }
                else
                {
                    float currentDoorSqrDist = float.MaxValue;
                    for (int j = 0; j < _doorDistanceCount; j++)
                    {
                        if (_doorDistances[j].doorNumber == state.assignedDoorNumber)
                        {
                            currentDoorSqrDist = _doorDistances[j].sqrDistance;
                            break;
                        }
                    }

                    float distanceRatio = currentDoorSqrDist / newDoor.sqrDistance;

                    if (distanceRatio > bestRatio)
                    {
                        bestRatio = distanceRatio;
                        bestInRangeSlot = i;
                    }
                }
            }

            if (outOfRangeSlot != -1)
            {
                return outOfRangeSlot;
            }

            return bestInRangeSlot;
        }

        #endregion


        #region Public API

        public void NotifyAudioPlayback(int doorNumber, AudioSource audioSource, float clipLength)
        {
            if (_poolStates == null) return;

            // Find the pool index for this AudioSource
            for (int i = 0; i < _poolStates.Length; i++)
            {
                if (_poolStates[i].audioSource == audioSource && _poolStates[i].assignedDoorNumber == doorNumber)
                {
                    var state = _poolStates[i];
                    state.isPlayingAudio = true;
                    _poolStates[i] = state;

                    StartCoroutine(ClearPlaybackFlag(i, clipLength));
                    break;
                }
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

        public void RegisterDoor(DoorIdentifier doorIdentifier)
        {
            if (doorIdentifier == null || _currentDoorList == null)
            {
                return;
            }

            if (_currentDoorList.Contains(doorIdentifier))
            {
                return;
            }

            _currentDoorList.Add(doorIdentifier);
        }

        public void UnregisterDoor(DoorIdentifier doorIdentifier)
        {
            if (doorIdentifier == null || _currentDoorList == null)
            {
                return;
            }

            _currentDoorList.Remove(doorIdentifier);
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

        public int GetRegisteredDoorCount()
        {
            return _currentDoorList?.Count ?? 0;
        }

        #endregion

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (_cameraTransform == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_cameraTransform.position, cullingDistance);

            if (_poolStates != null)
            {
                Gizmos.color = Color.green;
                for (int i = 0; i < maxPoolSize; i++)
                {
                    if (_poolStates[i].assignedDoorNumber != -1 && _poolStates[i].audioSource != null)
                    {
                        Gizmos.DrawWireSphere(_poolStates[i].audioSource.transform.position, 1f);
                    }
                }
            }
        }

        #endregion
    }
}
