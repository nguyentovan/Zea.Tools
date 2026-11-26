using Antlr4.Runtime;
using static Zea.SqlFormat.Parsing.SQLStandardLexer;

namespace Zea.SqlFormat.Parsing;

// Port of the common SELECT, JOIN and WITH grammar sequences from SQLFluff
// 4.5.0's ANSI dialect. DBeaver supplies the dialect-aware tokens and the
// full parse when a sequence cannot consume the entire script.
internal sealed class SqlFluffSelectParser(IReadOnlyList<IToken> tokens, string dialect)
{
    private int _position;

    public SqlSegment? Match()
    {
        if (dialect is not ("tsql" or "postgres" or "mysql" or "oracle"))
        {
            return null;
        }

        var file = new List<SqlSegment>();
        ReadTrivia(file);
        while (_position < tokens.Count)
        {
            SqlSegment? query;
            if (Next() == SELECT)
            {
                query = ReadSelect();
            }
            else if (dialect == "postgres" && Next() == WITH)
            {
                query = ReadWith();
            }
            else
            {
                return null;
            }

            if (query == null)
            {
                return null;
            }

            var separator = Next();
            if (separator is not (Semicolon or TokenConstants.EOF))
            {
                return null;
            }

            var statement = new List<SqlSegment> { query };
            if (dialect == "tsql" && separator == Semicolon)
            {
                Take(statement, "statement_terminator");
            }

            file.Add(Group("statement", statement));
            if (dialect != "tsql" && separator == Semicolon)
            {
                Take(file, "statement_terminator");
            }

            ReadTrivia(file);
        }

        var result = Group("file", file);
        return result;
    }

