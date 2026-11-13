using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using RiskPilot.Diagnostics.Nt8.Core.Contract;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Reads the production source for the security scan from the file system. It is strictly
    /// read-only: it enumerates and reads files and never writes.
    /// <para>
    /// The scan is <b>scoped to the RiskPilot.BOOT installation directory only</b>. It resolves
    /// either the standalone repo source folder (<c>src/RiskPilot.Diagnostics.Nt8</c>) or the
    /// NinjaScript custom-compile folder
    /// (<c>Documents\NinjaTrader 8\bin\Custom\AddOns\RiskPilot.Diagnostics.Nt8</c>). It never
    /// resolves a bare <c>AddOns</c> parent folder, so an unrelated third-party or user-installed
    /// AddOn can never be scanned or produce a BOOT security finding.
    /// </para>
    /// <para>
    /// Every path is canonicalized and confined to the resolved root before it is read or
    /// enumerated. Rooted and <c>..</c>-bearing relative paths, and any symbolic-link / reparse
    /// point (on the root, a file, or a directory) are refused, so the scan cannot traverse into a
    /// sibling AddOn directory or a parent directory. A file or directory that cannot be enumerated
    /// or read marks the source incomplete (never a silently-clean scan). When the expected BOOT
    /// directory is missing, inaccessible or invalid, the source is reported unavailable so the
    /// scan stays INCONCLUSIVE, never a false PASS.
    /// </para>
    /// </summary>
    public sealed class FileSystemSecurityScanSource : ISecurityScanSource
    {
        /// <summary>The BOOT installation directory name that terminates every resolved root.</summary>
        private const string BootDirectoryName = "RiskPilot.Diagnostics.Nt8";

        /// <summary>
        /// How deep the candidate-root presence probe looks for a <c>*.cs</c> file. It is a
        /// bounded, reparse-aware probe (never <c>SearchOption.AllDirectories</c>, which would
        /// follow a symbolic link out of the tree).
        /// </summary>
        private const int RootProbeDepth = 8;

        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private readonly string _sourceRoot;
        private readonly List<string> _relativeFiles;
        private readonly bool _complete;

        public FileSystemSecurityScanSource()
            : this(null)
        {
        }

        /// <summary>
        /// Creates the source. When <paramref name="sourceRoot"/> is supplied it is used as the
        /// BOOT root after validation (it must be a real, non-reparse directory); otherwise a
        /// candidate BOOT root is resolved. A missing, invalid or reparse-point root yields an
        /// unavailable source rather than an empty (falsely clean) scan.
        /// </summary>
        public FileSystemSecurityScanSource(string sourceRoot)
        {
            _relativeFiles = new List<string>();
            _sourceRoot = ResolveRoot(sourceRoot);

            var complete = false;
            if (!string.IsNullOrEmpty(_sourceRoot))
            {
                var files = new List<string>();
                complete = CollectFiles(_sourceRoot, _sourceRoot, files);
                files.Sort(StringComparer.Ordinal);
                _relativeFiles.AddRange(files);
            }

            // Complete only when the whole permitted tree was enumerated and it was non-empty:
            // an empty or partially-enumerated tree is never reported as a clean, complete scan.
            _complete = complete && _relativeFiles.Count > 0;
        }

        /// <summary>Resolved production source root, or empty when none was found.</summary>
        public string SourceRoot { get { return _sourceRoot ?? string.Empty; } }

        public bool IsAvailable
        {
            get { return !string.IsNullOrEmpty(_sourceRoot) && _relativeFiles.Count > 0; }
        }

        /// <summary>
        /// True only when the entire permitted tree was enumerated and every file was read without
        /// error. A skipped symlink, an unreadable directory or a partial enumeration makes this
        /// false, so the scan can never claim a clean, complete pass over a source set it did not
        /// fully cover.
        /// </summary>
        public bool IsComplete { get { return _complete; } }

        public IReadOnlyList<string> GetSourceFiles()
        {
            return new List<string>(_relativeFiles);
        }

        public string ReadFile(string relativePath)
        {
            var full = ResolveWithinRoot(relativePath);
            if (full == null)
            {
                return null;
            }

            try
            {
                return File.Exists(full) ? File.ReadAllText(full) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool FileExists(string relativePath)
        {
            var full = ResolveWithinRoot(relativePath);
            if (full == null)
            {
                return false;
            }

            try
            {
                return File.Exists(full);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves a relative path to a full path inside the source root, or null when it is
        /// rooted, escapes the root via <c>..</c>, or crosses a reparse point. This is the single
        /// containment gate every read passes through.
        /// </summary>
        private string ResolveWithinRoot(string relativePath)
        {
            if (string.IsNullOrEmpty(_sourceRoot) || string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            try
            {
                // A rooted (absolute or drive-qualified) relative path is never accepted.
                if (Path.IsPathRooted(relativePath))
                {
                    return null;
                }

                var rootFull = Path.GetFullPath(_sourceRoot);
                var full = Path.GetFullPath(Path.Combine(rootFull, relativePath));
                if (!IsWithinRoot(rootFull, full))
                {
                    return null;
                }

                // Walk each component from the root: a reparse point anywhere on the path (a
                // symbolic link or junction) is refused so a read can never escape the tree.
                var current = rootFull;
                foreach (var part in RelativeParts(rootFull, full))
                {
                    current = Path.Combine(current, part);
                    if (HasReparsePoint(current))
                    {
                        return null;
                    }
                }

                return full;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ResolveRoot(string explicitRoot)
        {
            if (!string.IsNullOrEmpty(explicitRoot))
            {
                // An explicit root is the caller's BOOT directory (the host or a test). It is still
                // validated, so a missing/invalid/reparse root is unavailable, never empty.
                return ValidateRoot(explicitRoot);
            }

            return ResolveDefaultRoot(SafeBaseDirectory(), SafeMyDocuments());
        }

        /// <summary>
        /// Resolves the BOOT source root from the supplied probe bases, or returns an empty string
        /// when no BOOT directory is present. It is public so the resolution (in particular that it
        /// selects the BOOT child of <c>AddOns</c> and never a bare <c>AddOns</c> parent) can be
        /// regression-tested without touching the real file system layout. The supplied bases are
        /// only probed, never written.
        /// </summary>
        public static string ResolveDefaultRoot(string baseDirectory, string documentsDirectory)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(baseDirectory))
            {
                // Standalone repo layout: <root>/src/RiskPilot.Diagnostics.Nt8 from the assembly's
                // own bin folder.
                candidates.Add(Path.Combine(baseDirectory, "..", "..", "..", "src", BootDirectoryName));
                candidates.Add(Path.Combine(baseDirectory, "..", "..", "src", BootDirectoryName));
                candidates.Add(Path.Combine(baseDirectory, "src", BootDirectoryName));
                // Standalone repo build output folder.
                candidates.Add(Path.Combine(baseDirectory, "..", "..", "..", "..", "src", BootDirectoryName));
                // NinjaScript custom-compile layout next to the assembly's Custom folder.
                candidates.Add(Path.Combine(baseDirectory, "AddOns", BootDirectoryName));
            }

            // NinjaScript custom-compile layout: source copied under
            // Documents\NinjaTrader 8\bin\Custom\AddOns\RiskPilot.Diagnostics.Nt8\. The BOOT
            // directory is the *child* of AddOns; a bare AddOns parent is never a candidate.
            if (!string.IsNullOrEmpty(documentsDirectory))
            {
                candidates.Add(Path.Combine(documentsDirectory, "NinjaTrader 8", "bin", "Custom", "AddOns", BootDirectoryName));
            }

            foreach (var candidate in candidates)
            {
                var validated = ValidateRoot(candidate);
                if (!string.IsNullOrEmpty(validated))
                {
                    return validated;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Validates a candidate BOOT root: it must be a real (non-reparse) directory that contains
        /// at least one <c>*.cs</c> file within the bounded probe depth. Returns the canonicalized
        /// root, or an empty string when the candidate is missing, invalid or a reparse point.
        /// </summary>
        private static string ValidateRoot(string candidate)
        {
            try
            {
                var full = NormalizeRoot(candidate);
                if (full == null || !Directory.Exists(full))
                {
                    return string.Empty;
                }

                // The BOOT root itself must be a real directory, never a symbolic link.
                if (HasReparsePoint(full))
                {
                    return string.Empty;
                }

                return ContainsCsFiles(full, RootProbeDepth) ? full : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Canonicalizes a candidate root and trims trailing separators so the containment prefix
        /// is unambiguous. Returns null for a blank path.
        /// </summary>
        private static string NormalizeRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root) && string.Equals(full, root, PathComparison))
            {
                return full;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// True when <paramref name="full"/> is the root itself or lies under it. Both paths must
        /// already be canonicalized full paths.
        /// </summary>
        private static bool IsWithinRoot(string rootFull, string full)
        {
            if (string.Equals(full, rootFull, PathComparison))
            {
                return true;
            }

            var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, PathComparison);
        }

        /// <summary>Returns the path components of <paramref name="full"/> below <paramref name="rootFull"/>.</summary>
        private static IEnumerable<string> RelativeParts(string rootFull, string full)
        {
            if (string.Equals(full, rootFull, PathComparison))
            {
                yield break;
            }

            var relative = full.Substring(rootFull.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                yield return part;
            }
        }

        /// <summary>
        /// Enumerates the <c>*.cs</c> files under the permitted tree. Returns false (incomplete) when
        /// any directory cannot be listed or any reparse point is encountered, so the scan is never
        /// reported as a clean, complete pass over a partially covered tree. Reparse points are
        /// never followed.
        /// </summary>
        private static bool CollectFiles(string root, string directory, List<string> into)
        {
            var complete = true;

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
            }
            catch (Exception)
            {
                return false;
            }

            foreach (var file in files)
            {
                if (HasReparsePoint(file))
                {
                    // A symlinked file is never followed: it could point at a sibling AddOn.
                    complete = false;
                    continue;
                }

                var relative = MakeRelative(root, file);
                if (IsExcluded(relative))
                {
                    continue;
                }

                into.Add(relative);
            }

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception)
            {
                return false;
            }

            foreach (var sub in subdirectories)
            {
                if (HasReparsePoint(sub))
                {
                    // A symlinked directory is never traversed: it could escape into a sibling.
                    complete = false;
                    continue;
                }

                var relativeDir = MakeRelative(root, sub);
                if (IsExcluded(relativeDir + "/"))
                {
                    continue;
                }

                if (!CollectFiles(root, sub, into))
                {
                    complete = false;
                }
            }

            return complete;
        }

        private static bool ContainsCsFiles(string directory, int depth)
        {
            try
            {
                if (Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return true;
                }

                if (depth <= 0)
                {
                    return false;
                }

                foreach (var sub in Directory.GetDirectories(directory))
                {
                    if (HasReparsePoint(sub))
                    {
                        continue;
                    }

                    if (ContainsCsFiles(sub, depth - 1))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // An unreadable directory simply contributes no evidence of production source.
            }

            return false;
        }

        private static bool HasReparsePoint(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsExcluded(string relative)
        {
            var normalized = "/" + (relative ?? string.Empty).Replace('\\', '/');
            foreach (var fragment in SecurityScanCatalog.ExcludedPathFragments)
            {
                if (normalized.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string MakeRelative(string root, string fullPath)
        {
            var rootFull = Path.GetFullPath(root);
            var pathFull = Path.GetFullPath(fullPath);
            if (pathFull.StartsWith(rootFull, PathComparison))
            {
                var trimmed = pathFull.Substring(rootFull.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return trimmed.Replace('\\', '/');
            }

            return Path.GetFileName(pathFull);
        }

        private static string SafeBaseDirectory()
        {
            try
            {
                return AppContext.BaseDirectory;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SafeMyDocuments()
        {
            try
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
