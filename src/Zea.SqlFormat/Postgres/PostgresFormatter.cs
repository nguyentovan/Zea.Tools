using System.Text;

namespace Zea.SqlFormat.Postgres;

internal static partial class PostgresFormatter
{
    internal static string Format(string sql)
    {
        int nul = sql.IndexOf('\0');
        if (nul >= 0)
        {
            throw Error(sql, nul, "NUL is not valid SQL text.");
        }

        Document original;
        try
        {
            original = Read(sql, backslashStrings: false);
        }
        catch (FormatException) when (sql.Contains('\\'))
        {
            // The session's ordinary-string setting is unavailable to a standalone formatter.
            _ = Read(sql, backslashStrings: true);
            return sql;
        }

        if (original.PreserveSource)
        {
            return sql;
        }

        if (original.HasOrdinaryBackslash)
        {
            try
            {
                var alternative = Read(sql, backslashStrings: true);
                if (!SameBoundaries(original, alternative))
                {
                    return sql;
                }
            }
            catch (FormatException)
            {
                return sql;
            }
        }

        var choices = Plan(original);
        return RenderAndVerify(original, choices);
    }

    private readonly record struct SourceSpan(int Start, int Length)
    {
        internal int End => Start + Length;
    }

    private enum Kind
    {
        Word, QuotedName, String, DollarString, Number, Parameter, ClientVariable, Operator,
        Punctuation, LineComment, BlockComment
    }

    private readonly record struct Atom(Kind Kind, SourceSpan Source, string Key)
    {
        internal bool IsComment => Kind is Kind.LineComment or Kind.BlockComment;
    }

    private enum GapRule { Flexible, Separator, Newline, Exact }
    private readonly record struct Gap(SourceSpan Source, GapRule Rule, bool Compact);

    private sealed record Document(
        string Source, Atom[] Atoms, Gap[] Gaps, int[] MatchingDelimiter,
        int[] NextCode, int[] PreviousCode, bool HasOrdinaryBackslash, bool PreserveSource, string Newline)
    {
        internal ReadOnlySpan<char> Text(int index) => Source.AsSpan(Atoms[index].Source.Start, Atoms[index].Source.Length);
        internal bool Is(int index, string text) => index >= 0 && index < Atoms.Length && Atoms[index].Key == text;
    }

    private enum BreakKind { Keep, Empty, Space, Line }
    private readonly record struct GapChoice(BreakKind Break, int Indent = 0);

    private static string RenderAndVerify(Document original, GapChoice[] choices)
    {
        var result = new StringBuilder(original.Source.Length);
        for (int index = 0; index < original.Gaps.Length; index++)
        {
            Gap gap = original.Gaps[index];
            GapChoice choice = choices[index];
            if (gap.Rule == GapRule.Exact || choice.Break == BreakKind.Keep)
            {
                result.Append(original.Source, gap.Source.Start, gap.Source.Length);
            }
            else if (choice.Break == BreakKind.Line)
            {
                result.Append(original.Newline);
                result.Append(' ', checked(choice.Indent * 4));
            }
            else if (gap.Rule == GapRule.Newline)
            {
                result.Append(original.Newline);
            }
            else if (choice.Break == BreakKind.Space || gap.Rule == GapRule.Separator)
            {
                result.Append(' ');
            }

            if (index < original.Atoms.Length)
            {
                SourceSpan source = original.Atoms[index].Source;
                result.Append(original.Source, source.Start, source.Length);
            }
        }

        string output = result.ToString();
        Document candidate;
        try
        {
            candidate = Read(output, backslashStrings: false);
        }
        catch (FormatException)
        {
            throw Error(original.Source, 0, "Formatting did not preserve lexical structure.");
        }

        if (candidate.Atoms.Length != original.Atoms.Length)
        {
            throw Error(original.Source, 0, "Formatting did not preserve lexical structure.");
        }

        for (int index = 0; index < original.Atoms.Length; index++)
        {
            if (original.Atoms[index].Kind != candidate.Atoms[index].Kind
                || !original.Text(index).SequenceEqual(candidate.Text(index))
                || original.MatchingDelimiter[index] != candidate.MatchingDelimiter[index])
            {
                throw Error(original.Source, original.Atoms[index].Source.Start, "Formatting did not preserve SQL text.");
            }
        }

        for (int index = 0; index < original.Gaps.Length; index++)
        {
            Gap gap = original.Gaps[index];
            Gap rendered = candidate.Gaps[index];
            ReadOnlySpan<char> actual = output.AsSpan(rendered.Source.Start, rendered.Source.Length);
            bool valid = gap.Rule switch
            {
                GapRule.Exact => original.Source.AsSpan(gap.Source.Start, gap.Source.Length).SequenceEqual(actual),
                GapRule.Separator => actual.Length > 0,
                GapRule.Newline => HasNewline(actual),
                _ => true
            };
            if (!valid)
            {
                throw Error(original.Source, gap.Source.Start, "Formatting did not preserve a protected SQL boundary.");
            }
        }

        return output;
    }

    private static bool SameBoundaries(Document left, Document right)
    {
        if (left.Atoms.Length != right.Atoms.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Atoms.Length; index++)
        {
            if (left.Atoms[index].Kind != right.Atoms[index].Kind
                || left.Atoms[index].Source != right.Atoms[index].Source
                || left.MatchingDelimiter[index] != right.MatchingDelimiter[index])
            {
                return false;
            }
        }

        return true;
    }

    private static FormatException Error(string source, int position, string reason)
    {
        int line = 1;
        int column = 1;
        for (int index = 0; index < position; index++)
        {
            if (source[index] == '\r')
            {
                line++;
                column = 1;
                if (index + 1 < position && source[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new FormatException($"Could not parse postgres. Line {line}, column {column}: {reason}");
    }

    private static bool IsSpace(char value) => value is ' ' or '\t' or '\n' or '\r' or '\f' or '\v';
    private static bool HasNewline(ReadOnlySpan<char> value) => value.IndexOfAny('\r', '\n') >= 0;
    private static bool IsDigit(char value) => value is >= '0' and <= '9';
    private static bool IsIdentifierStart(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or >= '\u0080';
    private static bool IsIdentifierPart(char value) => IsIdentifierStart(value) || IsDigit(value) || value == '$';
    private static bool IsOperator(char value) => value is '~' or '!' or '@' or '#' or '^' or '&' or '|' or '`' or '?' or '+' or '-' or '*' or '/' or '%' or '<' or '>' or '=';
}
