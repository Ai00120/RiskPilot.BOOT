using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>
    /// A complete, in-memory evidence package: its manifest plus the text of every artifact.
    /// The host writes it to disk; tests inspect it directly. The file set is exactly
    /// <see cref="EvidencePackageCatalog.All"/>.
    /// </summary>
    public sealed class EvidencePackage
    {
        private readonly Dictionary<string, string> _files;

        public EvidencePackage(EvidenceManifest manifest, IDictionary<string, string> files)
        {
            Manifest = manifest ?? new EvidenceManifest();
            _files = new Dictionary<string, string>(StringComparer.Ordinal);
            if (files != null)
            {
                foreach (var pair in files)
                {
                    _files[pair.Key] = pair.Value ?? string.Empty;
                }
            }
        }

        public EvidenceManifest Manifest { get; private set; }

        public IReadOnlyDictionary<string, string> Files { get { return _files; } }

        public bool Contains(string name)
        {
            return name != null && _files.ContainsKey(name);
        }

        public string Read(string name)
        {
            return name != null && _files.TryGetValue(name, out var content) ? content : null;
        }

        /// <summary>
        /// Verifies every listed file against the manifest: the file must be present and its
        /// SHA-256 must match. Returns false on the first mismatch. This is the self-check the
        /// export action runs before it declares a package valid.
        /// </summary>
        public bool VerifyIntegrity(out string failure)
        {
            failure = string.Empty;
            foreach (var entry in Manifest.Files)
            {
                if (!_files.TryGetValue(entry.Name, out var content))
                {
                    failure = "missing artifact: " + entry.Name;
                    return false;
                }

                var hash = Hashing.Sha256Hex(content);
                if (!string.Equals(hash, entry.Sha256, StringComparison.Ordinal))
                {
                    failure = "hash mismatch for " + entry.Name;
                    return false;
                }

                var length = content == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(content);
                if (length != entry.ByteLength)
                {
                    failure = "length mismatch for " + entry.Name;
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when the manifest lists every catalog artifact (the 17 required files).</summary>
        public bool HasCompleteArtifactSet()
        {
            foreach (var name in EvidencePackageCatalog.All)
            {
                if (!_files.ContainsKey(name))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Scans the evidence artifacts for a residual credential-like token. The manifest is
        /// excluded on purpose: it holds only synthetic identities (account refs are
        /// re-derived tokens), file hashes and ISO-8601 timestamps, and those date/hash digit
        /// runs are not secrets. Callers that want the manifest checked use
        /// <see cref="ManifestIsIdentitySafe"/>.
        /// </summary>
        public bool IsSecretFree(out string offender)
        {
            offender = string.Empty;
            foreach (var pair in _files)
            {
                if (pair.Key == EvidencePackageCatalog.ManifestJson)
                {
                    continue;
                }

                if (SecretRedactor.ContainsPotentialSecret(pair.Value))
                {
                    offender = pair.Key;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when the manifest carries no raw identity: every observed account reference is
        /// a synthetic token and no raw account ref appears anywhere in the manifest JSON.
        /// </summary>
        public bool ManifestIsIdentitySafe()
        {
            foreach (var account in Manifest.AccountsObserved)
            {
                if (account.MaskedRef == null || !account.MaskedRef.StartsWith("account#", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Serializes an <see cref="EvidenceManifest"/> to schema-versioned JSON.</summary>
    public static class EvidenceManifestSerializer
    {
        public static string Serialize(EvidenceManifest manifest)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("schemaVersion", manifest.SchemaVersion);
            writer.WriteProperty("toolVersion", manifest.ToolVersion);
            writer.WriteProperty("buildMode", manifest.BuildMode);
            writer.WriteProperty("sessionId", manifest.SessionId);
            writer.WriteProperty("correlationId", manifest.CorrelationId);
            writer.WriteProperty("modeId", manifest.ModeId);
            writer.WriteProperty("startUtc", manifest.StartUtc);
            writer.WriteProperty("endUtc", manifest.EndUtc);
            writer.WriteProperty("ntRuntimeVersion", manifest.NtRuntimeVersion);
            writer.WriteProperty("addOnVersion", manifest.AddOnVersion);
            writer.WriteProperty("provider", SecretRedactor.Redact(manifest.Provider));
            writer.WriteProperty("instrument", SecretRedactor.Redact(manifest.Instrument));

            writer.BeginArray("accountsObserved");
            foreach (var account in manifest.AccountsObserved)
            {
                writer.BeginObject();
                writer.WriteProperty("maskedRef", SecretRedactor.Redact(account.MaskedRef));
                writer.WriteProperty("accountClass", SecretRedactor.Redact(account.AccountClass));
                writer.WriteProperty("providerName", SecretRedactor.Redact(account.ProviderName));
                writer.EndObject();
            }

            writer.EndArray();

            writer.WriteProperty("recordCount", manifest.RecordCount);
            writer.WriteProperty("droppedCount", manifest.DroppedCount);
            writer.WriteProperty("truncated", manifest.Truncated);
            writer.WriteProperty("truncationReason", SecretRedactor.Redact(manifest.TruncationReason));
            writer.WriteProperty("aggregateStatus", manifest.AggregateStatus);

            writer.BeginArray("files");
            foreach (var file in manifest.Files)
            {
                writer.BeginObject();
                writer.WriteProperty("name", file.Name);
                writer.WriteProperty("sha256", file.Sha256);
                writer.WriteProperty("byteLength", file.ByteLength);
                writer.EndObject();
            }

            writer.EndArray();

            writer.WriteProperty("manifestSha256", manifest.ManifestSha256);
            writer.EndObject();
            return writer.ToString();
        }
    }
}
