using Zea.SqlFormat.Formatters.RightWay.ParseStructure;
using static Zea.SqlFormat.Formatters.RightWay.Interfaces.SqlStructureConstants;

namespace Zea.SqlFormat.Formatters.RightWay;

internal sealed partial class RightWayTreeAdapter
{
    private bool TryAppendDdlLayout(SqlSegment segment, Node parent, string context)
    {
        switch (segment.Kind)
        {
            case "create_table_statement":
            case "alter_table_statement":
            case "create_index_statement":
            case "create_queue_statement":
                AppendSchemaDdl(segment, parent);
                return true;
            case "create_view_statement":
            case "create_function_statement":
            case "create_procedure_statement":
            case "create_trigger":
                AppendDdlDefinition(segment, parent);
                return true;
            case "drop_table_statement":
            case "drop_procedure_statement":
            case "truncate_table":
            case "return_segment":
            case "set_segment":
            case "goto_statement":
                var clause = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(segment, clause, context);
                return true;
            case "label_segment":
                var labelClause = Add(parent, ENAME_SQL_CLAUSE);
                Add(labelClause, ENAME_LABEL, segment.Raw());
                return true;
            case "identity_grammar":
                AppendParts(segment, parent, "function");
                return true;
            case "references_constraint_grammar" when context != "alter_table_statement":
                AppendParts(segment, parent, "data_type");
                return true;
            case "keyword" when context is "ddl_options" or "service_broker" &&
                segment.Text != null && !SqlServerFormattingKeywords.Contains(segment.Text):
                Add(parent, ENAME_OTHERNODE, segment.Text);
                return true;
            case "bracketed" when context is "ddl_options" or "service_broker":
                AppendLayoutOptionList(segment, parent, context);
                return true;
            case "column_constraint_segment":
                AppendColumnConstraint(segment, parent);
                return true;
            case "statement_terminator":
            case "semicolon":
                return TryAppendDdlTerminator(segment, parent);
            default:
                return TryAppendServiceBrokerLayout(segment, parent);
        }
    }

    private void AppendSchemaDdl(SqlSegment segment, Node statement)
    {
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var definition = Add(clause, ENAME_DDL_OTHER_BLOCK);
        if (segment.Kind == "alter_table_statement" && statement.Parent != null)
        {
            AppendAlterTableParts(segment, statement.Parent, definition);
            return;
        }

        foreach (var part in segment.Parts)
        {
            if (part.Kind == "where_clause")
            {
                clause = Add(statement, ENAME_SQL_CLAUSE);
                AppendParts(part, clause, "where_clause");
                definition = clause;
            }
            else if (part.Kind == "relational_index_options")
            {
                if (definition.Name == ENAME_DDL_OTHER_BLOCK)
                {
                    definition = AppendDdlOptions(part, definition);
                }
                else
                {
                    clause = Add(statement, ENAME_SQL_CLAUSE);
                    AppendParts(part, clause, "ddl_options");
                    definition = clause;
                }
            }
            else if (segment.Kind == "create_queue_statement" && IsKeyword(part, "with"))
            {
                definition = OpenDdlOptions(definition, part);
            }
            else if (definition.Name == ENAME_CONTAINER_GENERALCONTENT)
            {
                Append(part, definition, "ddl_options");
            }
            else if (part.Kind == "bracketed_index_column_list_grammar")
            {
                AppendParts(part, definition, "create_table_statement");
            }
            else if (segment.Kind == "create_index_statement" && part.Text != null &&
                part.Kind == "keyword" && !SqlServerFormattingKeywords.Contains(part.Text))
            {
                Add(definition, ENAME_OTHERNODE, part.Text);
            }
            else
            {
                var context = segment.Kind;
                if (segment.Kind == "create_index_statement")
                {
                    context = "create_table_statement";
                }

                Append(part, definition, context);
            }
        }
    }

