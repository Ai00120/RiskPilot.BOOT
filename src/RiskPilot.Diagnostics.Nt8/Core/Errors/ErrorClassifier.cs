using System;
using System.IO;
using System.Text;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Core.Errors
{
    /// <summary>
    /// Maps exceptions and failure signals onto a stable <see cref="ErrorClass"/> so that
    /// reports classify failures consistently and never leak a raw stack trace.
    /// </summary>
    public static class ErrorClassifier
    {
        public static ErrorClass Classify(Exception exception)
        {
            if (exception == null)
            {
                return ErrorClass.None;
            }

            if (exception is ObjectDisposedException)
            {
                return ErrorClass.HostUnavailable;
            }

            if (exception is NullReferenceException)
            {
                return ErrorClass.HostUnavailable;
            }

            if (exception is InvalidOperationException)
            {
                return ErrorClass.DataUnavailable;
            }

            if (exception is ArgumentException || exception is FormatException)
            {
                return ErrorClass.ConfigurationInvalid;
            }

            if (exception is OverflowException)
            {
                return ErrorClass.SerializationFailure;
            }

            if (exception is IOException)
            {
                return ErrorClass.SerializationFailure;
            }

            if (exception is ObjectDisposedException)
            {
                return ErrorClass.HostUnavailable;
            }

            return ErrorClass.Unexpected;
        }

        /// <summary>
        /// Produces a redacted, single-line description of an exception (type and message
        /// only) suitable for a log line or an evidence value.
        /// </summary>
        public static string Describe(Exception exception)
        {
            if (exception == null)
            {
                return "no error";
            }

            var builder = new StringBuilder();
            builder.Append(exception.GetType().Name);
            if (!string.IsNullOrEmpty(exception.Message))
            {
                builder.Append(": ").Append(exception.Message);
            }

            return SecretRedactor.Redact(builder.ToString());
        }
    }
}
