using Zea.SqlFormat.Formatters.RightWay.ParseStructure;
using static Zea.SqlFormat.Formatters.RightWay.Interfaces.SqlStructureConstants;

namespace Zea.SqlFormat.Formatters.RightWay;

internal sealed partial class RightWayTreeAdapter
{
    private bool TryAppendQueryLayout(SqlSegment segment, Node parent, string context)
    {
        switch (segment.Kind)
        {
            case "from_expression_element":
                AppendTableReferenceLayout(segment, parent);
                return true;
            case "table_expression":
                AppendParts(segment, parent, segment.Kind);
                return true;
            case "alias_column_list":
            case "cube_rollup_clause":
            case "grouping_sets_clause":
                AppendParts(segment, parent, "function");
                return true;
            case "bracketed" when context is "table_expression" or "from_expression_element":
                if (segment.Parts.Any(part => part.Kind == "join_clause"))
                {
                    AppendJoinedTableParens(segment, parent);
                    return true;
                }

                if (segment.Parts.Any(IsDerivedTableContent))
                {
                    AppendBrackets(segment, parent, "cte_body");
                    return true;
                }

                break;
            case "from_pivot_expression":
                AppendParts(segment, parent, segment.Kind);
                return true;
            case "bracketed" when context == "from_pivot_expression":
                AppendPivotArguments(segment, parent);
                return true;
            case "openjson_segment":
            case "openxml_segment":
            case "openquery_segment":
            case "openrowset_segment":
                AppendParts(segment, parent, "function");
                return true;
            case "openjson_with_clause":
            case "openrowset_with_clause":
                AppendParts(segment, parent, "");
                return true;
            case "post_table_expression" when (context is "table_expression" or "from_expression_element") &&
                                              !segment.Parts.Any(part => IsKeyword(part, "with")):
                AppendParts(segment, parent, "function");
                return true;
            case "query_hint_segment":
                return TryAppendQueryHint(segment, parent);
            case "cast_expression" when TryGetStaticTypeMethod(segment, out var receiver, out var call):
                AppendStaticTypeMethod(segment, parent, receiver, call);
                return true;
            case "option_clause":
                var option = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(segment, option, "values_clause");
                return true;
            case "over_clause":
                AppendParts(segment, parent, "");
                return true;
            case "window_specification":
                AppendWindowSpecification(segment, parent);
                return true;
            case "frame_clause":
                AppendExpression(segment, parent, segment.Kind);
                return true;
            case "keyword" when context is "frame_clause" or "function" or "from_pivot_expression":
                if (segment.Text != null && !SqlServerFormattingKeywords.Contains(segment.Text))
                {
                    Add(parent, ENAME_OTHERNODE, segment.Text);
                    return true;
                }

                break;
            case "function_name":
                if (segment.Parts.Count == 1 && segment.Parts[0].Text is string functionName &&
                    segment.Parts[0].Kind == "keyword" && !SqlServerFormattingKeywords.Contains(functionName))
                {
                    Add(parent, ENAME_OTHERNODE, functionName);
                    return true;
                }

                break;
            case "set_operator":
                var setClause = Add(parent, ENAME_SQL_CLAUSE);
                var setOperator = Add(setClause, ENAME_SET_OPERATOR_CLAUSE);
                AppendParts(segment, setOperator, context);
                return true;
            case "select_clause":
                AppendSelectLayout(segment, parent);
                return true;
            case "within_group_clause" when parent.Name == ENAME_SQL_CLAUSE:
                AppendWithinGroupLayout(segment, parent);
                return true;
        }

        return false;
    }

