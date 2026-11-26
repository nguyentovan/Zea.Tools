using Zea.SqlFormat.Formatters.RightWay.ParseStructure;
using static Zea.SqlFormat.Formatters.RightWay.Interfaces.SqlStructureConstants;

namespace Zea.SqlFormat.Formatters.RightWay;

internal sealed partial class RightWayTreeAdapter
{
    private bool TryAppendServiceBrokerLayout(SqlSegment segment, Node parent)
    {
        switch (segment.Kind)
        {
            case "begin_dialog_statement":
                AppendBeginDialog(segment, parent);
                return true;
            case "send_statement":
                AppendSend(segment, parent);
                return true;
            case "end_conversation_statement":
            case "begin_conversation_timer_statement":
                var clause = Add(parent, ENAME_SQL_CLAUSE);
                int start = AppendBrokerKeywordPrefix(segment.Parts, clause);
                AppendBrokerRange(segment.Parts, start, clause);
                return true;
            case "waitfor_statement":
                var wait = Add(parent, ENAME_SQL_CLAUSE);
                AppendParts(segment, wait, "service_broker");
                return true;
            case "receive_statement" when parent.Name == ENAME_EXPRESSION_PARENS:
            case "get_conversation_group_statement" when parent.Name == ENAME_EXPRESSION_PARENS:
                AppendParts(segment, parent, "service_broker");
                return true;
            default:
                return false;
        }
    }

    private void AppendBeginDialog(SqlSegment segment, Node statement)
    {
        var current = Add(statement, ENAME_SQL_CLAUSE);
        var parts = segment.Parts;
        int start = AppendBrokerKeywordPrefix(parts, current);
        for (int i = start; i < parts.Count; i++)
        {
            var part = parts[i];
            if (IsKeyword(part, "from") || IsKeyword(part, "with"))
            {
                current = Add(statement, ENAME_SQL_CLAUSE);
                Append(part, current, "service_broker");
            }
            else if (IsKeyword(part, "on"))
            {
                var contract = Add(current, ENAME_JOIN_ON_SECTION);
                var open = Add(contract, ENAME_CONTAINER_OPEN);
                Append(part, open, "service_broker");
                current = Add(contract, ENAME_CONTAINER_GENERALCONTENT);
            }
            else
            {
                Append(part, current, "service_broker");
            }
        }
    }

    private void AppendSend(SqlSegment segment, Node statement)
    {
        var clause = Add(statement, ENAME_SQL_CLAUSE);
        var parts = segment.Parts;
        int start = AppendBrokerKeywordPrefix(parts, clause);
        bool hasHandle = false;
        for (int i = start; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Kind == "bracketed" && hasHandle)
            {
                AppendBrackets(part, clause, "function_contents");
            }
            else
            {
                Append(part, clause, "service_broker");
            }

            if (!IsTrivia(part))
            {
                hasHandle = true;
            }
        }
    }

    private int AppendBrokerKeywordPrefix(IReadOnlyList<SqlSegment> parts, Node parent)
    {
        var keywords = new List<string>();
        int i = 0;
        for (; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Kind == "keyword" && part.Text != null)
            {
                keywords.Add(part.Text);
            }
            else if (!IsTrivia(part))
            {
                break;
            }
        }

        if (keywords.Count == 0)
        {
            return 0;
        }

        var compound = Add(parent, ENAME_COMPOUNDKEYWORD);
        compound.SetAttribute(ANAME_SIMPLETEXT, string.Join(" ", keywords));
        AppendRange(parts, 0, i, compound);
        return i;
    }

    private void AppendBrokerRange(IReadOnlyList<SqlSegment> parts, int start, Node parent)
    {
        for (int i = start; i < parts.Count; i++)
        {
            Append(parts[i], parent, "service_broker");
        }
    }
}
