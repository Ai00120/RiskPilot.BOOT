using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>
    /// Bounded, thread-safe collector of normalized records for the evidence package. It is
    /// an <see cref="INormalizedEventConsumer"/> that runs on the runtime's drain thread and
    /// only appends; the diagnostic pass reads a copy under the same lock. It is strictly
    /// read-only with respect to the host: it stores what the host already reported.
    /// <para>
    /// The collector is bounded so a long session can never grow memory without limit. When a
    /// list reaches its capacity the newest records are still dropped and the overflow is
    /// recorded as a truncation reason, so the evidence package reports
    /// <c>truncated=true</c> rather than silently presenting a partial list as complete.
    /// </para>
    /// </summary>
    public sealed class EvidenceCollector : INormalizedEventConsumer
    {
        private readonly object _gate = new object();
        private readonly int _capacity;

        private readonly List<OrderRecord> _orders = new List<OrderRecord>();
        private readonly List<ExecutionRecord> _executions = new List<ExecutionRecord>();
        private readonly List<PositionRecord> _positions = new List<PositionRecord>();
        private readonly List<MarketDataRecord> _marketData = new List<MarketDataRecord>();
        private readonly List<DomRecord> _dom = new List<DomRecord>();
        private readonly List<StrategyRecord> _strategies = new List<StrategyRecord>();

        private long _truncatedRecords;
        private bool _truncated;

        public EvidenceCollector(int capacity)
        {
            _capacity = capacity > 0 ? capacity : 10000;
        }

        public string Name { get { return "EvidenceCollector"; } }

        public int Capacity { get { return _capacity; } }

        public bool Truncated { get { lock (_gate) { return _truncated; } } }

        public long TruncatedRecordCount { get { lock (_gate) { return _truncatedRecords; } } }

        public void Consume(NormalizedRecord record)
        {
            if (record == null)
            {
                return;
            }

            lock (_gate)
            {
                if (record is OrderRecord order)
                {
                    Add(_orders, order);
                }
                else if (record is ExecutionRecord execution)
                {
                    Add(_executions, execution);
                }
                else if (record is PositionRecord position)
                {
                    Add(_positions, position);
                }
                else if (record is MarketDataRecord marketData)
                {
                    Add(_marketData, marketData);
                }
                else if (record is DomRecord dom)
                {
                    Add(_dom, dom);
                }
                else if (record is StrategyRecord strategy)
                {
                    Add(_strategies, strategy);
                }
            }
        }

        private void Add<T>(List<T> list, T item)
        {
            if (list.Count >= _capacity)
            {
                _truncated = true;
                _truncatedRecords++;
                return;
            }

            list.Add(item);
        }

        /// <summary>
        /// Copies the collected records into a fresh <see cref="EvidenceDataset"/>. The copy
        /// is taken under the lock so a reader never observes a half-written list.
        /// </summary>
        public EvidenceDataset Snapshot()
        {
            var dataset = new EvidenceDataset();
            lock (_gate)
            {
                foreach (var order in _orders) dataset.Orders.Add(order);
                foreach (var execution in _executions) dataset.Executions.Add(execution);
                foreach (var position in _positions) dataset.Positions.Add(position);
                foreach (var marketData in _marketData) dataset.MarketData.Add(marketData);
                foreach (var dom in _dom) dataset.Dom.Add(dom);
                foreach (var strategy in _strategies) dataset.Strategies.Add(strategy);

                if (_truncated)
                {
                    dataset.TruncationReason = "EVIDENCE_COLLECTOR_TRUNCATED: dropped " + _truncatedRecords
                        + " record(s) beyond capacity " + _capacity + ".";
                }
            }

            return dataset;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _orders.Clear();
                _executions.Clear();
                _positions.Clear();
                _marketData.Clear();
                _dom.Clear();
                _strategies.Clear();
                _truncated = false;
                _truncatedRecords = 0;
            }
        }
    }
}