    private SqlSegment? ReadSelect()
    {
        var select = new List<SqlSegment>();
        Take(select, "keyword");
        while (true)
        {
            if (Next() == Asterisk)
            {
                Take(select, "star");
            }
            else
            {
                if (!ReadValue(select, allowWildcard: true))
                {
                    return null;
                }

                if (dialect == "postgres" && Next() == TypeCast)
                {
                    Take(select, "casting_operator");
                    if (Next() != Identifier || !string.Equals(Peek()!.Text, "int", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    ReadTrivia(select);
                    var type = new List<SqlSegment>();
                    Take(type, "keyword");
                    select.Add(Group("data_type", type));
                }
            }

            if (Next() != Comma)
            {
                break;
            }

            Take(select, "comma");
        }

        var query = new List<SqlSegment> { Group("select_clause", select) };
        if (Next() == FROM)
        {
            ReadTrivia(query);
            var from = ReadFrom();
            if (from == null)
            {
                return null;
            }

            query.Add(from);
        }

        if (Next() == WHERE)
        {
            if (query[^1].Kind != "from_clause")
            {
                return null;
            }

            ReadTrivia(query);
            var where = ReadCondition("where_clause");
            if (where == null)
            {
                return null;
            }

            query.Add(where);
        }

        if (Next() == LIMIT && dialect is "postgres" or "mysql")
        {
            ReadTrivia(query);
            var limit = new List<SqlSegment>();
            Take(limit, "keyword");
            if (Next() != UnsignedInteger)
            {
                return null;
            }

            Take(limit, "numeric_literal");
            query.Add(Group("limit_clause", limit));
        }

        var result = Group("select_statement", query);
        return result;
    }

    private SqlSegment? ReadWith()
    {
        var compound = new List<SqlSegment>();
        Take(compound, "keyword");
        ReadTrivia(compound);
        var cte = new List<SqlSegment>();
        if (Next() != Identifier)
        {
            return null;
        }

        Take(cte, "naked_identifier");
        if (Next() != AS)
        {
            return null;
        }

        Take(cte, "keyword");
        ReadTrivia(cte);

        var body = new List<SqlSegment>();
        if (Next() != LeftParen)
        {
            return null;
        }

        Take(body, "start_bracket");
        ReadTrivia(body);
        if (Next() != SELECT)
        {
            return null;
        }

        var inner = ReadSelect();
        if (inner == null || Next() != RightParen)
        {
            return null;
        }

        body.AddRange(inner.Parts);
        Take(body, "end_bracket");
        cte.Add(Group("bracketed", body));
        compound.Add(Group("common_table_expression", cte));

        var main = new List<SqlSegment>();
        ReadTrivia(main);
        if (Next() != SELECT)
        {
            return null;
        }

        var outer = ReadSelect();
        if (outer == null)
        {
            return null;
        }

        main.Add(outer);
        compound.Add(Group("query_statement", main));
        var result = Group("with_compound_statement", compound);
        return result;
    }

    private SqlSegment? ReadFrom()
    {
        var from = new List<SqlSegment>();
        Take(from, "keyword");
        ReadTrivia(from);
        var table = ReadTable();
        if (table == null)
        {
            return null;
        }

        from.Add(table);
        while (Next() is JOIN or INNER or LEFT or RIGHT)
        {
            ReadTrivia(from);
            var join = new List<SqlSegment>();
            if (Next() is LEFT or RIGHT)
            {
                Take(join, "keyword");
                if (Next() == OUTER)
                {
                    Take(join, "keyword");
                }
            }
            else if (Next() == INNER)
            {
                Take(join, "keyword");
            }

            if (Next() != JOIN)
            {
                return null;
            }

            Take(join, "keyword");
            ReadTrivia(join);
            var right = ReadTable();
            if (right == null || Next() != ON)
            {
                return null;
            }

            join.Add(right);
            ReadTrivia(join);
            var condition = ReadCondition("join_on_condition");
            if (condition == null)
            {
                return null;
            }

            join.Add(condition);
            from.Add(Group("join_clause", join));
        }

        var result = Group("from_clause", from);
        return result;
    }

    private SqlSegment? ReadTable()
    {
        var table = new List<SqlSegment>();
        if (!ReadReference(table))
        {
            return null;
        }

        var element = new List<SqlSegment> { Group("table_reference", table) };
        if (Next() is AS or Identifier)
        {
            ReadTrivia(element);
            var alias = new List<SqlSegment>();
            if (Next() == AS)
            {
                if (dialect == "oracle")
                {
                    return null;
                }

                Take(alias, "keyword");
            }
            else if (dialect == "postgres" && !SQLStandardParser.AllowsImplicitCorrelationName(dialect, Peek()!.Text))
            {
                return null;
            }

            if (Next() != Identifier)
            {
                return null;
            }

            Take(alias, "naked_identifier");
            element.Add(Group("alias_expression", alias));
        }

        var result = Group("from_expression_element", element);
        return result;
    }

    private SqlSegment? ReadCondition(string kind)
    {
        var clause = new List<SqlSegment>();
        Take(clause, "keyword");
        ReadTrivia(clause);
        var expression = new List<SqlSegment>();
        if (!ReadReference(expression) || Next() != EqualsOperator)
        {
            return null;
        }

        ReadTrivia(expression);
        var comparison = new List<SqlSegment>();
        Take(comparison, "comparison_operator");
        expression.Add(Group("comparison_operator", comparison));
        if (!ReadValue(expression))
        {
            return null;
        }

        clause.Add(Group("expression", expression));
        var result = Group(kind, clause);
        return result;
    }

    private bool ReadValue(List<SqlSegment> destination, bool allowWildcard = false)
    {
        switch (Next())
        {
            case UnsignedInteger:
            case DecimalLiteral:
                Take(destination, "numeric_literal");
                return true;
            case StringLiteralContent:
                Take(destination, "quoted_literal");
                return true;
            default:
                var matched = ReadReference(destination, allowWildcard);
                return matched;
        }
    }

    private bool ReadReference(List<SqlSegment> destination, bool allowWildcard = false)
    {
        if (Next() != Identifier)
        {
            return false;
        }

        Take(destination, "naked_identifier");
        var parts = 1;
        while (Next() == Period)
        {
            if (parts == 3)
            {
                return false;
            }

            Take(destination, "dot");
            if (allowWildcard && Next() == Asterisk)
            {
                Take(destination, "star");
                return true;
            }

            if (Next() != Identifier)
            {
                return false;
            }

            Take(destination, "naked_identifier");
            parts++;
        }

        return true;
    }

    private int Next()
    {
        var type = Peek()?.Type ?? TokenConstants.EOF;
        return type;
    }

    private IToken? Peek()
    {
        var index = _position;
        while (index < tokens.Count && IsTrivia(index))
        {
            index++;
        }

        if (index == tokens.Count)
        {
            return null;
        }

        return tokens[index];
    }

    private void Take(List<SqlSegment> destination, string kind)
    {
        ReadTrivia(destination);
        var segment = new SqlSegment(kind, tokens[_position].Text, null);
        destination.Add(segment);
        _position++;
    }

    private void ReadTrivia(List<SqlSegment> destination)
    {
        while (_position < tokens.Count && IsTrivia(_position))
        {
            var token = tokens[_position];
            if (token.Type == Separator)
            {
                SqlSegment.AppendWhitespace(destination, token.Text);
            }
            else
            {
                destination.Add(new SqlSegment("block_comment", token.Text, null));
            }

            _position++;
        }
    }

    private bool IsTrivia(int index)
    {
        var token = tokens[index];
        if (token.Type == Separator)
        {
            return true;
        }

        if (dialect != "tsql" || token.Type != Comment)
        {
            return false;
        }

        var text = token.Text.AsSpan();
        if (!text.StartsWith("/*", StringComparison.Ordinal) || text.IndexOfAny('[', '\r', '\n') >= 0)
        {
            return false;
        }

        // The full parser attaches comments adjacent to terminators and GO
        // differently. Keep only simple block comments at clause boundaries here.
        var next = index + 1;
        while (next < tokens.Count && tokens[next].Type == Separator)
        {
            next++;
        }

        var atClauseBoundary = next < tokens.Count && tokens[next].Type is FROM or WHERE;
        return atClauseBoundary;
    }

    private static SqlSegment Group(string kind, List<SqlSegment> children)
    {
        var segment = new SqlSegment(kind, null, children.ToArray());
        return segment;
    }
}
