using Zea.SqlFormat.Formatters.RightWay.ParseStructure;
using static Zea.SqlFormat.Formatters.RightWay.Interfaces.SqlStructureConstants;

namespace Zea.SqlFormat.Formatters.RightWay;

internal sealed partial class RightWayTreeAdapter
{
    private bool TryAppendTsqlClauseLayout(SqlSegment segment, Node parent, string context)
    {
        switch (segment.Kind)
        {
            case "data_type":
                AppendTsqlDataType(segment, parent);
                return true;
            case "select_clause_modifier" when parent.Name == ENAME_SQL_CLAUSE:
                if (segment.Parts.Any(part => IsKeyword(part, "ties")))
                {
                    var clause = parent;
                    foreach (var part in segment.Parts)
                    {
                        if (IsKeyword(part, "with"))
                        {
                            clause = Add(parent, ENAME_SQL_CLAUSE);
                        }

                        Append(part, clause, "legacy_keyword");
                    }

                    return true;
                }

                break;
            case "select_statement" when parent.Name == ENAME_SQL_STATEMENT:
                if (segment.Parts.Any(IsFetchPart))
                {
                    AppendSelectWithFetch(segment, parent);
                    return true;
                }

                break;
            case "for_clause":
                var output = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(segment, output, segment.Kind);
                return true;
            case "xml_namespaces_clause":
                var namespaces = Add(parent, ENAME_CTE_ALIAS);
                AppendParts(segment, namespaces, "xml_namespaces_clause");
                return true;
            case "bracketed" when context == "xml_namespaces_clause":
                AppendBrackets(segment, parent, "create_table_statement");
                return true;
            case "bracketed" when context == "for_clause":
                AppendBrackets(segment, parent, "function_contents");
                return true;
            case "keyword" when context is "legacy_keyword" or "for_clause" or "temporal_query"
                or "xml_namespaces_clause":
            case "keyword" when IsKeyword(segment, "collate"):
                if (segment.Text != null && !SqlServerFormattingKeywords.Contains(segment.Text))
                {
                    Add(parent, ENAME_OTHERNODE, segment.Text);
                    return true;
                }

                break;
            case "function" when segment.Parts.FirstOrDefault(part => !IsTrivia(part)) is SqlSegment functionName:
                var parts = functionName.Parts;
                if (functionName.Kind == "function_name" && parts.Count == 1 &&
                    parts[0].Text is string name && SqlServerFormattingKeywords.Contains(name) && !IsTsqlBuiltInFunction(name))
                {
                    AppendKeywordFunction(segment, parent, functionName, name);
                    return true;
                }

                break;
        }

        return false;
    }

    private void AppendTsqlDataType(SqlSegment segment, Node parent)
    {
        var start = 0;
        if (TryGetCanonicalTsqlDataType(segment, out var canonical, out var afterName))
        {
            Add(parent, ENAME_DATATYPE_KEYWORD, canonical);
            start = afterName;
        }

        var qualified = segment.Parts.Any(part => part.Kind == "dot");
        foreach (var part in segment.Parts.Skip(start))
        {
            if (qualified && IsTsqlDataTypeNamePart(part) && part.Text is string name)
            {
                Add(parent, ENAME_OTHERNODE, name);
                continue;
            }

            Append(part, parent, segment.Kind);
        }
    }

    private static IEnumerable<string> StandardizedTsqlDataTypeTokens(SqlSegment segment)
    {
        var start = 0;
        if (TryGetCanonicalTsqlDataType(segment, out var canonical, out var afterName))
        {
            yield return canonical;
            start = afterName;
        }

        var qualified = segment.Parts.Any(part => part.Kind == "dot");
        foreach (var part in segment.Parts.Skip(start))
        {
            if (qualified && IsTsqlDataTypeNamePart(part) && part.Text is string name)
            {
                yield return name;
                continue;
            }

            foreach (var token in StandardizedTsqlTokens(part))
            {
                yield return token;
            }
        }
    }

