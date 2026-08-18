using AutomaticDoorSystem.Utilities;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// Pool slots are keyed by doorId, so a doorId must never occupy two slots. When it did,
    /// the next frame's NativeHashMap.Add threw "An item with the same key has already been
    /// added: 0" - the default id every un-numbered DoorAuthoring carries.
    /// </summary>
    public class DoorSelectionStrategyTests
    {
        private DoorSelectionStrategy _strategy;

        private const int MaxPool = 4;
        private const float Threshold = 1.3f;

        [SetUp]
        public void SetUp()
        {
            _strategy = new DoorSelectionStrategy(64, MaxPool, Allocator.Temp);
        }

        [TearDown]
        public void TearDown()
        {
            _strategy.Dispose();
        }

        private void AddCandidates(params (int id, float distance)[] doors)
        {
            _strategy.BeginSelection();
            foreach (var (id, distance) in doors)
            {
                _strategy.AddCandidate(id, new float3(distance, 0f, 0f), float3.zero);
            }
        }

        private int[] Assignments()
        {
            var result = new int[MaxPool];
            for (int i = 0; i < MaxPool; i++)
            {
                result[i] = _strategy.GetDoorIdForSlot(i);
            }
            return result;
        }

        [Test]
        public void RemoveDuplicateIds_KeepsNearestAndReportsCount()
        {
            AddCandidates((7, 1f), (7, 5f), (8, 2f), (7, 9f));
            _strategy.SortByDistance();

            int removed = _strategy.RemoveDuplicateIds();

            Assert.AreEqual(2, removed, "two extra copies of door 7 should be dropped");
            Assert.AreEqual(2, _strategy.GetCandidateCount());
            Assert.AreEqual(7, _strategy.GetCandidate(0).doorId, "nearest door 7 survives");
            Assert.AreEqual(1f, _strategy.GetCandidate(0).sqrDistance, 0.001f);
            Assert.AreEqual(8, _strategy.GetCandidate(1).doorId);
        }

        [Test]
        public void RemoveDuplicateIds_LeavesDistinctIdsAlone()
        {
            AddCandidates((1, 1f), (2, 2f), (3, 3f));
            _strategy.SortByDistance();

            Assert.AreEqual(0, _strategy.RemoveDuplicateIds());
            Assert.AreEqual(3, _strategy.GetCandidateCount());
        }

        [Test]
        public void AssignPoolSlots_DuplicateIdNeverTakesTwoSlots()
        {
            // The exact scene mistake behind the crash: several doors left on the default id 0.
            AddCandidates((0, 1f), (0, 2f), (0, 3f));
            _strategy.SortByDistance();

            Assert.DoesNotThrow(() => _strategy.AssignPoolSlots(MaxPool, true, Threshold));

            var assignments = Assignments();
            Assert.AreEqual(1, System.Array.FindAll(assignments, id => id == 0).Length,
                "door 0 must hold exactly one slot");
        }

        [Test]
        public void AssignPoolSlots_SecondPassAfterDuplicatesDoesNotThrow()
        {
            // The crash surfaced one frame late: the first pass created the double assignment,
            // the second tripped over it while rebuilding its doorId -> slot map.
            AddCandidates((0, 1f), (0, 2f));
            _strategy.SortByDistance();
            _strategy.AssignPoolSlots(MaxPool, true, Threshold);

            AddCandidates((0, 1f), (0, 2f));
            _strategy.SortByDistance();

            Assert.DoesNotThrow(() => _strategy.AssignPoolSlots(MaxPool, true, Threshold));
        }

        [Test]
        public void AssignPoolSlots_KeepsExistingSlotForDoorStillInRange()
        {
            AddCandidates((10, 1f), (20, 2f));
            _strategy.SortByDistance();
            _strategy.AssignPoolSlots(MaxPool, true, Threshold);

            int slotForTen = _strategy.GetPoolIndexForDoor(10, MaxPool);
            Assert.AreNotEqual(-1, slotForTen);

            // Door 20 is now nearer, but 10 should not be shuffled to another slot.
            AddCandidates((20, 1f), (10, 2f));
            _strategy.SortByDistance();
            _strategy.AssignPoolSlots(MaxPool, true, Threshold);

            Assert.AreEqual(slotForTen, _strategy.GetPoolIndexForDoor(10, MaxPool));
        }

        [Test]
        public void AssignPoolSlots_ReleasesOutOfRangeSlotsWhenNotKeepingThem()
        {
            AddCandidates((1, 1f), (2, 2f));
            _strategy.SortByDistance();
            _strategy.AssignPoolSlots(MaxPool, false, Threshold);

            AddCandidates((1, 1f));
            _strategy.SortByDistance();
            _strategy.AssignPoolSlots(MaxPool, false, Threshold);

            Assert.AreNotEqual(-1, _strategy.GetPoolIndexForDoor(1, MaxPool));
            Assert.AreEqual(-1, _strategy.GetPoolIndexForDoor(2, MaxPool),
                "door 2 left the candidate set, so its slot should be free for reuse");
        }

        [Test]
        public void AssignPoolSlots_LockedSlotIsNotReleasedOrStolen()
        {
            AddCandidates((1, 1f));
            _strategy.SortByDistance();
            _strategy.AssignPoolSlots(MaxPool, false, Threshold);

            int slot = _strategy.GetPoolIndexForDoor(1, MaxPool);
            _strategy.SetSlotLocked(slot, true);

            // Door 1 is gone from the candidate set, but its source is still mid-clip.
            AddCandidates((2, 1f));
            _strategy.SortByDistance();
            _strategy.AssignPoolSlots(MaxPool, false, Threshold);

            Assert.AreEqual(slot, _strategy.GetPoolIndexForDoor(1, MaxPool));
        }

        [Test]
        public void AssignPoolSlots_MoreDoorsThanSlotsActivatesTheNearest()
        {
            AddCandidates((1, 5f), (2, 1f), (3, 2f), (4, 3f), (5, 4f), (6, 6f));
            _strategy.SortByDistance();

            int activated = _strategy.AssignPoolSlots(MaxPool, true, Threshold);

            Assert.AreEqual(MaxPool, activated);
            foreach (int id in new[] { 2, 3, 4, 5 })
            {
                Assert.AreNotEqual(-1, _strategy.GetPoolIndexForDoor(id, MaxPool), $"door {id} should be active");
            }
            Assert.AreEqual(-1, _strategy.GetPoolIndexForDoor(6, MaxPool), "farthest door stays out");
        }

        [Test]
        public void FilterByDistance_DropsDoorsBeyondRange()
        {
            AddCandidates((1, 1f), (2, 10f), (3, 3f));

            _strategy.FilterByDistance(5f);

            Assert.AreEqual(2, _strategy.GetCandidateCount());
        }
    }
}
