/*
Poor Man's T-SQL Formatter - a small free Transact-SQL formatting 
library for .Net 2.0 and JS, written in C#. 
Copyright (C) 2011-2017 Tao Klerks

Additional Contributors:
 * Timothy Klenke, 2012

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Zea.SqlFormat.Formatters.RightWay.Interfaces;
using Zea.SqlFormat.Formatters.RightWay.ParseStructure;

namespace Zea.SqlFormat.Formatters.RightWay
{
    internal sealed partial class RightWayFormatter
    {
        public RightWayFormatter() : this(new RightWayFormatterOptions()) { }
        
        public RightWayFormatter(RightWayFormatterOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            Options = options;

            if (options.KeywordStandardization)
                KeywordMapping = StandardKeywordRemapping.Instance;
            ErrorOutputPrefix = MessagingConstants.FormatErrorDefaultMessage + Environment.NewLine;
        }

        public RightWayFormatterOptions Options { get; private set; }

        public IDictionary<string, string> KeywordMapping = new Dictionary<string, string>();

        public bool HTMLFormatted { get { return Options.HTMLColoring; } }
        public string ErrorOutputPrefix { get; set; }

        // Recursion-depth counter for ProcessSqlNode; reset per FormatSQLTree call so a
        // formatter instance reused across calls (and one that aborted mid-walk) starts clean.
        private int _nodeRecursionDepth;

        // Reentrancy guard for the post-pass fixed-point reformat (see FormatSQLTree): the
        // single extra pass must not itself trigger another, so the whole thing is bounded
        // to exactly two formats.
        private bool _inReformatPass;

        internal string FormatSQLTree(Node sqlTreeDoc, Func<string, Node> reparse)
        {
            _nodeRecursionDepth = 0;
            // Output goes into a per-call state object, but a few bookkeeping flags
            // (_nodeRecursionDepth, _inReformatPass) live on the formatter INSTANCE, so a
            // single formatter is single-threaded: format concurrently only on separate
            // instances (the CLI is one-per-process; each test constructs its own; SSMS/VS Code
            // format one document at a time). Reused sequentially across calls is fine - both
            // flags reset here at entry.
            FormattingState state = new FormattingState(Options.HTMLColoring, Options.IndentString, Options.SpacesPerTab, Options.MaxLineWidth, 0);

            if (sqlTreeDoc.Name == SqlStructureConstants.ENAME_SQL_ROOT && sqlTreeDoc.GetAttributeValue(SqlStructureConstants.ANAME_ERRORFOUND) == "1")
                state.AddOutputContent(ErrorOutputPrefix);

            ProcessSqlNodeList(sqlTreeDoc.Children, state);

            WhiteSpace_BreakAsExpected(state);

            //someone forgot to close a "[noformat]" or "[minify]" region... we'll assume that's ok
            if (state.SpecialRegionActive is SpecialRegionType regionType)
            {
                var regionContent = RenderSpecialRegion(regionType, state.RegionStartNode, sqlTreeDoc);
                if (regionContent != null)
                {
                    state.AddOutputContentRaw(regionContent);
                }
            }
            string output = state.DumpOutput();
            string preText = output;

            output = ApplyTextLayout(output);

            // Fixed-point guarantee. A text post-pass can LENGTHEN a line that the core
            // tree-walk already wrapped (it inserts `as`/`ColumnAlias_N = `), so the core's
            // re-wrap of that line only settles on the NEXT format pass -
            // format(format(x)) != format(x) for otherwise-cosmetic layout drift. When the
            // post-passes actually changed the output, reformat once so the emitted result
            // IS that fixed point (the corpus has no period-3+ cycle, so one extra pass
            // suffices - verified corpus-wide). Guarded so the DEFAULT profile, whose gated
            // passes never run and whose output is therefore unchanged here, never reformats
            // and stays byte-for-byte identical at zero extra cost; and bounded by
            // _inReformatPass to a single extra format.
            if (!_inReformatPass && !ReferenceEquals(output, preText) && output != preText)
            {
                _inReformatPass = true;
                try
                {
                    var reparsed = reparse(output);
                    output = FormatSQLTree(reparsed, reparse);
                }
                finally { _inReformatPass = false; }
            }

            return output;
        }

        private string? RenderSpecialRegion(SpecialRegionType regionType, Node? start, Node end)
        {
            var region = NodeExtensions.ExtractStructureBetween(start, end);
            if (region == null)
            {
                return null;
            }

            if (regionType == SpecialRegionType.NoFormat)
            {
                var identityFormatter = new SqlIdentityFormatter(Options.HTMLColoring);
                var verbatim = FormatNoFormatRegion(identityFormatter, region, start);
                return verbatim;
            }

            var minifyFormatter = new SqlObfuscatingFormatter();
            var content = minifyFormatter.FormatSQLTree(region);
            if (HTMLFormatted)
            {
                content = Utils.HtmlEncode(content)!;
            }

            return content;
        }

        private static bool IsClosingSpecialRegion(SpecialRegionType regionType, string comment)
        {
            var marker = "[/MINIFY]";
            if (regionType == SpecialRegionType.NoFormat)
            {
                marker = "[/NOFORMAT]";
            }

            var isClosing = comment.ToUpperInvariant().Contains(marker);
            return isClosing;
        }

        private static string FormatNoFormatRegion(SqlIdentityFormatter formatter, Node region, Node? start)
        {
            var content = formatter.FormatSQLTree(region);
            if (start?.Name is not (SqlStructureConstants.ENAME_COMMENT_SINGLELINE or
                SqlStructureConstants.ENAME_COMMENT_SINGLELINE_CSTYLE))
            {
                return content;
            }

            // Right Way writes a line break after the opening comment. The parse tree
            // may retain that original line break as a separate trivia node.
            if (content.StartsWith("\r\n", StringComparison.Ordinal))
            {
                return content[2..];
            }

            if (content.StartsWith('\n') || content.StartsWith('\r'))
            {
                return content[1..];
            }

            return content;
        }

        private void ProcessSqlNodeList(IEnumerable<Node> rootList, FormattingState state)
        {
            foreach (Node contentElement in rootList)
                ProcessSqlNode(contentElement, state);
        }

        /// <summary>
        /// Returns a helper that knows whether a node is inside a DDL column list
        /// (i.e. a DDL_PARENS or DDLDETAIL_PARENS context).
        /// </summary>
        private static bool IsInsideDdlDetailParens(Node node)
        {
            var p = node.Parent;
            while (p != null)
            {
                if (p.Name == SqlStructureConstants.ENAME_DDL_PARENS
                    || p.Name == SqlStructureConstants.ENAME_DDLDETAIL_PARENS)
                    return true;
                if (p.Name == SqlStructureConstants.ENAME_SQL_ROOT ||
                    p.Name == SqlStructureConstants.ENAME_SQL_STATEMENT)
                    return false;
                p = p.Parent;
            }
            return false;
        }

        /// <summary>
        /// True when a DDLParens node actually contains column/parameter DEFINITIONS
        /// (CREATE/ALTER object bodies, DECLARE @t TABLE) — the only shapes
        /// AlignColumnDefinitionsInDDL may rewrite. INSERT/VALUES/OPTION parens and CTE
        /// column-name lists share the element name but hold arbitrary content.
        /// </summary>
        private static bool IsColumnDefinitionParens(Node parensNode)
        {
            string? parentName = parensNode.Parent?.Name;
            return parentName == SqlStructureConstants.ENAME_DDL_PROCEDURAL_BLOCK
                || parentName == SqlStructureConstants.ENAME_DDL_OTHER_BLOCK
                || parentName == SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK;
        }

        private void ProcessSqlNode(Node contentElement, FormattingState state)
        {
            // Stop descending past MAX_NESTING_DEPTH so pathologically nested input degrades
            // gracefully instead of overflowing the stack (the parser has already flagged such
            // a tree as a parse error). No output is emitted for the truncated subtree; nothing
            // below this point touched indent/state yet, so an early return leaves state intact.
            if (++_nodeRecursionDepth > SqlStructureConstants.MAX_NESTING_DEPTH)
            {
                _nodeRecursionDepth--;
                return;
            }

            int initialIndent = state.IndentLevel;

            if (contentElement.GetAttributeValue(SqlStructureConstants.ANAME_HASERROR) == "1")
                state.OpenClass(SqlHtmlConstants.CLASS_ERRORHIGHLIGHT);

            switch (contentElement.Name)
            {
                case SqlStructureConstants.ENAME_SQL_STATEMENT:
                    //a LEADING empty statement (the implicit statement before a GO that opens
                    // the file) renders nothing - otherwise it emits a stray blank line and
                    // files starting with a batch separator reformat differently every pass.
                    // Trailing/mid-file empty statements keep their historical rendering.
                    if (contentElement.PreviousSibling() == null
                        && !StatementHasRenderableContent(contentElement))
                        break;
                    WhiteSpace_SeparateStatements(contentElement, state);
                    state.ResetKeywords();
                    ProcessSqlNodeList(contentElement.Children, state);
                    state.StatementBreakExpected = true;
                    break;

                case SqlStructureConstants.ENAME_SQL_CLAUSE:
                    state.UnIndentInitialBreak = true;
                    ProcessSqlNodeList(contentElement.Children, state.IncrementIndent());
                    state.DecrementIndent();
                    if (Options.NewClauseLineBreaks > 0)
                        state.BreakExpected = true;
                    if (Options.NewClauseLineBreaks > 1)
                        state.AdditionalBreaksExpected = Options.NewClauseLineBreaks - 1;
                    break;

                case SqlStructureConstants.ENAME_SET_OPERATOR_CLAUSE:
                    state.DecrementIndent();
                    state.WhiteSpace_BreakToNextLine(); //this is the one already recommended by the start of the clause
                    state.WhiteSpace_BreakToNextLine(); //this is the one we additionally want to apply
                    ProcessSqlNodeList(contentElement.Children, state.IncrementIndent());
                    state.BreakExpected = true;
                    state.AdditionalBreaksExpected = 1;
                    break;

                case SqlStructureConstants.ENAME_BATCH_SEPARATOR:
                    //newline regardless of whether previous element recommended a break or not
                    // - unless nothing has been output yet (GO as the file's first statement),
                    // where a break would just emit a stray leading blank line.
                    if (state.CurrentLineHasContent || state.OutputContainsLineBreak)
                        state.WhiteSpace_BreakToNextLine();
                    else
                        state.BreakExpected = false;
                    ProcessSqlNodeList(contentElement.Children, state);
                    state.BreakExpected = true;
                    break;

                case SqlStructureConstants.ENAME_DDL_PROCEDURAL_BLOCK:
                case SqlStructureConstants.ENAME_DDL_OTHER_BLOCK:
                case SqlStructureConstants.ENAME_DDL_DECLARE_BLOCK:
                case SqlStructureConstants.ENAME_CURSOR_DECLARATION:
                case SqlStructureConstants.ENAME_BEGIN_TRANSACTION:
                case SqlStructureConstants.ENAME_SAVE_TRANSACTION:
                case SqlStructureConstants.ENAME_COMMIT_TRANSACTION:
                case SqlStructureConstants.ENAME_ROLLBACK_TRANSACTION:
                case SqlStructureConstants.ENAME_CONTAINER_OPEN:
                case SqlStructureConstants.ENAME_CONTAINER_CLOSE:
                case SqlStructureConstants.ENAME_WHILE_LOOP:
                case SqlStructureConstants.ENAME_IF_STATEMENT:
                case SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT:
                case SqlStructureConstants.ENAME_CTE_WITH_CLAUSE:
                case SqlStructureConstants.ENAME_PERMISSIONS_BLOCK:
                case SqlStructureConstants.ENAME_PERMISSIONS_DETAIL:
                case SqlStructureConstants.ENAME_MERGE_CLAUSE:
                case SqlStructureConstants.ENAME_MERGE_TARGET:
                    ProcessSqlNodeList(contentElement.Children, state);
                    break;

                case SqlStructureConstants.ENAME_SELECTIONTARGET:
                    ProcessSqlNodeList(contentElement.Children, state);
                    break;

                case SqlStructureConstants.ENAME_CASE_INPUT:
                case SqlStructureConstants.ENAME_BOOLEAN_EXPRESSION:
                case SqlStructureConstants.ENAME_BETWEEN_LOWERBOUND:
                case SqlStructureConstants.ENAME_BETWEEN_UPPERBOUND:
                    WhiteSpace_SeparateWords(state);
                    ProcessSqlNodeList(contentElement.Children, state);
                    break;

                case SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT:
                case SqlStructureConstants.ENAME_CONTAINER_MULTISTATEMENT:
                case SqlStructureConstants.ENAME_MERGE_ACTION:

                    bool singleStatementIsIf = false;
                    foreach (Node statement in contentElement.ChildrenByName(SqlStructureConstants.ENAME_SQL_STATEMENT))
                        foreach (Node clause in statement.ChildrenByName(SqlStructureConstants.ENAME_SQL_CLAUSE))
                            foreach (Node ifStatement in clause.ChildrenByName(SqlStructureConstants.ENAME_IF_STATEMENT))
                                singleStatementIsIf = true;

					//CompactSingleStatementBlocks: render a single-statement IF/ELSE/WHILE body
					// inline on the same line when it stays single-line and fits MaxLineWidth.
					if (Options.CompactSingleStatementBlocks
						&& contentElement.Name.Equals(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT)
						&& !singleStatementIsIf
						&& IsSmallSubtree(contentElement, 60)
						)
					{
						FormattingState compactState = new FormattingState(state);
						compactState.BreakExpected = false;
						compactState.StatementBreakExpected = false;
						compactState.WordSeparatorExpected = false;
						compactState.SourceBreakPending = false;
						ProcessSqlNodeList(contentElement.Children, compactState);
						if (!compactState.OutputContainsLineBreak
							&& state.CurrentLineLength + 1 + compactState.CurrentLineLength <= Options.MaxLineWidth
							)
						{
							state.WordSeparatorExpected = true;
							WhiteSpace_SeparateWords(state);
							state.Assimilate(compactState);
							state.WordSeparatorExpected = false;
							//mirror the standard exit: the OUTER statement owns statement
							// separation; anything following (e.g. ELSE) starts on a new line
							state.BreakExpected = true;
							state.StatementBreakExpected = false;
							state.UnIndentInitialBreak = false;
							break;
						}
					}

					if (singleStatementIsIf && contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_ELSE_CLAUSE))
					{
						//artificially decrement indent and skip new statement break for "ELSE IF" constructs
						state.DecrementIndent();
					}
					else
					{
						state.BreakExpected = true;
					}
                    ProcessSqlNodeList(contentElement.Children, state);
					if (singleStatementIsIf && contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_ELSE_CLAUSE))
					{
						//bring indent back to symmetrical level
						state.IncrementIndent();
					}

					state.StatementBreakExpected = false; //the responsibility for breaking will be with the OUTER statement; there should be no consequence propagating out from statements in this container;
                    state.UnIndentInitialBreak = false; //if there was no word spacing after the last content statement's clause starter, doesn't mean the unIndent should propagate to the following content!
                    break;

                case SqlStructureConstants.ENAME_PERMISSIONS_TARGET:
                case SqlStructureConstants.ENAME_PERMISSIONS_RECIPIENT:
                case SqlStructureConstants.ENAME_DDL_WITH_CLAUSE:
                case SqlStructureConstants.ENAME_MERGE_CONDITION:
                case SqlStructureConstants.ENAME_MERGE_THEN:
                    state.BreakExpected = true;
                    state.UnIndentInitialBreak = true;
                    ProcessSqlNodeList(contentElement.Children, state.IncrementIndent());
                    state.DecrementIndent();
                    break;

                case SqlStructureConstants.ENAME_JOIN_ON_SECTION:
                    if (Options.BreakJoinOnSections)
                        state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state);
                    if (Options.BreakJoinOnSections)
                        state.IncrementIndent();
                    if (Options.IndentJoinOnClause)
                    {
                        state.BreakExpected = true;
                        state.IncrementIndent();
                    }
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT), state);
                    if (Options.IndentJoinOnClause)
                        state.DecrementIndent();
                    if (Options.BreakJoinOnSections)
                        state.DecrementIndent();
                    break;

                case SqlStructureConstants.ENAME_CTE_ALIAS:
                    state.UnIndentInitialBreak = true;
                    ProcessSqlNodeList(contentElement.Children, state);
                    break;

                case SqlStructureConstants.ENAME_ELSE_CLAUSE:
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state.DecrementIndent());
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT), state.IncrementIndent());
                    break;

                case SqlStructureConstants.ENAME_DDL_AS_BLOCK:
                case SqlStructureConstants.ENAME_CURSOR_FOR_BLOCK:
                    state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state.DecrementIndent());
                    state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT), state);
                    state.IncrementIndent();
                    break;

                case SqlStructureConstants.ENAME_TRIGGER_CONDITION:
                    state.DecrementIndent();
                    state.WhiteSpace_BreakToNextLine();
                    ProcessSqlNodeList(contentElement.Children, state.IncrementIndent());
                    break;

                case SqlStructureConstants.ENAME_CURSOR_FOR_OPTIONS:
                case SqlStructureConstants.ENAME_CTE_AS_BLOCK:
                    state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state.DecrementIndent());
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT), state.IncrementIndent());
                    break;

                case SqlStructureConstants.ENAME_DDL_RETURNS:
                case SqlStructureConstants.ENAME_MERGE_USING:
                case SqlStructureConstants.ENAME_MERGE_WHEN:
                    state.BreakExpected = true;
                    state.UnIndentInitialBreak = true;
                    ProcessSqlNodeList(contentElement.Children, state);
                    break;

                case SqlStructureConstants.ENAME_BETWEEN_CONDITION:
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state);
                    state.IncrementIndent();
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_BETWEEN_LOWERBOUND), state.IncrementIndent());
                    if (Options.ExpandBetweenConditions)
                        state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_CLOSE), state.DecrementIndent());
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_BETWEEN_UPPERBOUND), state.IncrementIndent());
                    state.DecrementIndent();
                    state.DecrementIndent();
                    break;

                case SqlStructureConstants.ENAME_DDLDETAIL_PARENS:
                case SqlStructureConstants.ENAME_FUNCTION_PARENS:
					//simply process sub-nodes - don't add space or expect any linebreaks (but respect linebreaks if necessary)
                    state.WordSeparatorExpected = false;
                    WhiteSpace_BreakAsExpected(state);
                    state.AddOutputContent(FormatOperator("("), SqlHtmlConstants.CLASS_OPERATOR);
                    ProcessSqlNodeList(contentElement.Children, state.IncrementIndent());
                    state.DecrementIndent();
                    WhiteSpace_BreakAsExpected(state);
                    state.AddOutputContent(FormatOperator(")"), SqlHtmlConstants.CLASS_OPERATOR);
                    //a source line-break INSIDE the (collapsed) argument list must not leak out:
                    // it would push a trailing comment after the call onto its own line, which
                    // re-attaches the comment to the next clause on reformat (indent drift /
                    // non-idempotent output). A break AFTER the call sets this flag again anyway.
                    state.SourceBreakPending = false;
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_DDL_PARENS:
                case SqlStructureConstants.ENAME_EXPRESSION_PARENS:
                case SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS:
				case SqlStructureConstants.ENAME_IN_PARENS:
                    // End modifier zone unless this is the TOP (N) argument — EXPRESSION_PARENS
                    // immediately after TOP stays on the SELECT line.
                    if (state.InSelectModifierZone)
                    {
                        if (contentElement.Name == SqlStructureConstants.ENAME_EXPRESSION_PARENS
                            && state.TopArgumentExpected)
                        {
                            state.TopArgumentExpected = false;
                        }
                        else
                        {
                            state.InSelectModifierZone = false;
                            state.TopArgumentExpected = false;
                            state.BreakExpected = true;
                        }
                    }
					WhiteSpace_SeparateWords(state);
					if (contentElement.Name.Equals(SqlStructureConstants.ENAME_EXPRESSION_PARENS) || contentElement.Name.Equals(SqlStructureConstants.ENAME_IN_PARENS))
                        state.IncrementIndent();
                    state.AddOutputContent(FormatOperator("("), SqlHtmlConstants.CLASS_OPERATOR);
                    FormattingState innerState = new FormattingState(state);
                    ProcessSqlNodeList(contentElement.Children, innerState);
                    //if there was a linebreak in the parens content, or if it wanted one to follow, then put linebreaks before and after.
                    if (innerState.BreakExpected || innerState.OutputContainsLineBreak)
                    {
                        if (!innerState.StartsWithBreak)
                            state.WhiteSpace_BreakToNextLine();
                        // Apply DDL column alignment post-processing if enabled.
                        // NOTE: the parser reuses ENAME_DDL_PARENS for INSERT column lists,
                        // VALUES tuples, and OPTION(...) hints (layout convenience) — those
                        // contain arbitrary expressions/literals, NOT "name type" pairs, and
                        // aligning them pads INSIDE strings and function calls. Only parens
                        // whose parent is a real DDL block hold column/parameter definitions.
                        if (Options.AlignColumnDefinitionsInDDL
                            && contentElement.Name == SqlStructureConstants.ENAME_DDL_PARENS
                            && IsColumnDefinitionParens(contentElement))
                        {
                            state.AddOutputContentRaw(AlignDdlColumns(innerState.DumpOutput()));
                        }
                        else
                        {
                            state.Assimilate(innerState);
                        }
                        state.WhiteSpace_BreakToNextLine();
                    }
                    else
                    {
                        state.Assimilate(innerState);
                    }
                    state.AddOutputContent(FormatOperator(")"), SqlHtmlConstants.CLASS_OPERATOR);
                    if (contentElement.Name.Equals(SqlStructureConstants.ENAME_EXPRESSION_PARENS) || contentElement.Name.Equals(SqlStructureConstants.ENAME_IN_PARENS))
                        state.DecrementIndent();
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_BEGIN_END_BLOCK:
                case SqlStructureConstants.ENAME_TRY_BLOCK:
                case SqlStructureConstants.ENAME_CATCH_BLOCK:
                    if (contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_SQL_CLAUSE)
                        && contentElement.Parent!.Parent!.Name.Equals(SqlStructureConstants.ENAME_SQL_STATEMENT)
                        && contentElement.Parent!.Parent!.Parent!.Name.Equals(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT)
                        )
                        state.DecrementIndent();
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state);
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_MULTISTATEMENT), state);
                    state.DecrementIndent();
                    state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_CLOSE), state);
                    state.IncrementIndent();
                    if (contentElement.Parent.Name.Equals(SqlStructureConstants.ENAME_SQL_CLAUSE)
                        && contentElement.Parent!.Parent!.Name.Equals(SqlStructureConstants.ENAME_SQL_STATEMENT)
                        && contentElement.Parent!.Parent!.Parent!.Name.Equals(SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT)
                        )
                        state.IncrementIndent();
                    break;

                case SqlStructureConstants.ENAME_CASE_STATEMENT:
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state);
                    state.IncrementIndent();
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CASE_INPUT), state);
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CASE_WHEN), state);
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CASE_ELSE), state);
                    if (Options.ExpandCaseStatements)
                        state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_CLOSE), state);
                    state.DecrementIndent();
                    break;

                case SqlStructureConstants.ENAME_CASE_WHEN:
                case SqlStructureConstants.ENAME_CASE_THEN:
                case SqlStructureConstants.ENAME_CASE_ELSE:
                    if (Options.ExpandCaseStatements)
                        state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_OPEN), state);
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT), state.IncrementIndent());
                    ProcessSqlNodeList(contentElement.ChildrenByName(SqlStructureConstants.ENAME_CASE_THEN), state);
                    state.DecrementIndent();
                    break;

                case SqlStructureConstants.ENAME_AND_OPERATOR:
                case SqlStructureConstants.ENAME_OR_OPERATOR:
                    //IndentWhereAndOrConditions promises "AND/OR onto separate lines", so it
                    // forces the break even when ExpandBooleanExpressions is disabled.
                    if (Options.ExpandBooleanExpressions || Options.IndentWhereAndOrConditions)
                        state.BreakExpected = true;
                    if (Options.IndentWhereAndOrConditions)
                        state.IncrementIndent();
                    ProcessSqlNode(contentElement.ChildByName(SqlStructureConstants.ENAME_OTHERKEYWORD)!, state);
                    if (Options.IndentWhereAndOrConditions)
                        state.DecrementIndent();
                    break;

                case SqlStructureConstants.ENAME_COMMENT_MULTILINE:
                    if (state.SpecialRegionActive is SpecialRegionType blockCommentRegion &&
                        IsClosingSpecialRegion(blockCommentRegion, contentElement.TextValue!))
                    {
                        var regionContent = RenderSpecialRegion(blockCommentRegion, state.RegionStartNode, contentElement);
                        if (!string.IsNullOrEmpty(regionContent))
                        {
                            state.EmitSpecialRegionContent(regionContent);
                        }
                        else if (state.RegionStartedInline && !state.SourceBreakPending)
                        {
                            state.WordSeparatorExpected = true;
                        }

                        state.SpecialRegionActive = null;
                        state.RegionStartNode = null;
                    }

                    bool followsContent = state.CurrentLineHasContent && !state.SourceBreakPending;
                    // A multi-line comment that started at column 0 in the source keeps
                    // column 0: indenting only its FIRST line (subsequent lines are
                    // emitted verbatim) breaks box-art banner alignment (upstream #99).
                    bool keepCommentAtColumnZero = state.SpecialRegionActive == null
                        && contentElement.TextValue != null
                        && contentElement.TextValue.Contains('\n')
                        && CommentStartsAtSourceColumnZero(contentElement);
                    if (keepCommentAtColumnZero)
                        state.OmitIndentOnFlushedBreaks = true;
                    WhiteSpace_SeparateComment(contentElement, state);
                    state.AddOutputContent("/*" + contentElement.TextValue + "*/", SqlHtmlConstants.CLASS_COMMENT);
                    state.OmitIndentOnFlushedBreaks = false;
                    if (contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_SQL_STATEMENT)
                        || CommentFollowedBySourceBreak(contentElement)
                        )
                        //if this block comment is at the start or end of a statement, or if it was followed by a 
                        // linebreak before any following content, then break here.
                        state.BreakExpected = true;
                    else
                    {
                        state.WordSeparatorExpected = true;
                    }

                    if (state.SpecialRegionActive == null && contentElement.TextValue!.ToUpperInvariant().Contains("[NOFORMAT]"))
                    {
                        //state.AddOutputLineBreak();
                        state.SpecialRegionActive = SpecialRegionType.NoFormat;
                        state.RegionStartNode = contentElement;
                        state.RegionStartedInline = followsContent;
                    }
                    else if (state.SpecialRegionActive == null && contentElement.TextValue!.ToUpperInvariant().Contains("[MINIFY]"))
                    {
                        //state.AddOutputLineBreak();
                        state.SpecialRegionActive = SpecialRegionType.Minify;
                        state.RegionStartNode = contentElement;
                        state.RegionStartedInline = followsContent;
                    }
                    break;

                case SqlStructureConstants.ENAME_RAW_SINGLELINE_COMMENT:
                    WhiteSpace_SeparateComment(contentElement, state);
                    state.AddOutputContent(contentElement.TextValue!, SqlHtmlConstants.CLASS_COMMENT);
                    state.BreakExpected = true;
                    state.SourceBreakPending = true;
                    break;

                case SqlStructureConstants.ENAME_COMMENT_SINGLELINE:
                case SqlStructureConstants.ENAME_COMMENT_SINGLELINE_CSTYLE:
                    if (state.SpecialRegionActive is SpecialRegionType lineCommentRegion &&
                        IsClosingSpecialRegion(lineCommentRegion, contentElement.TextValue!))
                    {
                        var regionContent = RenderSpecialRegion(lineCommentRegion, state.RegionStartNode, contentElement);
                        if (regionContent != null)
                        {
                            state.EmitSpecialRegionContent(regionContent);
                        }

                        state.SpecialRegionActive = null;
                        state.RegionStartNode = null;
                    }

                    WhiteSpace_SeparateComment(contentElement, state);
                    state.AddOutputContent((contentElement.Name == SqlStructureConstants.ENAME_COMMENT_SINGLELINE ? "--" : "//") + contentElement.TextValue!.Replace("\r", "").Replace("\n", ""), SqlHtmlConstants.CLASS_COMMENT);
                    state.BreakExpected = true;
                    state.SourceBreakPending = true;

                    if (state.SpecialRegionActive == null && contentElement.TextValue.ToUpperInvariant().Contains("[NOFORMAT]"))
                    {
                        state.AddOutputLineBreak();
                        state.SpecialRegionActive = SpecialRegionType.NoFormat;
                        state.RegionStartNode = contentElement;
                        state.RegionStartedInline = false;
                    }
                    else if (state.SpecialRegionActive == null && contentElement.TextValue.ToUpperInvariant().Contains("[MINIFY]"))
                    {
                        state.AddOutputLineBreak();
                        state.SpecialRegionActive = SpecialRegionType.Minify;
                        state.RegionStartNode = contentElement;
                        state.RegionStartedInline = false;
                    }
                    break;

                case SqlStructureConstants.ENAME_STRING:
                case SqlStructureConstants.ENAME_NSTRING:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    // Empty string literals ('') are usually escaped quotes inside a
                    // dynamic-SQL FRAGMENT being formatted on its own. When the source
                    // had no whitespace between such a literal and a neighboring
                    // word/value token, preserve that adjacency verbatim - injecting a
                    // space would change the text the user pastes back inside the outer
                    // string (upstream #200: ''.txt'' became ''.txt ''). Keyword and
                    // comma spacing is unaffected (those are not word/value tokens).
                    bool isEmptyStringLiteral = string.IsNullOrEmpty(contentElement.TextValue);
                    if (isEmptyStringLiteral
                        && !state.BreakExpected && state.AdditionalBreaksExpected == 0
                        && IsAdjacencyPreservingToken(contentElement.PreviousSibling(), includeStrings: true))
                        state.WordSeparatorExpected = false;
                    WhiteSpace_SeparateWords(state);
                    string? outValue = null;
                    if (contentElement.Name.Equals(SqlStructureConstants.ENAME_NSTRING))
                        outValue = "N'" + contentElement.TextValue!.Replace("'", "''") + "'";
                    else
                        outValue = "'" + contentElement.TextValue!.Replace("'", "''") + "'";
                    state.AddOutputContent(outValue, SqlHtmlConstants.CLASS_STRING);
                    state.WordSeparatorExpected = !(isEmptyStringLiteral
                        && IsAdjacencyPreservingToken(contentElement.NextSibling(), includeStrings: true));
                    break;

                case SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    // RemoveHarmlessBrackets (upstream #133, opt-in): strip brackets when
                    // the name provably doesn't need them - a valid regular identifier,
                    // not in the keyword list (keeps [Order]/[definition] safe from
                    // reinterpretation and keyword-uppercasing on reformat), and not
                    // directly adjacent to a token it would merge with.
                    bool stripBrackets = Options.RemoveHarmlessBrackets
                        && IsHarmlessUnbracketableName(contentElement.TextValue!)
                        && !IsTokenMergeRisk(contentElement.PreviousSibling())
                        && !IsTokenMergeRisk(contentElement.NextSibling());
                    // Preserve source adjacency between words and bracket names
                    // ("table_[some_id]" templating - upstream #240). Keywords and
                    // commas keep their usual spacing; whitespace in the source is
                    // its own sibling node, so adjacency here means "no gap written".
                    if (!stripBrackets
                        && !state.BreakExpected && state.AdditionalBreaksExpected == 0
                        && IsAdjacencyPreservingToken(contentElement.PreviousSibling(), includeStrings: false))
                        state.WordSeparatorExpected = false;
                    WhiteSpace_SeparateWords(state);
                    state.AddOutputContent(stripBrackets
                        ? contentElement.TextValue!
                        : "[" + contentElement.TextValue!.Replace("]", "]]") + "]");
                    state.WordSeparatorExpected = stripBrackets
                        || !IsAdjacencyPreservingToken(contentElement.NextSibling(), includeStrings: false);
                    break;

                case SqlStructureConstants.ENAME_QUOTED_STRING:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    WhiteSpace_SeparateWords(state);
                    state.AddOutputContent("\"" + contentElement.TextValue!.Replace("\"", "\"\"") + "\"");
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_COMMA:
                    //comma always ignores requested word spacing
                    if (Options.TrailingCommas)
                    {
                        WhiteSpace_BreakAsExpected(state);
                        state.AddOutputContent(FormatOperator(","), SqlHtmlConstants.CLASS_OPERATOR);

                        if ((Options.ExpandCommaLists
								&& !(contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_DDLDETAIL_PARENS)
									|| contentElement.Parent.Name.Equals(SqlStructureConstants.ENAME_FUNCTION_PARENS)
									|| contentElement.Parent.Name.Equals(SqlStructureConstants.ENAME_IN_PARENS)
									|| IsCompactRaiserrorArgs(contentElement)
									)
								)
							|| (Options.ExpandInLists
								&& contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_IN_PARENS)
								)
							)
                            state.BreakExpected = true;
                        else
                            state.WordSeparatorExpected = true;
                    }
                    else
                    {
                        if ((Options.ExpandCommaLists
								&& !(contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_DDLDETAIL_PARENS)
									|| contentElement.Parent.Name.Equals(SqlStructureConstants.ENAME_FUNCTION_PARENS)
									|| contentElement.Parent.Name.Equals(SqlStructureConstants.ENAME_IN_PARENS)
									|| IsCompactRaiserrorArgs(contentElement)
									)
								)
							|| (Options.ExpandInLists
								&& contentElement.Parent!.Name.Equals(SqlStructureConstants.ENAME_IN_PARENS)
								)
							)
                        {
                            state.WhiteSpace_BreakToNextLine();
                            state.AddOutputContent(FormatOperator(","), SqlHtmlConstants.CLASS_OPERATOR);
                            if (Options.SpaceAfterExpandedComma)
                                state.WordSeparatorExpected = true;
                        }
                        else
                        {
                            WhiteSpace_BreakAsExpected(state);
                            state.AddOutputContent(FormatOperator(","), SqlHtmlConstants.CLASS_OPERATOR);
                            state.WordSeparatorExpected = true;
                        }

                    }
                    break;

                case SqlStructureConstants.ENAME_RAW_SUFFIX:
                    state.WordSeparatorExpected = false;
                    WhiteSpace_BreakAsExpected(state);
                    state.AddOutputContent(contentElement.TextValue!);
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_PERIOD:
                case SqlStructureConstants.ENAME_SEMICOLON:
                case SqlStructureConstants.ENAME_SCOPERESOLUTIONOPERATOR:
                    //always ignores requested word spacing, and doesn't request a following space either.
                    state.WordSeparatorExpected = false;
                    WhiteSpace_BreakAsExpected(state);
                    state.AddOutputContent(FormatOperator(contentElement.TextValue!), SqlHtmlConstants.CLASS_OPERATOR);
                    break;

                case SqlStructureConstants.ENAME_ASTERISK:
                case SqlStructureConstants.ENAME_EQUALSSIGN:
                case SqlStructureConstants.ENAME_ALPHAOPERATOR:
                case SqlStructureConstants.ENAME_OTHEROPERATOR:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    WhiteSpace_SeparateWords(state);
                    state.AddOutputContent(FormatOperator(contentElement.TextValue!), SqlHtmlConstants.CLASS_OPERATOR);
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_COMPOUNDKEYWORD:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    WhiteSpace_SeparateWords(state);
                    state.SetRecentKeyword(contentElement.GetAttributeValue(SqlStructureConstants.ANAME_SIMPLETEXT)!);
                    state.AddOutputContent(FormatKeyword(contentElement.GetAttributeValue(SqlStructureConstants.ANAME_SIMPLETEXT)!), SqlHtmlConstants.CLASS_KEYWORD);
                    state.WordSeparatorExpected = true;
                    ProcessSqlNodeList(contentElement.ChildrenByNames(SqlStructureConstants.ENAMELIST_COMMENT), state.IncrementIndent());
                    state.DecrementIndent();
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_OTHERKEYWORD:
                case SqlStructureConstants.ENAME_DATATYPE_KEYWORD:
                {
                    string kwUpper = contentElement.TextValue!.ToUpperInvariant();

                    // DDLConstraintsOnNewLine: force a line break before constraint-starting keywords
                    // inside a DDL column list (DDL_PARENS context), but only when the keyword is
                    // NOT the first content item on the line (skip table-level constraints that
                    // already start on their own comma-separated line).
                    if (Options.DDLConstraintsOnNewLine
                        && (kwUpper == "CONSTRAINT" || kwUpper == "CHECK"
                            || kwUpper == "DEFAULT" || kwUpper == "REFERENCES")
                        && IsInsideDdlDetailParens(contentElement))
                    {
                        // Don't add extra break if the nearest preceding non-whitespace sibling is a comma
                        // (that means this constraint is already on its own comma-separated line).
                        Node? prev = contentElement.PreviousSibling();
                        while (prev != null && prev.Name == SqlStructureConstants.ENAME_WHITESPACE)
                            prev = prev!.PreviousSibling();
                        if (prev == null || prev.Name != SqlStructureConstants.ENAME_COMMA)
                            state.BreakExpected = true;
                    }

                    // If we're in the SELECT modifier zone and this keyword is NOT a modifier
                    // (DISTINCT/TOP/PERCENT/WITH/TIES), end the zone and arm the break so this
                    // token becomes the first column on a new line.
                    if (state.InSelectModifierZone && !IsSelectModifierKeyword(kwUpper))
                    {
                        state.InSelectModifierZone = false;
                        state.TopArgumentExpected = false;
                        state.BreakExpected = true;
                    }

                    WhiteSpace_SeparateWords(state);
                    state.SetRecentKeyword(contentElement.TextValue);
                    state.AddOutputContent(FormatKeyword(contentElement.TextValue), SqlHtmlConstants.CLASS_KEYWORD);
                    state.WordSeparatorExpected = true;

                    if (state.InSelectModifierZone && kwUpper == "TOP")
                        state.TopArgumentExpected = true;

                    // When SelectFirstColumnOnNewLine is on and this is a SELECT keyword, enter the
                    // modifier zone: suppress breaks for DISTINCT/TOP/etc. and arm one for the first
                    // real column instead.
                    if (Options.SelectFirstColumnOnNewLine
                        && Options.ExpandCommaLists
                        && kwUpper == "SELECT")
                        state.InSelectModifierZone = true;

                    break;
                }

                case SqlStructureConstants.ENAME_PSEUDONAME:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    WhiteSpace_SeparateWords(state);
                    state.AddOutputContent(FormatKeyword(contentElement.TextValue!), SqlHtmlConstants.CLASS_KEYWORD);
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_FUNCTION_KEYWORD:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    WhiteSpace_SeparateWords(state);
                    state.SetRecentKeyword(contentElement.TextValue!);
                    state.AddOutputContent(FormatKeyword(contentElement.TextValue!), SqlHtmlConstants.CLASS_FUNCTION);
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_OTHERNODE:
                case SqlStructureConstants.ENAME_MONETARY_VALUE:
                case SqlStructureConstants.ENAME_LABEL:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    WhiteSpace_SeparateWords(state);
                    state.AddOutputContent(contentElement.TextValue!);
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_NUMBER_VALUE:
                    // In modifier zone: a bare number is the TOP N count — stay on SELECT line.
                    // Anything else (rare literal-number first column) gets the break.
                    if (state.InSelectModifierZone)
                    {
                        if (state.TopArgumentExpected)
                            state.TopArgumentExpected = false;
                        else
                        {
                            state.InSelectModifierZone = false;
                            state.BreakExpected = true;
                        }
                    }
                    WhiteSpace_SeparateWords(state);
                    state.AddOutputContent(contentElement.TextValue!.ToLowerInvariant());
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_BINARY_VALUE:
                    if (state.InSelectModifierZone) { state.InSelectModifierZone = false; state.BreakExpected = true; }
                    WhiteSpace_SeparateWords(state);
                    //single AddOutputContent call: the max-line-width logic may break BETWEEN
                    // content calls, and a binary literal must never be split across lines
                    // (whitespace inside 0x... changes the SQL's meaning).
                    state.AddOutputContent("0x" + contentElement.TextValue!.Substring(2).ToUpperInvariant());
                    state.WordSeparatorExpected = true;
                    break;

                case SqlStructureConstants.ENAME_WHITESPACE:
                    //take note if it's a line-breaking space, but don't DO anything here
                    if (Regex.IsMatch(contentElement.TextValue!, @"(\r|\n)+"))
                        state.SourceBreakPending = true;
                    break;
                default:
                    throw new Exception("Unrecognized element in SQL Xml!");
            }

            if (contentElement.GetAttributeValue(SqlStructureConstants.ANAME_HASERROR) == "1")
                state.CloseClass();

            if (initialIndent != state.IndentLevel)
                throw new Exception("Messed up the indenting!! Check code/stack or panic!");

            _nodeRecursionDepth--;
        }


        private string FormatKeyword(string keyword)
        {
            string? outputKeyword;
            if (!KeywordMapping.TryGetValue(keyword.ToUpperInvariant(), out outputKeyword))
                outputKeyword = keyword;

            if (Options.UppercaseKeywords)
                return outputKeyword.ToUpperInvariant();
            else
                return outputKeyword.ToLowerInvariant();
        }

        private string FormatOperator(string operatorValue)
        {
            if (Options.UppercaseKeywords)
                return operatorValue.ToUpperInvariant();
            else
                return operatorValue.ToLowerInvariant();
        }

        private void WhiteSpace_SeparateStatements(Node contentElement, FormattingState state)
        {
            if (state.StatementBreakExpected)
            {
                //check whether this is a DECLARE/SET clause with similar precedent, and therefore exempt from double-linebreak.
                Node? thisClauseStarter = FirstSemanticElementChild(contentElement);
				if (!(thisClauseStarter != null
					&& thisClauseStarter.Name.Equals(SqlStructureConstants.ENAME_OTHERKEYWORD)
					&& state.GetRecentKeyword() != null
					&& ((thisClauseStarter.TextValue!.ToUpperInvariant().Equals("SET")
							&& state.GetRecentKeyword()!.Equals("SET")
							)
						|| (thisClauseStarter.TextValue.ToUpperInvariant().Equals("DECLARE")
							&& state.GetRecentKeyword()!.Equals("DECLARE")
							)
						|| (thisClauseStarter.TextValue.ToUpperInvariant().Equals("PRINT")
							&& state.GetRecentKeyword()!.Equals("PRINT")
							)
						)
					))
				{
					for (int i = Options.NewStatementLineBreaks; i > 0; i--)
						state.AddOutputLineBreak();
				}
				else
				{
					for (int i = Options.NewClauseLineBreaks; i > 0; i--)
						state.AddOutputLineBreak();
				}

                state.Indent(state.IndentLevel);
                state.BreakExpected = false;
				state.AdditionalBreaksExpected = 0;
                state.SourceBreakPending = false;
                state.StatementBreakExpected = false;
                state.WordSeparatorExpected = false;
            }
        }

        /// <summary>
        /// Cheap bounded check: true when the subtree contains at most maxNodes nodes.
        /// Used to gate the speculative inline rendering of CompactSingleStatementBlocks -
        /// without a bound, the try-inline-then-fallback double rendering is exponential
        /// on nested single-statement bodies (observed: >30s on a 2MB script).
        /// </summary>
        private static bool IsSmallSubtree(Node container, int maxNodes)
        {
            int count = 0;
            return CountNodesUpTo(container, maxNodes, ref count);
        }

        private static bool CountNodesUpTo(Node node, int maxNodes, ref int count)
        {
            foreach (Node child in node.Children)
            {
                if (++count > maxNodes)
                    return false;
                if (!CountNodesUpTo(child, maxNodes, ref count))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// True when the statement contains anything that produces output: any element
        /// other than whitespace, including comments. Empty clauses are recursed into.
        /// </summary>
        private static bool StatementHasRenderableContent(Node statement)
        {
            foreach (Node child in statement.Children)
            {
                if (child.Name.Equals(SqlStructureConstants.ENAME_WHITESPACE))
                    continue;
                if (child.Name.Equals(SqlStructureConstants.ENAME_SQL_CLAUSE))
                {
                    if (StatementHasRenderableContent(child))
                        return true;
                    continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// True when this comma belongs to a RAISERROR(...) argument list and the
        /// CompactRaiserror option is active - such lists stay on one line.
        /// </summary>
        private bool IsCompactRaiserrorArgs(Node commaElement)
        {
            if (!Options.CompactRaiserror)
                return false;
            Node? parens = commaElement.Parent;
            if (parens == null || !parens.Name.Equals(SqlStructureConstants.ENAME_EXPRESSION_PARENS))
                return false;
            Node? prev = parens.PreviousSibling();
            while (prev != null && SqlStructureConstants.ENAMELIST_NONCONTENT.Contains(prev.Name))
                prev = prev.PreviousSibling();
            return prev != null
                && prev.Name.Equals(SqlStructureConstants.ENAME_OTHERKEYWORD)
                && "RAISERROR".Equals(prev.TextValue, StringComparison.OrdinalIgnoreCase);
        }

        private Node? FirstSemanticElementChild(Node? contentElement)
        {
            Node? target = null;
            while (contentElement != null)
            {
                target = contentElement.ChildrenExcludingNames(SqlStructureConstants.ENAMELIST_NONCONTENT).FirstOrDefault();

                if (target != null && SqlStructureConstants.ENAMELIST_NONSEMANTICCONTENT.Contains(target.Name))
                    contentElement = target;
                else
                    contentElement = null;
            }

            return target;
        }

        /// <summary>
        /// Word/value tokens whose direct source-adjacency to an EMPTY string literal
        /// is preserved (see upstream #200). A sibling of one of these types means the
        /// source had no whitespace in between (whitespace is its own sibling node).
        /// Deliberately excludes keywords and commas so their spacing is untouched.
        /// </summary>
        private static readonly Regex _regularIdentifierRegex = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        /// <summary>
        /// True when a bracket-quoted name could safely be written without brackets:
        /// a conservative regular-identifier shape (ASCII letter/underscore start,
        /// letters/digits/underscores only) that is not in the T-SQL keyword list.
        /// </summary>
        private static bool IsHarmlessUnbracketableName(string name)
        {
            return _regularIdentifierRegex.IsMatch(name)
                && !SqlServerFormattingKeywords.Contains(name);
        }

        /// <summary>
        /// True when the sibling is a token that an unbracketed identifier would merge
        /// with if the brackets between them disappeared (no whitespace node = direct
        /// source adjacency). Periods, commas, operators and parens are safe separators.
        /// </summary>
        private static bool IsTokenMergeRisk(Node? node)
        {
            if (node == null) return false;
            switch (node.Name)
            {
                case SqlStructureConstants.ENAME_OTHERNODE:
                case SqlStructureConstants.ENAME_NUMBER_VALUE:
                case SqlStructureConstants.ENAME_MONETARY_VALUE:
                case SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME:
                case SqlStructureConstants.ENAME_STRING:
                case SqlStructureConstants.ENAME_NSTRING:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAdjacencyPreservingToken(Node? node, bool includeStrings)
        {
            if (node == null) return false;
            switch (node.Name)
            {
                case SqlStructureConstants.ENAME_OTHERNODE:
                case SqlStructureConstants.ENAME_NUMBER_VALUE:
                case SqlStructureConstants.ENAME_MONETARY_VALUE:
                case SqlStructureConstants.ENAME_PERIOD:
                case SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME:
                    return true;
                case SqlStructureConstants.ENAME_STRING:
                case SqlStructureConstants.ENAME_NSTRING:
                    // Strings only count for the empty-string fragment scenario (#200);
                    // bracket adjacency (#240) excludes them so "[col] 'alias'" keeps
                    // its historical readable spacing.
                    return includeStrings;
                default:
                    return false;
            }
        }

        private void WhiteSpace_SeparateWords(FormattingState state)
        {
            if (state.BreakExpected || state.AdditionalBreaksExpected > 0)
            {
                bool wasUnIndent = state.UnIndentInitialBreak;
                if (wasUnIndent) state.DecrementIndent();
                WhiteSpace_BreakAsExpected(state);
                if (wasUnIndent) state.IncrementIndent();
            }
            else if (state.WordSeparatorExpected)
            {
                state.AddOutputSpace();
            }
            state.UnIndentInitialBreak = false;
            state.SourceBreakPending = false;
            state.WordSeparatorExpected = false;
        }

        /// <summary>
        /// True when the comment began at column 0 in the source: its preceding sibling
        /// is whitespace ending in a line break (no horizontal offset after it).
        /// </summary>
        private static bool CommentStartsAtSourceColumnZero(Node contentElement)
        {
            Node? prev = contentElement.PreviousSibling();
            if (prev == null || !prev.Name.Equals(SqlStructureConstants.ENAME_WHITESPACE))
                return false;
            string? ws = prev.TextValue;
            return !string.IsNullOrEmpty(ws) && (ws!.EndsWith("\n") || ws.EndsWith("\r"));
        }

        private static bool CommentFollowedBySourceBreak(Node comment)
        {
            for (var next = comment.NextSibling(); next?.Name == SqlStructureConstants.ENAME_WHITESPACE;
                next = next.NextSibling())
            {
                if (next.TextValue?.Contains('\n') == true || next.TextValue?.Contains('\r') == true)
                {
                    return true;
                }
            }

            return false;
        }

        private void WhiteSpace_SeparateComment(Node contentElement, FormattingState state)
        {
            if (state.CurrentLineHasContent && state.SourceBreakPending)
            {
                state.BreakExpected = true;
                WhiteSpace_BreakAsExpected(state);
            }
            else if (state.WordSeparatorExpected)
                state.AddOutputSpace();
            state.SourceBreakPending = false;
            state.WordSeparatorExpected = false;
        }

        private void WhiteSpace_BreakAsExpected(FormattingState state)
        {
            if (state.BreakExpected)
                state.WhiteSpace_BreakToNextLine();
            while (state.AdditionalBreaksExpected > 0)
            {
                state.WhiteSpace_BreakToNextLine();
                state.AdditionalBreaksExpected--;
            }
        }

        class FormattingState : BaseFormatterState
        {
            //normal constructor
            public FormattingState(bool htmlOutput, string indentString, int spacesPerTab, int maxLineWidth, int initialIndentLevel)
                : base(htmlOutput)
            {
                IndentLevel = initialIndentLevel;
                HtmlOutput = htmlOutput;
                IndentString = indentString;
				MaxLineWidth = maxLineWidth;

                int tabCount = indentString.Split('\t').Length - 1;
                int tabExtraCharacters = tabCount * (spacesPerTab - 1);
                IndentLength = indentString.Length + tabExtraCharacters;
            }

            //special "we want isolated state, but inheriting existing conditions" constructor
            public FormattingState(FormattingState sourceState)
                : base(sourceState.HtmlOutput)
            {
                IndentLevel = sourceState.IndentLevel;
                HtmlOutput = sourceState.HtmlOutput;
                IndentString = sourceState.IndentString;
                IndentLength = sourceState.IndentLength;
                MaxLineWidth = sourceState.MaxLineWidth;
				//TODO: find a way out of the cross-dependent wrapping maze...
                //CurrentLineLength = sourceState.CurrentLineLength;
                CurrentLineLength = IndentLevel * IndentLength;
                CurrentLineHasContent = sourceState.CurrentLineHasContent;
            }

            private string IndentString { get; set; }
            private int IndentLength { get; set; }
            private int MaxLineWidth { get; set; }

            public bool StatementBreakExpected { get; set; }
            public bool BreakExpected { get; set; }
            public bool InSelectModifierZone { get; set; }
            public bool TopArgumentExpected { get; set; }
            public bool WordSeparatorExpected { get; set; }
            public bool SourceBreakPending { get; set; }
            public int AdditionalBreaksExpected { get; set; }

            public bool UnIndentInitialBreak { get; set; }
            public int IndentLevel { get; private set; }
            public int CurrentLineLength { get; private set; }
            public bool CurrentLineHasContent { get; private set; }

            public SpecialRegionType? SpecialRegionActive { get; set; }
            public bool RegionStartedInline { get; set; }
            public Node? RegionStartNode { get; set; }

            private static Regex _startsWithBreakChecker = new Regex(@"^\s*(\r|\n)", RegexOptions.None);
            public bool StartsWithBreak
            {
                get
                {
                    return _startsWithBreakChecker.IsMatch(_outBuilder.ToString());
                }
            }

            public override void AddOutputContent(string content)
            {
                if (SpecialRegionActive == null)
                    AddOutputContent(content, null);
            }

            public override void AddOutputContent(string content, string? htmlClassName)
            {
                if (CurrentLineHasContent && (content.Length + CurrentLineLength > MaxLineWidth))
                {
                    if (SpecialRegionActive == null && _outBuilder.Length > 0
                        && _outBuilder[_outBuilder.Length - 1] == ' ')
                        _outBuilder.Length--;
                    WhiteSpace_BreakToNextLine();
                }

                if (SpecialRegionActive == null)
                    base.AddOutputContent(content, htmlClassName);

                CurrentLineHasContent = true;
                CurrentLineLength += content.Length;
            }

            public override void AddOutputLineBreak()
            {
#if DEBUG
                //hints for debugging line-width issues:
                //_outBuilder.Append(" (" + CurrentLineLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
#endif

                //if linebreaks are added directly in the content (eg in comments or strings), they 
                // won't be accounted for here - that's ok.
                if (SpecialRegionActive == null)
                    base.AddOutputLineBreak();
                CurrentLineLength = 0;
                CurrentLineHasContent = false;
            }

            internal void AddOutputSpace()
            {
                if (SpecialRegionActive == null)
                    _outBuilder.Append(" ");
            }

            /// <summary>
            /// Emits the verbatim (identity- or minify-rendered) content of a special
            /// region and synchronizes line-state bookkeeping with it. During the region,
            /// suppressed AddOutputContent calls left CurrentLineHasContent stale, so the
            /// closing marker's comment-separation logic added a line break even when the
            /// region text already ended with one - accumulating a blank line inside the
            /// region on every pass (upstream #215/#292).
            /// </summary>
            internal void EmitSpecialRegionContent(string regionContent)
            {
                if (regionContent.Length == 0)
                {
                    return;
                }

                AddOutputContentRaw(regionContent);
                WordSeparatorExpected = false;
                BreakExpected = false;
                // SourceBreakPending is deliberately left alone: for minified regions
                // (whose rendering strips newlines) it still reflects whether the SOURCE
                // broke the line before the closing marker.
                bool endsWithLineBreak = regionContent.EndsWith("\n") || regionContent.EndsWith("\r");
                CurrentLineHasContent = !endsWithLineBreak;
                if (endsWithLineBreak)
                    CurrentLineLength = 0;
            }

            public void Indent(int indentLevel)
            {
                for (int i = 0; i < indentLevel; i++)
                {
                    if (SpecialRegionActive == null)
                        base.AddOutputContent(IndentString, ""); //that is, add the indent as HTMLEncoded content if necessary, but no weird linebreak-adding
                    CurrentLineLength += IndentLength;
                }
            }

            /// <summary>
            /// When set, breaks flushed by WhiteSpace_BreakToNextLine skip the indent -
            /// used to keep column-0 multi-line comments at column 0 (upstream #99).
            /// </summary>
            internal bool OmitIndentOnFlushedBreaks;

            internal void WhiteSpace_BreakToNextLine()
            {
                AddOutputLineBreak();
                if (!OmitIndentOnFlushedBreaks)
                    Indent(IndentLevel);
                BreakExpected = false;
                SourceBreakPending = false;
                WordSeparatorExpected = false;
            }

            //for linebreak detection, use actual string content rather than counting "AddOutputLineBreak()" calls,
            // because we also want to detect the content of strings and comments.
#if SIMPLIFIEDFW
            private static Regex _lineBreakMatcher = new Regex(@"(\r|\n)+");
#else
            private static Regex _lineBreakMatcher = new Regex(@"(\r|\n)+", RegexOptions.Compiled);
#endif

            public bool OutputContainsLineBreak { get { return _lineBreakMatcher.IsMatch(_outBuilder.ToString()); } }

            public void Assimilate(FormattingState partialState)
            {
                //TODO: find a way out of the cross-dependent wrapping maze...
                CurrentLineLength = CurrentLineLength + partialState.CurrentLineLength;
                CurrentLineHasContent = CurrentLineHasContent || partialState.CurrentLineHasContent;
                if (SpecialRegionActive == null)
                    _outBuilder.Append(partialState.DumpOutput());
            }


            private Dictionary<int, string> _mostRecentKeywordsAtEachLevel = new Dictionary<int, string>();

            public FormattingState IncrementIndent()
            {
                IndentLevel++;
                return this;
            }

            public FormattingState DecrementIndent()
            {
                IndentLevel--;
                return this;
            }

            public void SetRecentKeyword(string ElementName)
            {
                if (!_mostRecentKeywordsAtEachLevel.ContainsKey(IndentLevel))
                    _mostRecentKeywordsAtEachLevel.Add(IndentLevel, ElementName.ToUpperInvariant());
            }

            public string? GetRecentKeyword()
            {
                string? keywordFound = null;
                int? keywordFoundAt = null;
                foreach (int key in _mostRecentKeywordsAtEachLevel.Keys)
                {
                    if ((keywordFoundAt == null || keywordFoundAt.Value > key) && key >= IndentLevel)
                    {
                        keywordFoundAt = key;
                        keywordFound = _mostRecentKeywordsAtEachLevel[key];
                    }
                }
                return keywordFound;
            }

            public void ResetKeywords()
            {
                List<int> descendentLevelKeys = new List<int>();
                foreach (int key in _mostRecentKeywordsAtEachLevel.Keys)
                    if (key >= IndentLevel)
                        descendentLevelKeys.Add(key);
                foreach (int key in descendentLevelKeys)
                    _mostRecentKeywordsAtEachLevel.Remove(key);
            }
        }

        public enum SpecialRegionType
        {
            NoFormat = 1,
            Minify = 2
        }
    }
}
