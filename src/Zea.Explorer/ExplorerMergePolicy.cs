using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Zea.Explorer;

internal sealed record ExplorerTabState(nint Handle, string Path, bool HasSelection, string Name = "");

internal sealed record ExplorerWindowState(nint Handle, uint ProcessId, uint ThreadId, nint ActiveTab, ExplorerTabState[] Tabs);

internal enum ExplorerForegroundAction
{
    CloseSource,
    ActivateTarget,
    Wait
}

internal sealed class ExplorerTabSearch(nint originalTab)
{
    private nint _activeBeforeRequest;
    private int _requestedIndex;

    public int RestoreIndex { get; private set; }

    public int Next(nint activeTab, int tabCount)
    {
        if (_requestedIndex != 0 && activeTab == originalTab && activeTab != _activeBeforeRequest)
        {
            RestoreIndex = _requestedIndex;
        }

        if (_requestedIndex >= tabCount)
        {
            return 0;
        }

        _activeBeforeRequest = activeTab;
        _requestedIndex++;
        return _requestedIndex;
    }
}

internal static class ExplorerMergePolicy
{
    public const int CandidateSettleMilliseconds = 450;

    public static int GetWatchInterval(bool watchingCandidateOrFocus, bool merging)
    {
        if (merging)
        {
            return 75;
        }

        if (watchingCandidateOrFocus)
        {
            return CandidateSettleMilliseconds;
        }

        return 0;
    }

    public static ExplorerWindowState? SelectTarget(IEnumerable<ExplorerWindowState?> windows, ISet<nint> newWindows)
    {
        ExplorerWindowState? fallback = null;
        foreach (var window in windows)
        {
            if (window is null || window.ActiveTab == 0 || window.Tabs.Length == 0
                || window.Tabs.Any(tab => !IsSupportedPath(tab.Path)))
            {
                continue;
            }

            if (!newWindows.Contains(window.Handle))
            {
                return window;
            }

            fallback = window;
        }

        return fallback;
    }

    public static ExplorerForegroundAction GetForegroundAction(nint foreground, nint source, nint target,
        bool desktopIsForeground, bool openedFromDesktop, bool activationRequested, bool targetIsMinimized)
    {
        if (foreground == target && !targetIsMinimized)
        {
            return ExplorerForegroundAction.CloseSource;
        }

        if (activationRequested)
        {
            return ExplorerForegroundAction.Wait;
        }

        if (foreground == target || foreground == source || (desktopIsForeground && openedFromDesktop))
        {
            return ExplorerForegroundAction.ActivateTarget;
        }

        if (openedFromDesktop)
        {
            return ExplorerForegroundAction.Wait;
        }

        return ExplorerForegroundAction.CloseSource;
    }

    public static bool IsDirectDesktopFolder(string path, string desktop, string commonDesktop)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        var matches = parent is not null && (IsSamePath(parent, Path.TrimEndingDirectorySeparator(desktop))
            || IsSamePath(parent, Path.TrimEndingDirectorySeparator(commonDesktop)));
        return matches;
    }

    public static bool IsSupportedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var supported = !path.Contains("::{21ec2020-3aea-1069-a2dd-08002b30309d}", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("::{26ee0668-a00a-44d7-9371-beb064c98683}", StringComparison.OrdinalIgnoreCase);
        return supported;
    }

    public static bool IsSameWindow(ExplorerWindowState before, ExplorerWindowState current)
    {
        var same = before.Handle == current.Handle && before.ThreadId == current.ThreadId && before.ProcessId == current.ProcessId;
        return same;
    }

    public static bool IsSamePath(string first, string second)
    {
        var same = string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        return same;
    }

    public static bool IsSourceUnchanged(ExplorerWindowState before, ExplorerWindowState current)
    {
        if (!IsSameWindow(before, current) || before.Tabs.Length != 1 || current.Tabs.Length != 1)
        {
            return false;
        }

        var original = before.Tabs[0];
        var tab = current.Tabs[0];
        var unchanged = original.Handle != 0 && original.Handle == tab.Handle && tab.Handle == current.ActiveTab
            && IsSupportedPath(tab.Path) && IsSamePath(original.Path, tab.Path)
            && !original.HasSelection && !tab.HasSelection;
        return unchanged;
    }

    public static bool CanCloseSource(ExplorerWindowState before, ExplorerWindowState source, ExplorerWindowState target,
        nint destinationTab, uint? originalInput, uint? currentInput)
    {
        if (source.Handle == target.Handle || destinationTab == 0 || target.ActiveTab != destinationTab
            || originalInput is null || originalInput != currentInput || !IsSourceUnchanged(before, source))
        {
            return false;
        }

        var destination = target.Tabs.FirstOrDefault(tab => tab.Handle == destinationTab);
        var confirmed = destination is not null && IsSamePath(destination.Path, source.Tabs[0].Path);
        return confirmed;
    }

    public static bool CanCloseCreatedTab(ExplorerWindowState before, ExplorerWindowState current, nint createdTab,
        string initialPath, string sourcePath, bool navigating, uint? originalInput, uint? currentInput)
    {
        if (originalInput is null || originalInput != currentInput || !IsSameWindow(before, current)
            || createdTab == 0 || current.ActiveTab != createdTab || !IsSupportedPath(initialPath)
            || before.Tabs.Length == 0 || current.Tabs.Length != before.Tabs.Length + 1
            || before.Tabs.Any(tab => tab.Handle == createdTab))
        {
            return false;
        }

        var currentHandles = current.Tabs.Select(tab => tab.Handle).ToHashSet();
        if (currentHandles.Count != current.Tabs.Length || !before.Tabs.All(tab => currentHandles.Contains(tab.Handle)))
        {
            return false;
        }

        var tab = current.Tabs.FirstOrDefault(tab => tab.Handle == createdTab);
        var owned = tab is not null && !tab.HasSelection && (IsSamePath(tab.Path, initialPath)
            || (navigating && IsSamePath(tab.Path, sourcePath)));
        return owned;
    }
}
