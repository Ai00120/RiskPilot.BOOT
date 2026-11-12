using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M4 — Instrument attestation &amp; D-078. Reproduces the D-096 configured-route
    /// preconditions and the D-078 operator field-approval gate from read-only host
    /// metadata, and reports the *precise* failure causes separately:
    /// <list type="bullet">
    /// <item><c>HOST_UNAVAILABLE</c> — the host metadata could not be read.</item>
    /// <item><c>D078_FIELD_APPROVAL_NOT_APPROVED</c> — no operator PASS is recorded.</item>
    /// <item><c>MISSING_INSTRUMENT_SPECIFICATION</c> — the contract is not in the registry.</item>
    /// </list>
    /// The tool never approves anything and never presents unapproved metadata as attested.
    /// </summary>
    public sealed class M4InstrumentAttestationProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "M4"; } }

        public string Title { get { return "Instrument Attestation / D-078"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.Attestation : null;

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M4.HOST_METADATA", correlation, now,
                    "No instrument attestation observation was supplied by the host layer.",
                    "Subscribe an instrument so its metadata becomes readable after DataLoaded."));
                return results;
            }

            results.Add(BuildHostMetadataCheck(observation, correlation, now));
            results.Add(BuildFieldMetadataCheck(observation, correlation, now));
            results.Add(BuildRouteCandidateCheck(observation, correlation, now));
            results.Add(BuildRegistryCheck(observation, correlation, now));
            results.Add(BuildD078Check(observation, correlation, now));
            results.Add(BuildAttestationOutcomeCheck(observation, correlation, now));

            return results;
        }

        private DiagnosticResult BuildHostMetadataCheck(InstrumentAttestationObservation observation, string correlation, DateTime now)
        {
            if (!observation.HostAvailable || observation.Metadata == null)
            {
                return DiagnosticResult
                    .Fail(ModuleId, "M4.HOST_METADATA", ErrorClass.HostUnavailable, correlation, now)
                    .WithFindingId(AttestationReasonCodes.HostUnavailable)
                    .WithReasonCode(AttestationReasonCodes.HostUnavailable)
                    .WithObservation("Host instrument metadata was not available.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Read of Instrument.FullName / Expiry / Exchange / MasterInstrument.* after DataLoaded.")
                    .WithLimitation("MasterInstrument.* must only be read after DataLoaded/Realtime; before that it is unavailable by design.")
                    .WithNextAction("Ensure the instrument is subscribed and the AddOn reached DataLoaded before running the pass.")
                    .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, AttestationReasonCodes.HostUnavailable, now));
            }

            return DiagnosticResult
                .Pass(ModuleId, "M4.HOST_METADATA", correlation, now)
                .WithFindingId("M4.HOST_METADATA")
                .WithReasonCode("HOST_METADATA_AVAILABLE")
                .WithObservation("fullName=" + observation.Metadata.FullName + " masterMetadataAvailable=" + observation.Metadata.MasterMetadataAvailable)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Read of Instrument.FullName / Expiry / Exchange / MasterInstrument.* after DataLoaded.")
                .WithLimitation("A value that could not be read is recorded as a read failure, never substituted.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "fullName=" + observation.Metadata.FullName, now));
        }

        private DiagnosticResult BuildFieldMetadataCheck(InstrumentAttestationObservation observation, string correlation, DateTime now)
        {
            var metadata = observation.Metadata;
            if (metadata == null)
            {
                return FindingFactory.Unobservable(ModuleId, "M4.FIELD_METADATA", correlation, now,
                    "No host metadata was available to read fields from.",
                    "Ensure the instrument is subscribed and the AddOn reached DataLoaded.");
            }

            var failures = metadata.ReadFailures ?? new List<string>();
            var evidence = new List<DiagnosticEvidence>
            {
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "expiry=" + metadata.ExpiryRaw + " kind=" + metadata.ExpiryKind, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "exchange=" + metadata.ExchangeRaw, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "root=" + metadata.RootName, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "instrumentType=" + metadata.InstrumentTypeRaw, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "currency=" + metadata.CurrencyRaw, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "pointValue=" + Value(metadata.PointValue), now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "tickSize=" + Value(metadata.TickSize), now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "tickValue(derived)=" + Value(metadata.TickValue), now)
            };

            if (failures.Count > 0)
            {
                return DiagnosticResult
                    .Fail(ModuleId, "M4.FIELD_METADATA", ErrorClass.HostUnavailable, correlation, now)
                    .WithFindingId(AttestationReasonCodes.HostReadFailed)
                    .WithReasonCode(AttestationReasonCodes.HostReadFailed, "FAILED_FIELDS=" + failures.Count)
                    .WithObservation("failedFields=" + string.Join(",", failures))
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Each host metadata property read is guarded; a throwing read is recorded, not substituted.")
                    .WithLimitation("A failed field read means the field is unverified; it can never be attested.")
                    .WithNextAction("Re-run after DataLoaded, or investigate why the specific property read failed.")
                    .WithEvidence(evidence.ToArray());
            }

            return DiagnosticResult
                .Pass(ModuleId, "M4.FIELD_METADATA", correlation, now)
                .WithFindingId("M4.FIELD_METADATA")
                .WithReasonCode("ALL_FIELDS_READ")
                .WithObservation("All instrument metadata fields were read without error.")
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Guarded property reads of the public instrument metadata surface.")
                .WithLimitation("A read value is only what the host reports; it is not independently verified against an exchange.")
                .WithNextAction("None.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildRouteCandidateCheck(InstrumentAttestationObservation observation, string correlation, DateTime now)
        {
            var satisfied = observation.D096RouteCandidateSatisfied;
            return (satisfied
                    ? DiagnosticResult.Pass(ModuleId, "M4.D096_ROUTE_CANDIDATE", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M4.D096_ROUTE_CANDIDATE", correlation, now))
                .WithFindingId("M4.D096_ROUTE_CANDIDATE")
                .WithReasonCode(satisfied ? "D096_ROUTE_MAPPING_SATISFIED" : "D096_GLOBEX_CME_ROUTE_MAPPING_NOT_SATISFIED")
                .WithObservation("configuredRouteCandidate=" + satisfied)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Compared Instrument.Exchange, MasterInstrument.Exchanges and the Rithmic-only provider condition (D-096).")
                .WithLimitation("The D-096 configured route is only a candidate identity check; it is NOT independent exchange-issued proof and it never substitutes for D-078 approval.")
                .WithNextAction(satisfied
                    ? "None; D-078 approval is still required separately."
                    : "Confirm the instrument route is Globex with the Rithmic provider if the ES evidence identity is intended.");
        }

        private DiagnosticResult BuildRegistryCheck(InstrumentAttestationObservation observation, string correlation, DateTime now)
        {
            var matched = observation.RegistryMatched;
            return (matched
                    ? DiagnosticResult.Pass(ModuleId, "M4.REGISTRY_MATCH", correlation, now)
                    : DiagnosticResult.Fail(ModuleId, "M4.REGISTRY_MATCH", ErrorClass.MissingInstrumentSpecification, correlation, now))
                .WithFindingId(AttestationReasonCodes.MissingInstrumentSpecification)
                .WithReasonCode(matched ? "REGISTRY_ENTRY_MATCHED" : AttestationReasonCodes.MissingInstrumentSpecification)
                .WithObservation("canonicalId=" + observation.CanonicalInstrumentId + " registryMatched=" + matched)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Derived the canonical instrument id from the host symbol and looked it up in the approved registry.")
                .WithLimitation("A contract absent from the registry has no approved specification; the tool never invents one.")
                .WithNextAction(matched
                    ? "None."
                    : "Confirm the intended contract, or add it to the reviewed registry through the normal Owner-controlled process.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "canonicalId=" + observation.CanonicalInstrumentId, now));
        }

        private DiagnosticResult BuildD078Check(InstrumentAttestationObservation observation, string correlation, DateTime now)
        {
            if (observation.D078Approved)
            {
                // The tool can only *observe* an approval; it never creates one. If an
                // approval is observed, it is still reported as observed evidence only.
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M4.D078_APPROVAL", correlation, now)
                    .WithFindingId(AttestationReasonCodes.D078FieldApprovalNotApproved)
                    .WithReasonCode("D078_APPROVAL_OBSERVED")
                    .WithObservation("A D-078 field approval was observed for epoch " + observation.AttestationEpoch + ".")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Read the operator field-approval evidence bound to the canonical contract and epoch.")
                    .WithLimitation("The tool only observes approval evidence; it never creates or infers one.")
                    .WithNextAction("None.");
            }

            return DiagnosticResult
                .Fail(ModuleId, "M4.D078_APPROVAL", ErrorClass.ApprovalRequired, correlation, now)
                .WithFindingId(AttestationReasonCodes.D078FieldApprovalNotApproved)
                .WithReasonCode(AttestationReasonCodes.D078FieldApprovalNotApproved)
                .WithObservation("No D-078 operator field approval is recorded for epoch " + observation.AttestationEpoch + ".")
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Read the operator field-approval evidence; the production registry ships with zero approvals.")
                .WithLimitation("The tool never auto-approves. Until a reviewed Owner-controlled PASS is recorded, the gate fails closed.")
                .WithNextAction("Record a real operator field-approval PASS through the reviewed Owner-controlled process, then re-run.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                    "d078State=NotApproved epoch=" + observation.AttestationEpoch, now));
        }

        private DiagnosticResult BuildAttestationOutcomeCheck(InstrumentAttestationObservation observation, string correlation, DateTime now)
        {
            var failures = observation.FailureReasons ?? new List<string>();
            var evidence = new List<DiagnosticEvidence>();
            foreach (var reason in failures)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "failureReason=" + reason, now));
            }

            if (failures.Count == 0)
            {
                return DiagnosticResult
                    .Pass(ModuleId, "M4.ATTESTATION_OUTCOME", correlation, now)
                    .WithFindingId("M4.ATTESTATION_OUTCOME")
                    .WithReasonCode("ATTESTED")
                    .WithObservation("Attestation succeeded; all predicates held.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Evaluated every D-096/D-078 predicate; success requires all of them.")
                    .WithLimitation("A tool-side PASS still requires the real host to have supplied the metadata and the approval to have been recorded out of band.")
                    .WithNextAction("None.")
                    .WithEvidence(evidence.ToArray());
            }

            return DiagnosticResult
                .Fail(ModuleId, "M4.ATTESTATION_OUTCOME", ErrorClass.ApprovalRequired, correlation, now)
                .WithFindingId("M4.ATTESTATION_OUTCOME")
                .WithReasonCode(JoinReasonCodes(failures))
                .WithObservation("Attestation failed. causes=" + string.Join(",", failures))
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Evaluated every D-096/D-078 predicate; failure causes are reported separately and de-duplicated.")
                .WithLimitation("This is the designed fail-closed outcome; the tool reports the cause, it never resolves it.")
                .WithNextAction("Address each reported cause separately; never auto-approve.")
                .WithEvidence(evidence.ToArray());
        }

        private static string[] JoinReasonCodes(IList<string> failures)
        {
            var codes = new string[failures.Count];
            for (var i = 0; i < failures.Count; i++)
            {
                codes[i] = failures[i];
            }

            return codes;
        }

        private static string Value(double? value)
        {
            return value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unavailable";
        }
    }
}
