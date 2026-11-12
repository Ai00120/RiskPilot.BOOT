using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class ManifestTests
    {
        [Fact]
        public void Sha256Hex_MatchesKnownVector()
        {
            // SHA-256 of the ASCII string "abc".
            Assert.Equal(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Hashing.Sha256Hex("abc"));
        }

        [Fact]
        public void AddEntry_ComputesHashAndLength()
        {
            var manifest = new ReportManifest();
            manifest.AddEntry("report.json", "{\"a\":1}");

            var entry = Assert.Single(manifest.Entries);
            Assert.Equal("report.json", entry.Name);
            Assert.Equal(7, entry.ByteLength);
            Assert.Equal(Hashing.Sha256Hex("{\"a\":1}"), entry.Sha256);
        }

        [Fact]
        public void ComputeManifestHash_IsDeterministic()
        {
            var first = new ReportManifest
            {
                SessionId = "diag_20261112_080059_" + new string('a', 32),
                GeneratedAtUtc = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc),
                AggregateStatus = DiagnosticStatus.PASS
            };
            first.AddEntry("report.json", "payload");

            var second = new ReportManifest
            {
                SessionId = first.SessionId,
                GeneratedAtUtc = first.GeneratedAtUtc,
                AggregateStatus = DiagnosticStatus.PASS
            };
            second.AddEntry("report.json", "payload");

            Assert.Equal(first.ComputeManifestHash(), second.ComputeManifestHash());
        }

        [Fact]
        public void ComputeManifestHash_ChangesWithContent()
        {
            var manifest = new ReportManifest { GeneratedAtUtc = DateTime.UtcNow };
            manifest.AddEntry("report.json", "one");
            var first = manifest.ComputeManifestHash();

            manifest.Entries.Clear();
            manifest.AddEntry("report.json", "two");
            var second = manifest.ComputeManifestHash();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void ManifestSerializer_EmitsValidJsonWithHash()
        {
            var manifest = new ReportManifest
            {
                SessionId = "diag_20261112_080059_" + new string('b', 32),
                GeneratedAtUtc = DateTime.UtcNow,
                AggregateStatus = DiagnosticStatus.FAIL
            };
            manifest.AddEntry("report.json", "payload");
            manifest.ComputeManifestHash();

            var json = ManifestSerializer.Serialize(manifest);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(manifest.ManifestSha256, root.GetProperty("manifestSha256").GetString());
            Assert.Equal("FAIL", root.GetProperty("aggregateStatus").GetString());
            Assert.Equal(1, root.GetProperty("entries").GetArrayLength());
        }

        [Fact]
        public void ManifestHash_IsSha256SizedHex()
        {
            var manifest = new ReportManifest { GeneratedAtUtc = DateTime.UtcNow };
            var hash = manifest.ComputeManifestHash();
            Assert.Equal(64, hash.Length);
            Assert.Matches("^[0-9a-f]{64}$", hash);
        }
    }
}
