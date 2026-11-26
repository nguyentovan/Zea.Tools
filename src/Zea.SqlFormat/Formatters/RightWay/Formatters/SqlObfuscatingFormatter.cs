/*
Poor Man's T-SQL Formatter - a small free Transact-SQL formatting 
library for .Net 2.0 and JS, written in C#. 
Copyright (C) 2011-2017 Tao Klerks

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
using Zea.SqlFormat.Formatters.RightWay.Interfaces;
using Zea.SqlFormat.Formatters.RightWay.ParseStructure;

namespace Zea.SqlFormat.Formatters.RightWay
{
    internal sealed class SqlObfuscatingFormatter
    {
        public string FormatSQLTree(Node sqlTreeDoc)
        {
            //thread-safe - each call to FormatSQLTree() gets its own independent state object
            ObfuscatingFormattingState state = new ObfuscatingFormattingState();

            if (sqlTreeDoc.Name == SqlStructureConstants.ENAME_SQL_ROOT && sqlTreeDoc.GetAttributeValue(SqlStructureConstants.ANAME_ERRORFOUND) == "1")
            {
                var errorPrefix = MessagingConstants.FormatErrorDefaultMessage + Environment.NewLine;
                state.AddOutputContent(errorPrefix);
            }

            //pass "doc" itself into process: useful/necessary when formatting MINIFY sub-regions from standard formatter
            ProcessSqlNodeList(new[] { sqlTreeDoc }, state);
            state.BreakIfExpected();
            return state.DumpOutput();
        }

        private void ProcessSqlNodeList(IEnumerable<Node> rootList, ObfuscatingFormattingState state)
        {
            foreach (Node contentElement in rootList)
                ProcessSqlNode(contentElement, state);
        }

        private void ProcessSqlNode(Node contentElement, ObfuscatingFormattingState state)
        {
            switch (contentElement.Name)
            {
                case SqlStructureConstants.ENAME_SQL_ROOT:
                case SqlStructureConstants.ENAME_SQL_STATEMENT:
                case SqlStructureConstants.ENAME_SQL_CLAUSE:
                case SqlStructureConstants.ENAME_SET_OPERATOR_CLAUSE:
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
                case SqlStructureConstants.ENAME_SELECTIONTARGET:
                case SqlStructureConstants.ENAME_CONTAINER_GENERALCONTENT:
                case SqlStructureConstants.ENAME_CTE_WITH_CLAUSE:
                case SqlStructureConstants.ENAME_PERMISSIONS_BLOCK:
                case SqlStructureConstants.ENAME_PERMISSIONS_DETAIL:
                case SqlStructureConstants.ENAME_MERGE_CLAUSE:
                case SqlStructureConstants.ENAME_MERGE_TARGET:
                case SqlStructureConstants.ENAME_CASE_INPUT:
                case SqlStructureConstants.ENAME_BOOLEAN_EXPRESSION:
                case SqlStructureConstants.ENAME_BETWEEN_LOWERBOUND:
                case SqlStructureConstants.ENAME_BETWEEN_UPPERBOUND:
                case SqlStructureConstants.ENAME_CONTAINER_SINGLESTATEMENT:
                case SqlStructureConstants.ENAME_CONTAINER_MULTISTATEMENT:
                case SqlStructureConstants.ENAME_MERGE_ACTION:
                case SqlStructureConstants.ENAME_PERMISSIONS_TARGET:
                case SqlStructureConstants.ENAME_PERMISSIONS_RECIPIENT:
                case SqlStructureConstants.ENAME_DDL_WITH_CLAUSE:
                case SqlStructureConstants.ENAME_MERGE_CONDITION:
                case SqlStructureConstants.ENAME_MERGE_THEN:
                case SqlStructureConstants.ENAME_JOIN_ON_SECTION:
                case SqlStructureConstants.ENAME_CTE_ALIAS:
                case SqlStructureConstants.ENAME_ELSE_CLAUSE:
                case SqlStructureConstants.ENAME_DDL_AS_BLOCK:
                case SqlStructureConstants.ENAME_CURSOR_FOR_BLOCK:
                case SqlStructureConstants.ENAME_TRIGGER_CONDITION:
                case SqlStructureConstants.ENAME_CURSOR_FOR_OPTIONS:
                case SqlStructureConstants.ENAME_CTE_AS_BLOCK:
                case SqlStructureConstants.ENAME_DDL_RETURNS:
                case SqlStructureConstants.ENAME_MERGE_USING:
                case SqlStructureConstants.ENAME_MERGE_WHEN:
                case SqlStructureConstants.ENAME_BETWEEN_CONDITION:
                case SqlStructureConstants.ENAME_BEGIN_END_BLOCK:
                case SqlStructureConstants.ENAME_TRY_BLOCK:
                case SqlStructureConstants.ENAME_CATCH_BLOCK:
                case SqlStructureConstants.ENAME_CASE_STATEMENT:
                case SqlStructureConstants.ENAME_CASE_WHEN:
                case SqlStructureConstants.ENAME_CASE_THEN:
                case SqlStructureConstants.ENAME_CASE_ELSE:
                case SqlStructureConstants.ENAME_AND_OPERATOR:
                case SqlStructureConstants.ENAME_OR_OPERATOR:
                    //these are all containers, and therefore have no impact on obfuscated output.
                    ProcessSqlNodeList(contentElement.Children, state);
                    break;

                case SqlStructureConstants.ENAME_DDLDETAIL_PARENS:
				case SqlStructureConstants.ENAME_FUNCTION_PARENS:
				case SqlStructureConstants.ENAME_IN_PARENS:
				case SqlStructureConstants.ENAME_DDL_PARENS:
                case SqlStructureConstants.ENAME_EXPRESSION_PARENS:
                case SqlStructureConstants.ENAME_SELECTIONTARGET_PARENS:
                    state.SpaceExpected = false;
                    state.AddOutputContent("(");
                    ProcessSqlNodeList(contentElement.Children, state);
                    state.SpaceExpected = false;
                    state.SpaceExpectedForAnsiString = false;
                    state.AddOutputContent(")");
                    break;

                case SqlStructureConstants.ENAME_WHITESPACE:
                    //do nothing
                    break;

                case SqlStructureConstants.ENAME_COMMENT_MULTILINE:
                case SqlStructureConstants.ENAME_COMMENT_SINGLELINE:
                case SqlStructureConstants.ENAME_RAW_SINGLELINE_COMMENT:
                case SqlStructureConstants.ENAME_COMMENT_SINGLELINE_CSTYLE:
                    break;

                case SqlStructureConstants.ENAME_BATCH_SEPARATOR:
                    //newline regardless of whether previous element recommended a break or not.
                    state.BreakExpected = true;
                    ProcessSqlNodeList(contentElement.Children, state);
                    state.BreakExpected = true;
                    break;

                case SqlStructureConstants.ENAME_STRING:
                    state.SpaceIfExpectedForAnsiString();
                    state.SpaceExpected = false;
                    state.AddOutputContent("'" + contentElement.TextValue!.Replace("'", "''") + "'");
                    state.SpaceExpectedForAnsiString = true;
                    break;

                case SqlStructureConstants.ENAME_NSTRING:
                    state.AddOutputContent("N'" + contentElement.TextValue!.Replace("'", "''") + "'");
                    state.SpaceExpectedForAnsiString = true;
                    break;

                case SqlStructureConstants.ENAME_BRACKET_QUOTED_NAME:
                    // The standard formatter preserves word<->bracket source adjacency
                    // (upstream #240), so minification must not erase a gap the source
                    // had - otherwise minify->reformat would diverge from direct
                    // formatting. Only cancel the pending space when the source was
                    // truly adjacent, and keep a source gap on the trailing side too.
                    if (!(contentElement.PreviousSibling()?.Name.Equals(SqlStructureConstants.ENAME_WHITESPACE) ?? false))
                        state.SpaceExpected = false;
                    state.AddOutputContent("[" + contentElement.TextValue!.Replace("]", "]]") + "]");
                    state.SpaceExpected = contentElement.NextSibling()?.Name.Equals(SqlStructureConstants.ENAME_WHITESPACE) ?? false;
                    break;

                case SqlStructureConstants.ENAME_QUOTED_STRING:
                    state.SpaceExpected = false;
                    state.AddOutputContent("\"" + contentElement.TextValue!.Replace("\"", "\"\"") + "\"");
                    break;

                case SqlStructureConstants.ENAME_COMMA:
                case SqlStructureConstants.ENAME_PERIOD:
                case SqlStructureConstants.ENAME_SEMICOLON:
                case SqlStructureConstants.ENAME_SCOPERESOLUTIONOPERATOR:
                case SqlStructureConstants.ENAME_ASTERISK:
                case SqlStructureConstants.ENAME_EQUALSSIGN:
                case SqlStructureConstants.ENAME_OTHEROPERATOR:
                    state.SpaceExpected = false;
                    state.AddOutputContent(contentElement.TextValue!);
                    break;

                case SqlStructureConstants.ENAME_COMPOUNDKEYWORD:
                    state.AddOutputContent(contentElement.GetAttributeValue(SqlStructureConstants.ANAME_SIMPLETEXT)!);
                    state.SpaceExpected = true;
                    break;


                case SqlStructureConstants.ENAME_LABEL:
                    state.AddOutputContent(contentElement.TextValue!);
                    state.BreakExpected = true;
                    break;

                case SqlStructureConstants.ENAME_OTHERKEYWORD:
                case SqlStructureConstants.ENAME_ALPHAOPERATOR:
                case SqlStructureConstants.ENAME_DATATYPE_KEYWORD:
                case SqlStructureConstants.ENAME_PSEUDONAME:
                case SqlStructureConstants.ENAME_BINARY_VALUE:
                    state.AddOutputContent(contentElement.TextValue!);
                    state.SpaceExpected = true;
                    break;

                case SqlStructureConstants.ENAME_NUMBER_VALUE:
                    state.AddOutputContent(contentElement.TextValue!);
                    if (!contentElement.TextValue!.ToLowerInvariant().Contains("e"))
                    {
                        state.SpaceExpectedForE = true;
                        if (contentElement.TextValue!.Equals("0"))
                            state.SpaceExpectedForX = true;
                    }
                    break;

                case SqlStructureConstants.ENAME_MONETARY_VALUE:
                    if (!contentElement.TextValue!.Substring(0, 1).Equals("$"))
                        state.SpaceExpected = false;

                    state.AddOutputContent(contentElement.TextValue!);

                    if (contentElement.TextValue!.Length == 1)
                        state.SpaceExpectedForPlusMinus = true;
                    break;

                case SqlStructureConstants.ENAME_RAW_SUFFIX:
                    state.SpaceExpected = false;
                    state.AddOutputContent(contentElement.TextValue!);
                    state.SpaceExpected = true;
                    break;
                case SqlStructureConstants.ENAME_OTHERNODE:
                case SqlStructureConstants.ENAME_FUNCTION_KEYWORD:
                    state.AddOutputContent(contentElement.TextValue!);
                    state.SpaceExpected = true;
                    break;

                default:
                    throw new Exception("Unrecognized element in SQL Xml!");
            }
        }

        private sealed class ObfuscatingFormattingState : BaseFormatterState
        {
            private const int MaxLineLength = 80;

            public ObfuscatingFormattingState() : base(false)
            {
            }

            internal bool BreakExpected { get; set; }
            internal bool SpaceExpectedForAnsiString { get; set; }
            internal bool SpaceExpectedForE { get; set; }
            internal bool SpaceExpectedForX { get; set; }
            internal bool SpaceExpectedForPlusMinus { get; set; }
            internal bool SpaceExpected { get; set; }

            private int _currentLineLength;

            public void BreakIfExpected()
            {
                if (BreakExpected)
                {
                    BreakExpected = false;
                    base.AddOutputLineBreak();
                    SetSpaceNoLongerExpected();
                    _currentLineLength = 0;
                }
            }

            public void SpaceIfExpectedForAnsiString()
            {
                if (SpaceExpectedForAnsiString)
                {
                    base.AddOutputContent(" ", null);
                    SetSpaceNoLongerExpected();
                }
            }

            public void SpaceIfExpected()
            {
                if (SpaceExpected)
                {
                    base.AddOutputContent(" ", null);
                    SetSpaceNoLongerExpected();
                }
            }

            public override void AddOutputContent(string content, string? htmlClassName)
            {
                if (htmlClassName != null)
                    throw new NotSupportedException("Obfuscating formatter does not use html class names...");

                BreakIfExpected();
                SpaceIfExpected();
                if (_currentLineLength > 0 && _currentLineLength + content.Length > MaxLineLength)
                {
                    BreakExpected = true;
                    BreakIfExpected();
                }
                else if ((SpaceExpectedForE && content.Substring(0, 1).ToLower().Equals("e"))
                    || (SpaceExpectedForX && content.Substring(0, 1).ToLower().Equals("x"))
                    || (SpaceExpectedForPlusMinus && content.Substring(0, 1).Equals("+"))
                    || (SpaceExpectedForPlusMinus && content.Substring(0, 1).Equals("-"))
                    )
                {
                    SpaceExpected = true;
                    SpaceIfExpected();
                }

                _currentLineLength += content.Length;
                base.AddOutputContent(content, null);
                SetSpaceNoLongerExpected();
            }

            private void SetSpaceNoLongerExpected()
            {
                SpaceExpected = false;
                SpaceExpectedForAnsiString = false;
                SpaceExpectedForE = false;
                SpaceExpectedForX = false;
                SpaceExpectedForPlusMinus = false;
            }

            public override void AddOutputLineBreak()
            {
                //don't want the outer code to write line breaks at all
                throw new NotSupportedException();
            }
        }
    }
}
