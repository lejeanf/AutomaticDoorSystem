using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace AutomaticDoorSystem.Utilities
{
    public class DoorDataBridge : MonoBehaviour
    {
        public static DoorDataBridge Instance { get; private set; }

        public struct DoorInfo
        {
            public int doorId;
            public Vector3 position;
            public Quaternion rotation;
            public DoorType doorType;
            public OpeningStyle openingStyle;
            public Vector3 slideOffset;
            public Vector3 triggerSize;
            public Vector3 triggerCenter;
            public bool isLocked;
            public Entity rootEntity;
            public DoorIdentifier doorIdentifier;
        }

        public struct DoorPanelInfo
        {
            public Entity panelEntity;
            public Vector3 position;
            public Quaternion rotation;
            public bool isLeftPanel;
            public Vector3 colliderSize;
            public Vector3 colliderCenter;
            public bool hasColliderData;
        }

        private EntityManager _entityManager;
        private EntityQuery _doorQuery;
        private List<DoorInfo> _cachedDoorInfo;
        private Dictionary<int, DoorIdentifier> _doorIdentifiers;
        private Dictionary<int, Entity> _doorEntityCache;
        private float _cacheRefreshTimer;
        private const float CACHE_REFRESH_INTERVAL = 1f;
        private DoorPanelInfo[] _panelArrayCache = new DoorPanelInfo[4];

        public int GetDoorPanelCount(int doorId)
        {
            if (_entityManager == null || _doorQuery == null)
                return 0;

            var doorComponents = _doorQuery.ToComponentDataArray<DoorComponent>(Allocator.Temp);
            var entities = _doorQuery.ToEntityArray(Allocator.Temp);

            int panelCount = 0;

            for (int i = 0; i < doorComponents.Length; i++)
            {
                if (doorComponents[i].DoorId == doorId)
                {
                    if (_entityManager.HasBuffer<DoubleDoorBuffer>(entities[i]))
                    {
                        var buffer = _entityManager.GetBuffer<DoubleDoorBuffer>(entities[i]);
                        panelCount = buffer.Length;
                    }
                    break;
                }
            }

            doorComponents.Dispose();
            entities.Dispose();

            return panelCount;
        }

        public bool TryGetDoorPanels(int doorId, out DoorPanelInfo[] panels, out int panelCount)
        {
            panels = null;
            panelCount = 0;

            if (_entityManager == null || !_doorEntityCache.TryGetValue(doorId, out Entity doorEntity))
                return false;

            if (!_entityManager.HasBuffer<DoubleDoorBuffer>(doorEntity))
                return false;

            var buffer = _entityManager.GetBuffer<DoubleDoorBuffer>(doorEntity);
            panelCount = buffer.Length;

            if (_panelArrayCache.Length < panelCount)
            {
                _panelArrayCache = new DoorPanelInfo[panelCount];
            }

            for (int j = 0; j < panelCount; j++)
            {
                var bufferElement = buffer[j];
                var panelEntity = bufferElement.DoorEntity;

                if (_entityManager.HasComponent<LocalToWorld>(panelEntity))
                {
                    var transform = _entityManager.GetComponentData<LocalToWorld>(panelEntity);
                    bool isLeftPanel = bufferElement.IsLeftDoor == 1;

                    _panelArrayCache[j] = new DoorPanelInfo
                    {
                        panelEntity = panelEntity,
                        position = transform.Position,
                        rotation = transform.Rotation,
                        isLeftPanel = isLeftPanel,
                        colliderSize = bufferElement.ColliderSize,
                        colliderCenter = bufferElement.ColliderCenter,
                        hasColliderData = bufferElement.HasColliderData == 1
                    };
                }
            }

            panels = _panelArrayCache;
            return true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            _cachedDoorInfo = new List<DoorInfo>(500);
            _doorIdentifiers = new Dictionary<int, DoorIdentifier>();
            _doorEntityCache = new Dictionary<int, Entity>();
            _cacheRefreshTimer = 0f;
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
                _doorQuery = _entityManager.CreateEntityQuery(
                    typeof(DoorComponent),
                    typeof(DoorStateComponent),
                    typeof(DoorTransformData),
                    typeof(LocalToWorld),
                    typeof(DoorTriggerVolume)
                );
            }

            _cacheRefreshTimer = 0f;
        }

        private void Start()
        {
            RegisterAllDoorIdentifiers();
            Invoke(nameof(InitialRefresh), 0.2f);
        }

        private void RegisterAllDoorIdentifiers()
        {
            var allIdentifiers =  FindObjectsByType<DoorIdentifier>(FindObjectsInactive.Include, FindObjectsSortMode.None);;
            foreach (var identifier in allIdentifiers)
            {
                RegisterDoorIdentifier(identifier);
            }
        }

        public void RegisterDoorIdentifier(DoorIdentifier identifier)
        {
            if (identifier == null) return;

            if (_doorIdentifiers.ContainsKey(identifier.doorNumber))
            {
                _doorIdentifiers[identifier.doorNumber] = identifier;
            }
            else
            {
                _doorIdentifiers.Add(identifier.doorNumber, identifier);
            }
        }

        public void UnregisterDoorIdentifier(DoorIdentifier identifier)
        {
            if (identifier != null && _doorIdentifiers.ContainsKey(identifier.doorNumber))
            {
                _doorIdentifiers.Remove(identifier.doorNumber);
            }
        }

        private void InitialRefresh()
        {
            RefreshAllDoorInfoCache();

            if (_cachedDoorInfo.Count == 0)
            {
                Invoke(nameof(InitialRefresh), 0.2f);
            }
        }

        private void Update()
        {
            _cacheRefreshTimer += Time.deltaTime;
            if (!(_cacheRefreshTimer >= CACHE_REFRESH_INTERVAL)) return;
            RefreshAllDoorInfoCache();
            _cacheRefreshTimer = 0f;
        }

        private void RefreshAllDoorInfoCache()
        {
            if (_entityManager == null || _doorQuery == null)
            {
                Debug.LogWarning("[DoorDataBridge] EntityManager or Query not initialized");
                return;
            }

            _cachedDoorInfo.Clear();
            _doorEntityCache.Clear();

            var doorComponents = _doorQuery.ToComponentDataArray<DoorComponent>(Allocator.Temp);
            var doorStates = _doorQuery.ToComponentDataArray<DoorStateComponent>(Allocator.Temp);
            var doorTransforms = _doorQuery.ToComponentDataArray<DoorTransformData>(Allocator.Temp);
            var transforms = _doorQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
            var triggers = _doorQuery.ToComponentDataArray<DoorTriggerVolume>(Allocator.Temp);

            var entities = _doorQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < doorComponents.Length; i++)
            {
                int doorId = doorComponents[i].DoorId;
                DoorIdentifier identifier = null;

                if (_doorIdentifiers.TryGetValue(doorId, out var foundIdentifier))
                {
                    identifier = foundIdentifier;
                }

                _doorEntityCache[doorId] = entities[i];

                var doorInfo = new DoorInfo
                {
                    doorId = doorId,
                    position = transforms[i].Position,
                    rotation = transforms[i].Rotation,
                    doorType = doorComponents[i].Type,
                    openingStyle = doorTransforms[i].OpeningStyle,
                    slideOffset = doorTransforms[i].SlideOffset,
                    triggerSize = triggers[i].Size,
                    triggerCenter = triggers[i].Center,
                    isLocked = doorStates[i].IsLocked == 1,
                    rootEntity = entities[i],
                    doorIdentifier = identifier
                };

                _cachedDoorInfo.Add(doorInfo);
            }

            entities.Dispose();
            doorTransforms.Dispose();

            doorComponents.Dispose();
            doorStates.Dispose();
            transforms.Dispose();
            triggers.Dispose();
        }

        public List<DoorInfo> GetAllDoorInfo()
        {
            return _cachedDoorInfo;
        }

        public bool TryGetDoorInfo(int doorId, out DoorInfo info)
        {
            info = default;

            if (_entityManager == null || !_doorEntityCache.TryGetValue(doorId, out Entity doorEntity))
            {
                return false;
            }

            if (!_entityManager.HasComponent<DoorComponent>(doorEntity))
            {
                return false;
            }

            var doorComponent = _entityManager.GetComponentData<DoorComponent>(doorEntity);
            var doorState = _entityManager.GetComponentData<DoorStateComponent>(doorEntity);
            var doorTransform = _entityManager.GetComponentData<DoorTransformData>(doorEntity);
            var transform = _entityManager.GetComponentData<LocalToWorld>(doorEntity);
            var trigger = _entityManager.GetComponentData<DoorTriggerVolume>(doorEntity);

            DoorIdentifier identifier = null;
            if (_doorIdentifiers.TryGetValue(doorId, out var foundIdentifier))
            {
                identifier = foundIdentifier;
            }

            info = new DoorInfo
            {
                doorId = doorComponent.DoorId,
                position = transform.Position,
                rotation = transform.Rotation,
                doorType = doorComponent.Type,
                openingStyle = doorTransform.OpeningStyle,
                slideOffset = doorTransform.SlideOffset,
                triggerSize = trigger.Size,
                triggerCenter = trigger.Center,
                isLocked = doorState.IsLocked == 1,
                rootEntity = doorEntity,
                doorIdentifier = identifier
            };

            return true;
        }

        public bool IsDoorLocked(int doorId)
        {
            if (_entityManager == null || !_doorEntityCache.TryGetValue(doorId, out Entity doorEntity))
            {
                return false;
            }

            if (!_entityManager.HasComponent<DoorStateComponent>(doorEntity))
            {
                return false;
            }

            var doorState = _entityManager.GetComponentData<DoorStateComponent>(doorEntity);
            return doorState.IsLocked == 1;
        }
    }
}
