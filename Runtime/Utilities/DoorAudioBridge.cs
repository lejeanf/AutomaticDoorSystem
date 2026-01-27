using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace AutomaticDoorSystem
{
    public class DoorAudioBridge : MonoBehaviour
    {
        public static DoorAudioBridge Instance { get; private set; }

        [Header("Fallback Audio Clips (Optional)")]
        [Tooltip("Fallback audio clips used only when a door has no configuration assigned.\n" +
            "Prefer using DoorAudioConfiguration ScriptableObjects on door identifiers.")]
        public AudioClip fallbackOpenClip;
        public AudioClip fallbackCloseClip;
        public AudioClip fallbackLockClip;
        public AudioClip fallbackUnlockClip;

        private EntityManager _entityManager;
        private EntityQuery _audioEventQuery;

        private Dictionary<int, AudioSource> _doorToAudioSourceCache;
        private Dictionary<int, DoorAudioConfiguration> _doorToConfigCache;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            _doorToAudioSourceCache = new Dictionary<int, AudioSource>();
            _doorToConfigCache = new Dictionary<int, DoorAudioConfiguration>();
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
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _entityManager = world.EntityManager;
                _audioEventQuery = _entityManager.CreateEntityQuery(typeof(DoorAudioEventComponent));
            }
        }

        private void Update()
        {
            if (_entityManager == null || _audioEventQuery == null) return;
            var eventCount = _audioEventQuery.CalculateEntityCount();

            if (eventCount > 0)
            {
                ProcessAudioEvents();
            }
        }

        private void ProcessAudioEvents()
        {
            if (_entityManager == null || _audioEventQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            var events = _audioEventQuery.ToComponentDataArray<DoorAudioEventComponent>(Unity.Collections.Allocator.Temp);
            var entities = _audioEventQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (var i = 0; i < events.Length; i++)
            {
                var audioEvent = events[i];
                ProcessSingleAudioEvent(audioEvent);
                _entityManager.DestroyEntity(entities[i]);
            }

            events.Dispose();
            entities.Dispose();
        }

        private void ProcessSingleAudioEvent(DoorAudioEventComponent audioEvent)
        {
            AudioSource targetSource = FindAudioSourceForDoor(audioEvent.DoorId);

            if (targetSource == null)
            {
                return;
            }

            AudioClip clip = GetClipForEvent(audioEvent);

            if (clip == null)
            {
                return;
            }

            if (AudioSourcePoolManager.Instance != null)
            {
                AudioSourcePoolManager.Instance.NotifyAudioPlayback(audioEvent.DoorId, targetSource, clip.length);
            }

            targetSource.PlayOneShot(clip);
        }

        private AudioSource FindAudioSourceForDoor(int doorNumber)
        {
            return _doorToAudioSourceCache.GetValueOrDefault(doorNumber);
        }

        private AudioClip GetClipForEvent(DoorAudioEventComponent audioEvent)
        {
            if (_doorToConfigCache.TryGetValue(audioEvent.DoorId, out DoorAudioConfiguration config))
            {
                if (config != null)
                {
                    AudioClip configClip = config.GetClipForEventType(audioEvent.EventType);
                    if (configClip != null)
                    {
                        return configClip;
                    }
                }
            }

            AudioClip fallbackClip = GetFallbackClipForEventType(audioEvent.EventType);
            if (fallbackClip != null)
            {
                return fallbackClip;
            }

            return null;
        }

        private AudioClip GetFallbackClipForEventType(AudioEventType eventType)
        {
            return eventType switch
            {
                AudioEventType.Open => fallbackOpenClip,
                AudioEventType.Close => fallbackCloseClip,
                AudioEventType.Lock => fallbackLockClip,
                AudioEventType.Unlock => fallbackUnlockClip,
                _ => null
            };
        }

        public void RegisterAudioSource(int doorNumber, AudioSource audioSource, DoorAudioConfiguration config = null)
        {
            if (_doorToAudioSourceCache == null || audioSource == null)
            {
                return;
            }

            if (config == null)
            {
                var identifier = audioSource.GetComponent<DoorIdentifier>();
                if (identifier != null)
                {
                    config = identifier.GetAudioConfiguration();
                }
            }

            if (_doorToAudioSourceCache.ContainsKey(doorNumber))
            {
                var existing = _doorToAudioSourceCache[doorNumber];
                if (existing == audioSource)
                {
                    return;
                }

                _doorToAudioSourceCache[doorNumber] = audioSource;

                if (config != null)
                {
                    _doorToConfigCache[doorNumber] = config;
                }
                else if (_doorToConfigCache.ContainsKey(doorNumber))
                {
                    _doorToConfigCache.Remove(doorNumber);
                }
            }
            else
            {
                _doorToAudioSourceCache.Add(doorNumber, audioSource);

                if (config != null)
                {
                    _doorToConfigCache.Add(doorNumber, config);
                }
            }
        }

        public void UnregisterAudioSource(int doorNumber, AudioSource audioSource)
        {
            if (_doorToAudioSourceCache == null)
            {
                return;
            }

            if (!_doorToAudioSourceCache.TryGetValue(doorNumber, out var registeredSource)) return;
            if (registeredSource == audioSource)
            {
                _doorToAudioSourceCache.Remove(doorNumber);

                if (_doorToConfigCache != null && _doorToConfigCache.ContainsKey(doorNumber))
                {
                    _doorToConfigCache.Remove(doorNumber);
                }
            }
        }
    }
}
