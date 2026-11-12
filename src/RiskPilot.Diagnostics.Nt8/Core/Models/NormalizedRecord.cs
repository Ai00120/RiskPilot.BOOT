using System;
using System.Globalization;
using System.Text;

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

        /// <summary>
        /// Collision-safe duplicate identity. It combines the stream key, the source
        /// sequence (when present), the source timestamp, and a content fingerprint of the
        /// record's distinguishing fields. Two genuinely different events can never share
        /// an identity, so a valid event is never discarded as a "duplicate" of another
        /// event that merely happened to share a timestamp.
        /// <para>
        /// The only unavoidable ambiguity is two events that are byte-for-byte identical in
        /// stream, sequence, timestamp and content. When the source provides no sequence,
        /// such events are indistinguishable and are intentionally treated as duplicates.
        /// </para>
        /// </summary>
        public virtual string DuplicateIdentity()
        {
            var builder = new StringBuilder(96);
            builder.Append(StreamKey ?? GetType().Name);
            builder.Append('|');
            builder.Append(SourceSequence.HasValue
                ? "seq:" + SourceSequence.Value.ToString(CultureInfo.InvariantCulture)
                : "noseq");
            builder.Append('|');
            builder.Append(SourceTimestampUtc.HasValue
                ? "ts:" + SourceTimestampUtc.Value.Ticks.ToString(CultureInfo.InvariantCulture)
                : "nots");
            builder.Append('|');
            builder.Append(ContentFingerprint());
            return builder.ToString();
        }

        /// <summary>
        /// Content-specific fingerprint of the fields that distinguish one record from
        /// another on the same stream. The base implementation returns an empty string;
        /// payload-bearing records override it so simultaneous distinct events do not
        /// collide. Implementations must be deterministic and culture-invariant.
        /// </summary>
        protected virtual string ContentFingerprint()
        {
            return string.Empty;
        }

        /// <summary>Culture-invariant formatting helper for fingerprints.</summary>
        protected static string Invariant(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Culture-invariant formatting helper for fingerprints.</summary>
        protected static string Invariant(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

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
