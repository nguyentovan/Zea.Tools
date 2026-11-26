using Avalonia.Threading;

namespace Zea.Explorer;

public sealed class ExplorerService : IDisposable
{
    private readonly ExplorerShell _shell = new();
    private readonly ExplorerWindowEvents _windowEvents;
    private readonly DispatcherTimer _timer;
    private readonly DesktopFolderClick _desktopClicks;
    private readonly Dictionary<nint, long> _candidates = new();
    private readonly List<nint> _expiredCandidates = new();
    private HashSet<nint> _known = new();
    private HashSet<nint> _current = new();
    private Merge? _merge;
    private DesktopFocus? _desktopFocus;
    private bool _handlingShortcut;
    private bool _checkingWindows;
    private bool _windowCheckPending;
    private bool _enabled;

    public ExplorerService()
    {
        _desktopClicks = new DesktopFolderClick(TrackDesktopFolder);
        _windowEvents = new ExplorerWindowEvents(OnWindowEvent);
        _timer = new DispatcherTimer();
        _timer.Tick += CheckWindows;
    }

    public void RecordInput(uint tick)
    {
        ExplorerInput.Shared.RecordAction(tick);
    }

    public void SetEnabled(bool enabled)
    {
        ExplorerTrace.Log($"watch.enabled requested={enabled} previous={_enabled}");
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        _desktopClicks.SetEnabled(enabled);
        _desktopFocus = null;
        _merge = null;
        _candidates.Clear();
        if (enabled)
        {
            _windowEvents.Start();
            _known.UnionWith(_shell.FindWindows());
            ExplorerTrace.Log($"watch.seed handles={string.Join(",", _known)} foreground={ExplorerShell.DescribeWindow(ExplorerShell.ForegroundWindow())}");
            UpdateWatchTimer();
        }
        else
        {
            _windowEvents.Stop();
            _timer.Stop();
            _windowCheckPending = false;
            _known.Clear();
            _current.Clear();
        }
    }

