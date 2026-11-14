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

            // The canonical instrument identity check is independent of the D-078/D-096 attestation
            // observation: it is emitted even when the attestation surface is unavailable, so M4
            // always reports whether the selected instrument matches the permanent ES/CME identity.
            results.Add(BuildInstrumentIdentityCheck(context, correlation, now));

            // The canonical ES economic specification (D-095 §4) is a separate, independent
            // read-only check: it proves the loaded instrument's own economics (point value, tick
            // size, derived tick value, currency) match the CME ES contract, not merely which
            // instrument it is. It is likewise emitted even when the attestation surface is absent.
            results.Add(BuildEsProductSpecificationCheck(context, correlation, now));

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

        /// <summary>
        /// Proves the instrument NinjaTrader actually has selected/loaded is the permanent ES/CME
        /// identity. The host reduces the loaded instrument surface to booleans/counts plus a
        /// synthetic instrument reference; the verifier
        /// (<see cref="Core.Identity.InstrumentIdentityVerifier"/>) decides PASS/FAIL/INCONCLUSIVE.
        /// The exchange is never inferred from the symbol text: the configured-route metadata must
        /// confirm CME, and an unconfirmed exchange keeps the result INCONCLUSIVE, never PASS.
        /// </summary>
        private DiagnosticResult BuildInstrumentIdentityCheck(ProbeContext context, string correlation, DateTime now)
        {
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.InstrumentIdentity : null;
            var verdict = Core.Identity.InstrumentIdentityVerifier.Evaluate(observation);

            var result = verdict.Status == DiagnosticStatus.PASS
                ? DiagnosticResult.Pass(ModuleId, "M4.INSTRUMENT_IDENTITY", correlation, now)
                : (verdict.Status == DiagnosticStatus.FAIL
                    ? DiagnosticResult.Fail(ModuleId, "M4.INSTRUMENT_IDENTITY", ErrorClass.MissingInstrumentSpecification, correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M4.INSTRUMENT_IDENTITY", correlation, now));

            var observationText = "instruments=" + verdict.ObservedInstrumentCount
                + " rootMatches=" + verdict.MatchingInstrumentCount
                + " instrumentRef=" + (string.IsNullOrEmpty(verdict.InstrumentRef) ? "none" : verdict.InstrumentRef)
                + " mismatchedFields=" + Describe(verdict.MismatchedFields)
                + " unobservableFields=" + Describe(verdict.UnobservableFields);

            var evidence = new List<DiagnosticEvidence>();
            foreach (var code in verdict.ReasonCodes)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "reason=" + code, now));
            }

            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                "instruments=" + verdict.ObservedInstrumentCount, now));
            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                "rootMatches=" + verdict.MatchingInstrumentCount, now));

            if (verdict.Status == DiagnosticStatus.INCONCLUSIVE
                && verdict.ObservedInstrumentCount == 0)
            {
                evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi,
                    "no loaded instrument identity was observable", now));
            }

            return result
                .WithFindingId("M4.INSTRUMENT_IDENTITY")
                .WithReasonCode(JoinReasonCodes(verdict.ReasonCodes))
                .WithObservation(observationText)
                .WithSource(verdict.Status == DiagnosticStatus.INCONCLUSIVE && verdict.ObservedInstrumentCount == 0
                    ? FindingFactory.SourceUnobservable
                    : FindingFactory.SourcePublicApi)
                .WithMethod("Read the loaded instrument discovery surface (Instrument.All), matched the root symbol "
                    + "exactly, and compared the selected instrument's root, instrument type, currency and the "
                    + "D-096 configured-route venue metadata against the permanent ES/CME identity.")
                .WithLimitation("The venue is a configured-route mapping, not an independent exchange-issued "
                    + "listing-venue attestation; an unconfirmed exchange or route keeps the identity unproven "
                    + "(INCONCLUSIVE). The raw symbol is never reported.")
                .WithNextAction(verdict.Status == DiagnosticStatus.PASS
                    ? "None."
                    : "Load/subscribe the permanent ES contract on the CME route, then re-run.")
                .WithEvidence(evidence.ToArray());
        }

        /// <summary>
        /// Proves the loaded ES instrument's own economics match the canonical CME E-mini S&amp;P 500
        /// Futures specification (D-095 §4): point value 50, tick size 0.25, derived tick value 12.50
        /// and USD. The host reduces the loaded instrument's <c>MasterInstrument.*</c> economics to
        /// numbers plus a synthetic reference; the verifier
        /// (<see cref="Core.Identity.EsProductSpecificationVerifier"/>) decides PASS/FAIL/INCONCLUSIVE.
        /// A missing field is INCONCLUSIVE, a contradicting field is FAIL, and only a fully observed,
        /// fully matching specification is PASS. This is source-level/runtime evidence of the
        /// instrument's economics; it is not a substitute for the D-078 host attestation.
        /// </summary>
        private DiagnosticResult BuildEsProductSpecificationCheck(ProbeContext context, string correlation, DateTime now)
        {
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.EsProductSpecification : null;
            var verdict = Core.Identity.EsProductSpecificationVerifier.Evaluate(observation);

            var result = verdict.Status == DiagnosticStatus.PASS
                ? DiagnosticResult.Pass(ModuleId, "M4.ES_PRODUCT_SPEC", correlation, now)
                : (verdict.Status == DiagnosticStatus.FAIL
                    ? DiagnosticResult.Fail(ModuleId, "M4.ES_PRODUCT_SPEC", ErrorClass.MissingInstrumentSpecification, correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M4.ES_PRODUCT_SPEC", correlation, now));

            var observationText = "instrumentObserved=" + (observation != null && observation.InstrumentObserved)
                + " instrumentRef=" + (observation == null || string.IsNullOrEmpty(observation.InstrumentRef) ? "none" : observation.InstrumentRef)
                + " pointValue=" + Value(observation != null && observation.PointValueObservable ? (double?)observation.PointValue : null)
                + " tickSize=" + Value(observation != null && observation.TickSizeObservable ? (double?)observation.TickSize : null)
                + " tickValue=" + Value(observation != null && observation.TickValueObservable ? (double?)observation.TickValue : null)
                + " currency=" + (observation != null && observation.CurrencyObservable ? observation.Currency : "UNOBSERVABLE")
                + " mismatchedFields=" + Describe(verdict.MismatchedFields)
                + " unobservableFields=" + Describe(verdict.UnobservableFields);

            var evidence = new List<DiagnosticEvidence>();
            foreach (var code in verdict.ReasonCodes)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "reason=" + code, now));
            }

            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                "pointValue=" + Value(observation != null && observation.PointValueObservable ? (double?)observation.PointValue : null), now));
            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                "tickSize=" + Value(observation != null && observation.TickSizeObservable ? (double?)observation.TickSize : null), now));
            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                "tickValue=" + Value(observation != null && observation.TickValueObservable ? (double?)observation.TickValue : null), now));

            if (verdict.Status == DiagnosticStatus.INCONCLUSIVE
                && (observation == null || !observation.InstrumentObserved))
            {
                evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi,
                    "no loaded ES instrument economics were observable", now));
            }

            return result
                .WithFindingId("M4.ES_PRODUCT_SPEC")
                .WithReasonCode(JoinReasonCodes(verdict.ReasonCodes))
                .WithObservation(observationText)
                .WithSource(verdict.Status == DiagnosticStatus.INCONCLUSIVE
                    && (observation == null || !observation.InstrumentObserved)
                    ? FindingFactory.SourceUnobservable
                    : FindingFactory.SourcePublicApi)
                .WithMethod("Read the loaded ES instrument's own MasterInstrument economics (point value, tick size, "
                    + "derived tick value, currency) and compared them against the canonical CME ES contract facts.")
                .WithLimitation("The canonical ES economics are published CME contract facts used as reference "
                    + "expectations, not an independent exchange-issued attestation; a field that could not be read "
                    + "keeps the specification unproven (INCONCLUSIVE). The raw symbol is never reported.")
                .WithNextAction(verdict.Status == DiagnosticStatus.PASS
                    ? "None."
                    : "Load/subscribe the permanent ES contract on the CME route, then re-run.")
                .WithEvidence(evidence.ToArray());
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

            // The host instrument object was resolved and its metadata surface was observable.
            // This check proves only that the object/surface was reachable; it deliberately does
            // NOT claim that every required field was read. Whether each required field was read
            // and validated is decided separately by M4.FIELD_METADATA.
            var metadata = observation.Metadata;
            var evidence = new List<DiagnosticEvidence>
            {
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "fullName=" + metadata.FullName, now)
            };
            if (metadata.HasReadFailures)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                    "fieldReadFailures=" + string.Join(",", metadata.ReadFailures), now));
            }

            return DiagnosticResult
                .Pass(ModuleId, "M4.HOST_METADATA", correlation, now)
                .WithFindingId("M4.HOST_METADATA")
                .WithReasonCode("HOST_METADATA_AVAILABLE")
                .WithObservation("fullName=" + metadata.FullName + " masterMetadataAvailable=" + metadata.MasterMetadataAvailable
                    + " fieldReadFailures=" + (metadata.HasReadFailures ? string.Join(",", metadata.ReadFailures) : "none"))
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Read of Instrument.FullName / Expiry / Exchange / MasterInstrument.* after DataLoaded.")
                .WithLimitation("This proves the host instrument object and metadata surface were reachable; it does not "
                    + "assert that every required field was read. M4.FIELD_METADATA proves the required-field reads.")
                .WithNextAction("None.")
                .WithEvidence(evidence.ToArray());
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

            // (1) A property getter that threw is a hard read failure: the field is unverified and
            //     can never be attested. This outranks an unobserved field (a definite failure is
            //     stronger evidence than an unknown one).
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

            // (2) No getter threw, but a required field was still not observed (for example the
            //     master metadata surface was not readable, or the observation came from a source
            //     that never read the field). The field set is incomplete, so it can never PASS.
            var unobserved = UnobservedRequiredFields(metadata);
            if (unobserved.Count > 0)
            {
                evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi,
                    "requiredFieldsNotRead=" + string.Join(",", unobserved), now));

                return DiagnosticResult
                    .Inconclusive(ModuleId, "M4.FIELD_METADATA", correlation, now)
                    .WithFindingId(AttestationReasonCodes.HostMetadataIncomplete)
                    .WithReasonCode(AttestationReasonCodes.HostMetadataIncomplete, "UNOBSERVED_FIELDS=" + unobserved.Count)
                    .WithObservation("requiredFieldsNotRead=" + string.Join(",", unobserved))
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Each host metadata property read is guarded and its read status is recorded independently.")
                    .WithLimitation("A required field that was never read leaves the field set incomplete, so it cannot be attested.")
                    .WithNextAction("Ensure the instrument is subscribed and the AddOn reached DataLoaded, then re-run.")
                    .WithEvidence(evidence.ToArray());
            }

            return DiagnosticResult
                .Pass(ModuleId, "M4.FIELD_METADATA", correlation, now)
                .WithFindingId("M4.FIELD_METADATA")
                .WithReasonCode("ALL_FIELDS_READ")
                .WithObservation("All required instrument metadata fields were read without error.")
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Guarded property reads of the public instrument metadata surface.")
                .WithLimitation("A read value is only what the host reports; it is not independently verified against an exchange.")
                .WithNextAction("None.")
                .WithEvidence(evidence.ToArray());
        }

        /// <summary>
        /// Names the required metadata fields whose read did not complete. A field is required
        /// when it is part of the canonical instrument metadata surface; a field whose getter did
        /// not throw is still required to have actually been read, so a field left unobserved
        /// (for example a master field read before readiness) is reported here rather than being
        /// treated as an empty valid value.
        /// </summary>
        private static List<string> UnobservedRequiredFields(InstrumentMetadataObservation metadata)
        {
            var missing = new List<string>();

            if (!metadata.FullNameObservable)
            {
                missing.Add("FullName");
            }

            if (!metadata.ExpiryObservable)
            {
                missing.Add("Expiry");
            }

            if (!metadata.ExchangeObservable)
            {
                missing.Add("Exchange");
            }

            if (!metadata.MasterMetadataAvailable)
            {
                missing.Add("MasterInstrument");
            }

            if (!metadata.RootNameObservable)
            {
                missing.Add("RootName");
            }

            if (!metadata.InstrumentTypeObservable)
            {
                missing.Add("InstrumentType");
            }

            if (!metadata.CurrencyObservable)
            {
                missing.Add("Currency");
            }

            if (!metadata.PointValue.HasValue)
            {
                missing.Add("PointValue");
            }

            if (!metadata.TickSize.HasValue)
            {
                missing.Add("TickSize");
            }

            return missing;
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

        private static string Describe(IList<string> fields)
        {
            return fields == null || fields.Count == 0 ? "none" : string.Join("|", fields);
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
