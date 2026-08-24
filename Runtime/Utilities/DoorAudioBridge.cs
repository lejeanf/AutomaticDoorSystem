using System.Collections.Generic;
using jeanf.audiosystems;
using Unity.Entities;
using UnityEngine;

namespace AutomaticDoorSystem
{
    [DefaultExecutionOrder(-100)]
    public class DoorAudioBridge : MonoBehaviour
    {
        public static DoorAudioBridge Instance { get; private set; }

        private EntityManager _entityManager;
        private EntityQuery _audioEventQuery;

        private Dictionary<int, AudioSource> _doorToAudioSourceCache;
        private Dictionary<int, DoorAudioConfiguration> _doorToConfigCache;
        private Dictionary<AudioSource, Sampler> _samplerCache;

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
            _samplerCache = new Dictionary<AudioSource, Sampler>();
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

            _doorToConfigCache.TryGetValue(audioEvent.DoorId, out DoorAudioConfiguration config);
            if (config == null)
            {
                return;
            }

            // Sampler path first: a SamplerData carries its own volume, in/out points and looping
            // windows, so it plays through the AudioSystems Sampler on the pooled source. Events
            // with no SamplerData authored fall back to the legacy clip lists below.
            SamplerData samplerData = config.GetSamplerDataForEventType(audioEvent.EventType);
            if (samplerData != null && samplerData.audioClip != null)
            {
                if (AudioSourcePoolManager.Instance != null)
                {
                    // Looping SamplerData plays longer than the clip; the pool's reclaim window is
                    // still keyed to the clip length, matching the legacy one-shot behaviour.
                    AudioSourcePoolManager.Instance.NotifyAudioPlayback(audioEvent.DoorId, targetSource, samplerData.audioClip.length);
                }

                var sampler = GetOrAddSampler(targetSource);
                sampler.PlayAudioClip(samplerData);
                return;
            }

            AudioClip clip = config.GetClipForEventType(audioEvent.EventType);

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

        /// <summary>
        /// The Sampler living on a pooled AudioSource, added on first use. Pooled sources are plain
        /// AudioSource prefabs (with optional Steam Audio components); the Sampler rides along and
        /// is rebound in case the pool prefab someday ships its own.
        /// </summary>
        private Sampler GetOrAddSampler(AudioSource source)
        {
            if (_samplerCache.TryGetValue(source, out var sampler) && sampler != null)
            {
                return sampler;
            }

            sampler = source.GetComponent<Sampler>();
            if (sampler == null)
            {
                sampler = source.gameObject.AddComponent<Sampler>();
            }
            sampler.audioSource = source;
            _samplerCache[source] = sampler;
            return sampler;
        }

        /// <summary>
        /// Diagnostic lookup for the registration this bridge would use for a door's next audio
        /// event. Used by the Door Audio Doctor window; not part of the playback path.
        /// </summary>
        public bool TryGetRegistration(int doorNumber, out AudioSource audioSource, out DoorAudioConfiguration config)
        {
            audioSource = null;
            config = null;
            if (_doorToAudioSourceCache == null) return false;

            _doorToConfigCache?.TryGetValue(doorNumber, out config);
            return _doorToAudioSourceCache.TryGetValue(doorNumber, out audioSource) && audioSource != null;
        }

        public void RegisterAudioSource(int doorNumber, AudioSource audioSource, DoorAudioConfiguration config)
        {
            if (_doorToAudioSourceCache == null || audioSource == null) return;

            _doorToAudioSourceCache[doorNumber] = audioSource;

            if (config != null)
            {
                _doorToConfigCache[doorNumber] = config;
            }
        }

        public void UnregisterAudioSource(int doorNumber, AudioSource audioSource)
        {
            if (_doorToAudioSourceCache == null) return;

            if (_doorToAudioSourceCache.TryGetValue(doorNumber, out var registeredSource) && registeredSource == audioSource)
            {
                _doorToAudioSourceCache.Remove(doorNumber);
                _doorToConfigCache.Remove(doorNumber);
                if (audioSource != null) _samplerCache.Remove(audioSource);
            }
        }
    }
}