    public async Task OpenTab(CancellationToken cancellationToken)
    {
        if (!_enabled || _handlingShortcut)
        {
            return;
        }

        _handlingShortcut = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep a snapshot since this handler can yield while the watcher is toggled.
            var windows = _shell.FindWindows().ToArray();
            foreach (var window in windows)
            {
                // A minimized window may need to be restored before its tab is visible.
                if (ExplorerShell.ActiveTab(window) == 0 && !ExplorerShell.IsMinimized(window))
                {
                    continue;
                }

                ExplorerShell.ActivateWindow(window);
                long deadline = Environment.TickCount64 + 700;
                while (_enabled && Environment.TickCount64 < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ExplorerShell.ForegroundWindow() == window && ExplorerShell.IsExplorerWindow(window))
                    {
                        var tab = ExplorerShell.ActiveTab(window);
                        if (tab != 0)
                        {
                            if (!ExplorerShell.NewTab(tab))
                            {
                                throw new InvalidOperationException("File Explorer không xác nhận đã mở tab mới.");
                            }

                            return;
                        }
                    }

                    await Task.Delay(25, cancellationToken);
                }

                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_enabled)
            {
                ExplorerShell.OpenWindow();
            }
        }
        finally
        {
            _handlingShortcut = false;
        }
    }

    public void Dispose()
    {
        SetEnabled(false);
        _timer.Tick -= CheckWindows;
        _desktopClicks.Dispose();
        _shell.Dispose();
    }

    private void OnWindowEvent(nint window, bool shown)
    {
        if (!_enabled || _windowCheckPending)
        {
            return;
        }

        var known = _known.Contains(window);
        if (shown ? known || !ExplorerShell.IsExplorerWindow(window) : !known)
        {
            return;
        }

        _windowCheckPending = true;
        ExplorerTrace.Log($"watch.event window={window} shown={shown}");
        Dispatcher.UIThread.Post(CheckWindowEvent, DispatcherPriority.Background);
    }

    private void CheckWindowEvent()
    {
        if (!_windowCheckPending || _merge is not null)
        {
            return;
        }

        if (_candidates.Count == 0)
        {
            _timer.Stop();
        }

        CheckWindows(null, EventArgs.Empty);
        if (_windowCheckPending)
        {
            UpdateWatchTimer();
        }
    }

    private void UpdateWatchTimer()
    {
        if (!_enabled)
        {
            return;
        }

        var pending = _windowCheckPending || _candidates.Count > 0 || _desktopFocus is not null;
        var intervalMs = ExplorerMergePolicy.GetWatchInterval(pending, _merge is not null);
        if (intervalMs == 0)
        {
            _timer.Stop();
            return;
        }

        var interval = TimeSpan.FromMilliseconds(intervalMs);
        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void CheckWindows(object? sender, EventArgs args)
    {
        if (!_enabled || _checkingWindows || _handlingShortcut)
        {
            return;
        }

        _checkingWindows = true;
        var scanWindows = _windowCheckPending || _candidates.Count > 0 || _merge is not null;
        _windowCheckPending = false;
        try
        {
            var now = Environment.TickCount64;
            if (!scanWindows)
            {
                FocusDesktopFolder(now);
                return;
            }

            var windows = _shell.FindWindows();
            _current.Clear();
            foreach (var window in windows)
            {
                _current.Add(window);
                if (!_known.Contains(window))
                {
                    _candidates[window] = now;
                    ExplorerTrace.Log($"window.new window={ExplorerShell.DescribeWindow(window)} foreground={ExplorerShell.DescribeWindow(ExplorerShell.ForegroundWindow())} input={ExplorerShell.LastInput()}");
                }
            }

            _expiredCandidates.Clear();
            foreach (var candidate in _candidates)
            {
                var stillOpen = _current.Contains(candidate.Key);
                var age = now - candidate.Value;
                if (!stillOpen || age >= 4000)
                {
                    ExplorerTrace.Log($"window.candidate.removed hwnd={candidate.Key} stillOpen={stillOpen} age={age}");
                    _expiredCandidates.Add(candidate.Key);
                }
            }

            foreach (var window in _expiredCandidates)
            {
                _candidates.Remove(window);
            }

            (_known, _current) = (_current, _known);
            if (windows.Count == 1)
            {
                _candidates.Remove(windows[0]);
            }
            if (_merge is not null)
            {
                AdvanceMerge(_merge, now);
            }
            else if (_candidates.Count != 0 && windows.Count > 1)
            {
                StartMerge(windows.ToArray(), now);
            }

            if (_merge is null)
            {
                FocusDesktopFolder(now);
            }
        }
        finally
        {
            UpdateWatchTimer();
            _checkingWindows = false;
        }
    }

    private void TrackDesktopFolder(DesktopFolderClick.DesktopClick click)
    {
        var input = ExplorerShell.LastInput();
        ExplorerTrace.Log($"desktop.click control={ExplorerShell.DescribeWindow(click.Control)} clickInput={click.Input} lastInput={input} foreground={ExplorerShell.DescribeWindow(ExplorerShell.ForegroundWindow())}");
        if (!_enabled || input != click.Input)
        {
            ExplorerTrace.Log($"desktop.click.ignored enabled={_enabled} clickInput={click.Input} lastInput={input}");
            return;
        }

        var path = _shell.ReadDesktopFolder(click);
        if (path is not null && ExplorerShell.LastInput() == click.Input)
        {
            _desktopFocus = new DesktopFocus(path, click.Input, Environment.TickCount64 + 2500);
            UpdateWatchTimer();
            ExplorerTrace.Log($"desktop.click.tracked path=\"{path}\" input={click.Input}");
        }
        else
        {
            ExplorerTrace.Log($"desktop.click.untracked path=\"{path}\" lastInput={ExplorerShell.LastInput()}");
        }
    }

    private void FocusDesktopFolder(long now)
    {
        if (_desktopFocus is not { } focus)
        {
            return;
        }

        if (now >= focus.Deadline || ExplorerShell.LastInput() != focus.Input)
        {
            ExplorerTrace.Log($"desktop.focus.expired path=\"{focus.Path}\" deadline={focus.Deadline} input={focus.Input} lastInput={ExplorerShell.LastInput()}");
            _desktopFocus = null;
            return;
        }

        var foreground = ExplorerShell.ForegroundWindow();
        var window = _shell.ReadWindow(foreground);
        var destination = window?.Tabs.FirstOrDefault(tab => ExplorerMergePolicy.IsSamePath(tab.Path, focus.Path));
        ExplorerTrace.Log($"desktop.focus.lookup path=\"{focus.Path}\" foreground={ExplorerShell.DescribeWindow(foreground)} destinationTab={destination?.Handle} activeTab={window?.ActiveTab}");
        if (window is null || destination is null)
        {
            return;
        }

        if (window.ActiveTab != destination.Handle && ExplorerShell.ForegroundWindow() == foreground)
        {
            var selected = ExplorerAutomation.SelectUniqueTab(window.Handle, destination.Name, window.Tabs.Length, focus.Input);
            ExplorerTrace.Log($"desktop.focus.select hwnd={window.Handle} tab={destination.Handle} selected={selected}");
        }
    }

    private void StartMerge(nint[] windows, long now)
    {
        var candidates = _candidates.Keys.ToHashSet();
        var target = ExplorerMergePolicy.SelectTarget(windows.Select(_shell.ReadWindow), candidates);
        if (target is null)
        {
            ExplorerTrace.Log($"merge.noTarget candidates={string.Join(",", candidates)} windows={string.Join(",", windows)}");
            return;
        }

        _candidates.Remove(target.Handle);
        foreach (var candidate in _candidates)
        {
            // Explorer can populate a file-reveal selection after its window first appears.
            if (now - candidate.Value < ExplorerMergePolicy.CandidateSettleMilliseconds)
            {
                continue;
            }

            var input = ExplorerShell.LastInput();
            var source = _shell.ReadWindow(candidate.Key);
            if (!_enabled || input is null || source is null || !ExplorerMergePolicy.IsSourceUnchanged(source, source)
                || ExplorerShell.LastInput() != input)
            {
                ExplorerTrace.Log($"merge.sourceNotReady hwnd={candidate.Key} enabled={_enabled} tabs={source?.Tabs.Length} activeTab={source?.ActiveTab} input={input} lastInput={ExplorerShell.LastInput()}");
                continue;
            }

            _candidates.Remove(candidate.Key);
            var path = source.Tabs[0].Path;
            var duplicate = target.Tabs.FirstOrDefault(tab => ExplorerMergePolicy.IsSamePath(tab.Path, path));
            var openedFromDesktop = _desktopFocus is { } focus && now < focus.Deadline && focus.Input == input.Value
                && ExplorerMergePolicy.IsSamePath(path, focus.Path);
            var merge = new Merge(source, target, input.Value, now + 3500)
            {
                OpenedFromDesktop = openedFromDesktop
            };
            _merge = merge;
            ExplorerTrace.Log($"merge.start source={source.Handle} target={target.Handle} path=\"{path}\" trackedDesktop={openedFromDesktop} duplicateTab={duplicate?.Handle} foreground={ExplorerShell.DescribeWindow(ExplorerShell.ForegroundWindow())} input={input}");
            if (duplicate is not null)
            {
                merge.DestinationTab = duplicate.Handle;
                AdvanceMerge(merge, now);
            }
            else if (ExplorerShell.ActiveTab(target.Handle) == target.ActiveTab && ExplorerShell.LastInput() == input)
            {
                merge.CreatingTab = true;
                merge.CreateConfirmed = ExplorerShell.NewTab(target.ActiveTab);
                ExplorerTrace.Log($"merge.newTab target={target.Handle} activeTab={target.ActiveTab} accepted={merge.CreateConfirmed}");
            }
            else
            {
                ExplorerTrace.Log("merge.aborted before tab creation: target active tab or input changed");
                _merge = null;
            }

            return;
        }
    }

    private void AdvanceMerge(Merge merge, long now)
    {
        if (!_enabled || now >= merge.Deadline || ExplorerShell.LastInput() != merge.Input)
        {
            ExplorerTrace.Log($"merge.stop enabled={_enabled} expired={now >= merge.Deadline} input={merge.Input} lastInput={ExplorerShell.LastInput()} foreground={ExplorerShell.DescribeWindow(ExplorerShell.ForegroundWindow())}");
            CancelMerge(merge);
            return;
        }

        var source = _shell.ReadWindow(merge.Source.Handle);
        var target = _shell.ReadWindow(merge.Target.Handle);
        if (source is null || !ExplorerMergePolicy.IsSourceUnchanged(merge.Source, source)
            || !ExplorerShell.IsExplorerWindow(merge.Target.Handle))
        {
            ExplorerTrace.Log($"merge.invalidWindow source={merge.Source.Handle} sourceTabs={source?.Tabs.Length} sourceActiveTab={source?.ActiveTab} target={merge.Target.Handle} targetTabs={target?.Tabs.Length}");
            CancelMerge(merge);
            return;
        }
        if (target is null)
        {
            ExplorerTrace.Log($"merge.waitTargetSnapshot target={merge.Target.Handle}");
            return;
        }
        if (!ExplorerMergePolicy.IsSameWindow(merge.Target, target))
        {
            ExplorerTrace.Log($"merge.drop reason=targetIdentityChanged target={merge.Target.Handle}");
            _merge = null;
            return;
        }

        var handles = target.Tabs.Select(tab => tab.Handle).ToHashSet();
        if (!merge.Before.IsSubsetOf(handles))
        {
            ExplorerTrace.Log($"merge.drop reason=targetTabSetChanged target={target.Handle} before={string.Join(",", merge.Before)} now={string.Join(",", handles)}");
            _merge = null;
            return;
        }

        if (merge.CreatingTab)
        {
            if (handles.Count == merge.Before.Count)
            {
                ExplorerTrace.Log($"merge.waitTab target={target.Handle} count={handles.Count} createAccepted={merge.CreateConfirmed}");
                return;
            }
            if (handles.Count != merge.Before.Count + 1)
            {
                ExplorerTrace.Log($"merge.drop reason=unexpectedNewTabCount target={target.Handle} before={merge.Before.Count} now={handles.Count}");
                _merge = null;
                return;
            }

            var created = handles.Single(handle => !merge.Before.Contains(handle));
            if (merge.DestinationTab != 0 && merge.DestinationTab != created)
            {
                ExplorerTrace.Log($"merge.drop reason=destinationTabChanged target={target.Handle} expected={merge.DestinationTab} actual={created}");
                _merge = null;
                return;
            }

            merge.DestinationTab = created;
        }
        else if (handles.Count != merge.Before.Count)
        {
            ExplorerTrace.Log($"merge.drop reason=targetTabCountChanged target={target.Handle} before={merge.Before.Count} now={handles.Count}");
            _merge = null;
            return;
        }

        var currentInput = ExplorerShell.LastInput();
        if (currentInput != merge.Input)
        {
            ExplorerTrace.Log($"merge.drop reason=inputChanged expectedInput={merge.Input} actualInput={currentInput}");
            _merge = null;
            return;
        }

        var destination = target.Tabs.First(tab => tab.Handle == merge.DestinationTab);
        if (target.ActiveTab != destination.Handle)
        {
            if (merge.CreatingTab)
            {
                CancelMerge(merge);
            }
            else
            {
                SelectExistingTab(merge, target, destination);
            }
            return;
        }

        if (ExplorerMergePolicy.CanCloseSource(merge.Source, source, target, destination.Handle, merge.Input, ExplorerShell.LastInput()))
        {
            CloseSource(merge);
            return;
        }

        if (merge.CreatingTab && !merge.Navigating && ExplorerMergePolicy.IsSupportedPath(destination.Path) && !destination.HasSelection)
        {
            merge.InitialPath = destination.Path;
            merge.Navigating = _shell.Navigate(target.Handle, destination.Handle, source.Tabs[0].Path, merge.Input);
            ExplorerTrace.Log($"merge.navigate target={target.Handle} tab={destination.Handle} path=\"{source.Tabs[0].Path}\" accepted={merge.Navigating}");
            if (!merge.Navigating)
            {
                CancelMerge(merge);
            }
        }
    }

    private void SelectExistingTab(Merge merge, ExplorerWindowState target, ExplorerTabState destination)
    {
        if (!ExplorerMergePolicy.IsSamePath(destination.Path, merge.Source.Tabs[0].Path))
        {
            CancelMerge(merge);
            return;
        }

        if (!merge.SelectingTab)
        {
            merge.SelectingTab = true;
            var minimized = ExplorerShell.IsMinimized(target.Handle);
            var selected = !minimized && ExplorerAutomation.SelectUniqueTab(target.Handle, destination.Name, target.Tabs.Length, merge.Input);
            ExplorerTrace.Log($"merge.select target={target.Handle} tab={destination.Handle} minimized={minimized} selected={selected}");
            if (selected)
            {
                return;
            }
        }

        // Explorer uses a one-based visual index. Keep each batch short so the watcher can yield.
        var search = merge.TabSearch ??= new ExplorerTabSearch(merge.Target.ActiveTab);
        var batchDeadline = Environment.TickCount64 + 50;
        do
        {
            var index = search.Next(target.ActiveTab, target.Tabs.Length);
            var sent = index != 0 && ExplorerShell.SelectTabByIndex(target, index, merge.Input);
            ExplorerTrace.Log($"merge.select.index target={target.Handle} tab={destination.Handle} activeTab={target.ActiveTab} index={index} sent={sent}");
            if (!sent)
            {
                if (search.RestoreIndex != 0 && target.ActiveTab != merge.Target.ActiveTab)
                {
                    ExplorerShell.SelectTabByIndex(target, search.RestoreIndex, merge.Input);
                }

                CancelMerge(merge);
                return;
            }

            var active = ExplorerShell.ActiveTab(target.Handle);
            if (active == destination.Handle || active == target.ActiveTab || !merge.Before.Contains(active))
            {
                // Re-read the COM snapshot before closing, and allow a delayed switch to complete.
                return;
            }

            target = target with { ActiveTab = active };
        }
        while (Environment.TickCount64 < batchDeadline && Environment.TickCount64 < merge.Deadline);
    }

    private void CancelMerge(Merge merge)
    {
        ExplorerTrace.Log($"merge.cancel source={merge.Source.Handle} target={merge.Target.Handle} tab={merge.DestinationTab} waitingForeground={merge.WaitingForForeground} foreground={ExplorerShell.DescribeWindow(ExplorerShell.ForegroundWindow())}");
        _merge = null;
        if (!_enabled || !merge.CreateConfirmed || merge.InitialPath.Length == 0 || ExplorerShell.LastInput() != merge.Input)
        {
            return;
        }

        var target = _shell.ReadWindow(merge.Target.Handle);
        if (target is not null && ExplorerMergePolicy.CanCloseCreatedTab(merge.Target, target, merge.DestinationTab,
            merge.InitialPath, merge.Source.Tabs[0].Path, merge.Navigating, merge.Input, ExplorerShell.LastInput()))
        {
            ExplorerShell.CloseTab(target, merge.DestinationTab, merge.Input);
        }
    }

    private void CloseSource(Merge merge)
    {
        var target = _shell.ReadWindow(merge.Target.Handle);
        var source = _shell.ReadWindow(merge.Source.Handle);
        if (!_enabled || target is null || source is null || !ExplorerMergePolicy.IsSameWindow(merge.Target, target)
            || !ExplorerMergePolicy.CanCloseSource(merge.Source, source, target, merge.DestinationTab, merge.Input, ExplorerShell.LastInput())
            || ExplorerShell.ActiveTab(target.Handle) != merge.DestinationTab)
        {
            ExplorerTrace.Log($"merge.closeValidationFailed source={merge.Source.Handle} target={merge.Target.Handle} activeTab={target?.ActiveTab} expectedTab={merge.DestinationTab} input={merge.Input} lastInput={ExplorerShell.LastInput()}");
            CancelMerge(merge);
            return;
        }

        var foreground = ExplorerShell.ForegroundWindow();
        var desktopIsForeground = ExplorerShell.IsDesktopWindow(foreground);
        var openedFromDesktop = merge.OpenedFromDesktop || ExplorerMergePolicy.IsDirectDesktopFolder(source.Tabs[0].Path,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        var targetIsMinimized = ExplorerShell.IsMinimized(target.Handle);
        var action = ExplorerMergePolicy.GetForegroundAction(foreground, source.Handle, target.Handle,
            desktopIsForeground, openedFromDesktop, merge.WaitingForForeground, targetIsMinimized);
        ExplorerTrace.Log($"merge.foreground action={action} source={source.Handle} target={target.Handle} tab={merge.DestinationTab} foreground={ExplorerShell.DescribeWindow(foreground)} desktopForeground={desktopIsForeground} desktopOrigin={openedFromDesktop} targetMinimized={targetIsMinimized} trackedDesktop={merge.OpenedFromDesktop} waiting={merge.WaitingForForeground} input={merge.Input}");
        if (action == ExplorerForegroundAction.Wait)
        {
            return;
        }

        if (action == ExplorerForegroundAction.ActivateTarget)
        {
            if (ExplorerShell.ForegroundWindow() != foreground || ExplorerShell.LastInput() != merge.Input)
            {
                ExplorerTrace.Log($"merge.activationAborted foreground={ExplorerShell.DescribeWindow(ExplorerShell.ForegroundWindow())} lastInput={ExplorerShell.LastInput()}");
                CancelMerge(merge);
                return;
            }

            merge.WaitingForForeground = true;
            ExplorerShell.ActivateMergeTarget(target.Handle, foreground, merge.Input);
        }

        // Foreground and restoring a minimized window can complete independently.
        if ((openedFromDesktop || merge.WaitingForForeground || foreground == target.Handle)
            && (ExplorerShell.ForegroundWindow() != target.Handle || ExplorerShell.IsMinimized(target.Handle)))
        {
            return;
        }

        _merge = null;
        var activeTab = ExplorerShell.ActiveTab(target.Handle);
        var sent = activeTab == merge.DestinationTab && ExplorerShell.CloseWindow(source, merge.Input);
        ExplorerTrace.Log($"merge.closeSource source={source.Handle} target={target.Handle} activeTab={activeTab} expectedTab={merge.DestinationTab} posted={sent}");
        if (!sent)
        {
            CancelMerge(merge);
        }
    }

    private sealed record DesktopFocus(string Path, uint Input, long Deadline);

    private sealed class Merge
    {
        public Merge(ExplorerWindowState source, ExplorerWindowState target, uint input, long deadline)
        {
            Source = source;
            Target = target;
            Input = input;
            Deadline = deadline;
            Before = target.Tabs.Select(tab => tab.Handle).ToHashSet();
        }

        public ExplorerWindowState Source { get; }
        public ExplorerWindowState Target { get; }
        public uint Input { get; }
        public long Deadline { get; }
        public HashSet<nint> Before { get; }
        public nint DestinationTab { get; set; }
        public bool OpenedFromDesktop { get; init; }
        public bool WaitingForForeground { get; set; }
        public bool SelectingTab { get; set; }
        public ExplorerTabSearch? TabSearch { get; set; }
        public bool CreatingTab { get; set; }
        public bool CreateConfirmed { get; set; }
        public bool Navigating { get; set; }
        public string InitialPath { get; set; } = "";
    }
}
