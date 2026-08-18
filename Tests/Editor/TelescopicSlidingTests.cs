using NUnit.Framework;

namespace AutomaticDoorSystem.Tests
{
    /// <summary>
    /// Locks the telescopic coupling: the right panel owns the timeline, the left panel waits
    /// until the right one has covered the catch-up distance, then follows in lockstep, and both
    /// arrive together. Spans: right 3, left 1.5 -> catch-up 1.5.
    /// </summary>
    public class TelescopicSlidingTests
    {
        private const float RightSpan = 3f;
        private const float LeftSpan = 1.5f;
        private const float Tolerance = 1e-4f;

        private static (float right, float left) Travel(float progress, bool isOpening = true, bool rightOnly = false)
        {
            DoorAnimationSystem.ComputeTelescopicTravel(
                RightSpan, LeftSpan, rightOnly, progress, isOpening,
                out var rightTravel, out var leftTravel);
            return (rightTravel, leftTravel);
        }

        [Test]
        public void Opening_LeftWaitsUntilRightCatchesUp()
        {
            // Right has travelled 1.2 of the 1.5 catch-up distance: left must not have moved.
            var (right, left) = Travel(0.4f);
            Assert.AreEqual(1.2f, right, Tolerance);
            Assert.AreEqual(0f, left, Tolerance);
        }

        [Test]
        public void Opening_LockstepAfterCatchUp()
        {
            // Past the catch-up point the left panel trails the right by exactly the catch-up distance.
            var (right, left) = Travel(0.75f);
            Assert.AreEqual(2.25f, right, Tolerance);
            Assert.AreEqual(right - 1.5f, left, Tolerance);
        }

        [Test]
        public void Opening_BothArriveTogether()
        {
            var (right, left) = Travel(1f);
            Assert.AreEqual(RightSpan, right, Tolerance);
            Assert.AreEqual(LeftSpan, left, Tolerance);
        }

        [Test]
        public void Closing_TravelTogetherThenLeftStopsFirst()
        {
            // Early in the close both still move together...
            var (right, left) = Travel(0.25f, isOpening: false);
            Assert.AreEqual(2.25f, right, Tolerance);
            Assert.AreEqual(right - 1.5f, left, Tolerance);

            // ...and near the end the left panel is home while the right one finishes alone.
            (right, left) = Travel(0.9f, isOpening: false);
            Assert.AreEqual(0.3f, right, Tolerance);
            Assert.AreEqual(0f, left, Tolerance);
        }

        [Test]
        public void RightDoorOnly_StopsWhereTheLeftDoorSits()
        {
            var (right, left) = Travel(1f, rightOnly: true);
            Assert.AreEqual(1.5f, right, Tolerance, "right door must stop at the catch-up point");
            Assert.AreEqual(0f, left, Tolerance, "left door must never move");
        }

        [Test]
        public void MisauthoredSpans_RightShorterThanLeft_MoveTogetherWithoutOvershoot()
        {
            // Catch-up clamps to 0: both move from the start, left clamps at its own span.
            DoorAnimationSystem.ComputeTelescopicTravel(
                1f, 2f, rightDoorOnly: false, easedProgress: 0.5f, isOpening: true,
                out var right, out var left);
            Assert.AreEqual(0.5f, right, Tolerance);
            Assert.AreEqual(0.5f, left, Tolerance);
        }
    }
}
