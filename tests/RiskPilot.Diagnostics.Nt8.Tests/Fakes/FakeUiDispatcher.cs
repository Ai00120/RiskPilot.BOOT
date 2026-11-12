using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Run;

namespace RiskPilot.Diagnostics.Nt8.Tests.Fakes
{
    /// <summary>
    /// Synchronous <see cref="IUiDispatcher"/> for tests. It records the thread each post is
    /// observed on and can be put into a "shutting down" state, so the coordinator's
    /// dispatcher-shutdown handling can be exercised without a real WPF dispatcher.
    /// </summary>
    public sealed class FakeUiDispatcher : IUiDispatcher
    {
        public bool OnDispatcherThread { get; set; }

        public bool ShutdownStarted { get; set; }

        /// <summary>Number of posts that were refused because the dispatcher was unavailable.</summary>
        public int RefusedPosts { get; private set; }

        /// <summary>Threads on which <see cref="Post"/> was called.</summary>
        public List<int> PostingThreads { get; private set; }

        /// <summary>True when <see cref="Post"/> ran the action inline (on the calling thread).</summary>
        public bool RunInline { get; set; }

        public FakeUiDispatcher()
        {
            PostingThreads = new List<int>();
            RunInline = true;
        }

        public bool HasShutdownStarted
        {
            get { return ShutdownStarted; }
        }

        public bool Post(Action action)
        {
            if (ShutdownStarted)
            {
                RefusedPosts++;
                return false;
            }

            lock (PostingThreads)
            {
                PostingThreads.Add(System.Threading.Thread.CurrentThread.ManagedThreadId);
            }

            if (RunInline && action != null)
            {
                action();
            }

            return true;
        }
    }
}
