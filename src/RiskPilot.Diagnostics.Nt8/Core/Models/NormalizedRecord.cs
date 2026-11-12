using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Common envelope shared by every normalized model. Records are immutable snapshots:
    /// the host layer fills them and the core only reads them.
    /// </summary>
    public abstract class NormalizedRecord
    {
        /// <summary>Session that produced the record (isolation boundary).</summary>
        public string SessionId { get; set; }

        /// <summary>Monotonic per-session sequence number assigned by the ingest queue.</summary>
        public long Sequence { get; set; }

        /// <summary>
        /// Source-reported per-stream sequence number, when the source exposes one. A gap in
        /// this value is detected independently of the internal <see cref="Sequence"/>.
        /// </summary>
        public long? SourceSequence { get; set; }

        /// <summary>
        /// Logical stream this record belongs to, used for sequence-gap detection. Streams
        /// are isolated so a gap in one stream never masks or merges with another.
        /// </summary>
        public virtual string StreamKey { get { return GetType().Name; } }

        /// <summary>Timestamp reported by the data source, in UTC. Null when not provided.</summary>
        public DateTime? SourceTimestampUtc { get; set; }

        /// <summary>Trust basis of <see cref="SourceTimestampUtc"/>.</summary>
        public SourceTimeBasis SourceTimeBasis { get; set; }

        /// <summary>Timestamp the diagnostic tool ingested the record, in UTC.</summary>
        public DateTime IngestedAtUtc { get; set; }

        /// <summary>Correlation id linking this record to the diagnostic pass that produced it.</summary>
        public string CorrelationId { get; set; }

        protected NormalizedRecord()
        {
            SessionId = string.Empty;
            CorrelationId = string.Empty;
            SourceTimeBasis = SourceTimeBasis.Unknown;
        }
    }
}
