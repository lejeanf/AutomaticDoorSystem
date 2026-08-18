using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace AutomaticDoorSystem.Utilities
{
    public struct DoorSelectionStrategy
    {
        public struct DoorCandidate
        {
            public int doorId;
            public float3 position;
            public float sqrDistance;
            public int customData0;
            public int customData1;
        }

        public struct DoorDistanceComparer : System.Collections.Generic.IComparer<DoorCandidate>
        {
            public int Compare(DoorCandidate a, DoorCandidate b)
            {
                return a.sqrDistance.CompareTo(b.sqrDistance);
            }
        }

        private NativeArray<DoorCandidate> _candidates;
        private NativeArray<int> _currentAssignments;
        private NativeArray<bool> _poolSlotUsed;
        private NativeArray<bool> _poolSlotLocked;
        private int _candidateCount;

        public DoorSelectionStrategy(int maxCandidates, int maxPoolSize, Allocator allocator)
        {
            _candidates = new NativeArray<DoorCandidate>(maxCandidates, allocator);
            _currentAssignments = new NativeArray<int>(maxPoolSize, allocator);
            _poolSlotUsed = new NativeArray<bool>(maxPoolSize, allocator);
            _poolSlotLocked = new NativeArray<bool>(maxPoolSize, allocator);
            _candidateCount = 0;

            for (int i = 0; i < maxPoolSize; i++)
            {
                _currentAssignments[i] = -1;
            }
        }

        public bool IsCreated => _candidates.IsCreated;

        public void Dispose()
        {
            if (_candidates.IsCreated) _candidates.Dispose();
            if (_currentAssignments.IsCreated) _currentAssignments.Dispose();
            if (_poolSlotUsed.IsCreated) _poolSlotUsed.Dispose();
            if (_poolSlotLocked.IsCreated) _poolSlotLocked.Dispose();
        }

        /// <summary>
        /// Marks a pool slot as un-stealable for the next <see cref="AssignPoolSlots"/> call.
        /// Used by the audio pool to protect a source that is mid-playback.
        /// </summary>
        public void SetSlotLocked(int poolIndex, bool locked)
        {
            if (poolIndex >= 0 && poolIndex < _poolSlotLocked.Length)
            {
                _poolSlotLocked[poolIndex] = locked;
            }
        }

        public void BeginSelection()
        {
            _candidateCount = 0;
        }

        public void AddCandidate(int doorId, float3 position, float3 observerPosition, int customData0 = 0, int customData1 = 0)
        {
            if (_candidateCount >= _candidates.Length)
            {
                Debug.LogWarning($"[DoorSelectionStrategy] Candidate buffer full ({_candidates.Length}). Increase maxCandidates.");
                return;
            }

            float sqrDist = math.distancesq(position, observerPosition);

            _candidates[_candidateCount] = new DoorCandidate
            {
                doorId = doorId,
                position = position,
                sqrDistance = sqrDist,
                customData0 = customData0,
                customData1 = customData1
            };

            _candidateCount++;
        }

        public void FilterByDistance(float maxDistance)
        {
            float sqrMaxDistance = maxDistance * maxDistance;
            int writeIndex = 0;

            for (int i = 0; i < _candidateCount; i++)
            {
                if (_candidates[i].sqrDistance <= sqrMaxDistance)
                {
                    if (writeIndex != i)
                    {
                        _candidates[writeIndex] = _candidates[i];
                    }
                    writeIndex++;
                }
            }

            _candidateCount = writeIndex;
        }

        public void SortByDistance()
        {
            if (_candidateCount <= 1) return;

            var comparer = new DoorDistanceComparer();

            var tempArray = new DoorCandidate[_candidateCount];
            NativeArray<DoorCandidate>.Copy(_candidates, tempArray, _candidateCount);
            System.Array.Sort(tempArray, 0, _candidateCount, comparer);
            NativeArray<DoorCandidate>.Copy(tempArray, _candidates, _candidateCount);
        }

        /// <summary>
        /// Drops candidates that repeat a doorId already seen, keeping the first occurrence.
        /// Call after <see cref="SortByDistance"/> so the nearest door wins.
        /// <para>
        /// Two doors sharing a doorId is always a scene authoring mistake (typically several
        /// DoorAuthoring components left on the default id 0). Pool slots are keyed by doorId,
        /// so without this the same id claims two slots and the next frame throws
        /// "An item with the same key has already been added". Use the Setup Validator
        /// (Tools > AutomaticDoorSystem > Setup Validator) to find and fix the duplicates.
        /// </para>
        /// </summary>
        /// <returns>Number of duplicate candidates removed.</returns>
        public int RemoveDuplicateIds()
        {
            if (_candidateCount <= 1) return 0;

            var seenIds = new NativeHashSet<int>(_candidateCount, Allocator.Temp);
            int writeIndex = 0;

            for (int i = 0; i < _candidateCount; i++)
            {
                if (!seenIds.Add(_candidates[i].doorId))
                    continue;

                if (writeIndex != i)
                {
                    _candidates[writeIndex] = _candidates[i];
                }
                writeIndex++;
            }

            seenIds.Dispose();

            int removed = _candidateCount - writeIndex;
            _candidateCount = writeIndex;
            return removed;
        }

        public void RemoveSpatialDuplicates(float minimumSpacing)
        {
            if (_candidateCount <= 1) return;

            float sqrMinSpacing = minimumSpacing * minimumSpacing;
            int writeIndex = 0;

            for (int i = 0; i < _candidateCount; i++)
            {
                bool isDuplicate = false;

                for (int j = 0; j < writeIndex; j++)
                {
                    float sqrDist = math.distancesq(_candidates[i].position, _candidates[j].position);
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
                        _candidates[writeIndex] = _candidates[i];
                    }
                    writeIndex++;
                }
            }

            _candidateCount = writeIndex;
        }

        public int AssignPoolSlots(int maxPoolSize, bool keepOutOfRangeAssignments, float reassignmentThreshold)
        {
            // The pool arrays were sized at construction; a caller raising its maxPoolSize field
            // afterwards must not walk off the end of them.
            maxPoolSize = math.min(maxPoolSize, _currentAssignments.Length);

            for (int i = 0; i < _poolSlotUsed.Length && i < maxPoolSize; i++)
            {
                _poolSlotUsed[i] = false;
            }

            int doorsToActivate = math.min(_candidateCount, maxPoolSize);

            NativeHashSet<int> inRangeDoors = new NativeHashSet<int>(doorsToActivate, Allocator.Temp);
            for (int i = 0; i < doorsToActivate; i++)
            {
                inRangeDoors.Add(_candidates[i].doorId);
            }

            // A doorId must never hold two slots. If it somehow does (duplicate ids in the scene,
            // or an older build that assigned before this guard existed), free the extra slot
            // rather than letting NativeHashMap.Add throw.
            NativeHashMap<int, int> doorToPoolIndex = new NativeHashMap<int, int>(maxPoolSize, Allocator.Temp);
            for (int i = 0; i < maxPoolSize; i++)
            {
                int assignedDoorId = _currentAssignments[i];
                if (assignedDoorId == -1) continue;

                if (!doorToPoolIndex.TryAdd(assignedDoorId, i))
                {
                    _currentAssignments[i] = -1;
                }
            }

            for (int i = 0; i < doorsToActivate; i++)
            {
                int doorId = _candidates[i].doorId;
                if (doorToPoolIndex.TryGetValue(doorId, out int poolIndex))
                {
                    _poolSlotUsed[poolIndex] = true;
                }
            }

            if (keepOutOfRangeAssignments)
            {
                for (int i = 0; i < maxPoolSize; i++)
                {
                    int assignedDoorId = _currentAssignments[i];
                    if (assignedDoorId != -1 && !_poolSlotUsed[i] && !inRangeDoors.Contains(assignedDoorId))
                    {
                        _poolSlotUsed[i] = true;
                    }
                }
            }

            int nextFreeSlot = 0;
            for (int i = 0; i < doorsToActivate; i++)
            {
                int doorId = _candidates[i].doorId;

                if (doorToPoolIndex.ContainsKey(doorId))
                    continue;

                while (nextFreeSlot < maxPoolSize && _currentAssignments[nextFreeSlot] != -1)
                {
                    nextFreeSlot++;
                }

                if (nextFreeSlot < maxPoolSize)
                {
                    _currentAssignments[nextFreeSlot] = doorId;
                    _poolSlotUsed[nextFreeSlot] = true;
                    doorToPoolIndex.TryAdd(doorId, nextFreeSlot);
                    nextFreeSlot++;
                }
                else
                {
                    int victimSlot = FindSlotToReassign(i, inRangeDoors, maxPoolSize, reassignmentThreshold);
                    if (victimSlot != -1)
                    {
                        doorToPoolIndex.Remove(_currentAssignments[victimSlot]);
                        _currentAssignments[victimSlot] = doorId;
                        _poolSlotUsed[victimSlot] = true;
                        doorToPoolIndex.TryAdd(doorId, victimSlot);
                    }
                }
            }

            if (!keepOutOfRangeAssignments)
            {
                for (int i = 0; i < maxPoolSize; i++)
                {
                    if (!_poolSlotUsed[i] && !_poolSlotLocked[i])
                    {
                        _currentAssignments[i] = -1;
                    }
                }
            }

            inRangeDoors.Dispose();
            doorToPoolIndex.Dispose();

            return doorsToActivate;
        }

        public int GetPoolIndexForDoor(int doorId, int maxPoolSize)
        {
            for (int i = 0; i < maxPoolSize && i < _currentAssignments.Length; i++)
            {
                if (_currentAssignments[i] == doorId)
                    return i;
            }
            return -1;
        }

        public int GetDoorIdForSlot(int poolIndex)
        {
            if (poolIndex < 0 || poolIndex >= _currentAssignments.Length)
                return -1;
            return _currentAssignments[poolIndex];
        }

        public void UnassignSlot(int poolIndex)
        {
            if (poolIndex >= 0 && poolIndex < _currentAssignments.Length)
            {
                _currentAssignments[poolIndex] = -1;
            }
        }

        public DoorCandidate GetCandidate(int index)
        {
            if (index < 0 || index >= _candidateCount)
            {
                Debug.LogError($"[DoorSelectionStrategy] Invalid candidate index {index} (count: {_candidateCount})");
                return default;
            }
            return _candidates[index];
        }

        public int GetCandidateCount() => _candidateCount;

        private int FindSlotToReassign(int newDoorIndex, NativeHashSet<int> inRangeDoors, int maxPoolSize, float reassignmentThreshold)
        {
            DoorCandidate newDoor = _candidates[newDoorIndex];
            int outOfRangeSlot = -1;
            int bestInRangeSlot = -1;
            float bestRatio = reassignmentThreshold;

            for (int i = 0; i < maxPoolSize; i++)
            {
                if (_currentAssignments[i] == -1) continue;
                if (i < _poolSlotLocked.Length && _poolSlotLocked[i]) continue;

                bool isInRange = inRangeDoors.Contains(_currentAssignments[i]);

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
                    for (int j = 0; j < _candidateCount; j++)
                    {
                        if (_candidates[j].doorId == _currentAssignments[i])
                        {
                            currentDoorSqrDist = _candidates[j].sqrDistance;
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
                return outOfRangeSlot;

            return bestInRangeSlot;
        }
    }
}