    private Node AppendDdlOptions(SqlSegment segment, Node definition)
    {
        var current = definition;
        foreach (var part in segment.Parts)
        {
            if (current == definition && IsKeyword(part, "with"))
            {
                current = OpenDdlOptions(definition, part);
            }
            else
            {
                Append(part, current, "ddl_options");
            }
        }

        return current;
    }

    private Node OpenDdlOptions(Node parent, SqlSegment with)
    {
        var options = Add(parent, ENAME_DDL_WITH_CLAUSE);
        var header = Add(options, ENAME_CONTAINER_OPEN);
        Append(with, header, "ddl_options");
        var body = Add(options, ENAME_CONTAINER_GENERALCONTENT);
        return body;
    }

    private void AppendLayoutOptionList(SqlSegment segment, Node parent, string context)
    {
        var parts = segment.Parts;
        if (parts.Count < 2 || parts[0].Text != "(" || parts[^1].Text != ")")
        {
            AppendParts(segment, parent, context);
            return;
        }

        var bracket = Add(parent, ENAME_EXPRESSION_PARENS);
        for (int i = 1; i < parts.Count - 1; i++)
        {
            Append(parts[i], bracket, context);
        }
    }

    private Node AppendAlterTableParts(SqlSegment segment, Node statementScope, Node current)
    {
        SqlSegment? previous = null;
        foreach (var part in segment.Parts)
        {
            if (part.Kind is "table_constraint" or "references_constraint_grammar")
            {
                current = AppendAlterTableParts(part, statementScope, current);
            }
            else
            {
                // Right Way gives a top-level referential action the same visual
                // separation as a new statement. Its tokens remain in the parsed statement.
                if (segment.Kind == "references_constraint_grammar" && previous != null &&
                    IsKeyword(previous, "on") &&
                    (IsKeyword(part, "delete") || IsKeyword(part, "update")))
                {
                    var continuation = Add(statementScope, ENAME_SQL_STATEMENT);
                    current = Add(continuation, ENAME_SQL_CLAUSE);
                }

                Append(part, current, "create_table_statement");
            }

            if (!IsTrivia(part))
            {
                previous = part;
            }
        }

        return current;
    }

    private void AppendColumnConstraint(SqlSegment segment, Node parent)
    {
        var first = segment.Parts.FirstOrDefault(part => !IsTrivia(part));
        if (first != null && IsKeyword(first, "default"))
        {
            AppendParts(segment, parent, "data_type");
        }
        else
        {
            AppendParts(segment, parent, context: "");
        }
    }

    private void AppendDdlDefinition(SqlSegment segment, Node statement)
    {
        var parts = segment.Parts;
        int start = 0;
        if (statement.Parent != null)
        {
            var prefix = parts.Select((part, index) => (Part: part, Index: index))
                .Where(item => !IsTrivia(item.Part)).Take(3).ToArray();
            if (prefix.Length == 3 && IsKeyword(prefix[0].Part, "create") &&
                IsKeyword(prefix[1].Part, "or") && IsKeyword(prefix[2].Part, "alter"))
            {
                var prefixClause = Add(statement, ENAME_SQL_CLAUSE);
                var prefixDdl = Add(prefixClause, ENAME_DDL_OTHER_BLOCK);
                AppendRange(parts, 0, prefix[1].Index, prefixDdl);
                var or = Add(prefixDdl, ENAME_OR_OPERATOR);
                Append(prefix[1].Part, or, "");
                AppendRange(parts, prefix[1].Index + 1, prefix[2].Index, prefixDdl);
                start = prefix[2].Index;
                statement = Add(statement.Parent, ENAME_SQL_STATEMENT);
            }
        }

        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var definition = Add(clause, ENAME_DDL_PROCEDURAL_BLOCK);
        Node current = definition;
        Node? body = null;
        for (int i = start; i < parts.Count; i++)
        {
            var part = parts[i];
            if (body != null)
            {
                AppendDdlBodyPart(part, body);
            }
            else if (IsDdlBodyIntroducer(segment, i))
            {
                var asBlock = Add(definition, ENAME_DDL_AS_BLOCK);
                var open = Add(asBlock, ENAME_CONTAINER_OPEN);
                Append(part, open, context: "");
                body = Add(asBlock, ENAME_CONTAINER_GENERALCONTENT);
            }
            else if (segment.Kind == "create_procedure_statement" && IsKeyword(part, "with"))
            {
                current = Add(definition, ENAME_DDL_WITH_CLAUSE);
                Append(part, current, context: "");
            }
            else if (segment.Kind == "create_trigger" &&
                (IsKeyword(part, "after") || IsKeyword(part, "for") || IsKeyword(part, "instead")))
            {
                current = Add(definition, ENAME_TRIGGER_CONDITION);
                Append(part, current, context: "");
            }
            else if (IsKeyword(part, "returns"))
            {
                var returns = Add(definition, ENAME_DDL_RETURNS);
                Append(part, returns, context: "");
            }
            else if (part.Kind is "function_parameter_list" or "procedure_parameter_list")
            {
                AppendParts(part, current, "create_table_statement");
            }
            else
            {
                Append(part, current, context: "");
            }
        }
    }

