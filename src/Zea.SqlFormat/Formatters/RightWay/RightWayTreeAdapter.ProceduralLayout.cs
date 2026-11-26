using Zea.SqlFormat.Formatters.RightWay.ParseStructure;
using static Zea.SqlFormat.Formatters.RightWay.Interfaces.SqlStructureConstants;

namespace Zea.SqlFormat.Formatters.RightWay;

internal sealed partial class RightWayTreeAdapter
{
    private bool TryAppendProceduralLayout(SqlSegment segment, Node parent, string context)
    {
        if (segment.Text is string text && IsProceduralLayoutKeyword(segment.Kind, text))
        {
            var nodeName = ENAME_OTHERKEYWORD;
            if (segment.Kind == "system_variable")
            {
                nodeName = ENAME_FUNCTION_KEYWORD;
            }
            else if (segment.Kind == "action_parameter")
            {
                nodeName = ENAME_PSEUDONAME;
            }

            Add(parent, nodeName, text);
            return true;
        }

        if (parent.Name == ENAME_MERGE_WHEN && IsKeyword(segment, "and"))
        {
            var conjunction = Add(parent, ENAME_AND_OPERATOR);
            Add(conjunction, ENAME_OTHERKEYWORD, segment.Text);
            return true;
        }

        if (segment.Kind == "bulk_insert_with_segment")
        {
            AppendBulkInsertOptions(segment, parent);
            return true;
        }

        if (parent.Name != ENAME_SQL_STATEMENT)
        {
            return false;
        }

        switch (segment.Kind)
        {
            case "grant_statement":
            case "deny_statement":
            case "revoke_statement":
                AppendPermissions(segment, parent);
                return true;
            case "declare_segment" when HasNamedCursorQuery(segment):
                AppendCursorDeclaration(segment, parent);
                return true;
            case "declare_segment" when segment.Parts.Any(part => IsKeyword(part, "table")):
                AppendTableDeclaration(segment, parent);
                return true;
            case "set_local_variable_segment" when segment.Parts.Any(IsCursorDefinitionWithQuery):
                AppendCursorAssignment(segment, parent);
                return true;
            case "bulk_insert_statement":
                AppendSplitProceduralClauses(segment, parent, "from");
                return true;
            case "fetch_cursor_statement":
                AppendSplitProceduralClauses(segment, parent, "from", "into");
                return true;
            case "raiserror_statement":
                AppendSplitProceduralClauses(segment, parent, "with");
                return true;
            case "open_cursor_statement":
            case "close_cursor_statement":
            case "deallocate_cursor_statement":
            case "transaction_statement":
            case "print_statement":
            case "throw_statement":
                var clause = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(segment, clause, context);
                return true;
            default:
                return false;
        }
    }

    private static bool IsProceduralLayoutKeyword(string kind, string text)
    {
        var usesKeywordCase = kind == "action_parameter" ||
            (kind == "system_variable" && SqlServerFormattingKeywords.IsBuiltInFunction(text));
        return usesKeywordCase;
    }

    private void AppendSplitProceduralClauses(SqlSegment segment, Node statement, params string[] boundaries)
    {
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "keyword" && boundaries.Any(keyword => IsKeyword(part, keyword)))
            {
                clause = Add(statement, ENAME_SQL_CLAUSE);
            }