    private static bool IsTsqlDataTypeNamePart(SqlSegment part)
    {
        var isName = part.Kind is "naked_identifier" or "quoted_identifier" or "data_type_identifier" or "keyword";
        return isName;
    }

    private static bool TryGetCanonicalTsqlDataType(SqlSegment segment, out string canonical, out int afterName)
    {
        var words = new List<string>();
        afterName = 0;
        canonical = "";
        for (var index = 0; index < segment.Parts.Count; index++)
        {
            var part = segment.Parts[index];
            if (IsWhitespace(part))
            {
                continue;
            }

            if (part.Kind != "keyword" || part.Text is not string word)
            {
                break;
            }

            words.Add(word.ToUpperInvariant());
            afterName = index + 1;
        }

        if (words.Count < 2 || segment.Parts.Skip(afterName).Any(part => !IsTrivia(part) && part.Kind != "bracketed_arguments"))
        {
            return false;
        }

        var spelling = string.Join(" ", words);
        if (!StandardKeywordRemapping.Instance.TryGetValue(spelling, out var replacement) ||
            !SqlServerFormattingKeywords.IsDataType(replacement))
        {
            return false;
        }

        canonical = replacement;
        return true;
    }

    private void AppendKeywordFunction(SqlSegment segment, Node parent, SqlSegment functionName, string name)
    {
        foreach (var part in segment.Parts)
        {
            if (part == functionName)
            {
                Add(parent, ENAME_OTHERKEYWORD, name);
            }
            else if (part.Kind == "function_contents")
            {
                var context = "";
                if (SqlServerFormattingKeywords.IsDataType(name))
                {
                    context = "data_type";
                }

                AppendParts(part, parent, context);
            }
            else
            {
                Append(part, parent, "");
            }
        }
    }

    private static bool IsFetchPart(SqlSegment segment)
    {
        return segment.Kind == "fetch_clause" ||
            segment.Kind == "orderby_clause" && segment.Parts.Any(IsFetchPart);
    }

    private void AppendSelectWithFetch(SqlSegment segment, Node parent)
    {
        var current = parent;
        Node? order = null;
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "orderby_clause")
            {
                order = Add(current, ENAME_SQL_CLAUSE);
                foreach (var item in part.Parts)
                {
                    if (item.Kind == "fetch_clause")
                    {
                        current = AppendFetchStatement(item, parent.Parent!);
                    }
                    else if (item.Kind == "offset_clause")
                    {
                        AppendParts(item, order, item.Kind);
                    }
                    else
                    {
                        Append(item, order, part.Kind);
                    }
                }
            }
            else if (part.Kind == "offset_clause" && order != null)
            {
                AppendParts(part, order, part.Kind);
            }
            else if (part.Kind == "fetch_clause")
            {
                current = AppendFetchStatement(part, parent.Parent!);
            }
            else
            {
                Append(part, current, segment.Kind);
            }
        }
    }

    private Node AppendFetchStatement(SqlSegment segment, Node container)
    {
        var statement = Add(container, ENAME_SQL_STATEMENT);
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        AppendParts(segment, clause, segment.Kind);
        return statement;
    }

    private static bool HasTemporalQuery(SqlSegment segment)
    {
        if (segment.Kind == "temporal_query")
        {
            return true;
        }

        return segment.Kind is "from_expression_element" or "table_expression" &&
            segment.Parts.Any(HasTemporalQuery);
    }

    private static Node? LastStatementClause(Node root)
    {
        var last = root.Children.LastOrDefault(child => child.Name != ENAME_WHITESPACE);
        if (last?.Name != ENAME_SQL_STATEMENT)
        {
            return null;
        }

        var clause = last.Children.LastOrDefault(child => child.Name != ENAME_WHITESPACE);
        if (clause?.Name != ENAME_SQL_CLAUSE)
        {
            return null;
        }

        foreach (var whitespace in root.Children.Reverse().TakeWhile(child => child.Name == ENAME_WHITESPACE).Reverse().ToArray())
        {
            root.RemoveChild(whitespace);
            clause.AddChild(whitespace);
        }

        return clause;
    }
}

