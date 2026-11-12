using System.Security.Cryptography;
using System.Text;

namespace RiskPilot.Diagnostics.Nt8.Core.Report
{
    /// <summary>
    /// Deterministic SHA-256 helpers used to fingerprint report payloads and manifest
    /// entries. Hashing operates on UTF-8 bytes so the digest is stable across hosts.
    /// </summary>
    public static class Hashing
    {
        public static string Sha256Hex(string content)
        {
            return Sha256Hex(Encoding.UTF8.GetBytes(content ?? string.Empty));
        }

        public static string Sha256Hex(byte[] content)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(content ?? new byte[0]);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
