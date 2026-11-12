using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Report
{
    /// <summary>Serializes a <see cref="ReportManifest"/> to schema-versioned JSON.</summary>
    public static class ManifestSerializer
    {
        public static string Serialize(ReportManifest manifest)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("schemaVersion", manifest.SchemaVersion);
            writer.WriteProperty("toolVersion", manifest.ToolVersion);
            writer.WriteProperty("buildMode", manifest.BuildMode);
            writer.WriteProperty("sessionId", manifest.SessionId);
            writer.WriteProperty("generatedAtUtc", manifest.GeneratedAtUtc);
            writer.WriteProperty("correlationId", manifest.CorrelationId);
            writer.WriteProperty("aggregateStatus", manifest.AggregateStatus.ToWire());
            writer.WriteProperty("manifestSha256", manifest.ManifestSha256);
            writer.BeginArray("entries");
            foreach (var entry in manifest.Entries)
            {
                writer.BeginObject();
                writer.WriteProperty("name", entry.Name);
                writer.WriteProperty("sha256", entry.Sha256);
                writer.WriteProperty("byteLength", entry.ByteLength);
                writer.EndObject();
            }

            writer.EndArray();
            writer.EndObject();
            return writer.ToString();
        }
    }
}
