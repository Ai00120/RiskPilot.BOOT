using System;
using System.IO;
using RiskPilot.Diagnostics.Nt8.Core.Run;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Writes an evidence package to a fresh, per-run directory on disk. Each run gets its own
    /// sub-directory (named by session id) under the configured root, so two runs never
    /// overwrite each other. The root is expected to live outside the Git working tree.
    /// <para>
    /// Every file is written atomically: content is written to a temporary name and then moved
    /// into place, so a reader never sees a partially-written artifact. A file name is
    /// sanitized and constrained to a single path segment so a package can never escape its
    /// own directory.
    /// </para>
    /// </summary>
    public sealed class EvidenceDirectorySink : IEvidenceSink
    {
        private readonly string _root;
        private string _directory;

        public EvidenceDirectorySink(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                throw new ArgumentException("root must be provided", nameof(root));
            }

            _root = root;
            _directory = string.Empty;
        }

        public string DirectoryPath { get { return _directory; } }

        public string Begin(DiagnosticRunResult runResult)
        {
            var sessionId = runResult != null && !string.IsNullOrEmpty(runResult.SessionId)
                ? runResult.SessionId
                : DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

            var leaf = SanitizeSegment(sessionId);
            var directory = Path.Combine(_root, leaf);

            // Never reuse an existing directory: a later run must not overwrite an earlier
            // package. A numeric suffix is appended until a free name is found.
            var attempt = 0;
            while (Directory.Exists(directory))
            {
                attempt++;
                directory = Path.Combine(_root, leaf + "_" + attempt);
            }

            Directory.CreateDirectory(directory);
            _directory = directory;
            return directory;
        }

        public void Write(string name, string content)
        {
            if (string.IsNullOrEmpty(_directory))
            {
                throw new InvalidOperationException("Begin must be called before Write.");
            }

            var safeName = SanitizeSegment(name);
            var target = Path.Combine(_directory, safeName);
            var temp = target + ".tmp";
            File.WriteAllText(temp, content ?? string.Empty);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temp, target);
        }

        private static string SanitizeSegment(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "artifact";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                foreach (var c in invalid)
                {
                    if (chars[i] == c)
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            var sanitized = new string(chars);
            // Constrain to a single path segment on every platform: both separators are
            // replaced explicitly, because the real host runs on Windows where '\' is a
            // separator even though it is not in this build's invalid-char set.
            sanitized = sanitized.Replace('\\', '_').Replace('/', '_');
            return sanitized.Length == 0 ? "artifact" : sanitized;
        }
    }
}
