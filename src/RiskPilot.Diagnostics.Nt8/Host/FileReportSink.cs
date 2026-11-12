using System;
using System.Collections.Generic;
using System.IO;
using RiskPilot.Diagnostics.Nt8.Core.Report;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Writes report artifacts to a directory on disk. The directory is expected to live
    /// outside the repository (per governance), and every file is created atomically by
    /// writing to a temp name first and then replacing the target.
    /// </summary>
    public sealed class FileReportSink : IReportSink
    {
        private readonly string _directory;

        public FileReportSink(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("directory must be provided", nameof(directory));
            }

            _directory = directory;
            Directory.CreateDirectory(_directory);
        }

        public string DirectoryPath { get { return _directory; } }

        public void Write(string name, string content)
        {
            var safeName = Sanitize(name);
            var target = Path.Combine(_directory, safeName);
            var temp = target + ".tmp";
            File.WriteAllText(temp, content ?? string.Empty);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temp, target);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "artifact.json";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
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

            return new string(chars);
        }
    }

    /// <summary>In-memory sink used by tests and by dry runs.</summary>
    public sealed class InMemoryReportSink : IReportSink
    {
        private readonly Dictionary<string, string> _artifacts = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Artifacts { get { return _artifacts; } }

        public void Write(string name, string content)
        {
            _artifacts[name ?? string.Empty] = content ?? string.Empty;
        }
    }
}
