using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>One file in an evidence package, with its content hash and size.</summary>
    public sealed class EvidenceFileEntry
    {
        public string Name { get; set; }

        public string Sha256 { get; set; }

        public long ByteLength { get; set; }

        public EvidenceFileEntry()
        {
            Name = string.Empty;
            Sha256 = string.Empty;
        }
    }

    /// <summary>One observed account, masked for the manifest (never the raw identity).</summary>
    public sealed class MaskedAccountRef
    {
        public string MaskedRef { get; set; }

        public string AccountClass { get; set; }

        public string ProviderName { get; set; }

        public MaskedAccountRef()
        {
            MaskedRef = string.Empty;
            AccountClass = string.Empty;
            ProviderName = string.Empty;
        }
    }

    /// <summary>
    /// The manifest of one evidence package. It carries the required run identity, the
    /// observed runtime facts, the record/drop counts, the truncation status, the list of
    /// output files with their SHA-256 digests, and the overall run status.
    /// </summary>
    public sealed class EvidenceManifest
    {
        public string SchemaVersion { get; set; }

        public string ToolVersion { get; set; }

        public string BuildMode { get; set; }

        public string SessionId { get; set; }

        public string CorrelationId { get; set; }

        public string ModeId { get; set; }

        public DateTime StartUtc { get; set; }

        public DateTime EndUtc { get; set; }

        /// <summary>
        /// NinjaTrader runtime version. There is no public API for it, so this is reported as
        /// <c>UNOBSERVABLE</c> rather than guessed from a file name or build id.
        /// </summary>
        public string NtRuntimeVersion { get; set; }

        public string AddOnVersion { get; set; }

        public string Provider { get; set; }

        public string Instrument { get; set; }

        /// <summary>Accounts observed during the run, masked (never raw).</summary>
        public IList<MaskedAccountRef> AccountsObserved { get; set; }

        public long RecordCount { get; set; }

        public long DroppedCount { get; set; }

        public bool Truncated { get; set; }

        public string TruncationReason { get; set; }

        public IList<EvidenceFileEntry> Files { get; set; }

        public string AggregateStatus { get; set; }

        /// <summary>SHA-256 over the canonical JSON of this manifest's own fields.</summary>
        public string ManifestSha256 { get; set; }

        public EvidenceManifest()
        {
            SchemaVersion = BuildIdentity.SchemaVersion;
            ToolVersion = BuildIdentity.ToolVersion;
            BuildMode = BuildIdentity.BuildMode;
            SessionId = string.Empty;
            CorrelationId = string.Empty;
            ModeId = string.Empty;
            NtRuntimeVersion = "UNOBSERVABLE";
            AddOnVersion = BuildIdentity.ToolVersion;
            Provider = string.Empty;
            Instrument = string.Empty;
            AccountsObserved = new List<MaskedAccountRef>();
            Files = new List<EvidenceFileEntry>();
            TruncationReason = string.Empty;
            AggregateStatus = DiagnosticStatus.NOT_APPLICABLE.ToWire();
            ManifestSha256 = string.Empty;
        }

        /// <summary>Records a file entry from its content (hash and byte length are derived).</summary>
        public void AddFile(string name, string content)
        {
            Files.Add(new EvidenceFileEntry
            {
                Name = name ?? string.Empty,
                Sha256 = Core.Report.Hashing.Sha256Hex(content),
                ByteLength = content == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(content)
            });
        }

        /// <summary>Computes the manifest root hash over a canonical serialization of its fields.</summary>
        public string ComputeManifestHash()
        {
            var writer = new Core.Report.JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("schemaVersion", SchemaVersion);
            writer.WriteProperty("toolVersion", ToolVersion);
            writer.WriteProperty("buildMode", BuildMode);
            writer.WriteProperty("sessionId", SessionId);
            writer.WriteProperty("correlationId", CorrelationId);
            writer.WriteProperty("modeId", ModeId);
            writer.WriteProperty("startUtc", StartUtc);
            writer.WriteProperty("endUtc", EndUtc);
            writer.WriteProperty("ntRuntimeVersion", NtRuntimeVersion);
            writer.WriteProperty("addOnVersion", AddOnVersion);
            writer.WriteProperty("provider", Provider);
            writer.WriteProperty("instrument", Instrument);
            writer.WriteProperty("recordCount", RecordCount);
            writer.WriteProperty("droppedCount", DroppedCount);
            writer.WriteProperty("truncated", Truncated);
            writer.WriteProperty("aggregateStatus", AggregateStatus);
            writer.BeginArray("files");
            foreach (var file in Files)
            {
                writer.BeginObject();
                writer.WriteProperty("name", file.Name);
                writer.WriteProperty("sha256", file.Sha256);
                writer.WriteProperty("byteLength", file.ByteLength);
                writer.EndObject();
            }

            writer.EndArray();
            writer.EndObject();
            ManifestSha256 = Core.Report.Hashing.Sha256Hex(writer.ToString());
            return ManifestSha256;
        }
    }
}
