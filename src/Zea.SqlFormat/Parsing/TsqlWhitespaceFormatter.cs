using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Zea.SqlFormat.Formatters.RightWay.Interfaces;

namespace Zea.SqlFormat.Parsing;

internal static class TsqlWhitespaceFormatter
{
    public static string? TryFormat(string sql, bool preferRecoveryDiagnostic)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var tokens = parser.GetTokenStream(new StringReader(sql), out var errors);
        if (errors.Count != 0)
        {
            return null;
        }

        var layout = ApplyLayout(sql, tokens);
        if (layout == null)
        {
            return null;
        }

        var source = parser.Parse(new StringReader(sql), out errors);
        if (errors.Count != 0)
        {
            var error = errors[0];
            CheckImplicitProcedureError(sql, parser, tokens, error);
            if (preferRecoveryDiagnostic)
            {
                throw new FormatException($"Could not parse tsql. Line {error.Line}, column {error.Column}: {error.Message}");
            }

            return null;
        }

        if (source is not TSqlScript sourceScript || sourceScript.Batches.Count == 0)
        {
            return null;
        }

        var validation = new PrimaryGrammarValidation();
        sourceScript.Accept(validation);
        if (validation.IsRequired)
        {
            return null;
        }

        if (string.Equals(sql, layout, StringComparison.Ordinal))
        {
            return layout;
        }

        var output = parser.Parse(new StringReader(layout), out errors);
        if (errors.Count != 0 || output is not TSqlScript outputScript ||
            !sourceScript.Batches.Select(batch => batch.Statements.Count)
                .SequenceEqual(outputScript.Batches.Select(batch => batch.Statements.Count)) ||
            !CodeTokens(tokens).SequenceEqual(CodeTokens(output.ScriptTokenStream)))
        {
            return sql;
        }

        return layout;
    }

    private static void CheckImplicitProcedureError(string sql, TSql170Parser parser, IList<TSqlParserToken> tokens, ParseError error)
    {
        var previous = tokens.LastOrDefault(token => token.Offset < error.Offset && token.TokenType is not
            (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment));
        if (previous?.TokenType is not (TSqlTokenType.Semicolon or TSqlTokenType.Begin))
        {
            return;
        }

        // The grammar may time out before its batch-boundary predicate runs.
        // Confirm that the same suffix is a qualified implicit EXEC at the start of a batch.
        var suffix = parser.Parse(new StringReader(sql[error.Offset..]), out _);
        if (suffix is TSqlScript { Batches.Count: > 0 } script &&
            script.Batches[0].Statements.FirstOrDefault() is ExecuteStatement
            {
                ExecuteSpecification.ExecutableEntity: ExecutableProcedureReference
                {
                    ProcedureReference.ProcedureReference.Name.Identifiers.Count: > 1
                }
            } execute && IsImplicitProcedure(execute))
        {
            throw ImplicitProcedureError(error.Line, error.Column);
        }
    }

    private static bool IsImplicitProcedure(ExecuteStatement statement)
    {
        var first = statement.ScriptTokenStream[statement.FirstTokenIndex].Text;
        var implicitProcedure = !string.Equals(first, "EXEC", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(first, "EXECUTE", StringComparison.OrdinalIgnoreCase);
        return implicitProcedure;
    }

    private static FormatException ImplicitProcedureError(int line, int column)
    {
        var error = new FormatException($"Could not parse tsql. Line {line}, column {column}: " +
            "Use EXECUTE when a procedure call is not the first statement of a batch.");
        return error;
    }

    private sealed class PrimaryGrammarValidation : TSqlFragmentVisitor
    {
        public bool IsRequired { get; private set; }

        // The primary grammar validates Service Broker operands more strictly than ScriptDom.
        public override void ExplicitVisit(BeginConversationTimerStatement node)
        {
            IsRequired = true;
        }

        public override void ExplicitVisit(CreateQueueStatement node)
        {
            IsRequired = true;
        }

        public override void ExplicitVisit(ReceiveStatement node)
        {
            IsRequired = true;
        }

        public override void ExplicitVisit(WaitForStatement node)
        {
            IsRequired = true;
        }
    }

    private static string? ApplyLayout(string sql, IList<TSqlParserToken> tokens)
    {
        var text = new StringBuilder(sql.Length);
        var depth = 0;
        var offset = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.TokenType == TSqlTokenType.EndOfFile)
            {
                break;
            }

            var original = token.Text;
            if (token.Offset != offset || !sql.AsSpan(offset).StartsWith(original, StringComparison.Ordinal))
            {
                return null;
            }

            offset += original.Length;
            if (token.TokenType == TSqlTokenType.LeftParenthesis)
            {
                if (++depth > SqlStructureConstants.MAX_NESTING_DEPTH)
                {
                    return null;
                }
            }
            else if (token.TokenType == TSqlTokenType.RightParenthesis)
            {
                depth--;
            }

            if (token.TokenType == TSqlTokenType.WhiteSpace && original.AsSpan().IndexOfAnyExcept(' ', '\t') < 0)
            {
                var next = tokens[index + 1].TokenType;
                if (depth == 0 && next is TSqlTokenType.From or TSqlTokenType.Where or TSqlTokenType.Group
                    or TSqlTokenType.Having or TSqlTokenType.Order or TSqlTokenType.Union
                    or TSqlTokenType.Except or TSqlTokenType.Intersect)
                {
                    text.AppendLine();
                }
                else
                {
                    text.Append(' ');
                }
            }
            else
            {
                text.Append(original);
            }
        }

        var result = offset == sql.Length ? text.ToString() : null;
        return result;
    }

    private static IEnumerable<(TSqlTokenType Kind, string Text)> CodeTokens(IEnumerable<TSqlParserToken> tokens)
    {
        foreach (var token in tokens)
        {
            if (token.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile))
            {
                yield return (token.TokenType, token.Text);
            }
        }
    }
}
