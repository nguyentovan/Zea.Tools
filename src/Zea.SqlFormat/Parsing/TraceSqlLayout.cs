using System.Text;
using Antlr4.Runtime;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Zea.SqlFormat.Parsing.SQLStandardLexer;

namespace Zea.SqlFormat.Parsing;

internal static class TraceSqlLayout
{
    public static string Format(string sql, string dialect)
    {
        try
        {
            var tokens = CodeTokens(sql, dialect);
            if (tokens.Length == 0 || dialect == "tsql" && HasSqlCmdDirective(sql, tokens))
            {
                return sql;
            }

            var formatted = ApplyLayout(sql, tokens);
            if (!string.Equals(sql, formatted, StringComparison.Ordinal) &&
                !tokens.Select(Signature).SequenceEqual(CodeTokens(formatted, dialect).Select(Signature)))
            {
                return sql;
            }

            return formatted;
        }
        catch (FormatException)
        {
            return sql;
        }
    }

    internal static string? TryApplyTsqlLayout(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out var errors);
        if (errors.Count != 0)
        {
            return null;
        }

        var options = new SqlScriptGeneratorOptions();
        options.PreserveComments = true;
        options.MultilineSelectElementsList = true;
        options.MultilineWherePredicatesList = true;
        options.NewLineBeforeJoinClause = true;
        options.AlignClauseBodies = false;
        var generator = new Sql170ScriptGenerator(options);
        generator.GenerateScript(fragment, out var reference);
        var formatted = TryApplyReference(sql, reference, "tsql", allowTemplateTokens: true);
        return formatted;
    }

    internal static string? TryApplyReference(string sql, string reference, string dialect, bool allowTemplateTokens = false)
    {
        if (string.Equals(sql, reference, StringComparison.Ordinal))
        {
            return sql;
        }

        var original = CodeTokens(sql, dialect);
        if (dialect == "tsql" && HasSqlCmdDirective(sql, original))
        {
            return sql;
        }

        var layout = CodeTokens(reference, dialect);
        if (original.Length == 0 || original.Length > layout.Length || !allowTemplateTokens && original.Length != layout.Length)
        {
            return null;
        }

        TSql170Parser? batchParser = null;
        BatchBoundary[]? originalBatches = null;
        HashSet<int>? batchOffsets = null;
        if (dialect == "tsql" && original.Any(token => string.Equals(token.Text, "GO", StringComparison.OrdinalIgnoreCase)))
        {
            // ScriptDom recognizes batch separators that the formatting lexer treats as identifiers.
            batchParser = new TSql170Parser(initialQuotedIdentifiers: true);
            originalBatches = ReadBatchBoundaries(batchParser, sql).ToArray();
            batchOffsets = originalBatches.Select(batch => batch.Offset).ToHashSet();
        }

        var output = new StringBuilder(reference.Length);
        output.Append(sql.AsSpan(0, original[0].StartIndex));
        var previousEnd = layout[0].StartIndex;
        var layoutIndex = 0;
        for (var index = 0; index < original.Length; index++)
        {
            var token = original[index];
            ReadOnlySpan<char> gap = default;
            while (layoutIndex < layout.Length && !Matches(token, layout[layoutIndex]))
            {
                if (!allowTemplateTokens || !IsOptionalTemplateToken(layout, layoutIndex))
                {
                    return null;
                }

                var skipped = layout[layoutIndex++];
                var before = reference.AsSpan(previousEnd, skipped.StartIndex - previousEnd);
                gap = PreferLineBreak(gap, before);
                previousEnd = skipped.StopIndex + 1;
            }

            if (layoutIndex == layout.Length)
            {
                return null;
            }

            var formattedToken = layout[layoutIndex++];
            if (index > 0)
            {
                var before = reference.AsSpan(previousEnd, formattedToken.StartIndex - previousEnd);
                var layoutGap = PreferLineBreak(gap, before);
                var previous = original[index - 1];
                if (batchOffsets != null && (batchOffsets.Contains(token.StartIndex) || batchOffsets.Contains(previous.StartIndex)))
                {
                    var sourceEnd = previous.StopIndex + 1;
                    layoutGap = sql.AsSpan(sourceEnd, token.StartIndex - sourceEnd);
                }

                output.Append(layoutGap);
            }

            output.Append(sql.AsSpan(token.StartIndex, token.StopIndex - token.StartIndex + 1));
            previousEnd = formattedToken.StopIndex + 1;
        }

        while (layoutIndex < layout.Length)
        {
            if (!allowTemplateTokens || layout[layoutIndex++].Type != Semicolon)
            {
                return null;
            }
        }

        output.Append(sql.AsSpan(original[^1].StopIndex + 1));
        var formatted = output.ToString();
        if (!original.Select(Signature).SequenceEqual(CodeTokens(formatted, dialect).Select(Signature)))
        {
            return null;
        }

        if (batchParser != null && originalBatches != null)
        {
            var actualBatches = ReadBatchBoundaries(batchParser, formatted);
            if (!originalBatches.Select(batch => (batch.TokenIndex, batch.HasRepeatCount))
                    .SequenceEqual(actualBatches.Select(batch => (batch.TokenIndex, batch.HasRepeatCount))))
            {
                return null;
            }
        }

        return formatted;
    }

    private static IEnumerable<BatchBoundary> ReadBatchBoundaries(TSql170Parser parser, string sql)
    {
        var tokens = parser.GetTokenStream(new StringReader(sql), out _)
            .Where(token => token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
            .ToArray();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.TokenType == TSqlTokenType.Go)
            {
                var hasRepeatCount = index + 1 < tokens.Length && tokens[index + 1].TokenType == TSqlTokenType.Integer &&
                    tokens[index + 1].Line == token.Line;
                yield return new BatchBoundary(token.Offset, index, hasRepeatCount);
            }
        }
    }

    private readonly record struct BatchBoundary(int Offset, int TokenIndex, bool HasRepeatCount);

    private static bool Matches(IToken original, IToken formatted)
    {
        var matches = original.Type == formatted.Type &&
            string.Equals(original.Text, formatted.Text, StringComparison.OrdinalIgnoreCase);
        return matches;
    }

    private static bool IsOptionalTemplateToken(IToken[] tokens, int index)
    {
        // ScriptDom can emit optional syntax in its template. Only the original tokens are written.
        var type = tokens[index].Type;
        var optional = type is AS or Semicolon ||
            type == INNER && index + 1 < tokens.Length && tokens[index + 1].Type == JOIN;
        return optional;
    }

    private static ReadOnlySpan<char> PreferLineBreak(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        if (first.IsEmpty || first.IndexOfAny('\r', '\n') < 0 && second.IndexOfAny('\r', '\n') >= 0)
        {
            return second;
        }

        return first;
    }

    private static bool HasSqlCmdDirective(string sql, IToken[] tokens)
    {
        foreach (var token in tokens)
        {
            var start = token.StartIndex;
            var next = sql.AsSpan(start);
            if (!next.StartsWith(":") && !next.StartsWith("!!"))
            {
                continue;
            }

            var line = sql.AsSpan(0, start);
            var lineBreak = line.LastIndexOfAny('\r', '\n');
            if (line[(lineBreak + 1)..].IndexOfAnyExcept(' ', '\t', '\u00A0') < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IToken[] CodeTokens(string sql, string dialect)
    {
        var tokens = DbeaverSqlParser.Tokenize(sql, dialect)
            .Where(token => token.Type != Separator && token.Type != WS)
            .ToArray();
        return tokens;
    }

    private static (int Type, string Text) Signature(IToken token)
    {
        return (token.Type, token.Text);
    }

    private static string ApplyLayout(string sql, IToken[] tokens)
    {
        var output = new StringBuilder(sql.Length);
        var previousEnd = 0;
        var depth = 0;
        var projection = false;
        var predicate = false;
        var between = false;

        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            var word = WordAt(tokens, index);
            var previous = WordAt(tokens, index - 1);
            var next = WordAt(tokens, index + 1);
            var afterNext = WordAt(tokens, index + 2);
            int? indent = null;

            if (depth == 0)
            {
                var join = word == "JOIN" && !JoinModifier(previous) ||
                    (word is "LEFT" or "RIGHT" or "FULL" or "INNER" or "CROSS") &&
                    (next is "JOIN" or "APPLY" || next == "OUTER" && afterNext == "JOIN") ||
                    word == "OUTER" && next == "APPLY";
                var clause = word is "FROM" or "WHERE" or "HAVING" or "UNION" or "EXCEPT" or "INTERSECT" or "LIMIT"
                    || word is "ORDER" or "GROUP" && next == "BY";

                if (join || clause || word == "SELECT" && previous == ";")
                {
                    indent = 0;
                    projection = false;
                    predicate = word is "WHERE" or "HAVING";
                    between = false;
                }
                else if (predicate && (word is "AND" or "OR"))
                {
                    if (!between || word == "OR")
                    {
                        indent = 4;
                    }

                    between = false;
                }
                else if (projection && previous == "," && word != ",")
                {
                    indent = 4;
                }

                if (word == "SELECT")
                {
                    projection = true;
                    predicate = false;
                    between = false;
                }
                else if (word == "ON")
                {
                    predicate = true;
                    between = false;
                }
                else if (word == "BETWEEN" && predicate)
                {
                    between = true;
                }
                else if (word is "INTO" or ";" or "GO")
                {
                    projection = false;
                    predicate = false;
                    between = false;
                }
            }

            var gap = sql.AsSpan(previousEnd, token.StartIndex - previousEnd);
            if (index > 0 && indent.HasValue && word != "GO" && previous != "GO" &&
                gap.IndexOfAnyExcept(" \t\r\n") < 0)
            {
                var lineBreak = gap.LastIndexOfAny('\r', '\n');
                if (lineBreak >= 0)
                {
                    output.Append(gap[..(lineBreak + 1)]);
                }
                else
                {
                    output.AppendLine();
                }

                output.Append(' ', indent.Value);
            }
            else
            {
                output.Append(gap);
            }

            output.Append(sql.AsSpan(token.StartIndex, token.StopIndex - token.StartIndex + 1));
            previousEnd = token.StopIndex + 1;
            if (token.Type == LeftParen)
            {
                depth++;
            }
            else if (token.Type == RightParen && depth > 0)
            {
                depth--;
            }
        }

        output.Append(sql.AsSpan(previousEnd));
        var result = output.ToString();
        return result;
    }

    private static string WordAt(IToken[] tokens, int index)
    {
        if (index < 0 || index >= tokens.Length || tokens[index].StopIndex - tokens[index].StartIndex >= 10)
        {
            return string.Empty;
        }

        var word = tokens[index].Text.ToUpperInvariant();
        return word;
    }

    private static bool JoinModifier(string word)
    {
        var modifier = word is "LEFT" or "RIGHT" or "FULL" or "INNER" or "CROSS" or "OUTER" or "NATURAL";
        return modifier;
    }
}
