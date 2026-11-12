using System;
using System.Security.Cryptography;
using System.Text;

namespace RiskPilot.Diagnostics.Nt8.Core.Security
{
    /// <summary>
    /// Builds stable, non-reversible reference tokens for identities that must never be
    /// written verbatim (account names, order ids, raw instrument handles). The token is
    /// a truncated SHA-256 of the raw value, prefixed by a human-readable kind so that
    /// two references of the same kind can be compared without exposing the source.
    /// </summary>
    public static class SyntheticRef
    {
        private const int HexLength = 10;

        public static string For(string kind, string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue))
            {
                return kind + "#none";
            }

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(kind + ":" + rawValue));
                var builder = new StringBuilder(kind.Length + 1 + HexLength);
                builder.Append(kind).Append('#');
                for (var i = 0; i < HexLength / 2; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
