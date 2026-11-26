using System;
using System.Collections.Generic;

namespace Zea.Tweakr;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8
}

internal enum HotkeyCommand
{
    None,
    FormatSql,
    ConvertLog,
    OpenExplorer
}

internal readonly record struct HotkeyResult(bool Suppress, bool MaskWindowsKey = false,
    HotkeyCommand Command = HotkeyCommand.None, nint Editor = 0);

internal sealed class HotkeyChord
{
    internal const int KeyK = 0x4B;
    internal const int KeyE = 0x45;
    private const int KeyG = 0x47;
    private const int KeyY = 0x59;

    private readonly HashSet<int> suppressedKeys = new HashSet<int>();
    private bool formatEnabled;
    private bool convertEnabled;
    private bool explorerEnabled;
    private nint editor;
    private long deadline;
    private HotkeyCommand pendingCommand;
    private int commandKey;

    internal bool HasSqlSequence => editor != 0;
    internal bool SqlEnabled => formatEnabled || convertEnabled;

    internal bool ShouldHandleKey(int key)
    {
        var shouldHandle = HasSqlSequence || pendingCommand != HotkeyCommand.None ||
            suppressedKeys.Contains(key) || (key == KeyK && SqlEnabled) || (key == KeyE && explorerEnabled);
        return shouldHandle;
    }

    internal void SetEnabled(bool formatSql, bool convertLog, bool explorer)
    {
        formatEnabled = formatSql;
        convertEnabled = convertLog;
        explorerEnabled = explorer;
        ResetCommand();
    }

    internal HotkeyResult Handle(int key, bool isDown, HotkeyModifiers modifiers, nint foreground,
        bool isSqlEditor, long now)
    {
        if (isDown && suppressedKeys.Contains(key))
        {
            return new HotkeyResult(true);
        }

        var suppress = !isDown && suppressedKeys.Remove(key);
        if (HasSqlSequence && (foreground != editor || now > deadline))
        {
            ResetCommand();
        }

        if (pendingCommand != HotkeyCommand.None)
        {
            var waitingFor = pendingCommand == HotkeyCommand.OpenExplorer ? HotkeyModifiers.Windows : HotkeyModifiers.Control;
            if (now > deadline || (modifiers & ~waitingFor) != HotkeyModifiers.None || (isDown && !IsModifier(key)))
            {
                ResetCommand();
            }
            else if (modifiers == HotkeyModifiers.None && !suppressedKeys.Contains(commandKey))
            {
                var result = new HotkeyResult(suppress, Command: pendingCommand, Editor: editor);
                ResetCommand();
                return result;
            }
            else
            {
                return new HotkeyResult(suppress);
            }
        }

        if (HasSqlSequence && modifiers != HotkeyModifiers.Control)
        {
            ResetCommand();
        }

        if (!isDown || IsModifier(key))
        {
            return new HotkeyResult(suppress);
        }

        if (HasSqlSequence)
        {
            if (key == KeyG && convertEnabled)
            {
                pendingCommand = HotkeyCommand.ConvertLog;
            }
            else if (key == KeyY && formatEnabled)
            {
                pendingCommand = HotkeyCommand.FormatSql;
            }

            if (pendingCommand != HotkeyCommand.None)
            {
                commandKey = key;
                deadline = now + 2000;
                suppressedKeys.Add(key);
                return new HotkeyResult(true);
            }

            ResetCommand();
        }

        if (key == KeyK && modifiers == HotkeyModifiers.Control && SqlEnabled && isSqlEditor && foreground != 0)
        {
            editor = foreground;
            deadline = now + 1500;
            suppressedKeys.Add(key);
            return new HotkeyResult(true);
        }

        if (key == KeyE && modifiers == HotkeyModifiers.Windows && explorerEnabled)
        {
            pendingCommand = HotkeyCommand.OpenExplorer;
            commandKey = key;
            deadline = now + 2000;
            suppressedKeys.Add(key);
            return new HotkeyResult(true, MaskWindowsKey: true);
        }

        return new HotkeyResult(suppress);
    }

    private void ResetCommand()
    {
        editor = 0;
        pendingCommand = HotkeyCommand.None;
        commandKey = 0;
        deadline = 0;
    }

    private static bool IsModifier(int key)
    {
        var isModifier = key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
        return isModifier;
    }
}