    private static bool IsDdlBodyIntroducer(SqlSegment segment, int index)
    {
        if (!IsKeyword(segment.Parts[index], "as"))
        {
            return false;
        }

        var next = segment.Parts.Skip(index + 1).FirstOrDefault(part => !IsTrivia(part));
        var isBody = next?.Kind is "procedure_statement" or "statement" or "select_statement"
            or "set_expression" or "with_compound_statement";
        return isBody;
    }

    private void AppendDdlBodyPart(SqlSegment part, Node body)
    {
        if (part.Kind == "procedure_statement")
        {
            foreach (var child in part.Parts)
            {
                AppendDdlBodyPart(child, body);
            }
        }
        else if (part.Kind is "statement_terminator" or "semicolon" &&
            TryAppendDdlBodyTerminator(part, body))
        {
            return;
        }
        else if (part.Kind is "select_statement" or "set_expression" or "with_compound_statement")
        {
            var statement = Add(body, ENAME_SQL_STATEMENT);
            Append(part, statement, context: "");
        }
        else
        {
            Append(part, body, context: "");
        }
    }

    private bool TryAppendDdlTerminator(SqlSegment segment, Node parent)
    {
        if (segment.Text != ";" || parent.Name is not
            (ENAME_SQL_ROOT or ENAME_CONTAINER_MULTISTATEMENT or ENAME_CONTAINER_SINGLESTATEMENT or ENAME_CONTAINER_GENERALCONTENT))
        {
            return false;
        }

        var statement = parent.Children.LastOrDefault(node => node.Name != ENAME_WHITESPACE);
        if (statement?.Name != ENAME_SQL_STATEMENT)
        {
            return false;
        }

        var definition = statement.Children.LastOrDefault(node => node.Name != ENAME_WHITESPACE)
            ?.ChildByName(ENAME_DDL_PROCEDURAL_BLOCK);
        var body = definition?.ChildByName(ENAME_DDL_AS_BLOCK)?.ChildByName(ENAME_CONTAINER_GENERALCONTENT);
        if (body == null)
        {
            return false;
        }

        var appended = TryAppendDdlBodyTerminator(segment, body);
        return appended;
    }

    private static bool TryAppendDdlBodyTerminator(SqlSegment segment, Node body)
    {
        var finalStatement = body.Children.LastOrDefault(node => node.Name != ENAME_WHITESPACE);
        if (finalStatement?.Name != ENAME_SQL_STATEMENT)
        {
            return false;
        }

        var clause = finalStatement.Children.LastOrDefault(node => node.Name != ENAME_WHITESPACE);
        if (clause?.Name != ENAME_SQL_CLAUSE)
        {
            return false;
        }

        Add(clause, ENAME_SEMICOLON, segment.Text);
        return true;
    }
}
