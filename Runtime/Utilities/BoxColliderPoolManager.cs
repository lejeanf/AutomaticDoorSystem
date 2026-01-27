using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace AutomaticDoorSystem.Utilities
{
    public class BoxColliderPoolManager : MonoBehaviour
    {
        public static BoxColliderPoolManager Instance { get; private set; }

        [Header("Pool Configuration")]
        [Tooltip("Maximum number of BoxColliders in the pool")]
        [Range(2, 100)]
        public int maxPoolSize = 25;

        [Tooltip("Distance from player at which BoxColliders are activated")]
        [Range(5f, 100f)]
        public float cullingDistance = 25f;

        [Tooltip("Frequency of distance checks in seconds")]
        [Range(0.1f, 2f)]
        public float distanceCheckInterval = 0.5f;

        [Tooltip("Minimum distance between BoxColliders to prevent overlap")]
        [Range(0f, 5f)]
        public float minimumSpacing = 0.5f;

        [Tooltip("How much closer a new door must be to steal a BoxCollider")]
        [Range(0.5f, 2f)]
        public float reassignmentThreshold = 1.3f;

        [Tooltip("Keep BoxColliders assigned to out-of-range doors until needed")]
        public bool keepOutOfRangeAssignments = true;

        private struct ColliderAssignment
        {
            public int doorId;
            public int panelIndex;
        }

        private Transform _cameraTransform;
        private Transform _poolContainer;
        private BoxCollider[] _colliderPool;
        private Rigidbody[] _rigidbodyPool;
        private ColliderAssignment[] _colliderAssignments;
        private DoorSelectionStrategy _selectionStrategy;
        private float _updateAccumulator;
        private WaitForSeconds _updateWait;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            if (DoorDataBridge.Instance == null)
            {
                gameObject.AddComponent<DoorDataBridge>();
                Debug.Log("[BoxColliderPoolManager] Auto-created DoorDataBridge component");
            }

            InitializePool();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_selectionStrategy.GetCandidateCount() >= 0)
            {
                _selectionStrategy.Dispose();
            }
        }

        private void Start()
        {
            CacheCameraReference();
            Invoke(nameof(ForceInitialUpdate), 0.5f);
        }

        private void ForceInitialUpdate()
        {
            if (DoorDataBridge.Instance != null)
            {
                UpdateColliderActivation();
            }
            else
            {
                Debug.LogWarning("[BoxColliderPoolManager] DoorDataBridge not ready yet, retrying...");
                Invoke(nameof(ForceInitialUpdate), 0.5f);
            }
        }

        private void OnEnable()
        {
            _updateAccumulator = 0f;
        }

        private void Update()
        {
            if (DoorDataBridge.Instance == null)
            {
                return;
            }

            _updateAccumulator += Time.deltaTime;
            if (_updateAccumulator >= distanceCheckInterval)
            {
                _updateAccumulator = 0f;
                UpdateColliderActivation();
            }
        }

        private void LateUpdate()
        {
            UpdateColliderPositions();
        }

        private void InitializePool()
        {
            _poolContainer = new GameObject("BoxCollider Pool").transform;
            _poolContainer.SetParent(transform);
            _poolContainer.localPosition = Vector3.zero;

            _colliderPool = new BoxCollider[maxPoolSize];
            _rigidbodyPool = new Rigidbody[maxPoolSize];
            _colliderAssignments = new ColliderAssignment[maxPoolSize];
            _selectionStrategy = new DoorSelectionStrategy(500, maxPoolSize, Allocator.Persistent);
            _updateWait = new WaitForSeconds(distanceCheckInterval);

            for (int i = 0; i < maxPoolSize; i++)
            {
                _colliderAssignments[i] = new ColliderAssignment { doorId = -1, panelIndex = -1 };
            }

            for (int i = 0; i < maxPoolSize; i++)
            {
                GameObject colliderObj = new GameObject($"PooledBoxCollider_{i:D2}");
                colliderObj.transform.SetParent(_poolContainer);
                colliderObj.transform.localPosition = Vector3.zero;

                Rigidbody rb = colliderObj.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                BoxCollider boxCollider = colliderObj.AddComponent<BoxCollider>();
                boxCollider.enabled = false;

                _colliderPool[i] = boxCollider;
                _rigidbodyPool[i] = rb;
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

        private void UpdateColliderActivation()
        {
            if (_cameraTransform == null || DoorDataBridge.Instance == null)
            {
                Debug.LogWarning("[BoxColliderPoolManager] Camera or DoorDataBridge not available");
                return;
            }

            float3 playerPosition = _cameraTransform.position;

            _selectionStrategy.BeginSelection();

            var allDoors = DoorDataBridge.Instance.GetAllDoorInfo();
            if (allDoors == null || allDoors.Count == 0)
            {
                return;
            }


            for (int i = 0; i < allDoors.Count; i++)
            {
                var doorInfo = allDoors[i];
                _selectionStrategy.AddCandidate(doorInfo.doorId, doorInfo.position, playerPosition);
            }

            _selectionStrategy.FilterByDistance(cullingDistance);
            _selectionStrategy.SortByDistance();
            _selectionStrategy.RemoveSpatialDuplicates(minimumSpacing);

            int assignedCount = _selectionStrategy.AssignPoolSlots(
                maxPoolSize,
                keepOutOfRangeAssignments,
                reassignmentThreshold
            );

            int colliderIndex = 0;

            for (int i = 0; i < assignedCount && colliderIndex < maxPoolSize; i++)
            {
                var candidate = _selectionStrategy.GetCandidate(i);
                int doorId = candidate.doorId;

                if (DoorDataBridge.Instance.TryGetDoorInfo(doorId, out var doorInfo))
                {
                    if (DoorDataBridge.Instance.TryGetDoorPanels(doorId, out var panels, out int panelCount))
                    {
                        for (int panelIdx = 0; panelIdx < panelCount && colliderIndex < maxPoolSize; panelIdx++)
                        {
                            ConfigureColliderForPanel(colliderIndex, doorInfo, panels[panelIdx], panelIdx);
                            _colliderAssignments[colliderIndex] = new ColliderAssignment { doorId = doorId, panelIndex = panelIdx };
                            colliderIndex++;
                        }
                    }
                }
            }

            for (int i = colliderIndex; i < maxPoolSize; i++)
            {
                _colliderPool[i].enabled = false;
                _colliderAssignments[i] = new ColliderAssignment { doorId = -1, panelIndex = -1 };
            }
        }

        private void UpdateColliderPositions()
        {
            if (DoorDataBridge.Instance == null)
            {
                return;
            }

            for (int i = 0; i < maxPoolSize; i++)
            {
                var assignment = _colliderAssignments[i];

                if (assignment.doorId == -1 || !_colliderPool[i].enabled)
                {
                    continue;
                }

                if (DoorDataBridge.Instance.IsDoorLocked(assignment.doorId))
                {
                    continue;
                }

                if (DoorDataBridge.Instance.TryGetDoorPanels(assignment.doorId, out var panels, out int panelCount))
                {
                    if (assignment.panelIndex >= 0 && assignment.panelIndex < panelCount)
                    {
                        var panelInfo = panels[assignment.panelIndex];
                        _rigidbodyPool[i].MovePosition(panelInfo.position);
                        _rigidbodyPool[i].MoveRotation(panelInfo.rotation);
                    }
                }
            }
        }

        private void ConfigureColliderForPanel(int poolIndex, DoorDataBridge.DoorInfo doorInfo, DoorDataBridge.DoorPanelInfo panelInfo, int panelIndex)
        {
            BoxCollider collider = _colliderPool[poolIndex];
            Rigidbody rb = _rigidbodyPool[poolIndex];

            // Use collider data from the subscene prefab BoxColliders
            if (panelInfo.hasColliderData)
            {
                collider.size = panelInfo.colliderSize;
                collider.center = panelInfo.colliderCenter;
            }
            else
            {
                // Fallback - generic door size (add BoxCollider to door panel in subscene prefab)
                collider.size = new Vector3(1f, 2.5f, 0.1f);
                collider.center = new Vector3(0.5f, 1.25f, 0f);
                Debug.LogWarning($"[BoxColliderPoolManager] Door {doorInfo.doorId} panel has no BoxCollider data - using fallback size");
            }

            rb.MoveRotation(panelInfo.rotation);
            rb.MovePosition(panelInfo.position);
            collider.enabled = true;
        }

        private int LayerMaskToLayer(LayerMask layerMask)
        {
            int layerNumber = 0;
            int layer = layerMask.value;
            while (layer > 1)
            {
                layer = layer >> 1;
                layerNumber++;
            }
            return layerNumber;
        }

        private void OnDrawGizmosSelected()
        {
            if (_cameraTransform == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_cameraTransform.position, cullingDistance);

            if (_colliderPool != null)
            {
                Gizmos.color = Color.cyan;
                for (int i = 0; i < maxPoolSize; i++)
                {
                    if (_colliderPool[i] != null && _colliderPool[i].enabled)
                    {
                        Gizmos.matrix = _colliderPool[i].transform.localToWorldMatrix;
                        Gizmos.DrawWireCube(Vector3.zero, _colliderPool[i].size);
                    }
                }
                Gizmos.matrix = Matrix4x4.identity;
            }
        }

        public int GetActiveColliderCount()
        {
            int count = 0;
            for (int i = 0; i < maxPoolSize; i++)
            {
                if (_colliderPool[i].enabled)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
