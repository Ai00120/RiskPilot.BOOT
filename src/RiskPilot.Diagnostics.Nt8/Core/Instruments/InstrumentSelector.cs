using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Instruments
{
    /// <summary>
    /// Outcome of resolving the explicitly selected target instrument against the host's loaded
    /// surface. Each value is a stable wire string so it can appear in evidence verbatim.
    /// </summary>
    public static class InstrumentSelectionStatus
    {
        /// <summary>Exactly one loaded instrument matched the explicit selection.</summary>
        public const string Resolved = "RESOLVED";

        /// <summary>No target instrument was configured/selected.</summary>
        public const string NotConfigured = "NOT_CONFIGURED";

        /// <summary>The loaded instrument discovery surface could not be enumerated.</summary>
        public const string SurfaceUnavailable = "SURFACE_UNAVAILABLE";

        /// <summary>The selected instrument is not present on the loaded surface.</summary>
        public const string NotLoaded = "NOT_LOADED";

        /// <summary>The selection matched more than one loaded instrument (insufficiently specific).</summary>
        public const string MultipleCandidates = "MULTIPLE_CANDIDATES";

        /// <summary>The observation originated from a stub/test double, so it can never establish identity.</summary>
        public const string StubOrMock = "STUB_OR_MOCK";
    }

    /// <summary>Stable reason codes emitted alongside an <see cref="InstrumentSelectionResult"/>.</summary>
    public static class InstrumentSelectionReasonCodes
    {
        public const string Resolved = "TARGET_INSTRUMENT_RESOLVED";
        public const string NotConfigured = "TARGET_INSTRUMENT_NOT_CONFIGURED";
        public const string SurfaceUnavailable = "INSTRUMENT_SURFACE_UNAVAILABLE";
        public const string NotLoaded = "TARGET_INSTRUMENT_NOT_LOADED";
        public const string MultipleCandidates = "TARGET_INSTRUMENT_MULTIPLE_CANDIDATES";
        public const string StubOrMock = "TARGET_INSTRUMENT_VALUE_FROM_STUB_OR_MOCK";
    }

    /// <summary>
    /// Minimal, host-free projection of one loaded instrument: only the two identity strings the
    /// selection rule needs, each with an explicit observability flag so an unreadable value is
    /// never treated as a match.
    /// </summary>
    public struct LoadedInstrumentCandidate
    {
        /// <summary>The instrument's host full name (for example "ES 12-26" or "ES DEC26"), or empty when unreadable.</summary>
        public string FullName;

        /// <summary>The instrument's root symbol (for example "ES"), or empty when unreadable.</summary>
        public string RootName;

        /// <summary>True when the root symbol was read (the getter did not throw and returned a value).</summary>
        public bool RootObservable;
    }

    /// <summary>
    /// Immutable result of the deterministic instrument-selection rule. It carries the chosen
    /// candidate index (or -1), the counts a reader needs and the stable status/reason codes. It
    /// never carries a raw symbol.
    /// </summary>
    public sealed class InstrumentSelectionResult
    {
        private InstrumentSelectionResult(string status, string reasonCode, int selectedIndex, int observedCount, int candidateCount)
        {
            Status = status;
            ReasonCode = reasonCode;
            SelectedIndex = selectedIndex;
            ObservedCount = observedCount;
            CandidateCount = candidateCount;
        }

        /// <summary>One of <see cref="InstrumentSelectionStatus"/>.</summary>
        public string Status { get; private set; }

        /// <summary>One of <see cref="InstrumentSelectionReasonCodes"/>.</summary>
        public string ReasonCode { get; private set; }

        /// <summary>Index of the resolved candidate in the loaded list, or -1 when unresolved.</summary>
        public int SelectedIndex { get; private set; }

        /// <summary>Number of instruments observed on the loaded surface.</summary>
        public int ObservedCount { get; private set; }

        /// <summary>Number of loaded instruments that matched the explicit selection.</summary>
        public int CandidateCount { get; private set; }

        /// <summary>True only when exactly one loaded instrument matched the explicit selection.</summary>
        public bool IsResolved { get { return string.Equals(Status, InstrumentSelectionStatus.Resolved, StringComparison.Ordinal); } }

        internal static InstrumentSelectionResult Create(string status, string reasonCode, int selectedIndex, int observedCount, int candidateCount)
        {
            return new InstrumentSelectionResult(status, reasonCode, selectedIndex, observedCount, candidateCount);
        }
    }

    /// <summary>
    /// The single, deterministic instrument-selection rule shared by every host component
    /// (identity observation, attestation, ES economics and market-data capture). It replaces the
    /// previous discovery heuristic that picked an instrument solely because its root matched the
    /// permanent ES root: with a real host that has many ES contracts loaded such a heuristic can
    /// never establish a unique target, so the tool must be told which instrument to target.
    /// <para>
    /// The rule is explicit-only and fail-closed:
    /// </para>
    /// <list type="bullet">
    /// <item>no selection ⇒ <see cref="InstrumentSelectionStatus.NotConfigured"/> (never a
    /// first/arbitrary instrument);</item>
    /// <item>a selection that matches no loaded instrument ⇒
    /// <see cref="InstrumentSelectionStatus.NotLoaded"/>;</item>
    /// <item>a selection that matches more than one loaded instrument (an insufficiently specific
    /// identity such as a bare root) ⇒ <see cref="InstrumentSelectionStatus.MultipleCandidates"/>;</item>
    /// <item>only an exact, unique match ⇒ <see cref="InstrumentSelectionStatus.Resolved"/>.</item>
    /// </list>
    /// A loaded instrument matches when its full name equals the selection exactly, when its full
    /// name denotes the same futures contract under the other accepted month notation (numeric
    /// <c>ES 12-26</c> vs host <c>ES DEC26</c>), or — only when its root symbol was actually read —
    /// when its root equals the selection exactly. All comparisons are ordinal and case-sensitive;
    /// contract equivalence is exact on root, month and year, never a substring match.
    /// </summary>
    public static class InstrumentSelector
    {
        public static InstrumentSelectionResult Select(
            string requestedInstrumentId,
            bool surfaceObservable,
            IReadOnlyList<LoadedInstrumentCandidate> loaded,
            bool isStubOrMock)
        {
            var observed = loaded != null ? loaded.Count : 0;

            if (isStubOrMock)
            {
                return InstrumentSelectionResult.Create(
                    InstrumentSelectionStatus.StubOrMock, InstrumentSelectionReasonCodes.StubOrMock, -1, observed, 0);
            }

            if (!surfaceObservable)
            {
                return InstrumentSelectionResult.Create(
                    InstrumentSelectionStatus.SurfaceUnavailable, InstrumentSelectionReasonCodes.SurfaceUnavailable, -1, observed, 0);
            }

            if (string.IsNullOrEmpty(requestedInstrumentId))
            {
                return InstrumentSelectionResult.Create(
                    InstrumentSelectionStatus.NotConfigured, InstrumentSelectionReasonCodes.NotConfigured, -1, observed, 0);
            }

            var matchCount = 0;
            var selectedIndex = -1;
            if (loaded != null)
            {
                for (var i = 0; i < loaded.Count; i++)
                {
                    var candidate = loaded[i];
                    var fullNameMatch = !string.IsNullOrEmpty(candidate.FullName)
                        && string.Equals(candidate.FullName, requestedInstrumentId, StringComparison.Ordinal);
                    var rootMatch = candidate.RootObservable
                        && !string.IsNullOrEmpty(candidate.RootName)
                        && string.Equals(candidate.RootName, requestedInstrumentId, StringComparison.Ordinal);
                    var contractMatch = !fullNameMatch && !rootMatch
                        && SameContract(candidate, requestedInstrumentId);

                    if (!fullNameMatch && !rootMatch && !contractMatch)
                    {
                        continue;
                    }

                    matchCount++;
                    selectedIndex = i;
                }
            }

            if (matchCount == 0)
            {
                return InstrumentSelectionResult.Create(
                    InstrumentSelectionStatus.NotLoaded, InstrumentSelectionReasonCodes.NotLoaded, -1, observed, 0);
            }

            if (matchCount > 1)
            {
                return InstrumentSelectionResult.Create(
                    InstrumentSelectionStatus.MultipleCandidates, InstrumentSelectionReasonCodes.MultipleCandidates, -1, observed, matchCount);
            }

            return InstrumentSelectionResult.Create(
                InstrumentSelectionStatus.Resolved, InstrumentSelectionReasonCodes.Resolved, selectedIndex, observed, 1);
        }

        /// <summary>
        /// True when the candidate's full name and the explicit selection denote the same futures
        /// contract under either accepted notation (numeric month or host month name). The
        /// comparison is exact on root, month and year; it is never a substring or prefix match.
        /// When the candidate's root was read, it must also agree with the parsed root, so a
        /// malformed candidate is not selected by its display name.
        /// </summary>
        private static bool SameContract(LoadedInstrumentCandidate candidate, string requestedInstrumentId)
        {
            if (string.IsNullOrEmpty(candidate.FullName)
                || !string.Equals(requestedInstrumentId, requestedInstrumentId.ToUpperInvariant(), StringComparison.Ordinal)
                || !string.Equals(candidate.FullName, candidate.FullName.ToUpperInvariant(), StringComparison.Ordinal))
            {
                return false;
            }

            string requestedRoot, candidateRoot;
            int requestedMonth, requestedYear, candidateMonth, candidateYear;
            if (!CanonicalInstrumentId.TryParse(requestedInstrumentId, out requestedRoot, out requestedMonth, out requestedYear)
                || !CanonicalInstrumentId.TryParse(candidate.FullName, out candidateRoot, out candidateMonth, out candidateYear))
            {
                return false;
            }

            if (!string.Equals(requestedRoot, candidateRoot, StringComparison.Ordinal)
                || requestedMonth != candidateMonth
                || requestedYear != candidateYear)
            {
                return false;
            }

            return !candidate.RootObservable
                || string.IsNullOrEmpty(candidate.RootName)
                || string.Equals(candidate.RootName, candidateRoot, StringComparison.Ordinal);
        }
    }
}
