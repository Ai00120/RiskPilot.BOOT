using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Gui.Tools;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// The "RiskPilot NT8 Diagnostic Center" window: a read-only NTWindow that offers the
    /// three diagnostic modes (Quick / Full / Post-Trade Audit), live per-module status and
    /// progress, PASS/FAIL/INCONCLUSIVE/NOT_APPLICABLE results, a Cancel that stops only the
    /// diagnostic processing, and an Export action for the evidence package.
    /// <para>
    /// Thread safety: the window only ever reads the immutable <see cref="DiagnosticViewState"/>
    /// on the dispatcher thread. It never runs a diagnostic on the UI thread, so the Control
    /// Center can never freeze. There is no code path here that submits, changes or cancels an
    /// order or mutates any account, connection or position.
    /// </para>
    /// This file is part of the real-host build only (see the csproj).
    /// </summary>
    public sealed class DiagnosticCenterWindow : NTWindow
    {
        private readonly DiagnosticViewState _viewState;
        private readonly DiagnosticRunCoordinator _coordinator;
        private readonly IEvidenceSink _evidenceSink;
        private readonly Action _exportAction;
        private readonly IInstrumentSelectionController _instrumentSelection;

        private readonly ComboBox _modeSelector = new ComboBox();
        private readonly Button _runButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly Button _exportButton = new Button();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly TextBlock _progressText = new TextBlock();
        private readonly TextBlock _statusBar = new TextBlock();
        private readonly DataGrid _moduleGrid = new DataGrid();
        private readonly ListBox _findingsList = new ListBox();
        private readonly ListBox _acceptanceList = new ListBox();
        private readonly TextBlock _disclaimer = new TextBlock();

        // Target-instrument picker. The operator explicitly chooses the contract; the window never
        // selects one on its own (no implicit first/nearest-expiry pick).
        private readonly ComboBox _instrumentSelector = new ComboBox();
        private readonly Button _instrumentRefreshButton = new Button();
        private readonly TextBlock _instrumentStatus = new TextBlock();
        private bool _updatingInstrumentSelector;

        public DiagnosticCenterWindow(DiagnosticViewState viewState, DiagnosticRunCoordinator coordinator, IEvidenceSink evidenceSink)
            : this(viewState, coordinator, evidenceSink, null, null)
        {
        }

        public DiagnosticCenterWindow(DiagnosticViewState viewState, DiagnosticRunCoordinator coordinator, IEvidenceSink evidenceSink, Action exportAction)
            : this(viewState, coordinator, evidenceSink, exportAction, null)
        {
        }

        public DiagnosticCenterWindow(DiagnosticViewState viewState, DiagnosticRunCoordinator coordinator, IEvidenceSink evidenceSink, Action exportAction, IInstrumentSelectionController instrumentSelection)
        {
            _viewState = viewState ?? throw new ArgumentNullException(nameof(viewState));
            _coordinator = coordinator;
            _evidenceSink = evidenceSink;
            _exportAction = exportAction;
            _instrumentSelection = instrumentSelection;

            Caption = BuildIdentity.ProductName;
            Width = 900;
            Height = 640;
            MinWidth = 720;
            MinHeight = 480;

            Content = BuildLayout();

            _viewState.Changed += OnViewStateChanged;
            if (_instrumentSelection != null)
            {
                _instrumentSelection.Selection.Changed += OnInstrumentSelectionChanged;
            }

            Closed += (_, __) =>
            {
                if (_viewState != null)
                {
                    _viewState.Changed -= OnViewStateChanged;
                }

                if (_instrumentSelection != null)
                {
                    _instrumentSelection.Selection.Changed -= OnInstrumentSelectionChanged;
                }
            };

            Refresh();
            RefreshInstrumentSelection();
        }

        private UIElement BuildLayout()
        {
            var root = new DockPanel { LastChildFill = true };

            _disclaimer.Text = "Trading: DISABLED — read-only diagnostics. No order, execution, account or connection mutation.";
            _disclaimer.Margin = new Thickness(8);
            _disclaimer.TextWrapping = TextWrapping.Wrap;
            _disclaimer.Foreground = Brushes.DarkRed;
            DockPanel.SetDock(_disclaimer, Dock.Top);
            root.Children.Add(_disclaimer);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 4) };
            controls.Children.Add(new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            foreach (var definition in DiagnosticModeCatalog.All)
            {
                _modeSelector.Items.Add(definition.Title);
            }

            _modeSelector.SelectedIndex = 0;
            _modeSelector.Width = 180;
            _modeSelector.SelectionChanged += (_, __) => { _viewState.SelectedMode = SelectedMode(); };
            controls.Children.Add(_modeSelector);

            _runButton.Content = "Run";
            _runButton.Width = 80;
            _runButton.Margin = new Thickness(8, 0, 0, 0);
            _runButton.Click += (_, __) => StartSelectedMode();
            controls.Children.Add(_runButton);

            _cancelButton.Content = "Cancel";
            _cancelButton.Width = 80;
            _cancelButton.Margin = new Thickness(6, 0, 0, 0);
            _cancelButton.Click += (_, __) => _coordinator?.Cancel();
            controls.Children.Add(_cancelButton);

            _exportButton.Content = "Export";
            _exportButton.Width = 80;
            _exportButton.Margin = new Thickness(6, 0, 0, 0);
            _exportButton.Click += (_, __) => ExportEvidence();
            controls.Children.Add(_exportButton);

            DockPanel.SetDock(controls, Dock.Top);
            root.Children.Add(controls);

            root.Children.Add(BuildInstrumentPicker());

            var progressPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 6) };
            _progress.Width = 500;
            _progress.Height = 16;
            progressPanel.Children.Add(_progress);
            _progressText.Margin = new Thickness(8, 0, 0, 0);
            progressPanel.Children.Add(_progressText);
            DockPanel.SetDock(progressPanel, Dock.Top);
            root.Children.Add(progressPanel);

            _statusBar.Margin = new Thickness(8);
            DockPanel.SetDock(_statusBar, Dock.Bottom);
            root.Children.Add(_statusBar);

            var tabs = new TabControl { Margin = new Thickness(8, 0, 8, 8) };

            _moduleGrid.AutoGenerateColumns = true;
            _moduleGrid.IsReadOnly = true;
            _moduleGrid.CanUserAddRows = false;
            tabs.Items.Add(BuildTab("Modules", _moduleGrid));
            tabs.Items.Add(BuildTab("Findings", _findingsList));
            tabs.Items.Add(BuildTab("Acceptance", _acceptanceList));

            root.Children.Add(tabs);
            return root;
        }

        private static TabItem BuildTab(string header, UIElement content)
        {
            return new TabItem { Header = header, Content = content };
        }

        /// <summary>
        /// Builds the target-instrument picker row: a ComboBox of the loaded instruments' exact
        /// FullNames, a Refresh button, and a status line showing the current selection and its
        /// resolution. The window never selects an instrument on its own; the ComboBox starts with
        /// no selection and the operator must choose explicitly.
        /// </summary>
        private UIElement BuildInstrumentPicker()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 6) };
            panel.Children.Add(new TextBlock
            {
                Text = "Target instrument:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            _instrumentSelector.Width = 240;
            _instrumentSelector.IsEditable = false;
            _instrumentSelector.IsTextSearchEnabled = false;
            // The operator's explicit choice only: writing through the controller records it in the
            // shared selection model (which the host provider and market-data capture read).
            _instrumentSelector.SelectionChanged += (_, __) => OnInstrumentSelectorChanged();
            panel.Children.Add(_instrumentSelector);

            _instrumentRefreshButton.Content = "Refresh";
            _instrumentRefreshButton.Width = 80;
            _instrumentRefreshButton.Margin = new Thickness(8, 0, 0, 0);
            _instrumentRefreshButton.Click += (_, __) => RefreshAvailableInstruments();
            panel.Children.Add(_instrumentRefreshButton);

            _instrumentStatus.VerticalAlignment = VerticalAlignment.Center;
            _instrumentStatus.Margin = new Thickness(10, 0, 0, 0);
            _instrumentStatus.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(_instrumentStatus);

            DockPanel.SetDock(panel, Dock.Top);
            return panel;
        }

        /// <summary>
        /// Reloads the available-instrument list from the live host surface. The current selection is
        /// never changed: a still-present contract stays selected, and a removed one is shown as
        /// NotLoaded rather than being replaced. Runs on the UI thread (it is a button click).
        /// </summary>
        private void RefreshAvailableInstruments()
        {
            if (_instrumentSelection == null)
            {
                _instrumentStatus.Text = "Instrument selection is unavailable.";
                return;
            }

            try
            {
                _instrumentSelection.RefreshAvailable();
            }
            catch (Exception ex)
            {
                _instrumentStatus.Text = "Refresh failed: " + Core.Errors.ErrorClassifier.Describe(ex);
                return;
            }

            RefreshInstrumentSelection();
        }

        /// <summary>
        /// Records the operator's explicit ComboBox choice into the shared selection model. The
        /// guard prevents the programmatic repopulation in <see cref="RefreshInstrumentSelection"/>
        /// from being mistaken for an operator action.
        /// </summary>
        private void OnInstrumentSelectorChanged()
        {
            if (_updatingInstrumentSelector || _instrumentSelection == null)
            {
                return;
            }

            var fullName = _instrumentSelector.SelectedItem as string;
            if (string.IsNullOrEmpty(fullName))
            {
                return;
            }

            _instrumentSelection.Select(fullName);
        }

        /// <summary>
        /// Re-renders the picker from the shared model: the available list, the current selection and
        /// its resolution. The ComboBox is repopulated under a guard so the programmatic change is
        /// not treated as an operator selection, and the operator's current selection is preserved
        /// across the repopulation (never silently switched).
        /// </summary>
        private void RefreshInstrumentSelection()
        {
            if (_instrumentSelection == null)
            {
                _instrumentStatus.Text = "Instrument selection is unavailable.";
                return;
            }

            var selection = _instrumentSelection.Selection;
            var available = selection.AvailableInstrumentIds;
            var selected = selection.SelectedInstrumentId;

            _updatingInstrumentSelector = true;
            try
            {
                _instrumentSelector.Items.Clear();
                foreach (var name in available)
                {
                    _instrumentSelector.Items.Add(name);
                }

                // Preserve the operator's selection. A selection that is no longer in the list is
                // not injected as an item and is not auto-replaced; the status line reports it as
                // NotLoaded (fail closed).
                var index = -1;
                if (!string.IsNullOrEmpty(selected))
                {
                    for (var i = 0; i < _instrumentSelector.Items.Count; i++)
                    {
                        if (string.Equals(_instrumentSelector.Items[i] as string, selected, StringComparison.Ordinal))
                        {
                            index = i;
                            break;
                        }
                    }
                }

                _instrumentSelector.SelectedIndex = index;
            }
            finally
            {
                _updatingInstrumentSelector = false;
            }

            _instrumentStatus.Text = DescribeSelection(selection.Resolution, selected, available.Count, selection.SurfaceObservable);
        }

        private static string DescribeSelection(
            Core.Instruments.InstrumentSelectionResult resolution, string selected, int availableCount, bool surfaceObservable)
        {
            if (!surfaceObservable)
            {
                return "Loaded instruments are not observable; select and refresh once the host is ready.";
            }

            if (string.IsNullOrEmpty(selected))
            {
                return "No target selected — " + availableCount + " loaded instrument(s). Choose one to enable instrument checks.";
            }

            switch (resolution != null ? resolution.Status : null)
            {
                case Core.Instruments.InstrumentSelectionStatus.Resolved:
                    return "Selected: " + selected + " (resolved).";
                case Core.Instruments.InstrumentSelectionStatus.NotLoaded:
                    return "Selected: " + selected + " — NOT LOADED. It is unavailable; choose another or refresh.";
                case Core.Instruments.InstrumentSelectionStatus.MultipleCandidates:
                    return "Selected: " + selected + " — AMBIGUOUS (" + resolution.CandidateCount + " matches). Choose a more specific contract.";
                case Core.Instruments.InstrumentSelectionStatus.SurfaceUnavailable:
                    return "Loaded instruments are not observable; select and refresh once the host is ready.";
                default:
                    return "Selected: " + selected + " (" + (resolution != null ? resolution.Status : "UNKNOWN") + ").";
            }
        }

        /// <summary>
        /// Marshals a selection-model change onto the UI thread before re-rendering, so a change
        /// raised from a non-UI thread (for example the readiness retry loop) never touches WPF
        /// controls cross-thread.
        /// </summary>
        private void OnInstrumentSelectionChanged()
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                RefreshInstrumentSelection();
            }
            else
            {
                dispatcher.InvokeAsync(RefreshInstrumentSelection);
            }
        }

        private DiagnosticMode SelectedMode()
        {
            var index = _modeSelector.SelectedIndex;
            switch (index)
            {
                case 1:
                    return DiagnosticMode.Full;
                case 2:
                    return DiagnosticMode.PostTradeAudit;
                default:
                    return DiagnosticMode.Quick;
            }
        }

        private void StartSelectedMode()
        {
            // Never fail silently: a stale window after an AddOn reload, or a failed AddOn
            // initialization, must be visible to the operator instead of leaving the UI at
            // "Not run" with an apparently working Run button.
            if (_coordinator == null)
            {
                _viewState.SetStatusLine("Trading: DISABLED | Cannot start: diagnostic coordinator is unavailable. Close and reopen the Diagnostic Center; check the NinjaTrader Log.");
                return;
            }

            var mode = SelectedMode();
            _viewState.SelectedMode = mode;

            // Reset the view before starting. Start publishes its initial progress
            // synchronously when already on the UI dispatcher; doing BeginRun afterwards
            // could overwrite that notification and create a misleading Idle/0% display.
            _viewState.BeginRun(mode);
            _runButton.IsEnabled = false;
            _statusBar.Text = "Trading: DISABLED | Starting " + DiagnosticModeCatalog.For(mode).Title + "...";

            try
            {
                if (!_coordinator.Start(mode))
                {
                    var reason = _coordinator.IsRunning
                        ? "A diagnostic run is already in progress."
                        : "The diagnostic coordinator rejected the start. Close and reopen the Diagnostic Center; check the NinjaTrader Log.";
                    if (_coordinator.IsRunning)
                    {
                        _viewState.SetStatusLine("Trading: DISABLED | " + reason);
                    }
                    else
                    {
                        _viewState.MarkStartFailed(mode, reason);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                _viewState.MarkStartFailed(mode, "Start failed: " + Core.Errors.ErrorClassifier.Describe(ex));
            }
        }

        private void ExportEvidence()
        {
            // Export writes (or re-writes) the evidence package for the last completed run.
            // It never re-runs the diagnostic and never touches the host.
            var result = _viewState.Result;
            if (result == null || _coordinator == null)
            {
                _viewState.SetStatusLine("Trading: DISABLED | Nothing to export yet.");
                return;
            }

            if (_exportAction != null)
            {
                try
                {
                    _exportAction();
                    return;
                }
                catch (Exception ex)
                {
                    _viewState.SetStatusLine("Trading: DISABLED | Export failed: " + Core.Errors.ErrorClassifier.Describe(ex));
                    return;
                }
            }

            var directory = result.EvidenceDirectory;
            _viewState.SetStatusLine("Trading: DISABLED | Evidence package: " + (string.IsNullOrEmpty(directory) ? "not available" : directory));
        }

        private void OnViewStateChanged()
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                Refresh();
            }
            else
            {
                dispatcher.InvokeAsync(Refresh);
            }
        }

        private void Refresh()
        {
            var progress = _viewState.Progress;
            _progress.Value = progress.Percent;
            _progressText.Text = progress.State + " " + progress.Percent + "%"
                + (string.IsNullOrEmpty(progress.CurrentModuleId) ? string.Empty : " (" + progress.CurrentModuleId + ")");

            _runButton.IsEnabled = !_viewState.IsRunning;
            _cancelButton.IsEnabled = _viewState.CanCancel;
            _exportButton.IsEnabled = !_viewState.IsRunning;

            var rows = new List<ModuleRow>();
            foreach (var module in _viewState.Modules)
            {
                rows.Add(new ModuleRow
                {
                    Module = module.ModuleId,
                    Title = module.Title,
                    Status = module.Status.ToWire(),
                    Executed = module.Executed ? "yes" : "no",
                    Checks = module.CheckCount
                });
            }

            _moduleGrid.ItemsSource = rows;

            _findingsList.Items.Clear();
            var result = _viewState.Result;
            if (result != null && result.Report != null)
            {
                foreach (var finding in result.Report.Findings)
                {
                    if (finding.Status == DiagnosticStatus.PASS)
                    {
                        continue;
                    }

                    _findingsList.Items.Add(SecretRedactor.Redact(
                        finding.FindingId + " [" + finding.Severity.ToWire() + "/" + finding.Status.ToWire() + "] " + finding.Observation));
                }
            }

            _acceptanceList.Items.Clear();
            foreach (var subject in _viewState.Acceptance)
            {
                _acceptanceList.Items.Add(SecretRedactor.Redact(
                    subject.Title + ": " + subject.Status.ToWire() + " — " + subject.Summary));
            }

            _statusBar.Text = _viewState.StatusLine;
        }

        /// <summary>Row shape for the module status grid.</summary>
        public sealed class ModuleRow
        {
            public string Module { get; set; }

            public string Title { get; set; }

            public string Status { get; set; }

            public string Executed { get; set; }

            public int Checks { get; set; }
        }
    }
}
