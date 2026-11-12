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

        public DiagnosticCenterWindow(DiagnosticViewState viewState, DiagnosticRunCoordinator coordinator, IEvidenceSink evidenceSink)
            : this(viewState, coordinator, evidenceSink, null)
        {
        }

        public DiagnosticCenterWindow(DiagnosticViewState viewState, DiagnosticRunCoordinator coordinator, IEvidenceSink evidenceSink, Action exportAction)
        {
            _viewState = viewState ?? throw new ArgumentNullException(nameof(viewState));
            _coordinator = coordinator;
            _evidenceSink = evidenceSink;
            _exportAction = exportAction;

            Caption = BuildIdentity.ProductName;
            Width = 900;
            Height = 640;
            MinWidth = 720;
            MinHeight = 480;

            Content = BuildLayout();

            _viewState.Changed += OnViewStateChanged;
            Closed += (_, __) =>
            {
                if (_viewState != null)
                {
                    _viewState.Changed -= OnViewStateChanged;
                }
            };

            Refresh();
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
            if (_coordinator == null)
            {
                return;
            }

            var mode = SelectedMode();
            _viewState.BeginRun(mode);
            if (!_coordinator.Start(mode))
            {
                // A run is already active; the coordinator refused the start. The UI stays
                // responsive and the status line explains why.
                _viewState.SetStatusLine("Trading: DISABLED | A diagnostic run is already in progress.");
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