    private void AppendTableReferenceLayout(SqlSegment segment, Node parent)
    {
        var parts = segment.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            if (index > 0 && part.Kind == "alias_expression" && parts[index - 1].Kind == "table_expression" &&
                part.Parts.Count == 1 && part.Parts[0].Kind == "quoted_identifier" &&
                part.Parts[0].Text is { } alias && alias.StartsWith('['))
            {
                // A bracketed alias can be adjacent to the relation. Preserve the original boundary.
                Add(parent, ENAME_RAW_SUFFIX, alias);
            }
            else
            {
                Append(part, parent, segment.Kind);
            }
        }
    }

    private bool TryAppendQueryHint(SqlSegment segment, Node parent)
    {
        var prefix = ReadJoinPrefix(segment.Parts);
        if (prefix.Length == 0)
        {
            return false;
        }

        AppendJoinKeywords(segment.Parts, parent, prefix);
        AppendRange(segment.Parts, prefix.Length, segment.Parts.Count, parent);
        return true;
    }

    private void AppendJoinKeywords(IReadOnlyList<SqlSegment> parts, Node parent, (int Length, string Keywords) prefix)
    {
        var compound = Add(parent, ENAME_COMPOUNDKEYWORD);
        compound.SetAttribute(ANAME_SIMPLETEXT, prefix.Keywords);
        AppendRange(parts, 0, prefix.Length, compound);
    }

    private Node AppendTsqlJoinLayout(SqlSegment segment, Node parent)
    {
        var clause = Add(parent, ENAME_SQL_CLAUSE);
        var parts = segment.Parts;
        var prefix = ReadJoinPrefix(parts);
        if (prefix.Length > 0)
        {
            AppendJoinKeywords(parts, clause, prefix);
        }

        // Right Way renders unparenthesized right-deep joins as sibling clauses.
        // Predicates remain in source order after the most recent sibling.
        for (int index = prefix.Length; index < parts.Count; index++)
        {
            var part = parts[index];
            if (part.Kind == "join_clause")
            {
                clause = AppendTsqlJoinLayout(part, parent);
            }
            else if (part.Kind == "join_on_condition")
            {
                AppendJoinPredicate(part, clause);
            }
            else
            {
                Append(part, clause, segment.Kind);
            }
        }

        return clause;
    }

    private void AppendJoinPredicate(SqlSegment segment, Node parent)
    {
        var condition = Add(parent, ENAME_JOIN_ON_SECTION);
        var open = Add(condition, ENAME_CONTAINER_OPEN);
        var current = open;
        foreach (var part in segment.Parts)
        {
            Append(part, current, segment.Kind);
            if (IsKeyword(part, "on") && current == open)
            {
                current = Add(condition, ENAME_CONTAINER_GENERALCONTENT);
            }
        }
    }

    private void AppendJoinedTableParens(SqlSegment segment, Node parent)
    {
        var brackets = Add(parent, ENAME_SELECTIONTARGET_PARENS);
        var parts = segment.Parts;
        for (int index = 1; index < parts.Count - 1; index++)
        {
            var part = parts[index];
            if (part.Kind == "join_clause")
            {
                AppendTsqlJoinLayout(part, brackets);
            }
            else
            {
                Append(part, brackets, "");
            }
        }
    }

    private static bool TryGetStaticTypeMethod(SqlSegment segment, out SqlSegment receiver, out SqlSegment call)
    {
        receiver = null!;
        call = null!;
        if (segment.Kind != "cast_expression")
        {
            return false;
        }

        // The grammar also recognizes T-SQL's :: invocation as a cast.
        var code = segment.Parts.Where(part => !IsTrivia(part)).ToArray();
        if (code.Length != 3 || code[0].Kind != "column_reference" || code[1].Text != "::" ||
            code[2].Kind != "data_type")
        {
            return false;
        }

        var method = code[2].Parts.Where(part => !IsTrivia(part)).ToArray();
        if (method.Length != 2 || method[0].Kind is not ("data_type_identifier" or "keyword") ||
            method[0].Text == null || method[1].Kind != "bracketed_arguments")
        {
            return false;
        }

        receiver = code[0];
        call = code[2];
        return true;
    }

    private static SqlSegment? GetKnownStaticTypeName(SqlSegment receiver)
    {
        if (receiver.Parts.Count != 1 || receiver.Parts[0].Kind != "naked_identifier")
        {
            return null;
        }

        var identifier = receiver.Parts[0];
        var canonical = identifier.Text?.ToUpperInvariant();
        if (canonical is "HIERARCHYID" or "GEOMETRY" or "GEOGRAPHY")
        {
            return identifier;
        }

        return null;
    }

    private void AppendStaticTypeMethod(SqlSegment segment, Node parent, SqlSegment receiver, SqlSegment call)
    {
        var knownType = GetKnownStaticTypeName(receiver);
        foreach (var part in segment.Parts)
        {
            if (part == receiver && knownType != null)
            {
                Add(parent, ENAME_DATATYPE_KEYWORD, knownType.Text);
            }
            else if (part == call)
            {
                foreach (var methodPart in call.Parts)
                {
                    if (methodPart.Kind is "data_type_identifier" or "keyword")
                    {
                        // A SQL Server static method is an identifier with case-sensitive spelling.
                        Add(parent, ENAME_OTHERNODE, methodPart.Text);
                    }
                    else if (methodPart.Kind == "bracketed_arguments")
                    {
                        AppendParts(methodPart, parent, "function");
                    }
                    else
                    {
                        Append(methodPart, parent, "");
                    }
                }
            }
            else
            {
                Append(part, parent, "");
            }
        }
    }

    private static IEnumerable<string> StandardizedStaticTypeMethodTokens(
        SqlSegment segment, SqlSegment receiver, SqlSegment call)
    {
        var knownType = GetKnownStaticTypeName(receiver);
        foreach (var part in segment.Parts)
        {
            if (part == receiver && knownType?.Text is string knownTypeName)
            {
                yield return knownTypeName.ToUpperInvariant();
            }
            else if (part == call)
            {
                foreach (var methodPart in call.Parts)
                {
                    if (methodPart.Kind is "data_type_identifier" or "keyword" && methodPart.Text != null)
                    {
                        yield return methodPart.Text;
                    }
                    else
                    {
                        foreach (var token in StandardizedTsqlTokens(methodPart))
                        {
                            yield return token;
                        }
                    }
                }
            }
            else
            {
                foreach (var token in StandardizedTsqlTokens(part))
                {
                    yield return token;
                }
            }
        }
    }

    private static bool IsDerivedTableContent(SqlSegment segment)
    {
        var isTableContent = segment.Kind is "select_statement" or "set_expression" or "with_compound_statement" or
            "values_clause" or "table_expression" or "from_expression" or "insert_statement" or "update_statement" or
            "delete_statement" or "merge_statement";
        return isTableContent;
    }

    private void AppendPivotArguments(SqlSegment segment, Node parent)
    {
        var arguments = Add(parent, ENAME_FUNCTION_PARENS);
        var parts = segment.Parts;
        int last = parts.Count - 1;
        for (int i = 1; i < last; i++)
        {
            i = AppendPart(parts, i, arguments, "", last);
        }
    }

    private void AppendWindowSpecification(SqlSegment segment, Node parent)
    {
        var current = parent;
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "orderby_clause")
            {
                Append(part, parent, segment.Kind);
                current = parent.Children.Last();
            }
            else
            {
                Append(part, current, segment.Kind);
            }
        }
    }

    private void AppendSelectLayout(SqlSegment segment, Node parent)
    {
        var select = Add(parent, ENAME_SQL_CLAUSE);
        AppendParts(segment, select, segment.Kind);

        if (!select.Children.Any(child => child.Name == ENAME_SQL_CLAUSE))
        {
            return;
        }

        // Right Way keeps WITHIN at the end of the current clause and starts a
        // new clause at GROUP. Move the following projection tokens with it.
        var current = select;
        foreach (var child in select.Children.ToArray())
        {
            if (child.Name == ENAME_SQL_CLAUSE)
            {
                select.RemoveChild(child);
                parent.AddChild(child);
                current = child;
            }
            else if (current != select)
            {
                select.RemoveChild(child);
                current.AddChild(child);
            }
        }
    }

    private void AppendWithinGroupLayout(SqlSegment segment, Node parent)
    {
        var current = parent;
        foreach (var part in segment.Parts)
        {
            if (IsKeyword(part, "group"))
            {
                current = Add(parent, ENAME_SQL_CLAUSE);
            }

            Append(part, current, "");
        }
    }
}
