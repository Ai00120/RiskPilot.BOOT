using System;
using System.Collections.Generic;
using System.IO;
using RiskPilot.Diagnostics.Nt8.Core.Contract;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Reads the production source for the security scan from the file system. It is strictly
    /// read-only: it enumerates and reads files and never writes. It resolves the source root by
    /// probing a small, deterministic set of candidate locations around the assembly directory
    /// (the standalone repo layout and the NinjaScript custom-compile folder). When no candidate
    /// contains the production source, the source is reported unavailable so the scan stays
    /// UNOBSERVABLE, never a false PASS.
    /// </summary>
    public sealed class FileSystemSecurityScanSource : ISecurityScanSource
    {
        private readonly string _sourceRoot;
        private readonly List<string> _relativeFiles;

        public FileSystemSecurityScanSource()
            : this(null)
        {
        }

        /// <summary>
        /// Creates the source. When <paramref name="sourceRoot"/> is supplied it is used directly;
        /// otherwise a candidate root is resolved. Both probe for the production source so a
        /// missing root yields an unavailable source rather than an empty (falsely clean) scan.
        /// </summary>
        public FileSystemSecurityScanSource(string sourceRoot)
        {
            _sourceRoot = ResolveRoot(sourceRoot);
            _relativeFiles = new List<string>();

            if (!string.IsNullOrEmpty(_sourceRoot))
            {
                try
                {
                    CollectFiles(_sourceRoot, _sourceRoot, _relativeFiles);
                    _relativeFiles.Sort(StringComparer.Ordinal);
                }
                catch (Exception)
                {
                    _relativeFiles.Clear();
                }
            }
        }

        /// <summary>Resolved production source root, or empty when none was found.</summary>
        public string SourceRoot { get { return _sourceRoot ?? string.Empty; } }

        public bool IsAvailable
        {
            get { return !string.IsNullOrEmpty(_sourceRoot) && _relativeFiles.Count > 0; }
        }

        public IReadOnlyList<string> GetSourceFiles()
        {
            return new List<string>(_relativeFiles);
        }

        public string ReadFile(string relativePath)
        {
            if (string.IsNullOrEmpty(_sourceRoot) || string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            try
            {
                var full = Path.GetFullPath(Path.Combine(_sourceRoot, relativePath));

                // Never read outside the resolved source root: a crafted relative path must not be
                // able to pull in an arbitrary file.
                if (!full.StartsWith(Path.GetFullPath(_sourceRoot), StringComparison.Ordinal))
                {
                    return null;
                }

                return File.Exists(full) ? File.ReadAllText(full) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool FileExists(string relativePath)
        {
            if (string.IsNullOrEmpty(_sourceRoot) || string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            try
            {
                return File.Exists(Path.Combine(_sourceRoot, relativePath));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ResolveRoot(string explicitRoot)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(explicitRoot))
            {
                candidates.Add(explicitRoot);
            }

            var baseDir = SafeBaseDirectory();
            if (!string.IsNullOrEmpty(baseDir))
            {
                // Standalone repo layout: <root>/src/RiskPilot.Diagnostics.Nt8/... from the
                // assembly's own bin folder.
                candidates.Add(Path.Combine(baseDir, "..", "..", "..", "src", "RiskPilot.Diagnostics.Nt8"));
                candidates.Add(Path.Combine(baseDir, "..", "..", "src", "RiskPilot.Diagnostics.Nt8"));
                candidates.Add(Path.Combine(baseDir, "src", "RiskPilot.Diagnostics.Nt8"));
                // Standalone repo build output folder.
                candidates.Add(Path.Combine(baseDir, "..", "..", "..", "..", "src", "RiskPilot.Diagnostics.Nt8"));
            }

            // NinjaScript custom-compile layout: source copied under
            // Documents\NinjaTrader 8\bin\Custom\AddOns\.
            var documents = SafeMyDocuments();
            if (!string.IsNullOrEmpty(documents))
            {
                candidates.Add(Path.Combine(documents, "NinjaTrader 8", "bin", "Custom", "AddOns"));
            }

            foreach (var candidate in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(candidate);
                    if (Directory.Exists(full) && Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories).Length > 0)
                    {
                        return full;
                    }
                }
                catch (Exception)
                {
                    // Try the next candidate.
                }
            }

            return string.Empty;
        }

        private static void CollectFiles(string root, string directory, List<string> into)
        {
            foreach (var file in Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
            {
                var relative = MakeRelative(root, file);
                if (IsExcluded(relative))
                {
                    continue;
                }

                into.Add(relative);
            }

            foreach (var sub in Directory.GetDirectories(directory))
            {
                var relativeDir = MakeRelative(root, sub);
                if (IsExcluded(relativeDir + "/"))
                {
                    continue;
                }

                CollectFiles(root, sub, into);
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
            if (pathFull.StartsWith(rootFull, StringComparison.Ordinal))
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
