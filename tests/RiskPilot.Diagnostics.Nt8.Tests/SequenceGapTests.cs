using RiskPilot.Diagnostics.Nt8.Core.Queue;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class SequenceGapTests
    {
        [Fact]
        public void FirstObservation_IsNotAGap()
        {
            var detector = new SequenceGapDetector();
            Assert.Equal(0, detector.Observe("quotes", 100));
            Assert.False(detector.HasGaps);
        }

        [Fact]
        public void ContiguousSequence_IsNotAGap()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 1);
            Assert.Equal(0, detector.Observe("quotes", 2));
            Assert.Equal(0, detector.Observe("quotes", 3));
            Assert.False(detector.HasGaps);
        }

        [Fact]
        public void MissingSequence_IsReported()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 10);
            var missing = detector.Observe("quotes", 15);
            Assert.Equal(4, missing);
            Assert.Equal(1, detector.TotalGaps);
            Assert.Equal(4, detector.TotalMissing);
            Assert.True(detector.HasGaps);
        }

        [Fact]
        public void StreamsAreIsolated()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 1);
            detector.Observe("trades", 50);
            Assert.Equal(0, detector.Observe("quotes", 2));
            Assert.Equal(0, detector.Observe("trades", 51));
            Assert.False(detector.HasGaps);
        }

        [Fact]
        public void BackwardsSequence_IsIgnored()
        {
            var detector = new SequenceGapDetector();
            detector.Observe("quotes", 10);
            Assert.Equal(0, detector.Observe("quotes", 9));
            Assert.False(detector.HasGaps);
        }
    }
}