            Append(part, clause, segment.Kind);
        }
    }

    private static bool HasNamedCursorQuery(SqlSegment segment)
    {
        var declaration = segment.Parts.FirstOrDefault(part => !IsTrivia(part) && !IsKeyword(part, "declare"));
        var hasNamedCursor = declaration?.Kind is "naked_identifier" or "quoted_identifier";
        var hasDirectCursorQuery = segment.Parts.Any(part => IsKeyword(part, "cursor")) &&
            segment.Parts.Any(IsCursorQuery);
        var hasCursorQuery = hasNamedCursor &&
            (hasDirectCursorQuery || segment.Parts.Any(IsCursorDefinitionWithQuery));
        return hasCursorQuery;
    }

    private void AppendCursorDeclaration(SqlSegment segment, Node statement)
    {
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var declaration = Add(clause, ENAME_CURSOR_DECLARATION);
        var body = declaration;
        foreach (var part in CursorParts(segment))
        {
            if (IsKeyword(part, "for"))
            {
                var section = ENAME_CURSOR_FOR_BLOCK;
                if (body != declaration)
                {
                    section = ENAME_CURSOR_FOR_OPTIONS;
                }

                var forBlock = Add(declaration, section);
                var open = Add(forBlock, ENAME_CONTAINER_OPEN);
                Append(part, open, segment.Kind);
                body = Add(forBlock, ENAME_CONTAINER_GENERALCONTENT);
            }
            else if (IsCursorQuery(part))
            {
                var query = Add(body, ENAME_SQL_STATEMENT);
                Append(part, query, segment.Kind);
            }
            else
            {
                Append(part, body, segment.Kind);
            }
        }
    }

    private static IEnumerable<SqlSegment> CursorParts(SqlSegment segment)
    {
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "cursor_definition")
            {
                foreach (var cursorPart in CursorParts(part))
                {
                    yield return cursorPart;
                }
            }
            else if (IsCursorQuery(part) &&
                part.Parts.LastOrDefault(child => !IsTrivia(child)) is { Kind: "for_clause" } options &&
                options.Parts.Any(child => IsKeyword(child, "read") || IsKeyword(child, "update")))
            {
                // The ISO declaration can place FOR READ ONLY on the SELECT
                // in the parse tree. It is a cursor option for the layout.
                var queryParts = part.Parts.TakeWhile(child => !ReferenceEquals(child, options)).ToArray();
                yield return part with { Children = queryParts };

                var optionIndex = queryParts.Length;
                foreach (var option in options.Parts)
                {
                    yield return option;
                }

                foreach (var trailing in part.Parts.Skip(optionIndex + 1))
                {
                    yield return trailing;
                }
            }
            else
            {
                yield return part;
            }
        }
    }

    private static bool IsCursorDefinitionWithQuery(SqlSegment segment)
    {
        var hasQuery = segment.Kind == "cursor_definition" && segment.Parts.Any(IsCursorQuery);
        return hasQuery;
    }

    private static bool IsCursorQuery(SqlSegment segment)
    {
        var isQuery = segment.Kind is "select_statement" or "set_expression" or "with_compound_statement";
        return isQuery;
    }

    private void AppendCursorAssignment(SqlSegment segment, Node statement)
    {
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var currentStatement = statement;
        var statementContainer = statement.Parent!;
        foreach (var part in segment.Parts)
        {
            if (part.Kind != "cursor_definition")
            {
                Append(part, clause, segment.Kind);
                continue;
            }

            foreach (var cursorPart in CursorParts(part))
            {
                if (IsCursorQuery(cursorPart))
                {
                    // The cursor query is part of the SET in the parse tree. Right Way
                    // gives it its own statement layout after the cursor's FOR.
                    currentStatement = Add(statementContainer, ENAME_SQL_STATEMENT);
                    Append(cursorPart, currentStatement, part.Kind);
                    clause = currentStatement.Children.LastOrDefault(child => child.Name == ENAME_SQL_CLAUSE) ?? currentStatement;
                    continue;
                }

                if (IsKeyword(cursorPart, "update") && currentStatement != statement)
                {
                    currentStatement = Add(statementContainer, ENAME_SQL_STATEMENT);
                    clause = Add(currentStatement, ENAME_SQL_CLAUSE);
                }
                else if (IsKeyword(cursorPart, "for"))
                {
                    clause = Add(currentStatement, ENAME_SQL_CLAUSE);
                }

                Append(cursorPart, clause, part.Kind);
            }
        }
    }

    private void AppendTableDeclaration(SqlSegment segment, Node statement)
    {
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var declaration = Add(clause, ENAME_DDL_DECLARE_BLOCK);
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "bracketed")
            {
                AppendBrackets(part, declaration, "create_table_statement");
            }
            else
            {
                Append(part, declaration, segment.Kind);
            }
        }
    }

    private void AppendBulkInsertOptions(SqlSegment segment, Node parent)
    {
        foreach (var part in segment.Parts)
        {
            if (part.Kind != "bracketed")
            {
                Append(part, parent, segment.Kind);
                continue;
            }

            var bracket = Add(parent, ENAME_EXPRESSION_PARENS);
            foreach (var option in part.Parts.Skip(1).SkipLast(1))
            {
                if (option.Kind == "keyword" && option.Text is string keyword &&
                    !SqlServerFormattingKeywords.Contains(keyword))
                {
                    // The legacy word list excludes contextual options such as
                    // field terminators. Their spelling belongs to the input.
                    Add(bracket, ENAME_OTHERNODE, keyword);
                    continue;
                }

                Append(option, bracket, segment.Kind);
            }
        }
    }

    private void AppendPermissions(SqlSegment segment, Node statement)
    {
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var permissions = Add(clause, ENAME_PERMISSIONS_BLOCK);
        var open = Add(permissions, ENAME_CONTAINER_OPEN);
        var current = open;
        foreach (var part in segment.Parts)
        {
            if (part.Kind == "permissions_segment")
            {
                current = Add(permissions, ENAME_PERMISSIONS_DETAIL);
            }
            else if (IsKeyword(part, "on") || IsKeyword(part, "to") || IsKeyword(part, "from"))
            {
                var name = ENAME_PERMISSIONS_RECIPIENT;
                if (IsKeyword(part, "on"))
                {
                    name = ENAME_PERMISSIONS_TARGET;
                }

                var section = Add(permissions, name);
                var sectionOpen = Add(section, ENAME_CONTAINER_OPEN);
                Append(part, sectionOpen, segment.Kind);
                current = Add(section, ENAME_CONTAINER_GENERALCONTENT);
                continue;
            }
            else if (IsKeyword(part, "with") || IsKeyword(part, "cascade"))
            {
                current = Add(permissions, ENAME_DDL_WITH_CLAUSE);
            }

            Append(part, current, segment.Kind);
        }
    }

    private bool TryAppendClosingBlockTrivia(
        IReadOnlyList<SqlSegment> parts, int start, int stop, Node parent, string context)
    {
        if (parent.Name != ENAME_CONTAINER_MULTISTATEMENT ||
            parent.Parent?.Name is not (ENAME_BEGIN_END_BLOCK or ENAME_TRY_BLOCK or ENAME_CATCH_BLOCK))
        {
            return false;
        }

        var clause = FindClosingBlockClause(parent);
        if (clause == null)
        {
            return false;
        }

        // No following statement claims these comments before END. Right Way
        // keeps them at the indentation of the last clause in the open body.
        for (int i = start; i < stop; i++)
        {
            Append(parts[i], clause, context);
        }

        return true;
    }

    private static Node? FindClosingBlockClause(Node body)
    {
        var statement = body.Children.LastOrDefault(child => child.Name == ENAME_SQL_STATEMENT);
        var clause = statement?.Children.LastOrDefault(child => child.Name == ENAME_SQL_CLAUSE);
        if (clause == null)
        {
            return null;
        }

        var content = clause.Children.LastOrDefault(child =>
            child.Name != ENAME_SEMICOLON && !ENAMELIST_NONCONTENT.Contains(child.Name));
        if (content?.Name is ENAME_IF_STATEMENT or ENAME_WHILE_LOOP)
        {
            var branch = content.Children.LastOrDefault(child => child.Name == ENAME_ELSE_CLAUSE) ?? content;
            var statements = branch.Children.LastOrDefault(child => child.Name == ENAME_CONTAINER_SINGLESTATEMENT);
            if (statements != null)
            {
                var branchClause = FindClosingBlockClause(statements);
                if (branchClause != null)
                {
                    return branchClause;
                }
            }
        }

        var destination = FindProceduralTerminatorTarget(clause);
        return destination;
    }

    private static Node FindProceduralTerminatorTarget(Node parent)
    {
        var contents = parent.Children.LastOrDefault(child => child.Name != ENAME_WHITESPACE);
        if (contents?.Name == ENAME_CURSOR_DECLARATION)
        {
            var options = contents.ChildByName(ENAME_CURSOR_FOR_OPTIONS)?.ChildByName(ENAME_CONTAINER_GENERALCONTENT);
            if (options != null)
            {
                return options;
            }

            var forBlock = contents.ChildByName(ENAME_CURSOR_FOR_BLOCK);
            var body = forBlock?.ChildByName(ENAME_CONTAINER_GENERALCONTENT);
            var query = body?.Children.LastOrDefault(child => child.Name == ENAME_SQL_STATEMENT);
            var queryClause = query?.Children.LastOrDefault(child => child.Name == ENAME_SQL_CLAUSE);
            if (queryClause != null)
            {
                return queryClause;
            }
        }
        else if (contents?.Name == ENAME_DDL_DECLARE_BLOCK)
        {
            return contents;
        }
        else if (contents?.Name == ENAME_PERMISSIONS_BLOCK)
        {
            var lastSection = contents.Children.LastOrDefault();
            if (lastSection?.Name is ENAME_PERMISSIONS_TARGET or ENAME_PERMISSIONS_RECIPIENT)
            {
                var body = lastSection.ChildByName(ENAME_CONTAINER_GENERALCONTENT);
                if (body != null)
                {
                    return body;
                }
            }
            else if (lastSection?.Name == ENAME_DDL_WITH_CLAUSE)
            {
                return lastSection;
            }
        }

        return parent;
    }
}

