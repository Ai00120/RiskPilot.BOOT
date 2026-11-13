using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Contract
{
    /// <summary>
    /// A read-only view of one production source file, as seen by the security scan. It resolves
    /// the file list and text but never writes. A host or a test supplies an implementation; the
    /// scanner itself never touches the file system directly, so the scan logic stays host-free
    /// and testable.
    /// </summary>
    public interface ISecurityScanSource
    {
        /// <summary>
        /// True when the source could be read at all. False (or a throwing implementation) makes
        /// the scan report UNOBSERVABLE, never PASS (fail-closed).
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Repo-relative paths (POSIX form) of the production source files to scan. It excludes
        /// offline stubs and build output. The list is ordered deterministically.
        /// </summary>
        IReadOnlyList<string> GetSourceFiles();

        /// <summary>Reads one file's text, or null when it cannot be read.</summary>
        string ReadFile(string relativePath);

        /// <summary>True when the repo-relative path exists in the source set.</summary>
        bool FileExists(string relativePath);
    }
}
