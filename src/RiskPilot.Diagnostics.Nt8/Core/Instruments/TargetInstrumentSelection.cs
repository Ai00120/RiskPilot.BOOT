using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Instruments
{
    /// <summary>
    /// Host-free, observable model of the operator's explicit target-instrument choice. It is the
    /// single source of truth the Diagnostic Center renders and the host reads: the window selects
    /// an instrument here, and the AddOn threads <see cref="SelectedInstrumentId"/> into the host
    /// provider and the market-data capture, so every component targets the same contract.
    /// <para>
    /// The model never selects an instrument on its own. <see cref="Refresh"/> reloads the
    /// available list without changing the current selection, so a refresh can never silently
    /// switch the operator's contract; a selection that is no longer available is reported as
    /// <see cref="InstrumentSelectionStatus.NotLoaded"/> rather than replaced. The resolution
    /// decision is delegated to <see cref="InstrumentSelector"/>, so there is no second selection
    /// rule.
    /// </para>
    /// All access is guarded by one lock and every collection is returned as a copy, so the UI
    /// thread never observes a partially-updated list. <see cref="Changed"/> is raised after each
    /// mutation; the window marshals it onto its dispatcher.
    /// </summary>
    public sealed class TargetInstrumentSelection
    {
        private readonly object _gate = new object();
        private string _selectedInstrumentId;
        private IReadOnlyList<string> _availableInstrumentIds = new List<string>();
        private bool _surfaceObservable;

        /// <summary>Raised after the selection or the available list changes. Handlers must not block.</summary>
        public event Action Changed;

        /// <summary>The operator's explicit selection (an exact instrument FullName), or null.</summary>
        public string SelectedInstrumentId
        {
            get { lock (_gate) { return _selectedInstrumentId; } }
        }

        /// <summary>True when an explicit selection is held (never set implicitly).</summary>
        public bool HasSelection
        {
            get { lock (_gate) { return !string.IsNullOrEmpty(_selectedInstrumentId); } }
        }

        /// <summary>Copy of the currently available instrument FullNames, in the order reported.</summary>
        public IReadOnlyList<string> AvailableInstrumentIds
        {
            get { lock (_gate) { return new List<string>(_availableInstrumentIds); } }
        }

        /// <summary>True when the loaded-instrument surface could be enumerated on the last refresh.</summary>
        public bool SurfaceObservable
        {
            get { lock (_gate) { return _surfaceObservable; } }
        }

        /// <summary>
        /// The current selection's resolution against the available list, decided by the shared
        /// <see cref="InstrumentSelector"/>. An unset selection is
        /// <see cref="InstrumentSelectionStatus.NotConfigured"/>, never an implicit first pick.
        /// </summary>
        public InstrumentSelectionResult Resolution
        {
            get
            {
                string selected;
                List<string> available;
                bool observable;
                lock (_gate)
                {
                    selected = _selectedInstrumentId;
                    available = new List<string>(_availableInstrumentIds);
                    observable = _surfaceObservable;
                }

                // The UI lists exact FullNames, so each candidate carries only its full name (the
                // root is not needed to match an exact selection and is left unobservable).
                var candidates = new List<LoadedInstrumentCandidate>(available.Count);
                foreach (var name in available)
                {
                    candidates.Add(new LoadedInstrumentCandidate { FullName = name, RootName = string.Empty, RootObservable = false });
                }

                return InstrumentSelector.Select(selected, observable, candidates, false);
            }
        }

        /// <summary>
        /// Records the operator's explicit selection. Returns true when the value changed. An
        /// empty/null value clears the selection (it never falls back to an implicit choice).
        /// </summary>
        public bool Select(string fullName)
        {
            var changed = false;
            lock (_gate)
            {
                if (!string.Equals(_selectedInstrumentId, fullName, StringComparison.Ordinal))
                {
                    _selectedInstrumentId = fullName;
                    changed = true;
                }
            }

            if (changed)
            {
                Raise();
            }

            return changed;
        }

        /// <summary>Clears the explicit selection. Returns true when a selection was cleared.</summary>
        public bool Clear()
        {
            return Select(null);
        }

        /// <summary>
        /// Reloads the available instrument list from the host surface. The current selection is
        /// never modified: a selection that is still present stays resolved, and one that has
        /// disappeared is reported NotLoaded (never replaced by another contract). Returns true
        /// when the available list changed.
        /// </summary>
        public bool Refresh(IReadOnlyList<string> availableInstrumentIds, bool surfaceObservable)
        {
            var list = availableInstrumentIds != null ? new List<string>(availableInstrumentIds) : new List<string>();
            var changed = false;

            lock (_gate)
            {
                if (_surfaceObservable != surfaceObservable || !SameList(_availableInstrumentIds, list))
                {
                    _availableInstrumentIds = list;
                    _surfaceObservable = surfaceObservable;
                    changed = true;
                }
            }

            if (changed)
            {
                Raise();
            }

            return changed;
        }

        private static bool SameList(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void Raise()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
