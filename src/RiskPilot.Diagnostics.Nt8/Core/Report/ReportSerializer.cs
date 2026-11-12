using System.Linq;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Report
{
    /// <summary>
    /// Serializes a <see cref="DiagnosticReport"/> to schema-versioned JSON. Every string
    /// value passes through <see cref="SecretRedactor"/> so the on-disk report can never
    /// carry a credential, account number or operator path.
    /// </summary>
    public static class ReportSerializer
    {
        public static string Serialize(DiagnosticReport report)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("schemaVersion", report.SchemaVersion);
            writer.WriteProperty("toolVersion", report.ToolVersion);
            writer.WriteProperty("buildMode", report.BuildMode);
            writer.WriteProperty("sessionId", report.SessionId);
            writer.WriteProperty("correlationId", report.CorrelationId);
            writer.WriteProperty("generatedAtUtc", report.GeneratedAtUtc);
            writer.WriteProperty("sessionStartedAtUtc", report.SessionStartedAtUtc);
            writer.WriteProperty("environmentClass", SecretRedactor.Redact(report.EnvironmentClass));
            writer.WriteProperty("tradingDisabled", report.TradingDisabled);
            writer.WriteProperty("sourceTimeBasis", report.SourceTimeBasis);
            writer.WriteProperty("aggregateStatus", report.AggregateStatus().ToWire());

            writer.WriteProperty("disclaimer",
                "Diagnostic evidence only. Historical baseline figures are NOT current runtime data. " +
                "Values marked Unobservable cannot be proven through public NinjaTrader APIs.");

            writer.BeginArray("results");
            foreach (var result in report.Results)
            {
                WriteResult(writer, result);
            }

            writer.EndArray();

            writer.BeginArray("findings");
            foreach (var finding in report.Findings)
            {
                writer.BeginObject();
                writer.WriteProperty("checkId", SecretRedactor.Redact(finding.CheckId));
                writer.WriteProperty("moduleId", SecretRedactor.Redact(finding.ModuleId));
                writer.WriteProperty("status", finding.Status.ToWire());
                writer.WriteProperty("severity", finding.Severity.ToWire());
                writer.WriteProperty("errorClass", finding.ErrorClass.ToWire());
                writer.WriteProperty("message", SecretRedactor.Redact(finding.Message));
                writer.WriteStringArrayProperty("reasonCodes", SecretRedactor.RedactArray(finding.ReasonCodes));
                writer.WriteProperty("correlationId", SecretRedactor.Redact(finding.CorrelationId));
                writer.WriteProperty("observedAtUtc", finding.ObservedAtUtc);
                writer.EndObject();
            }

            writer.EndArray();

            writer.WriteStringArrayProperty("unobservable", SecretRedactor.RedactArray(report.Unobservable.ToArray()));

            writer.BeginObject("counters");
            writer.WriteProperty("enqueued", report.Counters.Enqueued);
            writer.WriteProperty("processed", report.Counters.Processed);
            writer.WriteProperty("dropped", report.Counters.Dropped);
            writer.WriteProperty("duplicates", report.Counters.Duplicates);
            writer.WriteProperty("sequenceGaps", report.Counters.SequenceGaps);
            writer.WriteProperty("timestampInvalid", report.Counters.TimestampInvalid);
            writer.EndObject();
            writer.EndObject();
            return writer.ToString();
        }

        private static void WriteResult(JsonWriter writer, Core.Result.DiagnosticResult result)
        {
            writer.BeginObject();
            writer.WriteProperty("moduleId", SecretRedactor.Redact(result.ModuleId));
            writer.WriteProperty("checkId", SecretRedactor.Redact(result.CheckId));
            writer.WriteProperty("status", result.Status.ToWire());
            writer.WriteProperty("severity", result.Severity.ToWire());
            writer.WriteProperty("errorClass", result.ErrorClass.ToWire());
            writer.WriteProperty("remediationHint", SecretRedactor.Redact(result.RemediationHint));
            writer.WriteProperty("correlationId", SecretRedactor.Redact(result.CorrelationId));
            writer.WriteProperty("evaluatedAtUtc", result.EvaluatedAtUtc);
            writer.WriteStringArrayProperty("reasonCodes", SecretRedactor.RedactArray(result.ReasonCodes));

            writer.BeginArray("evidence");
            foreach (var evidence in result.Evidence)
            {
                writer.BeginObject();
                writer.WriteProperty("source", SecretRedactor.Redact(evidence.Source));
                writer.WriteProperty("observedValue", SecretRedactor.Redact(evidence.ObservedValue));
                writer.WriteProperty("isObservable", evidence.IsObservable);
                writer.WriteProperty("observedAtUtc", evidence.ObservedAtUtc);
                writer.EndObject();
            }

            writer.EndArray();
            writer.EndObject();
        }
    }
}
