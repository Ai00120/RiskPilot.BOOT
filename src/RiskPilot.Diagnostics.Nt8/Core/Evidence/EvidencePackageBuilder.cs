using System;
using System.Collections.Generic;
using System.Text;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>
    /// Builds the 17-artifact evidence package for one diagnostic run from raw, read-only
    /// evidence. It is a pure function of its inputs: no host type, no file system, no clock.
    /// <para>
    /// Only data that was actually observable is exported. A section whose source could not be
    /// read is written with an explicit <c>status=UNAVAILABLE</c> and a <c>reason</c>, never
    /// fabricated and never omitted silently. Every emitted string passes through
    /// <see cref="SecretRedactor"/>, and identities use <see cref="SyntheticRef"/> tokens.
    /// </para>
    /// </summary>
    public static class EvidencePackageBuilder
    {
        public const string UnavailableStatus = "UNAVAILABLE";
        public const string AvailableStatus = "AVAILABLE";

        /// <summary>Builds the package contents (file name → text) plus its manifest.</summary>
        public static EvidencePackage Build(DiagnosticRunResult result, EvidenceDataset dataset)
        {
            if (dataset == null)
            {
                dataset = new EvidenceDataset();
            }

            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var findings = result != null && result.Report != null ? result.Report.Findings : null;
            var results = result != null && result.Report != null ? result.Report.Results : null;

            files[EvidencePackageCatalog.AcceptanceMatrixCsv] = BuildAcceptanceMatrixCsv(results);
            files[EvidencePackageCatalog.EnvironmentJson] = BuildEnvironmentJson(dataset);
            files[EvidencePackageCatalog.ConnectionsJson] = BuildConnectionsJson(dataset);
            files[EvidencePackageCatalog.AccountsJson] = BuildAccountsJson(dataset);
            files[EvidencePackageCatalog.InstrumentsJson] = BuildInstrumentsJson(dataset);
            files[EvidencePackageCatalog.OrdersCsv] = BuildOrdersCsv(dataset);
            files[EvidencePackageCatalog.ExecutionsCsv] = BuildExecutionsCsv(dataset);
            files[EvidencePackageCatalog.PositionsCsv] = BuildPositionsCsv(dataset);
            files[EvidencePackageCatalog.MarketDataSummaryJson] = BuildMarketDataSummaryJson(dataset);
            files[EvidencePackageCatalog.DomSummaryJson] = BuildDomSummaryJson(dataset);
            files[EvidencePackageCatalog.OrderFlowSummaryJson] = BuildOrderFlowSummaryJson(dataset);
            files[EvidencePackageCatalog.StrategiesJson] = BuildStrategiesJson(dataset);
            files[EvidencePackageCatalog.SafetyAuditJson] = BuildSafetyAuditJson(dataset);
            files[EvidencePackageCatalog.FindingsJson] = BuildFindingsJson(findings);
            files[EvidencePackageCatalog.SummaryHtml] = BuildSummaryHtml(result, dataset);
            files[EvidencePackageCatalog.FullReportHtml] = BuildFullReportHtml(result, dataset);

            var manifest = BuildManifest(result, dataset, files);
            files[EvidencePackageCatalog.ManifestJson] = EvidenceManifestSerializer.Serialize(manifest);

            return new EvidencePackage(manifest, files);
        }

        // ---------------------------------------------------------------- manifest

        private static EvidenceManifest BuildManifest(DiagnosticRunResult result, EvidenceDataset dataset, IDictionary<string, string> files)
        {
            var manifest = new EvidenceManifest
            {
                SessionId = result != null ? result.SessionId : string.Empty,
                CorrelationId = result != null ? result.CorrelationId : string.Empty,
                ModeId = result != null ? result.ModeId : string.Empty,
                StartUtc = result != null ? result.StartedAtUtc : default(DateTime),
                EndUtc = result != null ? result.FinishedAtUtc : default(DateTime),
                NtRuntimeVersion = "UNOBSERVABLE",
                AddOnVersion = BuildIdentitySafe(),
                Provider = ProviderLabel(dataset),
                Instrument = InstrumentLabel(dataset),
                RecordCount = CountRecords(dataset),
                DroppedCount = dataset.DroppedCount,
                Truncated = !string.IsNullOrEmpty(dataset.TruncationReason),
                TruncationReason = dataset.TruncationReason ?? string.Empty,
                Provenance = string.IsNullOrEmpty(dataset.Provenance) ? EvidenceProvenance.Runtime : dataset.Provenance,
                AggregateStatus = (result != null ? result.AggregateStatus : DiagnosticStatus.NOT_APPLICABLE).ToWire()
            };

            foreach (var account in dataset.Accounts)
            {
                manifest.AccountsObserved.Add(new MaskedAccountRef
                {
                    MaskedRef = MaskedAccount(account),
                    AccountClass = account.AccountClass.ToWire(),
                    ProviderName = SecretRedactor.Redact(account.ProviderName)
                });
            }

            foreach (var name in EvidencePackageCatalog.All)
            {
                if (name == EvidencePackageCatalog.ManifestJson)
                {
                    continue;
                }

                files.TryGetValue(name, out var content);
                manifest.AddFile(name, content ?? string.Empty);
            }

            manifest.ComputeManifestHash();
            return manifest;
        }

        private static string BuildIdentitySafe()
        {
            return Core.Identity.BuildIdentity.ToolVersion;
        }

        private static long CountRecords(EvidenceDataset dataset)
        {
            return dataset.Orders.Count
                + dataset.Executions.Count
                + dataset.Positions.Count
                + dataset.MarketData.Count
                + dataset.Dom.Count
                + dataset.Strategies.Count
                + dataset.OrderLifecycleEvents.Count;
        }

        private static string ProviderLabel(EvidenceDataset dataset)
        {
            if (dataset.Provider != null && !string.IsNullOrEmpty(dataset.Provider.Name))
            {
                return SecretRedactor.Redact(dataset.Provider.Name);
            }

            if (dataset.Connections.Count > 0 && !string.IsNullOrEmpty(dataset.Connections[0].ProviderName))
            {
                return SecretRedactor.Redact(dataset.Connections[0].ProviderName);
            }

            return string.Empty;
        }

        private static string InstrumentLabel(EvidenceDataset dataset)
        {
            if (dataset.Instruments.Count > 0)
            {
                return SecretRedactor.Redact(dataset.Instruments[0].FullName);
            }

            if (dataset.Snapshot != null && dataset.Snapshot.Attestation != null
                && dataset.Snapshot.Attestation.Metadata != null)
            {
                return SecretRedactor.Redact(dataset.Snapshot.Attestation.Metadata.FullName);
            }

            return string.Empty;
        }

        private static string MaskedAccount(AccountRecord account)
        {
            // The manifest never carries a raw account identity: the ref is always
            // re-derived as a non-reversible synthetic token, even when the source ref
            // already looks synthetic.
            var raw = string.IsNullOrEmpty(account.AccountRef)
                ? account.SessionId + ":" + account.AccountClass.ToWire()
                : account.AccountRef;
            return SyntheticRef.For("account", raw);
        }

        // ---------------------------------------------------------------- CSV artifacts

        private static string BuildAcceptanceMatrixCsv(IList<Core.Result.DiagnosticResult> results)
        {
            var csv = new CsvWriter(new[] { "moduleId", "checkId", "findingId", "status", "severity", "errorClass", "source", "observation", "limitation", "nextAction" });
            if (results != null)
            {
                foreach (var r in results)
                {
                    csv.WriteRow(r.ModuleId, r.CheckId, r.FindingId, r.Status.ToWire(), r.Severity.ToWire(), r.ErrorClass.ToWire(),
                        r.Source, r.Observation, r.Limitation, r.NextAction);
                }
            }

            return csv.ToString();
        }

        private static string BuildOrdersCsv(EvidenceDataset dataset)
        {
            var csv = new CsvWriter(new[] { "orderRef", "instrumentId", "state", "quantity", "filledQuantity", "protectiveStop", "ocoLinked", "source", "ingestedAtUtc" });
            foreach (var order in dataset.Orders)
            {
                csv.WriteRow(order.OrderRef, order.InstrumentId, order.State.ToWire(),
                    CsvWriter.Invariant(order.Quantity), CsvWriter.Invariant(order.FilledQuantity),
                    order.IsProtectiveStop ? "true" : "false", order.OcoLinked ? "true" : "false",
                    "Account.Orders", FormatUtc(order.IngestedAtUtc));
            }

            return csv.ToString();
        }

        private static string BuildExecutionsCsv(EvidenceDataset dataset)
        {
            var csv = new CsvWriter(new[] { "executionRef", "orderRef", "instrumentId", "price", "quantity", "positionSide", "ingestedAtUtc" });
            foreach (var execution in dataset.Executions)
            {
                csv.WriteRow(execution.ExecutionRef, execution.OrderRef, execution.InstrumentId,
                    CsvWriter.Invariant(execution.Price), CsvWriter.Invariant(execution.Quantity),
                    execution.PositionSide.ToWire(), FormatUtc(execution.IngestedAtUtc));
            }

            return csv.ToString();
        }

        private static string BuildPositionsCsv(EvidenceDataset dataset)
        {
            var csv = new CsvWriter(new[] { "accountRef", "instrumentId", "side", "quantity", "averagePrice", "ingestedAtUtc" });
            foreach (var position in dataset.Positions)
            {
                csv.WriteRow(position.AccountRef, position.InstrumentId, position.Side.ToWire(),
                    CsvWriter.Invariant(position.Quantity), CsvWriter.Invariant(position.AveragePrice),
                    FormatUtc(position.IngestedAtUtc));
            }

            return csv.ToString();
        }

        // ---------------------------------------------------------------- JSON artifacts

        private static string BuildEnvironmentJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            var environment = dataset.Environment;
            if (environment == null)
            {
                WriteUnavailable(writer, "no environment record was readable from the host");
            }
            else
            {
                writer.WriteProperty("status", AvailableStatus);
                writer.WriteProperty("environmentClass", environment.Environment.ToWire());
                writer.WriteProperty("hostVersion", string.IsNullOrEmpty(environment.HostVersion) ? "UNOBSERVABLE" : SecretRedactor.Redact(environment.HostVersion));
                writer.WriteProperty("hostVersionObservable", environment.HostVersionObservable);
                writer.WriteProperty("toolBuildMode", SecretRedactor.Redact(environment.ToolBuildMode));
                writer.WriteProperty("targetFramework", SecretRedactor.Redact(environment.TargetFramework));
                writer.WriteProperty("utcOffsetMinutes", environment.UtcOffsetMinutes);
                writer.WriteProperty("sourceTimeBasis", environment.SourceTimeBasis.ToWire());
            }

            writer.EndObject();
            return writer.ToString();
        }

        private static string BuildConnectionsJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("count", dataset.Connections.Count);
            if (dataset.Connections.Count == 0)
            {
                WriteUnavailable(writer, "no connection was observable");
            }
            else
            {
                writer.WriteProperty("status", AvailableStatus);
            }

            writer.BeginArray("connections");
            foreach (var connection in dataset.Connections)
            {
                writer.BeginObject();
                writer.WriteProperty("name", SecretRedactor.Redact(connection.ConnectionName));
                writer.WriteProperty("priceState", connection.PriceState.ToWire());
                writer.WriteProperty("orderChannelState", connection.OrderChannelState.ToWire());
                writer.WriteProperty("providerName", SecretRedactor.Redact(connection.ProviderName));
                writer.WriteProperty("providerDetectedByString", connection.ProviderDetectedByString);
                writer.WriteProperty("role", connection.Role.ToWire());
                writer.WriteProperty("reconnectCount", connection.ReconnectCount);
                writer.EndObject();
            }

            writer.EndArray();

            if (dataset.Provider != null)
            {
                writer.BeginObject("provider");
                writer.WriteProperty("name", SecretRedactor.Redact(dataset.Provider.Name));
                writer.WriteProperty("resolutionMethod", SecretRedactor.Redact(dataset.Provider.ResolutionMethod));
                writer.WriteProperty("isExpectedEvidenceProvider", dataset.Provider.IsExpectedEvidenceProvider);
                writer.WriteProperty("providerBuildObservable", dataset.Provider.ProviderBuildObservable);
                writer.WriteProperty("unobservableReason", SecretRedactor.Redact(dataset.Provider.UnobservableReason));
                writer.EndObject();
            }
            else
            {
                writer.WriteProperty("provider", "UNOBSERVABLE");
            }

            writer.EndObject();
            return writer.ToString();
        }

        private static string BuildAccountsJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("count", dataset.Accounts.Count);
            if (dataset.Accounts.Count == 0)
            {
                WriteUnavailable(writer, "no account was observable");
            }
            else
            {
                writer.WriteProperty("status", AvailableStatus);
            }

            writer.BeginArray("accounts");
            foreach (var account in dataset.Accounts)
            {
                writer.BeginObject();
                writer.WriteProperty("accountRef", MaskedAccount(account));
                writer.WriteProperty("accountClass", account.AccountClass.ToWire());
                writer.WriteProperty("denomination", SecretRedactor.Redact(account.Denomination));
                writer.WriteProperty("providerName", SecretRedactor.Redact(account.ProviderName));
                writer.WriteProperty("positionCount", account.PositionCount);
                writer.WriteProperty("workingOrderCount", account.WorkingOrderCount);
                writer.WriteProperty("executionsInScope", account.ExecutionsInScope);
                writer.EndObject();
            }

            writer.EndArray();

            WriteIdentityVerification(writer, dataset);
            WriteConnectionAssociation(writer, dataset);

            var associations = dataset.Snapshot != null ? dataset.Snapshot.AccountAssociations : null;
            writer.BeginArray("associations");
            if (associations != null)
            {
                foreach (var association in associations)
                {
                    writer.BeginObject();
                    writer.WriteProperty("accountRef", SecretRedactor.Redact(association.AccountRef));
                    writer.WriteProperty("accountProvider", SecretRedactor.Redact(association.AccountProvider));
                    writer.WriteProperty("hasConnection", association.HasConnection);
                    writer.WriteProperty("connectionName", SecretRedactor.Redact(association.ConnectionName));
                    writer.WriteProperty("connectionProvider", SecretRedactor.Redact(association.ConnectionProvider));
                    writer.WriteProperty("providersAgree", association.ProvidersAgree);
                    writer.EndObject();
                }
            }

            writer.EndArray();
            writer.EndObject();
            return writer.ToString();
        }

        /// <summary>
        /// Writes the exact account-identity verification result. Only a status, reason codes and
        /// the synthetic account reference are written: the required account/connection names and
        /// the observed raw names are never emitted, so the artifact proves the result without
        /// disclosing an identifier.
        /// </summary>
        private static void WriteIdentityVerification(JsonWriter writer, EvidenceDataset dataset)
        {
            var observation = dataset.Snapshot != null ? dataset.Snapshot.AccountIdentity : null;
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            writer.BeginObject("identityVerification");
            writer.WriteProperty("status", verdict.Status.ToWire());
            writer.WriteStringArrayProperty("reasonCodes", SecretRedactor.RedactArray(ToArray(verdict.ReasonCodes)));
            writer.WriteStringArrayProperty("mismatchedFields", SecretRedactor.RedactArray(ToArray(verdict.MismatchedFields)));
            writer.WriteStringArrayProperty("unobservableFields", SecretRedactor.RedactArray(ToArray(verdict.UnobservableFields)));
            writer.WriteProperty("observedAccountCount", verdict.ObservedAccountCount);
            writer.WriteProperty("exactNameMatchCount", verdict.MatchingAccountCount);
            writer.WriteProperty("accountRef", string.IsNullOrEmpty(verdict.AccountRef)
                ? string.Empty
                : SyntheticRef.For("account", verdict.AccountRef));
            writer.EndObject();
        }

        /// <summary>
        /// Writes the target-account→connection→provider association result. Only a status,
        /// reason codes, field-level detail and counts are written: the required account and
        /// connection names and the observed raw names are never emitted, so the artifact proves
        /// the association without disclosing an identifier.
        /// </summary>
        private static void WriteConnectionAssociation(JsonWriter writer, EvidenceDataset dataset)
        {
            var observation = dataset.Snapshot != null ? dataset.Snapshot.TargetAccountConnection : null;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            writer.BeginObject("connectionAssociation");
            writer.WriteProperty("status", verdict.Status.ToWire());
            writer.WriteStringArrayProperty("reasonCodes", SecretRedactor.RedactArray(ToArray(verdict.ReasonCodes)));
            writer.WriteStringArrayProperty("mismatchedFields", SecretRedactor.RedactArray(ToArray(verdict.MismatchedFields)));
            writer.WriteStringArrayProperty("unobservableFields", SecretRedactor.RedactArray(ToArray(verdict.UnobservableFields)));
            writer.WriteProperty("observedAccountCount", verdict.ObservedAccountCount);
            writer.WriteProperty("exactNameMatchCount", verdict.MatchingAccountCount);
            writer.WriteProperty("connectionCount", verdict.ConnectionCount);
            writer.WriteProperty("matchingConnectionCount", verdict.MatchingConnectionCount);
            writer.WriteProperty("accountRef", string.IsNullOrEmpty(verdict.AccountRef)
                ? string.Empty
                : SyntheticRef.For("account", verdict.AccountRef));
            writer.EndObject();
        }

        private static string BuildInstrumentsJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("count", dataset.Instruments.Count);
            writer.BeginArray("instruments");
            foreach (var instrument in dataset.Instruments)
            {
                writer.BeginObject();
                writer.WriteProperty("fullName", SecretRedactor.Redact(instrument.FullName));
                writer.WriteProperty("instrumentId", SecretRedactor.Redact(instrument.InstrumentId));
                writer.WriteProperty("rootSymbol", SecretRedactor.Redact(instrument.RootSymbol));
                writer.WriteProperty("expiry", SecretRedactor.Redact(instrument.Expiry));
                writer.WriteProperty("exchange", SecretRedactor.Redact(instrument.Exchange));
                writer.WriteProperty("instrumentType", SecretRedactor.Redact(instrument.InstrumentType));
                writer.WriteProperty("currency", SecretRedactor.Redact(instrument.Currency));
                writer.WriteProperty("tickSize", instrument.TickSize);
                writer.WriteProperty("pointValue", instrument.PointValue);
                writer.WriteProperty("masterMetadataAvailable", instrument.MasterMetadataAvailable);
                writer.EndObject();
            }

            writer.EndArray();

            var attestation = dataset.Snapshot != null ? dataset.Snapshot.Attestation : null;
            if (attestation != null)
            {
                writer.BeginObject("attestation");
                writer.WriteProperty("hostAvailable", attestation.HostAvailable);
                writer.WriteProperty("requestedInstrumentId", SecretRedactor.Redact(attestation.RequestedInstrumentId));
                writer.WriteProperty("canonicalInstrumentId", SecretRedactor.Redact(attestation.CanonicalInstrumentId));
                writer.WriteProperty("d096RouteCandidateSatisfied", attestation.D096RouteCandidateSatisfied);
                writer.WriteProperty("registryMatched", attestation.RegistryMatched);
                writer.WriteProperty("d078Approved", attestation.D078Approved);
                writer.WriteProperty("attestationEpoch", attestation.AttestationEpoch);
                writer.WriteStringArrayProperty("failureReasons", SecretRedactor.RedactArray(ToArray(attestation.FailureReasons)));
                writer.EndObject();
            }
            else
            {
                writer.WriteProperty("attestation", "UNOBSERVABLE");
            }

            writer.EndObject();
            return writer.ToString();
        }

        private static string BuildMarketDataSummaryJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            long quotes = 0, trades = 0, bars = 0, resets = 0;
            foreach (var record in dataset.MarketData)
            {
                switch (record.Kind)
                {
                    case MarketDataKind.Quote:
                        quotes++;
                        break;
                    case MarketDataKind.Trade:
                        trades++;
                        break;
                    case MarketDataKind.Bar:
                        bars++;
                        break;
                }

                if (record.IsReset)
                {
                    resets++;
                }
            }

            writer.WriteProperty("totalRecords", dataset.MarketData.Count);
            writer.WriteProperty("quotes", quotes);
            writer.WriteProperty("trades", trades);
            writer.WriteProperty("bars", bars);
            writer.WriteProperty("resets", resets);

            // The trust basis of the source timestamps is derived from what was actually
            // observed, never assumed. With no records it is UNOBSERVABLE; if any record's
            // basis is Unknown the aggregate is UNKNOWN (fail-closed: never claim a stronger
            // basis than the weakest observed record).
            var basis = AggregateSourceTimeBasis(dataset.MarketData);
            writer.WriteProperty("sourceTimeBasis", basis.HasValue ? basis.Value.ToWire() : "UNOBSERVABLE");

            var dom = dataset.Snapshot != null ? dataset.Snapshot.DomMarketData : null;
            if (dom != null)
            {
                writer.WriteProperty("level1QuoteObserved", dom.Level1QuoteObserved);
                writer.WriteProperty("lastTradeObserved", dom.LastTradeObserved);
                if (dom.LastDomEventAgeSeconds.HasValue)
                {
                    writer.WriteProperty("lastDomEventAgeSeconds", dom.LastDomEventAgeSeconds.Value);
                }
                else
                {
                    writer.WriteProperty("lastDomEventAgeSeconds", "UNOBSERVABLE");
                }
            }
            else
            {
                writer.WriteProperty("level1", "UNOBSERVABLE");
            }

            writer.EndObject();
            return writer.ToString();
        }

        /// <summary>
        /// Aggregates the source-time basis across market-data records using the weakest
        /// (most conservative) basis present. Returns null when there is no record to observe.
        /// </summary>
        private static SourceTimeBasis? AggregateSourceTimeBasis(IList<MarketDataRecord> records)
        {
            if (records == null || records.Count == 0)
            {
                return null;
            }

            var worst = SourceTimeBasis.ExchangeTimeVerified;
            foreach (var record in records)
            {
                if (record.SourceTimeBasis < worst)
                {
                    worst = record.SourceTimeBasis;
                }
            }

            return worst;
        }

        private static string BuildDomSummaryJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            var dom = dataset.Snapshot != null ? dataset.Snapshot.DomMarketData : null;
            if (dom == null)
            {
                WriteUnavailable(writer, "no DOM/market-data observation was supplied");
                writer.EndObject();
                return writer.ToString();
            }

            writer.WriteProperty("status", AvailableStatus);
            WriteDomBlock(writer, "hostAdapter", dom.HostAdapter);
            WriteDomBlock(writer, "recorder", dom.Recorder);
            writer.WriteProperty("malformedCount", dom.MalformedCount);
            writer.WriteProperty("sequenceGapCount", dom.SequenceGapCount);
            writer.WriteProperty("droppedEventCount", dom.DroppedEventCount);
            writer.EndObject();
            return writer.ToString();
        }

        private static void WriteDomBlock(JsonWriter writer, string name, DomCounterBlock block)
        {
            if (block == null)
            {
                writer.WriteProperty(name, "UNAVAILABLE");
                return;
            }

            writer.BeginObject(name);
            writer.WriteProperty("layer", SecretRedactor.Redact(block.Layer));
            writer.WriteProperty("observable", block.Observable);
            writer.WriteProperty("insert", block.InsertCount);
            writer.WriteProperty("update", block.UpdateCount);
            writer.WriteProperty("remove", block.RemoveCount);
            writer.WriteProperty("reset", block.ResetCount);
            writer.WriteProperty("snapshotSync", block.SnapshotSyncCount);
            writer.WriteProperty("snapshotSyncFailure", block.SnapshotSyncFailureCount);
            writer.WriteProperty("insertOutOfBounds", block.InsertOutOfBoundsCount);
            writer.WriteProperty("updateOutOfBounds", block.UpdateOutOfBoundsCount);
            writer.WriteProperty("removeOutOfBounds", block.RemoveOutOfBoundsCount);
            writer.WriteProperty("invalidPrice", block.InvalidPriceCount);
            writer.WriteProperty("invalidSize", block.InvalidSizeCount);
            writer.WriteProperty("invalidPosition", block.InvalidPositionCount);
            writer.EndObject();
        }

        private static string BuildOrderFlowSummaryJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            var td = dataset.Snapshot != null ? dataset.Snapshot.TradeDelta : null;
            if (td == null)
            {
                WriteUnavailable(writer, "no TradeDelta/OrderFlow observation was supplied");
                writer.EndObject();
                return writer.ToString();
            }

            writer.WriteProperty("status", AvailableStatus);
            writer.WriteProperty("buyDerived", td.BuyDerivedCount);
            writer.WriteProperty("sellDerived", td.SellDerivedCount);
            writer.WriteProperty("unknown", td.UnknownCount);
            writer.WriteProperty("unknownInsideSpread", td.UnknownInsideSpreadCount);
            writer.WriteProperty("unknownNoBbo", td.UnknownNoBboCount);
            writer.WriteProperty("unknownBboIncomplete", td.UnknownBboIncompleteCount);
            writer.WriteProperty("unknownBboInvalid", td.UnknownBboInvalidCount);
            writer.WriteProperty("unknownProviderReported", td.UnknownProviderReportedCount);
            writer.WriteProperty("allDirectionsQuoteDerived", td.AllDirectionsQuoteDerived);
            writer.WriteProperty("anyProviderSuppliedDirection", td.AnyProviderSuppliedDirection);
            writer.WriteProperty("orderFlowReadiness", SecretRedactor.Redact(td.OrderFlowReadiness));
            writer.WriteProperty("orderFlowUnavailableReason", SecretRedactor.Redact(td.OrderFlowUnavailableReason));
            writer.WriteProperty("tradeDeltaCompleteness", SecretRedactor.Redact(td.TradeDeltaCompleteness));
            writer.WriteProperty("tradeDeltaUnavailableReason", SecretRedactor.Redact(td.TradeDeltaUnavailableReason));
            writer.WriteProperty("resetObserved", td.ResetObserved);
            writer.WriteProperty("resetCount", td.ResetCount);
            writer.WriteProperty("sequenceGapCount", td.SequenceGapCount);
            writer.EndObject();
            return writer.ToString();
        }

        private static string BuildStrategiesJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("recordCount", dataset.Strategies.Count);
            writer.BeginArray("records");
            foreach (var strategy in dataset.Strategies)
            {
                writer.BeginObject();
                writer.WriteProperty("strategyId", SecretRedactor.Redact(strategy.StrategyId));
                writer.WriteProperty("activity", strategy.Activity.ToWire());
                writer.WriteStringArrayProperty("reasonCodes", SecretRedactor.RedactArray(strategy.ReasonCodes));
                writer.EndObject();
            }

            writer.EndArray();

            var sr = dataset.Snapshot != null ? dataset.Snapshot.StrategyRiskGate : null;
            if (sr != null)
            {
                writer.BeginObject("riskGate");
                writer.WriteProperty("strategySurfaceObservable", sr.StrategySurfaceObservable);
                writer.WriteProperty("allCandidatesInvalidCount", sr.AllCandidatesInvalidCount);
                writer.WriteProperty("stage3ZeroQuantityCount", sr.Stage3ZeroQuantityCount);
                writer.WriteProperty("riskGateVetoCount", sr.RiskGateVetoCount);
                writer.BeginArray("strategies");
                foreach (var strategy in sr.Strategies)
                {
                    writer.BeginObject();
                    writer.WriteProperty("strategyId", SecretRedactor.Redact(strategy.StrategyId));
                    writer.WriteProperty("available", strategy.Available);
                    writer.WriteProperty("missingCapability", strategy.MissingCapability);
                    writer.WriteProperty("candidateCount", strategy.CandidateCount);
                    writer.WriteProperty("validCandidateCount", strategy.ValidCandidateCount);
                    writer.WriteProperty("invalidCandidateCount", strategy.InvalidCandidateCount);
                    writer.WriteProperty("unavailableReason", SecretRedactor.Redact(strategy.UnavailableReason));
                    writer.EndObject();
                }

                writer.EndArray();
                writer.EndObject();
            }
            else
            {
                writer.WriteProperty("riskGate", "UNOBSERVABLE");
            }

            writer.EndObject();
            return writer.ToString();
        }

        private static string BuildSafetyAuditJson(EvidenceDataset dataset)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            var safety = dataset.Snapshot != null ? dataset.Snapshot.Safety : null;
            if (safety == null)
            {
                WriteUnavailable(writer, "no safety observation was supplied");
                writer.EndObject();
                return writer.ToString();
            }

            writer.WriteProperty("status", AvailableStatus);
            writer.WriteProperty("tradingDisabledRuntimeEvidence", safety.TradingDisabledRuntimeEvidence);
            writer.WriteProperty("readOnlyPostureObserved", safety.ReadOnlyPostureObserved);
            writer.WriteProperty("staticScanPassed", safety.StaticScanPassed);
            writer.WriteProperty("staticScanForbiddenApiCount", safety.StaticScanForbiddenApiCount);
            writer.WriteProperty("staticScanFileCount", safety.StaticScanFileCount);
            writer.WriteProperty("reportSecretFree", safety.ReportSecretFree);
            writer.WriteStringArrayProperty("unobservableItems", SecretRedactor.RedactArray(ToArray(safety.UnobservableItems)));
            writer.BeginArray("evidence");
            foreach (var item in safety.Evidence)
            {
                writer.BeginObject();
                writer.WriteProperty("name", SecretRedactor.Redact(item.Name));
                writer.WriteProperty("observed", item.Observed);
                writer.WriteProperty("detail", SecretRedactor.Redact(item.Detail));
                writer.WriteProperty("source", SecretRedactor.Redact(item.Source));
                writer.EndObject();
            }

            writer.EndArray();
            writer.WriteProperty("tradingDisabledInvariant", true);
            writer.EndObject();
            return writer.ToString();
        }

        private static string BuildFindingsJson(IList<DiagnosticFinding> findings)
        {
            var writer = new JsonWriter();
            writer.BeginObject();
            writer.WriteProperty("count", findings != null ? findings.Count : 0);
            writer.BeginArray("findings");
            if (findings != null)
            {
                foreach (var finding in findings)
                {
                    writer.BeginObject();
                    writer.WriteProperty("findingId", SecretRedactor.Redact(finding.FindingId));
                    writer.WriteProperty("checkId", SecretRedactor.Redact(finding.CheckId));
                    writer.WriteProperty("moduleId", SecretRedactor.Redact(finding.ModuleId));
                    writer.WriteProperty("severity", finding.Severity.ToWire());
                    writer.WriteProperty("status", finding.Status.ToWire());
                    writer.WriteProperty("observation", SecretRedactor.Redact(finding.Observation));
                    writer.WriteProperty("timestampUtc", finding.ObservedAtUtc);
                    writer.WriteProperty("source", SecretRedactor.Redact(finding.Source));
                    writer.WriteProperty("collectionMethod", SecretRedactor.Redact(finding.Method));
                    writer.WriteProperty("evidenceReference", "results[]/" + SecretRedactor.Redact(finding.CheckId));
                    writer.WriteProperty("limitation", SecretRedactor.Redact(finding.Limitation));
                    writer.WriteProperty("recommendedNextAction", SecretRedactor.Redact(finding.NextAction));
                    writer.WriteStringArrayProperty("reasonCodes", SecretRedactor.RedactArray(finding.ReasonCodes));
                    writer.EndObject();
                }
            }

            writer.EndArray();
            writer.EndObject();
            return writer.ToString();
        }

        // ---------------------------------------------------------------- HTML artifacts

        private static string BuildSummaryHtml(DiagnosticRunResult result, EvidenceDataset dataset)
        {
            var html = new HtmlWriter();
            html.WriteRaw("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            html.WriteRaw("<title>RiskPilot NT8 Diagnostic Center — Summary</title>");
            html.WriteRaw("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1a1a1a}" +
                          "table{border-collapse:collapse;margin:8px 0 20px}th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;font-size:13px}" +
                          "th{background:#f0f0f0}.PASS{color:#0a7a0a}.FAIL{color:#b00020}.INCONCLUSIVE{color:#a06000}.NOT_APPLICABLE{color:#555}" +
                          "h1{font-size:20px}h2{font-size:16px;border-bottom:1px solid #ddd;padding-bottom:4px}.note{background:#fff8e1;padding:8px;border:1px solid #e0c060}</style>");
            html.WriteRaw("</head><body>\n");

            html.Element("h1", "RiskPilot NT8 Diagnostic Center — Summary");
            html.Element("div", "Trading: DISABLED — read-only diagnostics. No order, execution, account or connection mutation.", "note");

            var provenance = string.IsNullOrEmpty(dataset.Provenance) ? EvidenceProvenance.Runtime : dataset.Provenance;
            if (provenance == EvidenceProvenance.Sample)
            {
                html.Element("div",
                    "SAMPLE DATA — this package was produced from synthetic documentation data, not a live "
                    + "NinjaTrader run. It must not be read as a runtime diagnostic result.",
                    "note");
            }

            html.Open("table");
            WriteKeyValue(html, "Evidence provenance", provenance);
            WriteKeyValue(html, "Session ID", result != null ? result.SessionId : string.Empty);
            WriteKeyValue(html, "Mode", result != null ? result.ModeId : string.Empty);
            WriteKeyValue(html, "Aggregate status", result != null ? result.AggregateStatus.ToWire() : "NOT_APPLICABLE");
            WriteKeyValue(html, "Started (UTC)", FormatUtc(result != null ? result.StartedAtUtc : default(DateTime)));
            WriteKeyValue(html, "Finished (UTC)", FormatUtc(result != null ? result.FinishedAtUtc : default(DateTime)));
            WriteKeyValue(html, "Duration (s)", result != null ? CsvWriter.Invariant(result.DurationSeconds) : "0");
            WriteKeyValue(html, "Provider", ProviderLabel(dataset));
            WriteKeyValue(html, "Instrument", InstrumentLabel(dataset));
            WriteKeyValue(html, "NT runtime version", "UNOBSERVABLE (no public API)");
            html.Close("table");

            html.Element("h2", "Module status");
            var moduleRows = new List<IReadOnlyList<string>>();
            if (result != null)
            {
                foreach (var module in result.Modules)
                {
                    moduleRows.Add(new[]
                    {
                        module.ModuleId,
                        module.Title,
                        StatusCell(module.Status),
                        module.Executed ? "yes" : "no",
                        CsvWriter.Invariant(module.CheckCount)
                    });
                }
            }

            html.Table(new[] { "Module", "Title", "Status", "Executed", "Checks" }, moduleRows);

            html.Element("h2", "Acceptance (three independent subjects)");
            var acceptance = AcceptanceSummaryBuilder.Build(result, Core.Identity.BuildIdentity.IsStubBuild);
            var acceptanceRows = new List<IReadOnlyList<string>>();
            foreach (var subject in acceptance)
            {
                acceptanceRows.Add(new[] { subject.Title, StatusCell(subject.Status), subject.Summary });
            }

            html.Table(new[] { "Subject", "Status", "Summary" }, acceptanceRows);

            html.Element("h2", "Top findings");
            var findingRows = new List<IReadOnlyList<string>>();
            if (result != null && result.Report != null)
            {
                var shown = 0;
                foreach (var finding in result.Report.Findings)
                {
                    if (finding.Status == DiagnosticStatus.PASS)
                    {
                        continue;
                    }

                    findingRows.Add(new[]
                    {
                        finding.FindingId,
                        finding.ModuleId,
                        finding.Severity.ToWire(),
                        StatusCell(finding.Status),
                        finding.Observation
                    });
                    shown++;
                    if (shown >= 25)
                    {
                        break;
                    }
                }
            }

            html.Table(new[] { "Finding", "Module", "Severity", "Status", "Observation" }, findingRows);

            html.Element("p", "Evidence only. Missing or unobservable data is reported as INCONCLUSIVE, never PASS. " +
                              "See full-report.html for every check and finding.");
            html.WriteRaw("</body></html>\n");
            return html.ToString();
        }

        private static string BuildFullReportHtml(DiagnosticRunResult result, EvidenceDataset dataset)
        {
            var html = new HtmlWriter();
            html.WriteRaw("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"/>");
            html.WriteRaw("<title>RiskPilot NT8 Diagnostic Center — Full Report</title>");
            html.WriteRaw("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1a1a1a}" +
                          "table{border-collapse:collapse;margin:8px 0 20px}th,td{border:1px solid #ccc;padding:4px 8px;text-align:left;font-size:13px;vertical-align:top}" +
                          "th{background:#f0f0f0}.PASS{color:#0a7a0a}.FAIL{color:#b00020}.INCONCLUSIVE{color:#a06000}.NOT_APPLICABLE{color:#555}" +
                          "h1{font-size:20px}h2{font-size:16px;border-bottom:1px solid #ddd;padding-bottom:4px}.note{background:#fff8e1;padding:8px;border:1px solid #e0c060}</style>");
            html.WriteRaw("</head><body>\n");

            html.Element("h1", "RiskPilot NT8 Diagnostic Center — Full Report");
            html.Element("div", "Trading: DISABLED — read-only. Historical baseline figures are NOT current runtime data.", "note");

            html.Element("h2", "Run");
            html.Open("table");
            WriteKeyValue(html, "Session ID", result != null ? result.SessionId : string.Empty);
            WriteKeyValue(html, "Correlation ID", result != null ? result.CorrelationId : string.Empty);
            WriteKeyValue(html, "Mode", result != null ? result.ModeId : string.Empty);
            WriteKeyValue(html, "Final state", result != null ? result.FinalState.ToString() : "Idle");
            WriteKeyValue(html, "Aggregate status", result != null ? result.AggregateStatus.ToWire() : "NOT_APPLICABLE");
            html.Close("table");

            html.Element("h2", "Checks");
            var resultRows = new List<IReadOnlyList<string>>();
            if (result != null && result.Report != null)
            {
                foreach (var r in result.Report.Results)
                {
                    resultRows.Add(new[]
                    {
                        r.ModuleId,
                        r.CheckId,
                        StatusCell(r.Status),
                        r.Severity.ToWire(),
                        r.Observation,
                        r.Source,
                        r.Method,
                        r.Limitation,
                        r.NextAction
                    });
                }
            }

            html.Table(new[] { "Module", "Check", "Status", "Severity", "Observation", "Source", "Method", "Limitation", "Next action" }, resultRows);

            html.Element("h2", "Findings");
            var findingRows = new List<IReadOnlyList<string>>();
            if (result != null && result.Report != null)
            {
                foreach (var finding in result.Report.Findings)
                {
                    findingRows.Add(new[]
                    {
                        finding.FindingId,
                        finding.ModuleId,
                        StatusCell(finding.Status),
                        finding.Severity.ToWire(),
                        finding.Observation,
                        finding.Source,
                        finding.Method,
                        finding.Limitation,
                        finding.NextAction
                    });
                }
            }

            html.Table(new[] { "Finding", "Module", "Status", "Severity", "Observation", "Source", "Collection method", "Limitation", "Recommended next action" }, findingRows);

            html.Element("h2", "Counters");
            html.Open("table");
            if (result != null && result.Report != null)
            {
                WriteKeyValue(html, "Enqueued", CsvWriter.Invariant(result.Report.Counters.Enqueued));
                WriteKeyValue(html, "Processed", CsvWriter.Invariant(result.Report.Counters.Processed));
                WriteKeyValue(html, "Dropped", CsvWriter.Invariant(result.Report.Counters.Dropped));
                WriteKeyValue(html, "Duplicates", CsvWriter.Invariant(result.Report.Counters.Duplicates));
                WriteKeyValue(html, "Sequence gaps", CsvWriter.Invariant(result.Report.Counters.SequenceGaps));
                WriteKeyValue(html, "Timestamp invalid", CsvWriter.Invariant(result.Report.Counters.TimestampInvalid));
            }

            html.Close("table");

            html.Element("h2", "Unobservable");
            var unobservableRows = new List<IReadOnlyList<string>>();
            if (result != null && result.Report != null)
            {
                foreach (var item in result.Report.Unobservable)
                {
                    unobservableRows.Add(new[] { item });
                }
            }

            html.Table(new[] { "Item" }, unobservableRows);

            html.WriteRaw("</body></html>\n");
            return html.ToString();
        }

        private static void WriteKeyValue(HtmlWriter html, string key, string value)
        {
            html.Open("tr");
            html.Element("th", key);
            html.Element("td", value);
            html.Close("tr");
        }

        private static string StatusCell(DiagnosticStatus status)
        {
            var wire = status.ToWire();
            return wire;
        }

        // ---------------------------------------------------------------- helpers

        private static void WriteUnavailable(JsonWriter writer, string reason)
        {
            writer.WriteProperty("status", UnavailableStatus);
            writer.WriteProperty("reason", SecretRedactor.Redact(reason));
        }

        private static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string[] ToArray(IList<string> values)
        {
            if (values == null)
            {
                return new string[0];
            }

            var array = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }
    }
}
