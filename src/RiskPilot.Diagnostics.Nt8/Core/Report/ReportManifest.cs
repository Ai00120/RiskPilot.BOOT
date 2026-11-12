using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Report
{
    /// <summary>One artifact listed in the manifest, with its content hash.</summary>
    public sealed class ManifestEntry
    {
        public string Name { get; set; }

        public string Sha256 { get; set; }

        public long ByteLength { get; set; }

        public ManifestEntry()
        {
            Name = string.Empty;
            Sha256 = string.Empty;
        }
    }

    /// <summary>
    /// Self-describing manifest for a diagnostic run. The manifest hash is computed over
    /// the canonical JSON of its own entries, giving a single verifiable root digest for
    /// the whole run.
    /// </summary>
    public sealed class ReportManifest
    {
        public string SchemaVersion { get; set; }

        public string ToolVersion { get; set; }

        public string BuildMode { get; set; }

        public string SessionId { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        public string CorrelationId { get; set; }

        public DiagnosticStatus AggregateStatus { get; set; }

        public IList<ManifestEntry> Entries { get; private set; }

        public string ManifestSha256 { get; set; }

        public ReportManifest()
        {
            SchemaVersion = BuildIdentity.SchemaVersion;
            ToolVersion = BuildIdentity.ToolVersion;
            BuildMode = BuildIdentity.BuildMode;
            SessionId = string.Empty;
            CorrelationId = string.Empty;
            Entries = new List<ManifestEntry>();
            ManifestSha256 = string.Empty;
        }

        /// <summary>
        /// Computes and stores the manifest hash over a canonical serialization of the
        /// entries. Returns the hex digest.
        /// </summary>
        public string ComputeManifestHash()
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("schemaVersion", SchemaVersion);
            writer.WriteProperty("toolVersion", ToolVersion);
            writer.WriteProperty("buildMode", BuildMode);
            writer.WriteProperty("sessionId", SessionId);
            writer.WriteProperty("generatedAtUtc", GeneratedAtUtc);
            writer.WriteProperty("correlationId", CorrelationId);
            writer.WriteProperty("aggregateStatus", AggregateStatus.ToWire());
            writer.BeginArray("entries");
            foreach (var entry in Entries)
            {
                writer.BeginObject();
                writer.WriteProperty("name", entry.Name);
                writer.WriteProperty("sha256", entry.Sha256);
                writer.WriteProperty("byteLength", entry.ByteLength);
                writer.EndObject();
            }

            writer.EndArray();
            writer.EndObject();

            ManifestSha256 = Hashing.Sha256Hex(writer.ToString());
            return ManifestSha256;
        }

        public void AddEntry(string name, string content)
        {
            Entries.Add(new ManifestEntry
            {
                Name = name ?? string.Empty,
                Sha256 = Hashing.Sha256Hex(content),
                ByteLength = content == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(content)
            });
        }
    }
}
