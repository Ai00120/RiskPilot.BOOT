using RiskPilot.Diagnostics.Nt8.Core.Queue;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class EventQueueTests
    {
        [Fact]
        public void TryEnqueue_RejectsNonPositiveCapacity()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EventQueue<int>(0));
        }

        [Fact]
        public void Enqueue_UpToCapacity_Succeeds()
        {
            var queue = new EventQueue<int>(3);
            Assert.True(queue.TryEnqueue(1));
            Assert.True(queue.TryEnqueue(2));
            Assert.True(queue.TryEnqueue(3));
            Assert.Equal(3, queue.Count);
            Assert.Equal(0, queue.DroppedCount);
        }

        [Fact]
        public void Enqueue_BeyondCapacity_DropsAndCounts()
        {
            var queue = new EventQueue<int>(2);
            Assert.True(queue.TryEnqueue(1));
            Assert.True(queue.TryEnqueue(2));
            Assert.False(queue.TryEnqueue(3));
            Assert.False(queue.TryEnqueue(4));

            Assert.Equal(2, queue.Count);
            Assert.Equal(2, queue.EnqueuedCount);
            Assert.Equal(2, queue.DroppedCount);
        }

        [Fact]
        public void Drain_ReturnsItemsInFifoOrder()
        {
            var queue = new EventQueue<int>(5);
            for (var i = 1; i <= 5; i++) queue.TryEnqueue(i);

            var buffer = new int[5];
            var drained = queue.Drain(buffer, 5);

            Assert.Equal(5, drained);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, buffer);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Drain_WhenEmpty_ReturnsZero()
        {
            var queue = new EventQueue<int>(5);
            var buffer = new int[5];
            Assert.Equal(0, queue.Drain(buffer, 5));
        }

        [Fact]
        public void EnqueueAfterDrain_SucceedsAgain()
        {
            var queue = new EventQueue<int>(1);
            Assert.True(queue.TryEnqueue(1));
            Assert.False(queue.TryEnqueue(2));
            Assert.True(queue.TryDequeue(out var value));
            Assert.Equal(1, value);
            Assert.True(queue.TryEnqueue(3));
            Assert.Equal(1, queue.DroppedCount);
        }
    }
}
