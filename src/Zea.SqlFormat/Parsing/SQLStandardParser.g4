/*
 * DBeaver - Universal Database Manager
 * Copyright (C) 2010-2026 DBeaver Corp and others
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
parser grammar SQLStandardParser;

options { tokenVocab=SQLStandardLexer; }

@header {
    // Adapted from DBeaver SQLStandard grammar.
    // Copyright (C) 2010-2026 DBeaver Corp and others.
    // Licensed under the Apache License, Version 2.0.
}

// root rule for script

sqlScript: scriptSeparator* (sqlQuery (scriptSeparator+ sqlQuery | {IsTsqlProceduralSyntax()}? sqlQuery)* scriptSeparator*)? EOF;

scriptSeparator: Semicolon | MySqlScriptDelimiter | delimiterStatement | GO UnsignedInteger? | {IsOracleSyntax()}? Solidus;

delimiterStatement: MySqlDelimiterDirective;

sqlQuery: oraclePackageDeclaration | variableStatement | sqlQueryBody | proceduralIf | proceduralLoop | proceduralTryCatch
    | mergeStatement | implicitProcedureCall | brokerStatement | tsqlRaiseError | tsqlObjectPermissions;

sqlQueryBody: mySqlUtilityStatement|explainableSqlQueryBody|explainStatement|anonymousBlockStatement|localRelationDeclaration|cursorOperation|
    sqlTransactionStatement|sqlSessionStatement|sqliteUtilityStatement|postgresUtilityStatement;
// Keep the explainable subset separate from session and transaction statements. The wrapper
// preserves EXPLAIN as a statement while retaining the explained SQL for semantic analysis.

explainableSqlQueryBody: directSqlDataStatement|callStatement|sqlSchemaStatement|cursorDeclaration|
    selectStatementSingleRow|explicitTable|tableValueConstructor;

variableStatement: {!IsProceduralDeclarationAhead()}? variableDeclarationStatement|variableAssignmentStatement;

variableDeclarationStatement: DECLARE (OR REPLACE)? VARIABLE? variableDeclaration (Comma variableDeclaration)*;

variableDeclaration: variableName dataType? variableInitializer?;

variableAssignmentStatement: SET (VAR|VARIABLE)? variableAssignment (Comma variableAssignment)*;

variableAssignment: variableName assignmentOperator valueExpression;

variableName: variableExpression|identifier;

variableInitializer: (EqualsOperator|DEFAULT) valueExpression;

directSqlDataStatement: withClause? (deleteStatement|selectStatement|insertStatement|updateStatement|dialectDataStatement);

selectStatement: queryExpression ({TrailingSelectIntoAllowed()}? selectIntoClause)?;

// data type literals

sign: PlusSign|MinusSign;

literal: (signedNumericLiteral|generalLiteral);

unsignedNumericLiteral: (UnsignedInteger|DecimalLiteral|ApproximateNumericLiteral|SqliteHexInteger
    |TsqlBinaryLiteral|MySqlHexInteger|MySqlBinaryInteger);

signedNumericLiteral: sign? unsignedNumericLiteral;

characterStringLiteral: MySqlIntroducedLiteral|((Introducer characterSetSpecification)? StringLiteralContent)
    |NationalCharacterStringLiteral|BitStringLiteral|HexStringLiteral|OracleAlternativeLiteral
    |PostgresEscapeStringLiteral|PostgresUnicodeStringLiteral postgresUnicodeEscape?|DollarQuotedStringLiteral;

postgresUnicodeEscape: {IsPostgresSyntax() && NextWordIs("UESCAPE")}? anyWord StringLiteralContent;

generalLiteral: characterStringLiteral|datetimeLiteral|intervalLiteral|dialectLiteral;

dialectLiteral: {Dialect == "db2"}? CURRENT (DATE|TIME|TIMESTAMP) | soqlRelativeDate;

soqlRelativeDate: {Dialect == "soql" && NextWordIs("LAST_N_DAYS")}? Identifier Colon UnsignedInteger;

datetimeLiteral: dateLiteral|timeLiteral|timestampLiteral;

dateLiteral: DATE StringLiteralContent;

timeLiteral: TIME ({IsPostgresSyntax()}? timeZoneQualifier TIME ZONE)? StringLiteralContent;

timestampLiteral: TIMESTAMP ({IsPostgresSyntax()}? timeZoneQualifier TIME ZONE)? StringLiteralContent;
// intervalLiteral: INTERVAL sign? StringLiteralContent intervalQualifier;

intervalLiteral: INTERVAL sign? valueExpressionPrimary intervalQualifier?;

// identifiers

characterSetSpecification: qualifiedName;

characterSetName: qualifiedName;

schemaName: qualifiedName;

qualifiedName: identifier (Period identifier)*;

identifier: (Introducer characterSetSpecification)? (actualIdentifier|PostgresUnicodeIdentifier postgresUnicodeEscape?);

actualIdentifier: (Identifier|DelimitedIdentifier|nonReserved|dialectNonReserved|oracleNonReserved|sqliteNonReserved|Quotted|BatchVariableName);

postgresKeyword: anyWord;

// data types

sqliteNonReserved: {IsSqliteSyntax()}? (UNKNOWN|TEMP|SQLITE_ABORT|SQLITE_ATTACH|SQLITE_AUTOINCREMENT|SQLITE_DETACH|
    SQLITE_EXCLUSIVE|SQLITE_FAIL|SQLITE_GLOB|SQLITE_IGNORE|SQLITE_INDEXED|SQLITE_PRAGMA|SQLITE_RELEASE|SQLITE_SAVEPOINT|
    SQLITE_VACUUM|SQLITE_VIRTUAL);

// data types

dialectNonReserved: DK_ABORT | DK_COPY | DK_DELIMITER | DK_ERROR | DK_NODES
    | DK_PROJECTION | DK_SEGMENTED | DK_STDIN | DK_ACCESS | DK_LOCKING
    | DK_MULTISET | DK_SEL | DK_VOLATILE | DK_ASOF | DK_CLUSTER
    | DK_SHOW | DK_VIEWS | DK_COMPUTE | DK_INCREMENTAL | DK_INVALIDATE
    | DK_METADATA | DK_STATS | DK_CONNECT | DK_HIGH | DK_LOW
    | DK_PREFERRING | DK_START | DK_CS | DK_RR | DK_RS
    | DK_UR | DK_DESCRIBE | DK_DETAIL | DK_VERSION | DK_DISTRIBUTE
    | DK_SORT | DK_ENGINE | DK_FINAL | DK_PREWHERE | DK_FORCE
    | DK_FORMAT | DK_HEADER | DK_READABLE | DK_IAM_ROLE | DK_UNLOAD
    | DK_LABEL | DK_EXTERNAL | DK_LOCATION | DK_ORDINALITY | DK_OVERWRITE
    | DK_PARTITIONED | DK_STORED | DK_SYSTEM_TIME | DK_WAREHOUSE;

// data types

dataType: (arrayType|datetimeType|intervalType|(qualifiedName|CHARACTER) typeModifier* typeArguments? (CHARACTER SET characterSetSpecification)?);

arrayType: ARRAY LeftParen dataType RightParen; // TODO parse it conditionally on the database engine?

datetimeType: DATE|TIME typeArguments? (timeZoneQualifier TIME ZONE)?|TIMESTAMP typeArguments? (timeZoneQualifier TIME ZONE)?;

intervalType: INTERVAL intervalQualifier;

intervalQualifier: (startField TO endField|singleDatetimeField);

startField: nonSecondDatetimeField (LeftParen intervalLeadingFieldPrecision RightParen)?;

nonSecondDatetimeField: (YEAR|MONTH|DAY|HOUR|MINUTE);

intervalLeadingFieldPrecision: UnsignedInteger;

endField: (nonSecondDatetimeField|SECOND (LeftParen intervalFractionalSecondsPrecision RightParen)?);

intervalFractionalSecondsPrecision: UnsignedInteger;

singleDatetimeField: (nonSecondDatetimeField (LeftParen intervalLeadingFieldPrecision RightParen)?|SECOND (LeftParen intervalLeadingFieldPrecision (Comma intervalFractionalSecondsPrecision)? RightParen)?);

// column definition

columnDefinition: {IsSqliteSyntax()}? columnName sqliteDeclaredType? sqliteColumnConstraint*
    | {!IsSqliteSyntax()}? columnName dataType typeModifier* tsqlIdentity?
      ({IsPostgresSyntax()}? columnConstraintDefinition+ defaultClause? columnConstraintDefinition*
      | defaultClause? columnConstraintDefinition*) anyWordWithAnyValue?
    | {IsOracleSyntax()}? columnName dataType? GENERATED {NextWordIs("ALWAYS")}? anyWord AS
      LeftParen valueExpression RightParen ({NextWordIs("VIRTUAL")}? anyWord)?;

sqliteDeclaredType: sqliteTypeWord+ (LeftParen signedNumericLiteral (Comma signedNumericLiteral)? RightParen)?;

sqliteTypeWord: {IsSqliteTypeWordAhead()}? actualIdentifier | CHARACTER;

sqliteColumnConstraint: constraintNameDefinition?
    (PRIMARY KEY orderingSpecification? sqliteConflictResolution? SQLITE_AUTOINCREMENT?
    | NOT NULL sqliteConflictResolution?
    | UNIQUE sqliteConflictResolution?
    | checkConstraintDefinition
    | DEFAULT sqliteDefaultValue
    | {NextWordIs("COLLATE")}? anyWord identifier
    | referencesSpecification constraintAttributes?
    | (GENERATED {NextWordIs("ALWAYS")}? anyWord)? AS LeftParen valueExpression RightParen sqliteGeneratedStorage?);

sqliteDefaultValue: signedNumericLiteral | generalLiteral | NULL | TRUE | FALSE | identifier | LeftParen valueExpression RightParen;

sqliteGeneratedStorage: SQLITE_VIRTUAL | {NextWordIs("STORED")}? anyWord;

sqliteConflictResolution: ON CONFLICT sqliteConflictAction;

sqliteConflictAction: ROLLBACK | SQLITE_ABORT | SQLITE_FAIL | SQLITE_IGNORE | REPLACE;

columnName: identifier;

tsqlIdentity: {IsTsqlProceduralSyntax() && NextWordIs("IDENTITY")}? anyWord
    (LeftParen signedNumericLiteral Comma signedNumericLiteral RightParen)?
    (NOT FOR {NextWordIs("REPLICATION")}? anyWord)?;

// default

defaultClause: DEFAULT valueExpression;

// column constraints

columnConstraintDefinition: (constraintNameDefinition)? columnConstraint (constraintAttributes)?;

constraintNameDefinition: CONSTRAINT constraintName;

constraintName: qualifiedName;

columnConstraint: columnConstraintNotNull|{IsMySqlSyntax()}? NULL|columnConstraintUnique|columnConstraintPrimaryKey|referencesSpecification|checkConstraintDefinition;

columnConstraintNotNull: NOT NULL;

columnConstraintUnique: UNIQUE;

columnConstraintPrimaryKey: PRIMARY KEY;

checkConstraintDefinition: CHECK LeftParen searchCondition? RightParen;

// references

referencesSpecification: REFERENCES referencedTableAndColumns (MATCH matchType)? referentialTriggeredAction?;

referencedTableAndColumns: tableName (LeftParen referenceColumnList RightParen)?;

tableName: qualifiedName;

referenceColumnList: columnNameList;

columnNameList: columnName (Comma columnName)*;

matchType: FULL|PARTIAL;

referentialTriggeredAction: updateRule deleteRule? | deleteRule updateRule?;

updateRule: ON UPDATE referentialAction;

referentialAction: CASCADE|SET NULL|SET DEFAULT|NO ACTION;

deleteRule: ON DELETE referentialAction;

// search conditions

searchCondition: booleanXorTerm (OR booleanXorTerm)*;

booleanXorTerm: booleanTerm ({IsMySqlSyntax()}? MYSQL_XOR booleanTerm)*;

booleanTerm: booleanFactor (AND booleanFactor)*;

booleanFactor: (NOT)? booleanTest;

booleanTest: booleanPrimary (IS (NOT)? truthValue)?;

booleanPrimary: (predicate|LeftParen searchCondition RightParen|truthValue);

predicate: (existsPredicate|likePredicate|rowValuePredicate);

rowValuePredicate: rowValueConstructor (comparisonPredicate|betweenPredicate|inPredicate|nullPredicate|quantifiedComparisonPredicate|matchPredicate|overlapsPredicate)?;

comparisonPredicate: compOp rowValueConstructor
    | {AllowsDistinctComparison()}? IS NOT? DISTINCT FROM rowValueConstructor
    | {IsSqliteSyntax()}? IS NOT? rowValueConstructor;

betweenPredicate: NOT? BETWEEN rowValueConstructor AND rowValueConstructor;

inPredicate: NOT? IN inPredicateValue;

nullPredicate: IS (NOT? NULL | NOTNULL);

quantifiedComparisonPredicate: compOp quantifier (tableSubquery|{IsPostgresSyntax()}? postgresQuantifiedValue);

postgresQuantifiedValue: LeftParen valueExpression RightParen;

matchPredicate: MATCH (UNIQUE)? (matchType)? tableSubquery?;

overlapsPredicate: OVERLAPS rowValueConstructor?;

compOp: ({IsMySqlSyntax()}? MySqlNullSafeEquals|EqualsOperator|NotEqualsOperator|LessThanOperator|GreaterThanOperator
    |LessThanOrEqualsOperator|GreaterThanOrEqualsOperator|Tilda|PostgresRegexOperator|PostgresPredicateOperator);

quantifier: (ALL|SOME|ANY);

truthValue: (TRUE|FALSE|UNKNOWN);

existsPredicate: EXISTS tableSubquery;

likePredicate: matchValue NOT? (LIKE|ILIKE|{IsSqliteSyntax()}? (SQLITE_GLOB|MATCH)
    |{IsSqliteSyntax() || IsMySqlSyntax()}? REGEXP|{IsMySqlSyntax()}? MYSQL_RLIKE)
    pattern (ESCAPE escapeCharacter)?;

matchValue: characterValueExpression;

pattern: characterValueExpression;

escapeCharacter: characterValueExpression;

inPredicateValue: (tableSubquery|(LeftParen (inPredicateItem (Comma inPredicateItem)*) RightParen));

rowValueConstructor: (rowValueConstructorElement|LeftParen rowValueConstructorList RightParen|rowSubquery);

rowValueConstructorElement: (valueExpression|defaultSpecification);

nullSpecification: NULL;

defaultSpecification: DEFAULT;

rowValueConstructorList: rowValueConstructorElement (Comma rowValueConstructorElement)*;

rowSubquery: subquery;

generalValueSpecification: (parameterSpecification|dynamicParameterSpecification|USER|CURRENT_USER|SESSION_USER|SYSTEM_USER|VALUE);

parameterSpecification: parameterName (indicatorParameter)?;

parameterName: Colon identifier;

indicatorParameter: (INDICATOR)? parameterName;

dynamicParameterSpecification: QuestionMark;

columnReference: qualifiedName tupleRefSuffix?;

tupleRefSuffix: Period Asterisk;

valueReference: oracleOuterJoinReference | valueRefNestedExpr valueRefIndexingStep* (valueRefMemberStep valueRefIndexingStep*)*
              | columnReference (valueRefIndexingStep+ (valueRefMemberStep valueRefIndexingStep*)*)?;

oracleOuterJoinReference: {IsOracleSyntax()}? columnReference LeftParen PlusSign RightParen;

valueRefNestedExpr: LeftParen valueReference RightParen;

valueRefIndexingStep: LeftBracket (valueRefIndexingStepDirect|valueRefIndexingStepSlice) RightBracket;

valueRefIndexingStepDirect: signedNumericLiteral;

valueRefIndexingStepSlice: signedNumericLiteral? Colon signedNumericLiteral?;

valueRefMemberStep: Period identifier?;

correlationName: identifier;

withClause: WITH RECURSIVE? cteList
    | {IsTsqlProceduralSyntax()}? WITH {NextWordIs("XMLNAMESPACES")}? anyWord LeftParen
      xmlNamespaceItem (Comma xmlNamespaceItem)* RightParen (Comma cteList)?;

xmlNamespaceItem: DEFAULT characterStringLiteral | characterStringLiteral AS identifier;

cteList: with_list_element (Comma with_list_element)*;

with_list_element: queryName (LeftParen columnNameList RightParen)? AS
    (cteMaterialization? (subquery|cteDataStatement) | anyWordsWithProperty subquery) oracleSearchClause? oracleCycleClause?;

cteMaterialization: {IsPostgresSyntax() || IsSqliteSyntax()}? NOT? MATERIALIZED;

cteDataStatement: {IsPostgresSyntax()}? LeftParen withClause? (insertStatement|updateStatement|deleteStatement|mergeStatement) RightParen;

queryName: identifier;

// select, subquery

scalarSubquery: subquery;

subquery: LeftParen directSubqueryExpression RightParen;

directSubqueryExpression: withClause? queryExpression;

unionTerm: UNION (ALL)? (correspondingSpec)? queryTerm;

exceptTerm: EXCEPT (ALL)? (correspondingSpec)? queryTerm;

nonJoinQueryExpression: nonJoinQueryTerm|queryExpression;

nonJoinQueryTerm: queryPrimary intersectTerm*;

intersectTerm: (INTERSECT (ALL)? (correspondingSpec)? queryPrimary);

nonJoinQueryPrimary: (simpleTable|LeftParen nonJoinQueryExpression RightParen);

simpleTable: (querySpecification|tableValueConstructor|explicitTable);

querySpecification: SELECT ({IsMySqlSyntax()}? SQL_CALC_FOUND_ROWS)? setQuantifier? topExpression? selectList tableExpression?
    | {Dialect == "duckdb"}? fromClause SELECT selectList whereClause?;

setQuantifier: DISTINCT distinctOnClause? | ALL | UNIQUE;

distinctOnClause: {IsPostgresSyntax()}? ON LeftParen valueExpression (Comma valueExpression)* RightParen;

selectList: selectSublist (Comma selectSublist)*; // (Comma selectSublist)* contains any quantifier for error recovery;

selectSublist: Asterisk wildcardModifiers?|derivedColumn;

wildcardModifiers:
    {Dialect == "bigquery" && TokenStream.LA(1) == EXCEPT}?
        EXCEPT LeftParen columnNameList RightParen (REPLACE LeftParen derivedColumn (Comma derivedColumn)* RightParen)?
    | {Dialect is "duckdb" or "snowflake"}? EXCLUDE LeftParen columnNameList RightParen
        (RENAME LeftParen columnName AS columnName (Comma columnName AS columnName)* RightParen)?;

derivedColumn: {IsTsqlProceduralSyntax()}? columnName EqualsOperator valueExpression
    | {IsSqliteSyntax()}? valueExpression IS NOT? valueExpression asClause?
    | valueExpression asClause?;

asClause: (AS)? columnName;

tableExpression: fromClause prewhereClause? whereClause? (hierarchicalClause|oracleHierarchyClause)? groupByClause?
    havingClause? oracleModelClause? windowClause? qualifyClause? preferenceClause?
    | whereClause groupByClause? havingClause? windowClause? qualifyClause?;

prewhereClause: {Dialect == "clickhouse"}? DK_PREWHERE searchCondition;

hierarchicalClause: {Dialect == "exasol"}? (DK_START WITH searchCondition)?
    DK_CONNECT BY PRIOR? searchCondition;

preferenceClause: {Dialect == "exasol"}? DK_PREFERRING (DK_HIGH|DK_LOW) valueExpression;

windowClause: WINDOW identifier AS LeftParen windowSpecification RightParen
    (Comma identifier AS LeftParen windowSpecification RightParen)*;

qualifyClause: {Dialect is "ansi" or "bigquery" or "databricks" or "duckdb" or "redshift" or "snowflake" or "teradata" or "vertica"}? QUALIFY searchCondition;

queryPrimary: nonJoinQueryPrimary;

queryTerm: nonJoinQueryTerm;

queryExpression: nonJoinQueryTerm (unionTerm|exceptTerm|oracleMinusTerm)* orderByClause? limitClause? offsetClause?
    fetchClause? (forClause|mySqlShareLock)? optionClause? queryDialectTail?;

queryDialectTail: {Dialect == "db2"}? WITH (DK_UR|DK_CS|DK_RS|DK_RR)
    | {Dialect is "sparksql" or "databricks" or "hive"}? DK_DISTRIBUTE BY valueExpression (Comma valueExpression)*
        (DK_SORT BY sortSpecificationList)?
    | {Dialect == "vertica"}? DK_SEGMENTED BY valueExpression ALL DK_NODES;

// from

fromClause: FROM tableReference (Comma fromClauseTerm)* lateralViewClause*;

fromClauseTerm:  LATERAL? tableReference;

nonjoinedTableReference: (tableName tsqlLegacyTableHints
    | openJsonTable | mySqlJsonTable | functionCallExpression ordinalityClause? | oracleCollectionTable | oracleJsonTable | oracleXmlTable
    | tableName oracleDatabaseLink? (oracleFlashbackClause|oracleSampleClause)? (PARTITION anyProperty)? (tableSourceModifier|mariaDbSystemTime)?
    | derivedTable | sqliteJoinedSource | parenthesizedJoinedTable
    | {IsMySqlSyntax()}? LeftParen tableReference (Comma tableReference)* RightParen)
    (oraclePivotClause|oracleUnpivotClause|oracleMatchRecognizeClause)? correlationSpecification?
    oracleJoinPartitionClause? tableOffsetClause? mySqlIndexHint* postgresTableSample?
    (sqliteIndexHint | {!IsSqliteSyntax()}? tableReferenceHints)??;

parenthesizedJoinedTable: {IsPostgresSyntax()}? LeftParen joinedTable RightParen;

postgresTableSample: {IsPostgresSyntax() && NextWordIs("TABLESAMPLE")}? postgresKeyword identifier
    LeftParen valueExpression (Comma valueExpression)* RightParen (REPEATABLE LeftParen valueExpression RightParen)?;

sqliteJoinedSource: {IsSqliteSyntax()}? LeftParen tableReference (Comma tableReference)* RightParen;

sqliteIndexHint: {IsSqliteSyntax()}? (SQLITE_INDEXED BY identifier | NOT SQLITE_INDEXED);

ordinalityClause: {Dialect is "postgres" or "greenplum" or "trino" or "athena"}? WITH DK_ORDINALITY;

tableOffsetClause: {Dialect == "bigquery"}? WITH OFFSET (AS? columnName)?;

tableSourceModifier: {Dialect == "clickhouse"}? DK_FINAL
    | {Dialect == "databricks"}? DK_VERSION AS OF valueExpression
    | {Dialect == "flink"}? FOR DK_SYSTEM_TIME AS OF valueExpression;

lateralViewClause: {Dialect is "hive" or "sparksql" or "databricks"}? LATERAL VIEW OUTER? functionCallExpression identifier AS columnNameList;

tsqlLegacyTableHints: {HasLegacyTableHintAhead()}? LeftParen anyWord RightParen;

oracleDatabaseLink: {IsOracleSyntax()}? BatchVariableName;

// Match the table before testing for JOIN clauses. Both original alternatives
// started with nonjoinedTableReference, so prediction could scan a whole derived table.
tableReference: nonjoinedTableReference (naturalJoinTerm|crossJoinTerm|oracleApplyTerm|applyJoinTerm)*
    | LeftParen joinedTable RightParen (naturalJoinTerm|crossJoinTerm|oracleApplyTerm|applyJoinTerm)+;

mySqlIndexHint: {IsMySqlSyntax()}? (MYSQL_USE|MYSQL_FORCE|MYSQL_IGNORE) (INDEX|KEY)
    (FOR (JOIN|ORDER BY|GROUP BY))? LeftParen (identifier (Comma identifier)*)? RightParen;

mariaDbSystemTime: {Dialect == "mariadb"}? FOR MARIA_SYSTEM_TIME (AS OF valueExpression |
    BETWEEN valueExpression AND valueExpression | FROM valueExpression TO valueExpression | ALL);

tableReferenceHints: {!IsTsqlForOutputClauseAhead()}? (tableHintKeywords|anyWord)+ anyProperty; // dialect-specific options, should be described and moved to dialects in future

joinedTable: (nonjoinedTableReference|(LeftParen joinedTable RightParen)) (naturalJoinTerm|crossJoinTerm|oracleApplyTerm|applyJoinTerm)+;

correlationSpecification: { ValidCorrelationNameFollows() }? (AS)? correlationName (LeftParen derivedColumnList RightParen)?;

derivedColumnList: columnNameList;

derivedTable: tableSubquery;

tableSubquery: subquery;

//joins

crossJoinTerm: CROSS JOIN LATERAL? tableReference;

naturalJoinTerm: ((NATURAL? joinType? tsqlJoinHint? JOIN LATERAL?)|(STRAIGHT_JOIN)
    | {Dialect == "duckdb"}? DK_ASOF joinType? JOIN) tableReference (joinSpecification)?;

tsqlJoinHint: {IsTsqlProceduralSyntax()}? (LOOP | MERGE | {NextWordIs("HASH") || NextWordIs("REMOTE")}? anyWord);

applyJoinTerm: {IsTsqlProceduralSyntax()}? (CROSS | OUTER) {NextWordIs("APPLY")}? anyWord tableReference;

openJsonTable: {IsTsqlProceduralSyntax() && NextWordIs("OPENJSON")}? anyWord
    LeftParen valueExpression (Comma valueExpression)? RightParen openJsonWithClause?;

openJsonWithClause: WITH LeftParen openJsonColumn (Comma openJsonColumn)* RightParen;

openJsonColumn: columnName dataType characterStringLiteral? (AS {NextWordIs("JSON")}? anyWord)?;

joinType: (INNER|outerJoinType (OUTER)?|UNION);

outerJoinType: (LEFT|RIGHT|FULL);

joinSpecification: (joinCondition|namedColumnsJoin);

joinCondition: ON searchCondition;

namedColumnsJoin: USING LeftParen joinColumnList RightParen;

joinColumnList: columnNameList;

// conditions

whereClause: WHERE searchCondition;

groupByClause: GROUP BY groupingColumnReferenceList ({IsMySqlSyntax()}? WITH ROLLUP)?;

groupingColumnReferenceList: groupingColumnReference (Comma groupingColumnReference)*;

groupingColumnReference: groupingSetList | groupingOperation | valueExpression;

groupingSetList: {AllowsGroupingSets()}? GROUPING SETS LeftParen groupingSet (Comma groupingSet)* RightParen;

groupingSet: groupingOperation
    | LeftParen (valueExpression (Comma valueExpression)*)? RightParen
    | valueExpression;

groupingOperation: (ROLLUP|CUBE) LeftParen groupingOperationItem (Comma groupingOperationItem)* RightParen;

groupingOperationItem: LeftParen valueExpression (Comma valueExpression)* RightParen
    | valueExpression;

havingClause: HAVING searchCondition;

tableValueConstructor: VALUES (rowValueConstructor (Comma rowValueConstructor)*);

explicitTable: TABLE tableName?;

correspondingSpec: CORRESPONDING (BY LeftParen correspondingColumnList RightParen)?;

correspondingColumnList: columnNameList;

caseExpression: (caseAbbreviation|simpleCase|searchedCase);

caseAbbreviation: (NULLIF LeftParen valueExpression Comma valueExpression RightParen|COALESCE LeftParen valueExpression (Comma valueExpression)+ RightParen);

simpleCase: CASE (valueExpression|searchCondition)? simpleWhenClause+ (elseClause)? END;

simpleWhenClause: WHEN (valueExpression|searchCondition)? THEN result?;

result: valueExpression|searchCondition;

elseClause: ELSE result?;

searchedCase: CASE (searchedWhenClause)* (elseClause)? END;

searchedWhenClause: WHEN searchCondition? THEN result?;

castSpecification: CAST LeftParen castOperand AS castDataType RightParen;

castDataType: dataType typeModifier* | {IsMySqlSyntax()}? (MYSQL_SIGNED|MYSQL_UNSIGNED) MYSQL_INTEGER?;

castOperand: valueExpression;

overClause: OVER (identifier|LeftParen windowSpecification RightParen);

windowSpecification: identifier? (PARTITION BY valueExpression (Comma valueExpression)*)? orderByClause? windowFrame?;

windowFrame: (ROWS | RANGE | {AllowsWindowExclusion()}? GROUPS)
    (windowFrameStart | BETWEEN windowFrameStart AND windowFrameEnd) windowExclusion?;

windowFrameStart: UNBOUNDED PRECEDING | CURRENT ROW | valueExpression (PRECEDING|FOLLOWING);

windowFrameEnd: UNBOUNDED FOLLOWING | CURRENT ROW | valueExpression (PRECEDING|FOLLOWING);

windowExclusion: {AllowsWindowExclusion()}? EXCLUDE (CURRENT ROW|GROUP|TIES|NO OTHERS);

// functions and operators
//valueExpression: (numericValueExpression|characterValueExpression|datetimeValueExpression|intervalValueExpression);

valueExpression: postgresBitwiseExpression|valueExpressionPrimaryBased|extractExpressionBased|extractExpression|anyWordsWithPropertyBased
    |valueExpressionPrimarySignedBased|extractExpressionSignedBased|anyWordsWithPropertySignedBased
    |intervalExpressionBased|{IsMySqlSyntax()}? signedNumericLiteral|(sign? valueExpressionPrimary);

valueExpressionPrimarySignedBased: {HasValueExpressionOperators}? sign valueExpressionPrimary (                       numericOperation|intervalOperation|intervalOperation2);
      valueExpressionPrimaryBased:      {HasValueExpressionOperators}? valueExpressionPrimary (concatenationOperation|numericOperation|intervalOperation|intervalOperation2);
     extractExpressionSignedBased: {HasValueExpressionOperators}? sign extractExpression      (                       numericOperation|                  intervalOperation2);
           extractExpressionBased:      {HasValueExpressionOperators}? extractExpression      (                       numericOperation|                  intervalOperation2);
  anyWordsWithPropertySignedBased: {HasValueExpressionOperators}? sign anyWordsWithProperty   (                       numericOperation|                  intervalOperation2);
        anyWordsWithPropertyBased:      {HasValueExpressionOperators}? anyWordsWithProperty   (concatenationOperation|numericOperation|                  intervalOperation2);
          intervalExpressionBased: {HasValueExpressionOperators}? (LeftParen datetimeValueExpression MinusSign anyWordsWithProperty RightParen intervalQualifier) (sign intervalTerm)*;

concatenationOperation: (ConcatenationOperator characterPrimary)+;

numericOperation: (numericOperator factor)+ (sign term)* | (sign term)+;

numericOperator: Asterisk | Solidus | ShiftLeft | ShiftRight | {IsSqliteSyntax()}? (Percent|Ampersand|VerticalBar)
    | {Dialect is "trino" or "athena" or "sparksql" or "databricks"}? Percent;

intervalOperation:       intervalQualifier?((((Asterisk|Solidus|ShiftLeft|ShiftRight) factor)+ (sign intervalTerm)*)|((sign intervalTerm)+));

intervalOperation2: Asterisk intervalFactor((((Asterisk|Solidus|ShiftLeft|ShiftRight) factor)+ (sign intervalTerm)*)|((sign intervalTerm)+));

postgresBitwiseExpression: {IsPostgresSyntax()}? postgresBitwiseOperand
    ((Ampersand|VerticalBar|Hashtag) postgresBitwiseOperand)+;

postgresBitwiseOperand: sign? valueExpressionPrimary numericOperation?;

valueExpressionPrimary: valueExpressionAtom
    ({IsPostgresSyntax()}? postgresValuePostfix+
    | (JsonArrow characterStringLiteral)* ({!IsOracleSyntax()}? valueExpressionAttributeSpec)? valueExpressionCastSpec? mySqlCollation?
        ({IsSqliteSyntax()}? expressionCollation)?);

postgresValuePostfix: valueExpressionCastSpec | valueRefIndexingStep
    | (JsonArrow|PostgresJsonPathOperator) postgresPostfixOperand
    | {NextWordIs("AT")}? postgresKeyword TIME ZONE postgresPostfixOperand
    | expressionCollation;

expressionCollation: {NextWordIs("COLLATE")}? postgresKeyword qualifiedName;

postgresPostfixOperand: signedNumericLiteral|generalLiteral|variableExpression
    | functionCallExpression|qualifiedName|LeftParen valueExpression RightParen;

mySqlCollation: {IsMySqlSyntax()}? MYSQL_COLLATE identifier;

valueExpressionAttributeSpec: Colon Identifier; // https://docs.snowflake.com/en/user-guide/tutorials/json-basics-tutorial

valueExpressionCastSpec: {AllowsTypeCast()}? TypeCast dataType typeModifier*;

valueExpressionAtom: oracleExpression|postgresXmlElement|postgresXmlAttributes|unsignedNumericLiteral|generalLiteral|countAllExpression|arrayValueConstructor
    |scalarSubquery|caseExpression|dollarQuotedBody|LeftParen valueExpression RightParen|castSpecification
    |nullSpecification|truthValue|variableExpression|generalValueSpecification|mySqlSpecialFunction|functionCallExpression|aggregateExpression|valueReference|anyWordsWithProperty
    |{IsSqliteSyntax()}? Tilda valueExpressionAtom
    |{IsMySqlSyntax()}? sign valueExpressionAtom;

arrayValueConstructor: {AllowsArrayLiteral()}? ARRAY? LeftBracket
    (valueExpression (Comma valueExpression)*)? RightBracket
    | {Dialect == "duckdb"}? LeftBracket valueExpression FOR identifier IN valueExpression (IF searchCondition)? RightBracket;

variableExpression: MySqlSystemVariable|BatchVariableName|ClientVariableName|PositionalParameter|SqliteParameter|namedParameter|anonymouseParameter;

namedParameter: {AllowsNamedParameters}? (Colon|CustomNamedParameterPrefix) Identifier;

anonymouseParameter: {AllowsAnonymousParameters}? (QuestionMark|CustomAnonymousParameterMark);

numericPrimary: (valueExpressionPrimary|extractExpression);

factor: (sign | {IsSqliteSyntax()}? Tilda)? numericPrimary;

term: factor (numericOperator factor)*;
// numericValueExpression: term (sign term)*;

characterPrimary: (valueExpressionPrimary);

characterValueExpression: characterPrimary (ConcatenationOperator characterPrimary)*;

intervalPrimary: valueExpressionPrimary (intervalQualifier)?;

intervalFactor: sign? intervalPrimary;

intervalTerm: ((intervalFactor)|(term Asterisk intervalFactor)) ((Asterisk|Solidus|ShiftLeft|ShiftRight) factor)*;

intervalValueExpression: ((intervalTerm)|(LeftParen datetimeValueExpression MinusSign anyWordsWithProperty RightParen intervalQualifier)) (sign intervalTerm)*;

datetimeValueExpression: (anyWordsWithProperty|(intervalValueExpression PlusSign anyWordsWithProperty)) (sign intervalTerm)*;

extractSource: (datetimeValueExpression|intervalValueExpression); // L

countAllExpression: COUNT LeftParen Asterisk RightParen filterClause? overClause?;

extractExpression: EXTRACT LeftParen extractField FROM extractSource RightParen;

postgresXmlElement: {IsPostgresSyntax() && NextWordIs("XMLELEMENT")}? anyWord LeftParen
    {NextWordIs("NAME")}? postgresKeyword identifier (Comma valueExpression)* RightParen;

postgresXmlAttributes: {IsPostgresSyntax() && NextWordIs("XMLATTRIBUTES")}? anyWord LeftParen
    valueExpression (AS identifier)? (Comma valueExpression (AS identifier)?)* RightParen;

extractField: (datetimeField|timeZoneField);

datetimeField: (nonSecondDatetimeField|SECOND);

timeZoneField: (TIMEZONE_HOUR|TIMEZONE_MINUTE);

// constraints

constraintAttributes: (constraintCheckTime ((NOT)? DEFERRABLE)?|(NOT)? DEFERRABLE (constraintCheckTime)?);

constraintCheckTime: (INITIALLY DEFERRED|INITIALLY IMMEDIATE);

tableConstraintDefinition: (constraintNameDefinition)? tableConstraint
    ({IsSqliteSyntax()}? constraintAttributes | {!IsSqliteSyntax()}? constraintCheckTime)?;

tableConstraint: (uniqueConstraintDefinition|referentialConstraintDefinition|checkConstraintDefinition);

uniqueConstraintDefinition: (UNIQUE|PRIMARY KEY) LeftParen uniqueColumnList RightParen ({IsSqliteSyntax()}? sqliteConflictResolution)?;

uniqueColumnList: columnNameList;

referentialConstraintDefinition: FOREIGN KEY LeftParen referencingColumns RightParen referencesSpecification;

referencingColumns: referenceColumnList;

// order by

orderByClause: ORDER ({IsOracleSyntax()}? ORACLE_SIBLINGS)? BY sortSpecificationList;

limitClause: {AllowsLimit()}? LIMIT (ALL | valueExpression) (Comma valueExpression)?
    ({Dialect == "clickhouse"}? BY valueExpression (Comma valueExpression)* | {Dialect == "vertica"}? overClause)?;

offsetClause: OFFSET valueExpression (ROW|ROWS)?;

fetchClause: FETCH (FIRST|NEXT) valueExpression? ({IsOracleSyntax()}? PERCENT)? (ROW|ROWS) (ONLY|WITH TIES);

forClause: FOR ((UPDATE|{NextWordIs("SHARE")}? anyWord|postgresLockStrength) (OF qualifiedName (Comma qualifiedName)*)? (SKIP_KW LOCKED|{NextWordIs("NOWAIT")}? anyWord)?
    | {IsTsqlProceduralSyntax()}? (tsqlForXml | tsqlForJson));

tsqlForXml: {NextWordIs("XML")}? anyWord
    ({NextWordIs("RAW") || NextWordIs("PATH")}? anyWord tsqlQuotedOption?
    | {NextWordIs("AUTO") || NextWordIs("EXPLICIT")}? anyWord)
    (Comma tsqlXmlOption)*;

tsqlQuotedOption: LeftParen characterStringLiteral RightParen;

tsqlXmlOption: TYPE
    | {NextWordIs("ROOT") || NextWordIs("XMLDATA") || NextWordIs("XMLSCHEMA")}? anyWord tsqlQuotedOption?
    | {NextWordIs("ELEMENTS")}? anyWord ({NextWordIs("XSINIL") || NextWordIs("ABSENT")}? anyWord)?
    | {NextWordIs("BINARY")}? anyWord {NextWordIs("BASE64")}? anyWord;

tsqlForJson: {NextWordIs("JSON")}? anyWord
    ({NextWordIs("AUTO") || NextWordIs("PATH")}? anyWord)
    (Comma ({NextWordIs("ROOT")}? anyWord tsqlQuotedOption?
    | {NextWordIs("INCLUDE_NULL_VALUES") || NextWordIs("WITHOUT_ARRAY_WRAPPER")}? anyWord))*;

postgresLockStrength: {IsPostgresSyntax()}? (NO KEY UPDATE | KEY {NextWordIs("SHARE")}? postgresKeyword);

mySqlShareLock: {IsMySqlSyntax()}? MYSQL_LOCK IN {NextWordIs("SHARE")}? anyWord MYSQL_MODE;

optionClause: {IsTsqlProceduralSyntax()}? OPTION LeftParen queryHint (Comma queryHint)* RightParen;

queryHint: (~(LeftParen|RightParen|Comma|Semicolon))+;

sortSpecificationList: sortSpecification (Comma sortSpecification)*;

sortSpecification: sortKey orderingSpecification? nullOrdering?;

sortKey: valueExpression | columnIndex;

columnIndex: UnsignedInteger;

orderingSpecification: ASC|DESC;

nullOrdering: {AllowsNullOrder()}? NULLS (FIRST|LAST);

// schema definition

sqlSchemaStatement: schemaDefinition|createCatalogDatabaseStatement|alterContainerStatement|dropCatalogDatabaseStatement|
    sqliteVirtualTableStatement|createNamedObjectStatement|createSynonymStatement|createTriggerStatement|createTableStatement|
    createViewStatement|createIndexStatement|alterTableStatement|alterTriggerStatement|dropSchemaStatement|dropTableStatement|
    dropViewStatement|dropProcedureStatement|dropTriggerStatement|dropIndexStatement|dropCharacterSetStatement|mySqlEventStatement|
    alterNamedObjectStatement|dropNamedObjectStatement|renameNamedObjectStatement|oracleSchemaStatement|dialectSchemaStatement|postgresSchemaStatement;

dialectSchemaStatement:
    {Dialect == "databricks" && NextWordIs("DESCRIBE")}? DK_DESCRIBE DK_DETAIL qualifiedName
    | {Dialect == "impala" && NextWordIs("COMPUTE")}? DK_COMPUTE DK_INCREMENTAL
        DK_STATS tableName partitionSpec?
    | {Dialect == "impala" && NextWordIs("INVALIDATE")}? DK_INVALIDATE DK_METADATA tableName?
    | {Dialect == "materialize" && TokenStream.LA(1) == CREATE && NextWordIs("DEFAULT", 2)}? CREATE DEFAULT INDEX clusterClause? ON tableName
    | {Dialect == "materialize" && NextWordIs("SHOW")}? DK_SHOW MATERIALIZED DK_VIEWS FROM qualifiedName clusterClause?
    | {Dialect == "snowflake" && TokenStream.LA(1) == CREATE && NextWordIs("WAREHOUSE", 2)}?
        CREATE DK_WAREHOUSE qualifiedName WITH? tableProperty+
    | {Dialect == "vertica" && TokenStream.LA(1) == CREATE && NextWordIs("PROJECTION", 2)}?
        CREATE DK_PROJECTION qualifiedName AS queryExpression;

schemaDefinition: CREATE (OR REPLACE)? SCHEMA (IF NOT EXISTS)? schemaNameClause schemaCharacterSetSpecification? schemaElement*;

schemaNameClause: AUTHORIZATION schemaAuthorizationIdentifier|schemaName AUTHORIZATION schemaAuthorizationIdentifier|schemaName;

schemaAuthorizationIdentifier: authorizationIdentifier;

authorizationIdentifier: identifier;

schemaCharacterSetSpecification: DEFAULT CHARACTER SET characterSetSpecification;

schemaElement: createTableStatement|createViewStatement;

createTableStatement: createTableHead tableName
    (postgresPartitionTable | createTableExtraHead
        (tableElementList createTableTail | createTableProperties? AS queryExpression greenplumDistributionClause?
        | {IsMySqlSyntax()}? mySqlCreateTableOption+ AS queryExpression));

mySqlCreateTableOption: (MYSQL_ENGINE|MYSQL_CHARSET|MYSQL_COLLATE) EqualsOperator? identifier;

createTableHead: CREATE (OR REPLACE)? (GLOBAL|LOCAL)? (TEMPORARY|TEMP)? dialectTableModifier? TABLE (IF NOT EXISTS)?;

dialectTableModifier:
    {Dialect == "greenplum"}? DK_READABLE? DK_EXTERNAL
    | {Dialect is "hive" or "sparksql" or "impala"}? DK_EXTERNAL
    | {Dialect == "teradata"}? (DK_MULTISET|SET)? DK_VOLATILE;

createTableProperties: {Dialect is "trino" or "athena"}? WITH LeftParen tableProperty (Comma tableProperty)* RightParen;

tableProperty: identifier EqualsOperator valueExpression;

createTableExtraHead: (OF identifier)?;

greenplumDistributionClause: {Dialect == "greenplum" && NextWordIs("DISTRIBUTED")}? postgresKeyword
    (BY LeftParen columnNameList RightParen | {NextWordIs("RANDOMLY") || NextWordIs("REPLICATED")}? postgresKeyword);

tableElementList: LeftParen tableElement (Comma tableElement)* RightParen;

tableElement: (postgresLikeTable|columnDefinition|tableConstraintDefinition)
    ({!IsSqliteSyntax() && IsTableElementSuffixAllowed()}? anyUnexpected)??;

createTableTail: {IsSqliteTableOptionAhead()}? sqliteTableOption (Comma sqliteTableOption)*
    | dialectTableTail
    | {IsCreateTableTailAllowed()}? ddlStatementTail
    | ;

dialectTableTail:
    {Dialect == "clickhouse" && NextWordIs("ENGINE")}? DK_ENGINE EqualsOperator functionCallExpression
        (PARTITION BY valueExpression)? (ORDER BY (LeftParen sortSpecificationList RightParen|sortSpecificationList))?
    | {Dialect == "greenplum" && NextWordIs("LOCATION")}? DK_LOCATION LeftParen characterStringLiteral (Comma characterStringLiteral)* RightParen
        DK_FORMAT characterStringLiteral (LeftParen DK_HEADER RightParen)?
    | {Dialect is "hive" or "sparksql" or "impala"}? DK_PARTITIONED BY tableElementList
        DK_STORED AS identifier (DK_LOCATION characterStringLiteral)?
    | {Dialect == "teradata"}? PRIMARY INDEX LeftParen columnNameList RightParen ON COMMIT PRESERVE ROWS;

sqliteTableOption: {IsSqliteSyntax()}? (WITHOUT {NextWordIs("ROWID")}? anyWord | {NextWordIs("STRICT")}? anyWord);
//                 createTableTailForValues? createTableTailOther*
//                 createTableTailPartition? createTableTailOther*
//                 createTableTailOnCommit? createTableTailOther*;
//createTableTailPartition: PARTITION BY (RANGE|LIST|HASH|anyUnexpected) LeftParen createTableTailPartitionColumnSpec (Comma createTableTailPartitionColumnSpec)* RightParen;
//createTableTailPartitionColumnSpec: (columnName|valueExpression) anyWordsWithProperty?;
//createTableTailOnCommit: ON COMMIT (DELETE|PRESERVE) ROWS;
//createTableTailForValues: FOR VALUES createTablePartitionValuesSpec|DEFAULT;
//createTablePartitionValuesSpec: IN LeftParen queryExpression (Comma queryExpression)* RightParen |
//                     FROM LeftParen (queryExpression | MINVALUE | MAXVALUE) (Comma queryExpression | MINVALUE | MAXVALUE)* RightParen
//                       TO LeftParen (queryExpression | MINVALUE | MAXVALUE) (Comma queryExpression | MINVALUE | MAXVALUE)* RightParen |
//                     WITH LeftParen MODULUS signedNumericLiteral Comma REMAINDER signedNumericLiteral RightParen;
//createTableTailOther: ((~(LeftParen|RightParen|Period|Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective|Comma|
//        PARTITION|ON|FOR
//    ))|(LeftParen anyUnexpected* RightParen))+;

sqliteVirtualTableStatement: {IsSqliteSyntax()}? CREATE SQLITE_VIRTUAL TABLE (IF NOT EXISTS)? tableName
    USING identifier (LeftParen sqliteModuleArgument (Comma sqliteModuleArgument)* RightParen)?;

sqliteModuleArgument: valueExpression (EqualsOperator valueExpression)?;
//                 createTableTailForValues? createTableTailOther*
//                 createTableTailPartition? createTableTailOther*
//                 createTableTailOnCommit? createTableTailOther*;
//createTableTailPartition: PARTITION BY (RANGE|LIST|HASH|anyUnexpected) LeftParen createTableTailPartitionColumnSpec (Comma createTableTailPartitionColumnSpec)* RightParen;
//createTableTailPartitionColumnSpec: (columnName|valueExpression) anyWordsWithProperty?;
//createTableTailOnCommit: ON COMMIT (DELETE|PRESERVE) ROWS;
//createTableTailForValues: FOR VALUES createTablePartitionValuesSpec|DEFAULT;
//createTablePartitionValuesSpec: IN LeftParen queryExpression (Comma queryExpression)* RightParen |
//                     FROM LeftParen (queryExpression | MINVALUE | MAXVALUE) (Comma queryExpression | MINVALUE | MAXVALUE)* RightParen
//                       TO LeftParen (queryExpression | MINVALUE | MAXVALUE) (Comma queryExpression | MINVALUE | MAXVALUE)* RightParen |
//                     WITH LeftParen MODULUS signedNumericLiteral Comma REMAINDER signedNumericLiteral RightParen;
//createTableTailOther: ((~(LeftParen|RightParen|Period|Semicolon|Comma|
//        PARTITION|ON|FOR
//    ))|(LeftParen anyUnexpected* RightParen))+;

createViewStatement: CREATE (OR REPLACE)? mySqlViewOptions?
    (MATERIALIZED VIEW tableName (LeftParen viewColumnList RightParen)? oracleMaterializedViewOptions? clusterClause?
        AS queryExpression postgresWithDataClause?
    | VIEW ({IsSqliteSyntax()}? IF NOT EXISTS)? tableName (LeftParen viewColumnList RightParen)? clusterClause?
        AS queryExpression (WITH levelsClause? CHECK OPTION ({IsOracleSyntax()}? constraintNameDefinition)?)?);

mySqlViewOptions: {IsMySqlSyntax()}? (
    MYSQL_ALGORITHM EqualsOperator (MYSQL_UNDEFINED|MERGE|MYSQL_TEMPTABLE) mySqlDefiner? mySqlViewSecurity?
    | mySqlDefiner mySqlViewSecurity? | mySqlViewSecurity);

mySqlDefiner: DEFINER EqualsOperator (identifier|CURRENT_USER (LeftParen RightParen)?) (At identifier)?;

mySqlViewSecurity: MYSQL_SQL MYSQL_SECURITY (DEFINER|MYSQL_INVOKER);

clusterClause: {Dialect == "materialize"}? IN DK_CLUSTER qualifiedName;

viewColumnList: columnNameList;

levelsClause: (CASCADED|LOCAL);

// schema ddl

createIndexStatement: CREATE (OR REPLACE)? (UNIQUE|{IsOracleSyntax()}? ORACLE_BITMAP)? tsqlIndexKind? INDEX (IF NOT EXISTS)? qualifiedName ON tableName
    ({TokenStream.LA(1) == USING && NextWordIs("GIN", 2)}? ginIndexTail
    | {!(TokenStream.LA(1) == USING && NextWordIs("GIN", 2))}? indexDefinitionTail)?
    | {IsMySqlSyntax()}? CREATE (MYSQL_FULLTEXT|MYSQL_SPATIAL) INDEX (IF NOT EXISTS)? qualifiedName ON tableName
        LeftParen mySqlIndexColumn (Comma mySqlIndexColumn)* RightParen;

tsqlIndexKind: {IsTsqlProceduralSyntax() && (NextWordIs("CLUSTERED") || NextWordIs("NONCLUSTERED"))}? anyWord;

ginIndexTail: {IsPostgresSyntax()}? USING anyWord indexDefinitionTail;

indexDefinitionTail: indexDefinitionItem+;

indexDefinitionItem: LeftParen indexDefinitionItem* RightParen | ~(LeftParen|RightParen|Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective|GO);

createNamedObjectStatement: (CREATE (OR (REPLACE|ALTER))?|RECREATE) routineCreateModifier* createObjectKind (IF NOT EXISTS)? qualifiedName
    (routineDefinition|ddlStatementTail)?;

createObjectKind: FUNCTION|PROCEDURE|PROC|TABLE FUNCTION|SEQUENCE|ROLE;

postgresSchemaStatement: {IsPostgresSyntax()}? (CREATE postgresCreateObject | postgresGrantStatement);

postgresCreateObject: postgresCreateDomain | postgresCreateEnum | postgresCreatePolicy;

postgresCreateDomain: {NextWordIs("DOMAIN")}? postgresKeyword qualifiedName AS? dataType defaultClause? columnConstraintDefinition*;

postgresCreateEnum: TYPE qualifiedName AS {NextWordIs("ENUM")}? postgresKeyword
    LeftParen characterStringLiteral (Comma characterStringLiteral)* RightParen;

postgresCreatePolicy: {NextWordIs("POLICY")}? postgresKeyword identifier ON tableName
    (AS {NextWordIs("PERMISSIVE") || NextWordIs("RESTRICTIVE")}? postgresKeyword)?
    (FOR (ALL|SELECT|INSERT|UPDATE|DELETE))?
    (TO postgresPolicyRole (Comma postgresPolicyRole)*)?
    (USING LeftParen searchCondition RightParen)?
    (WITH CHECK LeftParen searchCondition RightParen)?;

postgresPolicyRole: identifier | CURRENT_USER | SESSION_USER;

postgresGrantStatement: {NextWordIs("GRANT")}? postgresKeyword
    (ALL ({NextWordIs("PRIVILEGES")}? postgresKeyword)? | postgresGrantPrivilege (Comma postgresGrantPrivilege)*)
    ON TABLE? tableName (Comma tableName)* TO identifier (Comma identifier)*
    (WITH {NextWordIs("GRANT")}? postgresKeyword OPTION)?;

postgresGrantPrivilege: (SELECT|INSERT|UPDATE|DELETE|REFERENCES|TRIGGER|{NextWordIs("TRUNCATE")}? postgresKeyword)
    (LeftParen columnNameList RightParen)?;

postgresPartitionTable: {IsPostgresSyntax()}? PARTITION OF tableName tableElementList?
    (DEFAULT | FOR VALUES postgresPartitionBounds);

postgresPartitionBounds: IN postgresPartitionValueList | FROM postgresPartitionValueList TO postgresPartitionValueList
    | WITH LeftParen {NextWordIs("MODULUS")}? postgresKeyword UnsignedInteger Comma {NextWordIs("REMAINDER")}? postgresKeyword UnsignedInteger RightParen;

postgresPartitionValueList: LeftParen valueExpression (Comma valueExpression)* RightParen;

postgresLikeTable: {IsPostgresSyntax()}? LIKE tableName postgresLikeOption*;

postgresLikeOption: {NextWordIs("INCLUDING") || NextWordIs("EXCLUDING")}? postgresKeyword
    (ALL|CONSTRAINTS|{NextWordIs("COMMENTS") || NextWordIs("COMPRESSION") || NextWordIs("DEFAULTS") ||
        NextWordIs("GENERATED") || NextWordIs("IDENTITY") || NextWordIs("INDEXES") || NextWordIs("STATISTICS") ||
        NextWordIs("STORAGE")}? postgresKeyword);

postgresWithDataClause: {IsPostgresSyntax()}? WITH NO? {NextWordIs("DATA")}? postgresKeyword;

createSynonymStatement: (CREATE (OR REPLACE)?) {IsOracleSyntax() || IsTsqlProceduralSyntax()}?
    SYNONYM qualifiedName FOR qualifiedName oracleDatabaseLink?;

alterNamedObjectStatement: ALTER alterObjectKind ifExistsSpec? qualifiedName routineSignature? setTableSchemaAction?
    (routineDefinition|ddlStatementTail)?;

alterObjectKind: VIEW|MATERIALIZED VIEW|INDEX|FUNCTION|PROCEDURE|PROC|SEQUENCE|SYNONYM;

dropIndexStatement: DROP INDEX CONCURRENTLY? ifExistsSpec? qualifiedName (ON tableName)? ddlStatementTail?;

dropNamedObjectStatement: DROP dropObjectKind ifExistsSpec? qualifiedName routineSignature? (Comma qualifiedName routineSignature?)* ddlStatementTail?;

dropObjectKind: FUNCTION|PROC|SEQUENCE|SYNONYM|ROLE|MATERIALIZED VIEW|identifier;

renameNamedObjectStatement: RENAME renameObjectKind qualifiedName TO qualifiedName (Comma qualifiedName TO qualifiedName)* ddlStatementTail?;

renameObjectKind: TABLE|VIEW|INDEX|SCHEMA|DATABASE|CATALOG|FUNCTION|PROCEDURE|SEQUENCE|SYNONYM|identifier;

routineSignature: LeftParen (routineArgument (Comma routineArgument?)* )? RightParen?;

ddlStatementTail: (~(Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective))+;

createCatalogDatabaseStatement: CREATE (OR REPLACE)? containerKind (IF NOT EXISTS)? qualifiedName containerStatementTail?;

alterContainerStatement: ALTER alterContainerKind ifExistsSpec? qualifiedName alterContainerAction;

alterContainerAction: renameContainerAction|nonRenameContainerTail;

renameContainerAction: RENAME TO qualifiedName containerStatementTail?;

nonRenameContainerTail: ~(RENAME|Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective) (~(Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective))*;

dropCatalogDatabaseStatement: DROP containerKind ifExistsSpec? qualifiedName containerStatementTail?;

containerKind: DATABASE|CATALOG;

alterContainerKind: SCHEMA|DATABASE|CATALOG;

containerStatementTail: (~(Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective))+;

dropSchemaStatement: DROP SCHEMA ifExistsSpec? schemaName dropBehaviour?;

dropBehaviour: (CASCADE|RESTRICT);

alterTableStatement: ALTER anyWord* TABLE (IF EXISTS)? ONLY? tableName
    (alterTableAction (Comma alterTableAction)* | {!IsTsqlProceduralSyntax() && !IsPostgresSyntax()}?);

alterTableAction: oracleAlterColumns|mySqlAddIndex|addColumnDefinition|alterColumnDefinition|renameColumnDefinition|dropColumnDefinition
    |addTableConstraintDefinition|dropTableConstraintDefinition|setTableSchemaAction|postgresRowLevelSecurity
    |{IsSqliteSyntax()}? RENAME TO identifier | {!IsSqliteSyntax() && !IsRowLevelSecurityAhead()}? anyWordsWithProperty;

postgresRowLevelSecurity: {IsPostgresSyntax() && IsRowLevelSecurityAhead()}? anyWord ROW LEVEL anyWord;

setTableSchemaAction: SET SCHEMA schemaName;

addColumnDefinition: ADD (COLUMN)? columnDefinition ({IsMySqlSyntax()}? (FIRST|AFTER columnName))?;

mySqlAddIndex: {IsMySqlSyntax()}? ADD (UNIQUE|MYSQL_FULLTEXT|MYSQL_SPATIAL)? (INDEX|KEY) identifier?
    LeftParen mySqlIndexColumn (Comma mySqlIndexColumn)* RightParen;

mySqlIndexColumn: columnName (LeftParen UnsignedInteger RightParen)? orderingSpecification?;

renameColumnDefinition: RENAME (COLUMN)? columnName TO identifier;

alterColumnDefinition: ALTER (COLUMN)? columnName alterColumnAction;

alterColumnAction: (setColumnDefaultClause|dropColumnDefaultClause|postgresAlterColumnNullability);

postgresAlterColumnNullability: {IsPostgresSyntax()}? (SET|DROP) NOT NULL;

setColumnDefaultClause: SET defaultClause;

dropColumnDefaultClause: DROP DEFAULT;

dropColumnDefinition: DROP COLUMN? columnName (dropBehaviour | {IsMySqlSyntax() || IsSqliteSyntax()}?);

addTableConstraintDefinition: ADD tableConstraintDefinition postgresConstraintValidation?;

postgresConstraintValidation: {IsPostgresSyntax()}? NOT {NextWordIs("VALID")}? postgresKeyword;

dropTableConstraintDefinition: DROP CONSTRAINT constraintName dropBehaviour;

dropTableStatement: DROP ({IsMySqlSyntax()}? TEMPORARY)? TABLE ifExistsSpec? tableName (Comma tableName)* (oracleDropTableOptions|dropBehaviour)?
    ({Dialect == "doris"}? DK_FORCE)?;

dropViewStatement: DROP VIEW ifExistsSpec? tableName (Comma tableName)* dropBehaviour?;

dropProcedureStatement: DROP PROCEDURE ifExistsSpec? qualifiedName ddlStatementTail?;

dropCharacterSetStatement: DROP CHARACTER SET characterSetName;

ifExistsSpec: IF EXISTS ;

// data statements

selectStatementSingleRow: SELECT (setQuantifier)? (topExpression)? selectList selectIntoClause tableExpression?;

selectIntoClause: {IsOracleSyntax()}? oracleIntoClause | {!IsOracleSyntax()}? INTO selectTargetList;

selectTargetList: selectTargetItem (Comma selectTargetItem)* Comma*;

selectTargetItem: parameterSpecification|tableName|anyUnexpected??;

deleteStatement: DELETE FROM tableName correlationSpecification? sqliteIndexHint? outputClause? postgresDeleteUsing?
    whereClause? orderByClause? limitClause? (oracleReturningClause|returningClause)?
    | {IsTsqlProceduralSyntax()}? DELETE tableName correlationSpecification? outputClause? fromClause? whereClause?
    | {IsMySqlSyntax()}? DELETE mySqlDeleteTarget (Comma mySqlDeleteTarget)* fromClause whereClause?
    | {IsMySqlSyntax()}? DELETE FROM mySqlDeleteTarget (Comma mySqlDeleteTarget)* USING tableReference (Comma tableReference)* whereClause?;

postgresDeleteUsing: {IsPostgresSyntax()}? USING tableReference (Comma tableReference)*;

mySqlDeleteTarget: tableName (Period Asterisk)?;

insertStatement: (INSERT ({IsSqliteSyntax()}? OR sqliteConflictAction)? | {IsSqliteSyntax()}? REPLACE) INTO?
    tableName ({IsPostgresSyntax()}? AS correlationName)? (LeftParen insertColumnList RightParen)? outputClause?
    (queryExpression|DEFAULT VALUES) conflictClause? ({IsSqliteSyntax()}? conflictClause)* (oracleReturningClause|returningClause)?
    | oracleInsertStatement
    | {Dialect == "doris"}? INSERT INTO tableName insertLabel (LeftParen insertColumnList RightParen)? queryExpression
    | {Dialect is "hive" or "sparksql" or "databricks" or "starrocks"}? INSERT DK_OVERWRITE TABLE? tableName
        partitionSpec? ({Dialect == "starrocks"}? insertLabel)? queryExpression
    | {IsMySqlSyntax()}? INSERT mySqlInsertPriority? MYSQL_IGNORE? INTO? tableName mySqlInsertSource mySqlDuplicateKey? returningClause?
    | {IsMySqlSyntax()}? REPLACE (MYSQL_LOW_PRIORITY|MYSQL_DELAYED)? INTO? tableName mySqlInsertSource returningClause?;

mySqlInsertPriority: MYSQL_LOW_PRIORITY|MYSQL_DELAYED|MYSQL_HIGH_PRIORITY;

mySqlInsertSource: SET setClauseList | (LeftParen insertColumnList RightParen)?
    (tableValueConstructor (AS correlationName (LeftParen columnNameList RightParen)?)? | withClause? queryExpression);

mySqlDuplicateKey: ON MYSQL_DUPLICATE KEY UPDATE setClauseList;

insertLabel: WITH DK_LABEL identifier;

dialectDataStatement:
    {Dialect == "redshift" && NextWordIs("UNLOAD")}? DK_UNLOAD LeftParen characterStringLiteral RightParen
        TO characterStringLiteral DK_IAM_ROLE (DEFAULT | characterStringLiteral)
        (DK_FORMAT AS? identifier)?
    | {Dialect == "teradata" && NextWordIs("LOCKING")}? DK_LOCKING ROW FOR DK_ACCESS
        DK_SEL selectList tableExpression?
    | {Dialect == "vertica" && NextWordIs("COPY")}? DK_COPY tableName
        (LeftParen columnNameList RightParen)? FROM DK_STDIN
        (DK_DELIMITER characterStringLiteral)? (DK_ABORT ON DK_ERROR)?;

partitionSpec: PARTITION LeftParen partitionItem (Comma partitionItem)* RightParen;

partitionItem: identifier (EqualsOperator valueExpression)?;

conflictClause: {AllowsConflictClause()}? ON CONFLICT (LeftParen columnNameList RightParen)? DO
    (NOTHING | UPDATE SET setClauseList whereClause?);

returningClause: {AllowsReturning()}? RETURNING selectList;

outputClause: {IsTsqlProceduralSyntax()}? OUTPUT selectList (INTO tableName (LeftParen columnNameList RightParen)?)?;

mergeStatement: {AllowsMerge()}? MERGE INTO? tableName correlationSpecification? USING tableReference ON searchCondition
    mergeWhenClause+ mergeOutputClause?;

mergeOutputClause: {IsTsqlProceduralSyntax()}? OUTPUT mergeOutputItem (Comma mergeOutputItem)*
    (INTO tableName (LeftParen columnNameList RightParen)?)?;

mergeOutputItem: DollarAction asClause? | selectSublist;

mergeWhenClause: WHEN MATCHED (AND searchCondition)? THEN mergeUpdateOrDelete
    | WHEN NOT MATCHED ({AllowsMergeByTarget()}? BY {NextWordIs("TARGET")}? anyWord)? (AND searchCondition)? THEN
      INSERT (LeftParen columnNameList RightParen)? tableValueConstructor ({IsOracleSyntax()}? whereClause)?
    | WHEN NOT MATCHED {AllowsMergeByTarget()}? BY {NextWordIs("SOURCE")}? anyWord (AND searchCondition)? THEN mergeUpdateOrDelete;

mergeUpdateOrDelete:
    UPDATE SET setClauseList oracleMergeUpdateTail? | DELETE;

insertColumnList: columnNameList;

topExpression: {AllowsTop()}? TOP anyValue PERCENT? (WITH TIES)?;

// UPDATE

updateStatement: UPDATE ({IsSqliteSyntax()}? OR sqliteConflictAction)?
    tableReference SET setClauseList outputClause? fromClause? whereClause? orderByClause? limitClause? (oracleReturningClause|returningClause)?;

setClauseList: setClause (Comma setClause)*;

setClause: (setTarget | setTargetList) assignmentOperator updateSource;

assignmentOperator: EqualsOperator | {IsTsqlProceduralSyntax()}? CompoundAssignment;
// Keep columnName for bare-target completion predictions alongside qualified value references.

setTarget: valueReference|columnName|variableExpression;

setTargetList: LeftParen (setTarget (Comma setTarget?)*)? RightParen?;

updateSource: updateValue | (LeftParen updateValue (Comma updateValue)* RightParen?);

updateValue: valueExpression|DEFAULT;

// transactions

sqlTransactionStatement: sqliteTransactionStatement|setTransactionStatement|setConstraintsModeStatement|commitStatement|rollbackStatement
    |oracleSavepointStatement|postgresTransactionStatement|savepointStatement|mySqlStartTransaction;

sqliteTransactionStatement: {IsSqliteSyntax()}? (
    BEGIN (DEFERRED|IMMEDIATE|SQLITE_EXCLUSIVE)? TRANSACTION?
    | (COMMIT|END) TRANSACTION?
    | ROLLBACK TRANSACTION? (TO SQLITE_SAVEPOINT? identifier)?
    | SQLITE_SAVEPOINT identifier
    | SQLITE_RELEASE SQLITE_SAVEPOINT? identifier);

sqliteUtilityStatement: {IsSqliteSyntax()}? (
    SQLITE_PRAGMA qualifiedName (EqualsOperator sqlitePragmaValue | sqlitePragmaArgument)?
    | SQLITE_ATTACH DATABASE? valueExpression AS identifier
    | SQLITE_DETACH DATABASE? identifier
    | SQLITE_VACUUM identifier? (INTO valueExpression)?);

sqlitePragmaArgument: LeftParen sqlitePragmaValue RightParen;

sqlitePragmaValue: signedNumericLiteral | characterStringLiteral | ON | TRUE | FALSE | identifier;

mySqlStartTransaction: {IsMySqlSyntax()}? MYSQL_START TRANSACTION
    (mySqlTransactionCharacteristic (Comma mySqlTransactionCharacteristic)*)?;

mySqlTransactionCharacteristic: WITH MYSQL_CONSISTENT MYSQL_SNAPSHOT | transactionAccessMode;

savepointStatement: {IsMySqlSyntax()}? (MYSQL_SAVEPOINT identifier | MYSQL_RELEASE MYSQL_SAVEPOINT identifier);

setTransactionStatement: SET TRANSACTION transactionMode (Comma transactionMode)*;

transactionMode: (isolationLevel|transactionAccessMode);

isolationLevel: ISOLATION LEVEL levelOfIsolation;

levelOfIsolation: (READ UNCOMMITTED|READ COMMITTED|REPEATABLE READ|SERIALIZABLE);

transactionAccessMode: (READ ONLY|READ WRITE);

setConstraintsModeStatement: SET CONSTRAINTS constraintNameList (DEFERRED|IMMEDIATE);

constraintNameList: (ALL|constraintName (Comma constraintName)*);

commitStatement: COMMIT WORK? mySqlTransactionCompletion?;

rollbackStatement: ROLLBACK WORK? (mySqlTransactionCompletion | {IsMySqlSyntax()}? TO MYSQL_SAVEPOINT? identifier
    | {IsOracleSyntax()}? TO ORACLE_SAVEPOINT? identifier | {IsPostgresSyntax()}? TO postgresSavepointKeyword? identifier)?;

// session

postgresTransactionStatement: {IsPostgresSyntax()}? (BEGIN (WORK|TRANSACTION)? (transactionMode (Comma transactionMode)*)?
    | postgresSavepointKeyword identifier
    | {NextWordIs("RELEASE")}? postgresKeyword postgresSavepointKeyword? identifier);

postgresSavepointKeyword: {NextWordIs("SAVEPOINT")}? postgresKeyword;

// session

postgresUtilityStatement: {IsPostgresSyntax()}? (postgresPrepareStatement|postgresNotificationStatement|postgresTruncateStatement
    |postgresCopyStatement|postgresVacuumStatement|postgresRefreshViewStatement);

postgresPrepareStatement: {NextWordIs("PREPARE")}? postgresKeyword identifier
    (LeftParen dataType (Comma dataType)* RightParen)? AS (directSqlDataStatement|mergeStatement);

postgresNotificationStatement: {NextWordIs("LISTEN")}? postgresKeyword identifier
    | {NextWordIs("UNLISTEN")}? postgresKeyword (identifier|Asterisk)
    | {NextWordIs("NOTIFY")}? postgresKeyword identifier (Comma characterStringLiteral)?;

postgresTruncateStatement: {NextWordIs("TRUNCATE")}? postgresKeyword TABLE? ONLY? tableName (Comma ONLY? tableName)*
    (({NextWordIs("CONTINUE") || NextWordIs("RESTART")}? postgresKeyword) {NextWordIs("IDENTITY")}? postgresKeyword)? dropBehaviour?;

postgresCopyStatement: {NextWordIs("COPY")}? postgresKeyword
    (LeftParen directSqlDataStatement RightParen TO postgresCopyDestination
    | tableName (LeftParen columnNameList RightParen)? (TO postgresCopyDestination | FROM postgresCopySource))
    (WITH? LeftParen postgresCopyOption (Comma postgresCopyOption)* RightParen)?;

postgresCopySource: characterStringLiteral | {NextWordIs("STDIN")}? postgresKeyword | {NextWordIs("PROGRAM")}? postgresKeyword characterStringLiteral;

postgresCopyDestination: characterStringLiteral | {NextWordIs("STDOUT")}? postgresKeyword | {NextWordIs("PROGRAM")}? postgresKeyword characterStringLiteral;

postgresCopyOption: {NextWordIs("FORMAT") || NextWordIs("ENCODING")}? postgresKeyword identifier
    | {NextWordIs("DELIMITER") || NextWordIs("QUOTE") || NextWordIs("ESCAPE") || NextWordIs("NULL") ||
        NextWordIs("DEFAULT")}? (postgresKeyword|ESCAPE|NULL|DEFAULT) characterStringLiteral
    | {NextWordIs("HEADER") || NextWordIs("FREEZE")}? postgresKeyword (TRUE|FALSE|ON)?;

postgresVacuumStatement: {NextWordIs("VACUUM")}? postgresKeyword
    (LeftParen postgresVacuumOption (Comma postgresVacuumOption)* RightParen | postgresVacuumOption*)
    (tableName (LeftParen columnNameList RightParen)? (Comma tableName (LeftParen columnNameList RightParen)?)*)?;

postgresVacuumOption: (FULL | {NextWordIs("ANALYZE") || NextWordIs("VERBOSE") || NextWordIs("FREEZE") ||
    NextWordIs("DISABLE_PAGE_SKIPPING") || NextWordIs("SKIP_LOCKED") || NextWordIs("PROCESS_MAIN") ||
    NextWordIs("PROCESS_TOAST") || NextWordIs("TRUNCATE") || NextWordIs("ONLY_DATABASE_STATS") ||
    NextWordIs("SKIP_DATABASE_STATS")}? postgresKeyword) (TRUE|FALSE|ON)?
    | {NextWordIs("INDEX_CLEANUP")}? postgresKeyword (TRUE|FALSE|ON|{NextWordIs("AUTO") || NextWordIs("OFF")}? postgresKeyword)?
    | {NextWordIs("PARALLEL")}? postgresKeyword UnsignedInteger;

postgresRefreshViewStatement: {NextWordIs("REFRESH")}? postgresKeyword MATERIALIZED VIEW CONCURRENTLY? tableName postgresWithDataClause?;

mySqlTransactionCompletion: {IsMySqlSyntax()}? (AND NO? MYSQL_CHAIN (NO? MYSQL_RELEASE)? | NO? MYSQL_RELEASE);

// session

sqlSessionStatement: (setCatalogStatement|setSchemaStatement|setNamesStatement|setSessionAuthorizationIdentifierStatement|setLocalTimeZoneStatement|oracleSessionStatement);

setCatalogStatement: SET CATALOG (identifier|valueSpecification);

valueSpecification: (literal|generalValueSpecification);

setSchemaStatement: SET SCHEMA (identifier|valueSpecification);

setNamesStatement: SET NAMES (identifier|valueSpecification);

setSessionAuthorizationIdentifierStatement: SET SESSION AUTHORIZATION valueSpecification;

setLocalTimeZoneStatement: SET TIME ZONE setTimeZoneValue;

setTimeZoneValue: (intervalValueExpression|LOCAL);

callStatement: CALL qualifiedName callStatementParams?
    | {IsTsqlProceduralSyntax()}? (EXEC|EXECUTE) qualifiedName (callStatementParams|execArguments)?
    | {IsMySqlSyntax()}? EXECUTE identifier (USING BatchVariableName (Comma BatchVariableName)*)?
    | {IsPostgresSyntax()}? EXECUTE identifier callStatementParams?
    | {!IsTsqlProceduralSyntax()}? (EXEC|EXECUTE) (FUNCTION|PROCEDURE) qualifiedName callStatementParams?;

execArguments: execArgument (Comma execArgument)*;

execArgument: (BatchVariableName EqualsOperator)? anyValue (OUTPUT | {NextWordIs("OUT")}? anyWord)?;

callStatementParams: LeftParen (anyValue (Comma anyValue)*)? RightParen;

implicitProcedureCall: {StartsQualifiedIdentifier()}? {RequireImplicitProcedureCallAtBatchStart();} identifier Period identifier (Period identifier)* execArguments?;

brokerStatement: {IsTsqlProceduralSyntax()}? (createQueueStatement|receiveStatement|waitForStatement|conversationTimerStatement);

createQueueStatement: CREATE QUEUE qualifiedName WITH {NextWordIs("ACTIVATION")}? anyWord LeftParen
    queueActivationOption (Comma queueActivationOption)* RightParen;

queueActivationOption:
    {NextWordIs("PROCEDURE_NAME")}? anyWord EqualsOperator qualifiedName
    | {NextWordIs("MAX_QUEUE_READERS")}? anyWord EqualsOperator UnsignedInteger
    | EXECUTE AS identifier;

receiveStatement: RECEIVE selectList FROM tableName (WHERE columnName EqualsOperator variableExpression)?;

waitForStatement: WAITFOR LeftParen ({NextWordIs("GET")}? anyWord CONVERSATION GROUP variableExpression FROM tableName
    | receiveStatement) RightParen (Comma TIMEOUT MinusSign? UnsignedInteger)?;

conversationTimerStatement: BEGIN CONVERSATION {NextWordIs("TIMER")}? anyWord
    LeftParen variableExpression RightParen TIMEOUT EqualsOperator (unsignedNumericLiteral|functionCallExpression|variableExpression);

// unknown keyword, data type or function name

anyWord: actualIdentifier;

anyValue: rowValueConstructor|searchCondition;

anyWordWithAnyValue: anyWord anyValue;

anyProperty: LeftParen (anyValue (Comma anyValue)*) RightParen;

anyWordsWithProperty: anyWord+ anyProperty?;

functionCallExpression: functionCallTargetName LeftParen (functionCallOperand ((Comma|anyWord) functionCallOperand)* oracleNullTreatment?)? RightParen oracleKeepClause? oracleNullTreatment? filterClause? overClause?;

functionCallTargetName: qualifiedName|IF|{IsSqliteSyntax()}? RAISE_KW
    | {Dialect is "trino" or "athena"}? FILTER
    | {Dialect == "flink"}? TABLE;

functionCallOperand: dialectFunctionOperand|anyValue|directSubqueryExpression;

mySqlSpecialFunction: {IsMySqlSyntax()}? (
    MYSQL_CONVERT LeftParen valueExpression (USING identifier|Comma castDataType) RightParen
    | VALUES LeftParen columnName RightParen
    | MATCH LeftParen columnNameList RightParen MYSQL_AGAINST LeftParen valueExpression
      (IN (MYSQL_BOOLEAN MYSQL_MODE|NATURAL LANGUAGE MYSQL_MODE) (WITH QUERY MYSQL_EXPANSION)?
      | WITH QUERY MYSQL_EXPANSION)? RightParen
    | MYSQL_JSON_VALUE LeftParen valueExpression Comma characterStringLiteral (RETURNING castDataType)?
      mySqlJsonResponse? RightParen);

mySqlJsonTable: {IsMySqlSyntax()}? MYSQL_JSON_TABLE LeftParen valueExpression Comma characterStringLiteral
    mySqlJsonColumns RightParen;

mySqlJsonColumns: MYSQL_COLUMNS LeftParen mySqlJsonColumn (Comma mySqlJsonColumn)* RightParen;

mySqlJsonColumn: columnName (FOR MYSQL_ORDINALITY | dataType EXISTS? MYSQL_PATH characterStringLiteral mySqlJsonResponse?)
    | MYSQL_NESTED MYSQL_PATH? characterStringLiteral mySqlJsonColumns;

mySqlJsonResponse: (mySqlJsonAction ON MYSQL_EMPTY (mySqlJsonAction ON MYSQL_ERROR)? | mySqlJsonAction ON MYSQL_ERROR);

mySqlJsonAction: NULL | MYSQL_ERROR | DEFAULT valueExpression;

dialectFunctionOperand:
    {Dialect == "flink"}? TABLE tableName
    | {Dialect is "trino" or "athena" or "sparksql" or "databricks"}? identifier JsonArrow anyValue
    | {Dialect == "snowflake"}? identifier NamedArgumentArrow qualifiedName (Colon identifier)*;

aggregateExpression: actualIdentifier LeftParen aggregateExprParam+ orderByClause? separatorClause? oracleNullTreatment? RightParen oracleKeepClause? (WITHIN GROUP LeftParen orderByClause RightParen)? oracleNullTreatment? filterClause? overClause?;

filterClause: {AllowsAggregateFilter()}? FILTER LeftParen WHERE searchCondition RightParen;

aggregateExprParam: DISTINCT|ALL|LIMIT|SEPARATOR|OFFSET|Comma|rowValueConstructor;

separatorClause: SEPARATOR valueExpression;

/*
All the logical boundary terms between query construct levels should be explicitly mentioned here for the anyUnexpected to NOT cross them
MATCH path = (a:RuleEnd)-[r:BeginAlternative|EndAlternativeBranch|EnterRule|ExitRule|Skip|Default*]->(b)-[t:Term]->(c),rrr2 = (f:RuleContainer)-[:RuleContains]->(a)
WHERE f.payload in ['naturalJoinTerm','searchCondition','selectSublist','tableReference'] return distinct t.payload
*/

anyUnexpected: ((~(LeftParen|RightParen|Period|Semicolon|Comma|
THEN|GROUP|HAVING|UNION|EXCEPT|WITH|INTERSECT|ORDER|ON|USING|WHERE|INTO|FROM
))|(identifier Period)|(LeftParen anyUnexpected* RightParen))+;

tableHintKeywords: WITH | UPDATE | IN | KEY | JOIN | ORDER BY | GROUP BY;

nonReserved: COMMITTED | REPEATABLE | SERIALIZABLE | TYPE | UNCOMMITTED |
    CURRENT_USER | SESSION_USER | SYSTEM_USER | USER | VALUE | RIGHT | LEFT |
    DATE | DATABASE | YEAR | MONTH | DAY | HOUR | MINUTE | SECOND | ZONE |
    ACTION | ADD | AUTHORIZATION | BY | CASCADE | CASCADED | CATALOG | COALESCE | COMMIT | CONCURRENTLY |
    CONSTRAINTS | CORRESPONDING | COUNT | DEFERRABLE | DEFERRED | IMMEDIATE |
    EXTRACT | FULL | GLOBAL | LOCAL | INDICATOR | INITIALLY | INTERVAL | ISOLATION | KEY | LEVEL |
    NAMES | NO | NULLIF| ONLY | OVERLAPS| PARTIAL | PRESERVE | READ | RESTRICT | ROLLBACK | SCHEMA |
    SESSION | TEMPORARY | TIME | TIMESTAMP | TIMEZONE_HOUR | TIMEZONE_MINUTE | TRANSACTION |
    VIEW | WORK | WRITE | WITHOUT | ARRAY | DECLARE | REPLACE | TOP | PERCENT | TIES | IN | VAR | VARIABLE | GROUPING | SETS | ROLLUP | CUBE | mySqlNonReserved |
    INDEX | MATERIALIZED | ROLE | SEQUENCE | SYNONYM | AFTER | BEFORE | ATOMIC | BLOCK |
    CATCH | CLOSE | CURSOR | DEALLOCATE | DEFINER | EACH | ELSIF | FETCH | FIRST | FOR | HANDLER |
    INSTEAD | LANGUAGE | LAST | NEXT | OPEN | {!IsOracleSyntax()}? PRIOR | PROC | QUEUE | RECREATE | RELATIVE |
    NEW | OLD | REFERENCING | RETURN | RETURNS | ROW | SIGNAL | TRIGGER | TRY | EXPLAIN | QUERY | PLAN
;

mySqlNonReserved: MYSQL_AGAINST | MYSQL_ALGORITHM | MYSQL_AT | MYSQL_BOOLEAN | MYSQL_BUCKETS | MYSQL_CHAIN
    | MYSQL_CHARSET | MYSQL_COLUMNS | MYSQL_COMMENT | MYSQL_COMPLETION | MYSQL_CONCURRENT | MYSQL_CONSISTENT
    | MYSQL_DATA | MYSQL_DISABLE | MYSQL_DUPLICATE | MYSQL_ENABLE | MYSQL_ENDS | MYSQL_ENGINE | MYSQL_ERROR
    | MYSQL_EVENT | MYSQL_EVERY | MYSQL_EXPANSION | MYSQL_EXPORT | MYSQL_FIELDS | MYSQL_FLUSH | MYSQL_FOUND
    | MYSQL_HISTOGRAM | MYSQL_IDENTIFIED | MYSQL_INVOKER | MYSQL_JSON_VALUE | MYSQL_LINES | MYSQL_MODE
    | MYSQL_NESTED | MYSQL_ORDINALITY | MYSQL_PATH | MYSQL_PREPARE | MYSQL_SAVEPOINT | MYSQL_SCHEDULE
    | MYSQL_SECURITY | MYSQL_SIGNED | MYSQL_SNAPSHOT | MYSQL_SQLEXCEPTION | MYSQL_SQLSTATE | MYSQL_SQLWARNING
    | MYSQL_START | MYSQL_STARTING | MYSQL_STARTS | MYSQL_TABLES | MYSQL_TEMPTABLE | MYSQL_TERMINATED
    | MYSQL_UNDEFINED | MYSQL_UNSIGNED | MYSQL_XML;

// Keep the procedural rules below in their existing relative order.

routineArgument: {NamedRoutineArgumentAhead()}? signatureModifier* identifier signatureModifier* dataType typeModifier* signatureModifier* routineParameterDefault?
    | signatureModifier* dataType typeModifier*;
// Routine options can precede or follow the body; return declarations belong only to the preamble.

routineCreateModifier: DEFINER EqualsOperator qualifiedName|TEMP|TEMPORARY|
    {TokenStream.LA(1) != SQLStandardLexer.PROCEDURE &&
     TokenStream.LA(1) != SQLStandardLexer.PROC &&
     TokenStream.LA(1) != SQLStandardLexer.FUNCTION &&
     TokenStream.LA(1) != SQLStandardLexer.TRIGGER}? anyWord;

routineDefinition: {IsTsqlProceduralSyntax()}? tsqlRoutineArguments routineTrailing
    | routineSignature routinePreamble? routineTrailing?
    | routinePreamble routineTrailing?
    | routineTrailing;

tsqlRoutineArguments: routineArgument (Comma routineArgument)*;

routineDefinitionExtension: LANGUAGE (anyWord|StringLiteralContent)?|MYSQL_SQL|routineHeaderWord;

routineHeaderWord: {!ReturnClauseAhead() && TokenStream.LA(1) != SQLStandardLexer.LANGUAGE}? anyWord|StringLiteralContent;

routineBody: (AS|IS) routineBodyContent? | routineBodyContent;

routineBodyContent: dollarQuotedBody|characterStringLiteral|routineDeclarationsAndBlock|firebirdBlock|proceduralTryCatch|proceduralBlock
    |LeftParen directSqlDataStatement RightParen|RETURN routineBodyTokens?|routineBodyTokens;

routineDeclarationsAndBlock: {TokenStream.LA(1) != SQLStandardLexer.DECLARE &&
    TokenStream.LA(1) != SQLStandardLexer.BEGIN}? (proceduralDeclaration Semicolon)+ proceduralBlock;

routineBodyTokens: (~(Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective|LANGUAGE|BEGIN|DECLARE|DoubleDollar|Dollar))+;

createTriggerStatement: {IsSqliteSyntax()}? CREATE (TEMP|TEMPORARY)? TRIGGER (IF NOT EXISTS)? qualifiedName
    (BEFORE|AFTER|INSTEAD OF)? (INSERT|DELETE|UPDATE (OF columnNameList)?) ON tableName (FOR EACH ROW)?
    (WHEN searchCondition)? sqliteTriggerBlock
    | {!IsSqliteSyntax()}? (CREATE (OR (REPLACE|ALTER))?|RECREATE) routineCreateModifier* TRIGGER (IF NOT EXISTS)? qualifiedName
      triggerHeaderPart* triggerBody?;

sqliteTriggerBlock: BEGIN (sqliteTriggerStatement Semicolon)+ END;

sqliteTriggerStatement: deleteStatement | selectStatement | insertStatement | updateStatement;

alterTriggerStatement: ALTER TRIGGER ifExistsSpec? qualifiedName triggerHeaderPart* (triggerBody|ddlStatementTail)?;

dropTriggerStatement: DROP TRIGGER ifExistsSpec? qualifiedName (ON tableName)? (Comma qualifiedName (ON tableName)?)* dropBehaviour?;

triggerHeaderPart: (BEFORE|AFTER|INSTEAD OF) triggerEventList?
    | ON tableName | FOR (EACH ROW|tableName) | REFERENCING triggerTransition+ | anyWord | routineSignature;

triggerEventList: triggerEvent (OR triggerEvent)*;

triggerEvent: INSERT|UPDATE (OF columnNameList)?|DELETE|TRIGGER|anyWord;

triggerTransition: (NEW|OLD) (ROW|TABLE)? AS? identifier;

triggerBody: (AS|IS)? (dollarQuotedBody|characterStringLiteral|firebirdBlock|proceduralTryCatch|proceduralBlock|callStatement|routineBodyTokens);

anonymousBlockStatement: DO (LANGUAGE anyWord)? ({IsPostgresSyntax()}? dollarQuotedBody|characterStringLiteral|proceduralBlock)? (LANGUAGE anyWord)?
    | EXECUTE BLOCK routineSignature? (RETURNS routineSignature)? (AS|IS)? (firebirdBlock|proceduralBlock)?
    | proceduralTryCatch | {!IsSqliteSyntax()}? proceduralBlock;

// The matching quote is checked before consuming each body item, including malformed or unfinished SQL.

dollarQuoteDelimiter: DoubleDollar|Dollar (~Dollar)? Dollar;

dollarQuotedBody: DollarQuotedStringLiteral | opening=dollarQuoteDelimiter dollarBodyContents[$opening.text]
    {AtDollarQuoteDelimiter($opening.text)}? dollarQuoteDelimiter;
dollarBodyContents[string opening]: ({!AtDollarQuoteDelimiter($opening)}? dollarBodyElement)*;
// Branch and block delimiters belong to their enclosing IF/block, not loose body text.

dollarBodyElement: proceduralTryCatch|proceduralBlock|proceduralIf|proceduralCase|proceduralLoop|proceduralRepeat|cursorOperation|localRelationDeclaration|variableAssignmentStatement
    |createTableStatement|directSqlDataStatement
    | ~(BEGIN|DECLARE|IF|CASE|WHILE|LOOP|FOR|REPEAT|UNTIL|ELSE|ELSEIF|ELSIF|END);

proceduralBlock: (DECLARE proceduralDeclarations)? BEGIN (NOT? ATOMIC)? proceduralStatements proceduralExceptionSection?
    END identifier?;

proceduralDeclarations: (proceduralDeclaration proceduralTerminator)*;

proceduralStatements: (proceduralStatement proceduralTerminator?)*;

proceduralTerminator: Semicolon | MySqlBodySemicolon;

proceduralStatement: oracleProceduralStatement|proceduralTryCatch|proceduralBlock|proceduralIf|proceduralCase|proceduralLoop|proceduralRepeat|localRoutineDeclaration
    |mySqlHandlerDeclaration|mySqlLabeledLoop|mySqlLeaveStatement
    |cursorDeclaration|cursorOperation|localRelationDeclaration|{TokenStream.LA(1) == SQLStandardLexer.DECLARE}? localVariableDeclaration
    |createTableStatement|dropTableStatement|selectStatementSingleRow|directSqlDataStatement|mergeStatement
    |callStatement|variableAssignmentStatement|implicitProcedureCall|tsqlRaiseError|tsqlObjectPermissions
    |{!StartsQualifiedIdentifier()}? proceduralStatementTokens;

tsqlRaiseError: {IsTsqlProceduralSyntax() && NextWordIs("RAISERROR")}? anyWord
    LeftParen valueExpression Comma valueExpression Comma valueExpression (Comma valueExpression)* RightParen
    (WITH tsqlRaiseErrorOption (Comma tsqlRaiseErrorOption)*)?;

tsqlRaiseErrorOption: {NextWordIs("LOG") || NextWordIs("NOWAIT") || NextWordIs("SETERROR")}? anyWord;

tsqlObjectPermissions: {IsTsqlProceduralSyntax() && (NextWordIs("GRANT") || NextWordIs("DENY"))}? anyWord
    tsqlObjectPermission (Comma tsqlObjectPermission)* ON
    ({NextWordIs("OBJECT")}? anyWord TypeCast)? qualifiedName
    TO identifier (Comma identifier)* (WITH {NextWordIs("GRANT")}? anyWord OPTION)? (AS identifier)?;

tsqlObjectPermission: (SELECT | UPDATE | INSERT | DELETE | REFERENCES | ALTER | EXECUTE
    | {NextWordIs("CONTROL") || NextWordIs("TAKE") || NextWordIs("VIEW")}? anyWord
      ({NextWordIs("OWNERSHIP") || NextWordIs("DEFINITION")}? anyWord))
    (LeftParen columnNameList RightParen)?;

localRoutineDeclaration: (FUNCTION|PROCEDURE|PROC) qualifiedName routineDefinition;

proceduralIf: oracleIfStatement
    | {IsGenericProceduralSyntax() && !IsOracleSyntax()}? IF proceduralCondition? (THEN proceduralStatements ((ELSIF|ELSEIF) proceduralCondition THEN proceduralStatements)*
    (ELSE proceduralStatements)? (END IF | EOF) | EOF)
    | {IsTsqlProceduralSyntax()}? IF nativeProceduralCondition proceduralStatement Semicolon? (ELSE proceduralStatement)?
    | {IsFirebirdProceduralSyntax()}? IF nativeProceduralCondition THEN proceduralStatement Semicolon? (ELSE proceduralStatement)?;

proceduralCase: CASE proceduralCaseCondition? (WHEN proceduralCondition THEN proceduralStatements)+
    (ELSE proceduralStatements)? END CASE?;

proceduralCaseCondition: (~(WHEN|THEN|Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective))+;

proceduralLoop: oracleLoopStatement
    | {IsGenericProceduralSyntax() && !IsOracleSyntax()}? LOOP proceduralStatements END LOOP
    | {IsGenericProceduralSyntax() && !IsOracleSyntax()}? (WHILE|FOR) proceduralCondition? (DO|LOOP) proceduralStatements END (LOOP|WHILE|FOR)
    | {IsTsqlProceduralSyntax()}? WHILE nativeProceduralCondition proceduralStatement
    | {IsFirebirdProceduralSyntax()}? WHILE nativeProceduralCondition DO proceduralStatement;

mySqlLabeledLoop: {IsMySqlSyntax()}? identifier Colon proceduralLoop identifier?;

mySqlLeaveStatement: {IsMySqlSyntax()}? MYSQL_LEAVE identifier;

mySqlHandlerDeclaration: {IsMySqlSyntax()}? DECLARE (MYSQL_CONTINUE|MYSQL_EXIT|MYSQL_UNDO) HANDLER
    FOR mySqlHandlerCondition (Comma mySqlHandlerCondition)* proceduralStatement;

mySqlHandlerCondition: NOT MYSQL_FOUND | MYSQL_SQLWARNING | MYSQL_SQLEXCEPTION
    | MYSQL_SQLSTATE VALUE? characterStringLiteral | UnsignedInteger | identifier;

proceduralExceptionSection: EXCEPTION (WHEN proceduralCondition THEN proceduralStatements)+;

proceduralCondition: (~(THEN|DO|LOOP|Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective|BEGIN|DECLARE|END|ELSE|ELSEIF|ELSIF|EXCEPTION))+;
// Structured statement starts must not also enter the unknown-statement fallback.

proceduralStatementTokens: ~(Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective|BEGIN|DECLARE|END|ELSE|ELSEIF|ELSIF|THEN|WHEN|IF|CASE|LOOP|WHILE|FOR|REPEAT|UNTIL|EXCEPTION|
    SELECT|WITH|INSERT|UPDATE|DELETE|CALL|EXEC|EXECUTE|OPEN|FETCH|CLOSE|DEALLOCATE|FUNCTION|PROCEDURE|PROC)
    (~(Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective|BEGIN|END|ELSE|ELSEIF|ELSIF|THEN|WHEN|IF|CASE|LOOP|WHILE|FOR|REPEAT|UNTIL|EXCEPTION))*;

// The cursor's query and any declared table shape remain visible as distinct syntax nodes.

cursorDeclaration: {IsTsqlProceduralSyntax()}? DECLARE cursorName CURSOR tsqlCursorOption+ FOR directSqlDataStatement
      (FOR (READ ONLY | UPDATE (OF columnNameList)?))?
    | DECLARE? cursorName (anyWord* CURSOR|CURSOR) routineSignature? (FOR|IS) directSqlDataStatement?
    | DECLARE cursorName anyWord* CURSOR;

tsqlCursorOption: LOCAL | GLOBAL | {NextWordIs("FORWARD_ONLY") || NextWordIs("SCROLL") ||
    NextWordIs("STATIC") || NextWordIs("KEYSET") || NextWordIs("DYNAMIC") ||
    NextWordIs("FAST_FORWARD") || NextWordIs("READ_ONLY") || NextWordIs("SCROLL_LOCKS") ||
    NextWordIs("OPTIMISTIC") || NextWordIs("TYPE_WARNING")}? anyWord;

cursorOperation: OPEN cursorName (FOR (directSqlDataStatement|routineBodyTokens))?
    | FETCH cursorFetchDirection? (FROM|IN)? cursorName? (INTO (cursorName (Comma cursorName)*)?)?
    | CLOSE cursorName?
    | {!(IsMySqlSyntax() && TokenStream.LA(2) == MYSQL_PREPARE)}? DEALLOCATE cursorName?;

cursorName: identifier;

cursorFetchDirection: NEXT|PRIOR|FIRST|LAST|RELATIVE signedNumericLiteral?;

localRelationDeclaration: DECLARE cursorName TABLE tableElementList?
    | DECLARE (GLOBAL|LOCAL)? (TEMPORARY|TEMP) TABLE (IF NOT EXISTS)? tableName? tableElementList?;

localVariableDeclaration: DECLARE? VARIABLE? (
    localTypedVariable (Comma localTypedVariable)*
    | identifier (Comma identifier)+ localVariableType? (DEFAULT valueExpression)?
    | identifier (DEFAULT valueExpression)?
);
// Native conditions have no permissive anyUnexpected tail: otherwise BEGIN/SELECT can become part of the condition.

nativeProceduralCondition: booleanTerm (OR booleanTerm)*;

firebirdBlock: {IsFirebirdProceduralSyntax()}?
    ({TokenStream.LA(1) == SQLStandardLexer.DECLARE}? localVariableDeclaration Semicolon)* proceduralBlock;
// Occupy the former Firebird declaration slot to preserve the ids of subsequent generated rules.

localTypedVariable: identifier localVariableType ((EqualsOperator|DEFAULT) valueExpression)?;
// UNTIL ends the body, rather than being consumed by a permissive unknown statement.

proceduralRepeat: {IsGenericProceduralSyntax()}? REPEAT proceduralStatements UNTIL proceduralRepeatCondition? (END REPEAT)?;

proceduralRepeatCondition: (~(END|Semicolon|MySqlBodySemicolon|MySqlScriptDelimiter|MySqlDelimiterDirective))+;
// T-SQL's END TRY / BEGIN CATCH delimit separate arms, not successive BEGIN/END blocks.

proceduralTryCatch: {IsTsqlProceduralSyntax()}? BEGIN TRY proceduralStatements END TRY BEGIN CATCH proceduralStatements END CATCH;

localVariableType: dataType typeModifier*;

typeArguments: LeftParen (anyValue (Comma anyValue?)* )? RightParen?;

typeModifier: {ModifierAhead(false)}? anyWord;

signatureModifier: {ModifierAhead(true)}? anyWord;

routineParameterDefault: (DEFAULT|EqualsOperator|Colon EqualsOperator) valueExpression?;

typeContinuation: typeModifier;

timeZoneQualifier: WITH|WITHOUT;

routinePreamble: routineDefinitionExtension* routineReturnClause routineDefinitionExtension*
    | routineDefinitionExtension+;

routineReturnClause: (RETURNS|RETURN) (TABLE routineResultList|routineResultList|dataType)?;

routineResultList: LeftParen (routineResultField (Comma routineResultField?)* )? RightParen?;

routineResultField: {NamedRoutineArgumentAhead()}? identifier dataType typeModifier* | dataType typeModifier*;

routineTrailing: routineBody routineDefinitionExtension*;
// A declaration section cannot consume the BEGIN block it is supposed to precede.

proceduralDeclaration: oracleDeclaration|localVariableDeclaration|cursorDeclaration|localRelationDeclaration|localRoutineDeclaration;

explainStatement: EXPLAIN explainOption* (explainableSqlQueryBody|explainTarget)
    | {IsMySqlSyntax()}? MYSQL_DESCRIBE (explainableSqlQueryBody|explainTarget);

explainOption: {IsSqliteSyntax()}? QUERY PLAN                        // SQLite
    | PLAN                                      // Oracle, DB2, HANA, ClickHouse
    | FOR (SCHEMA|DATABASE) identifier          // MySQL
    | FOR                                       // Oracle, DB2, HANA, H2
    | SET anyWord EqualsOperator explainOptionValue // Oracle, DB2
    | INTO (qualifiedName|variableExpression)  // Oracle, MySQL
    | USING anyWord                             // Snowflake
    | LeftParen explainOptionItem (Comma explainOptionItem)* RightParen // PostgreSQL, Trino, CockroachDB
    | explainOptionItem;                        // MySQL, MariaDB, Hive, Spark, ClickHouse, etc.

explainOptionItem: {IsExplainOptionAllowed()}? anyWord (EqualsOperator? explainOptionValue)?;

explainOptionValue: anyWord|literal|TRUE|FALSE|ON;

explainTarget: {!(NextWordIs("QUERY") && NextWordIs("PLAN", 2))}? qualifiedName (identifier|StringLiteralContent|Asterisk)? // EXPLAIN table [column or wildcard]
    | FOR anyWord UnsignedInteger;              // MySQL/MariaDB/TiDB FOR CONNECTION

// Oracle query expressions and relational clauses.

mySqlUtilityStatement: {IsMySqlSyntax()}? (
    mySqlLoadStatement|mySqlPreparedStatement|mySqlLockStatement|mySqlAnalyzeStatement|mySqlShowStatement|mySqlFlushStatement
    | MYSQL_USE identifier);

mySqlLoadStatement: MYSQL_LOAD (
    MYSQL_DATA (MYSQL_LOW_PRIORITY|MYSQL_CONCURRENT)? LOCAL? MYSQL_INFILE characterStringLiteral
      (REPLACE|MYSQL_IGNORE)? INTO TABLE tableName (PARTITION LeftParen columnNameList RightParen)?
      (CHARACTER SET identifier)? mySqlLoadFields? mySqlLoadLines? mySqlLoadSkip?
      (LeftParen columnNameList RightParen)? (SET setClauseList)?
    | MYSQL_XML (MYSQL_LOW_PRIORITY|MYSQL_CONCURRENT)? LOCAL? MYSQL_INFILE characterStringLiteral
      (REPLACE|MYSQL_IGNORE)? INTO TABLE tableName (CHARACTER SET identifier)?
      (ROWS MYSQL_IDENTIFIED BY characterStringLiteral)? mySqlLoadSkip?
      (LeftParen columnNameList RightParen)? (SET setClauseList)?);

mySqlLoadFields: (MYSQL_FIELDS|MYSQL_COLUMNS)
    (MYSQL_TERMINATED BY characterStringLiteral)?
    (MYSQL_OPTIONALLY? MYSQL_ENCLOSED BY characterStringLiteral)?
    (MYSQL_ESCAPED BY characterStringLiteral)?;

mySqlLoadLines: MYSQL_LINES (MYSQL_STARTING BY characterStringLiteral)? (MYSQL_TERMINATED BY characterStringLiteral)?;

mySqlLoadSkip: MYSQL_IGNORE UnsignedInteger (MYSQL_LINES|ROWS);

mySqlPreparedStatement: MYSQL_PREPARE identifier FROM (characterStringLiteral|BatchVariableName)
    | (DEALLOCATE|DROP) MYSQL_PREPARE identifier;

mySqlLockStatement: MYSQL_LOCK MYSQL_TABLES mySqlTableLock (Comma mySqlTableLock)*
    | MYSQL_UNLOCK MYSQL_TABLES;

mySqlTableLock: tableName (AS? correlationName)? (READ LOCAL?|MYSQL_LOW_PRIORITY? WRITE);

mySqlAnalyzeStatement: MYSQL_ANALYZE (LOCAL|MYSQL_NO_WRITE_TO_BINLOG)? TABLE tableName (Comma tableName)*
    (UPDATE MYSQL_HISTOGRAM ON columnNameList (WITH UnsignedInteger MYSQL_BUCKETS)?
    | DROP MYSQL_HISTOGRAM ON columnNameList)?;

mySqlShowStatement: MYSQL_SHOW (
    CREATE (TABLE|VIEW|PROCEDURE|FUNCTION|MYSQL_EVENT|TRIGGER) qualifiedName
    | (FULL? MYSQL_TABLES ((FROM|IN) identifier)?
      | {NextWordIs("DATABASES") || NextWordIs("SCHEMAS")}? anyWord
      | FULL? (MYSQL_COLUMNS|MYSQL_FIELDS) (FROM|IN) tableName ((FROM|IN) identifier)?)
      (LIKE characterStringLiteral|whereClause)?);

mySqlFlushStatement: MYSQL_FLUSH (LOCAL|MYSQL_NO_WRITE_TO_BINLOG)? MYSQL_TABLES
    (tableName (Comma tableName)*)? (WITH READ MYSQL_LOCK|FOR MYSQL_EXPORT)?;

mySqlEventStatement: {IsMySqlSyntax()}? CREATE (DEFINER EqualsOperator identifier (At identifier)?)? MYSQL_EVENT
    (IF NOT EXISTS)? qualifiedName ON MYSQL_SCHEDULE
    (MYSQL_AT valueExpression | MYSQL_EVERY valueExpression intervalQualifier
      (MYSQL_STARTS valueExpression)? (MYSQL_ENDS valueExpression)?)
    (ON MYSQL_COMPLETION NOT? PRESERVE)? (MYSQL_ENABLE|MYSQL_DISABLE)?
    (MYSQL_COMMENT characterStringLiteral)? DO proceduralStatement;

inPredicateItem: valueExpression
    | {IsOracleSyntax()}? LeftParen valueExpression Comma valueExpression (Comma valueExpression)* RightParen;

oracleMinusTerm: {IsOracleSyntax()}? ORACLE_MINUS queryTerm;

oracleHierarchyClause: {IsOracleSyntax()}? (oracleStartWith oracleConnectBy | oracleConnectBy oracleStartWith?);

oracleStartWith: ORACLE_START WITH searchCondition;

oracleConnectBy: ORACLE_CONNECT BY (ORACLE_NOCYCLE | {TokenStream.LA(1) != ORACLE_NOCYCLE}?) searchCondition;

oracleSearchClause: {IsOracleSyntax()}? ORACLE_SEARCH (ORACLE_DEPTH|ORACLE_BREADTH) FIRST BY sortSpecificationList SET columnName;

oracleCycleClause: {IsOracleSyntax()}? ORACLE_CYCLE columnNameList SET columnName TO literal DEFAULT literal;

oracleCollectionTable: {IsOracleSyntax()}? TABLE LeftParen valueExpression RightParen;

oracleApplyTerm: {IsOracleSyntax()}? (CROSS|OUTER) ORACLE_APPLY tableReference;

oracleJoinPartitionClause: {IsOracleSyntax()}? PARTITION BY LeftParen oracleExpressionList RightParen;

oracleFlashbackClause: {IsOracleSyntax()}?
    (AS OF (ORACLE_SCN|TIMESTAMP) valueExpression
    | ORACLE_VERSIONS BETWEEN (ORACLE_SCN|TIMESTAMP) oracleFlashbackBound AND oracleFlashbackBound);

oracleFlashbackBound: ORACLE_MINVALUE | ORACLE_MAXVALUE | valueExpression;

oracleSampleClause: {IsOracleSyntax()}? ORACLE_SAMPLE BLOCK? LeftParen valueExpression RightParen
    (ORACLE_SEED LeftParen valueExpression RightParen)?;

oraclePivotClause: {IsOracleSyntax()}? ORACLE_PIVOT LeftParen oraclePivotAggregate (Comma oraclePivotAggregate)*
    oraclePivotFor IN LeftParen oraclePivotValue (Comma oraclePivotValue)* RightParen RightParen;

oraclePivotAggregate: functionCallExpression asClause?;

oraclePivotFor: FOR oracleColumnOrTuple;

oracleColumnOrTuple: columnName | LeftParen columnNameList RightParen;

oraclePivotValue: inPredicateItem asClause?;

oracleUnpivotClause: {IsOracleSyntax()}? ORACLE_UNPIVOT ((ORACLE_INCLUDE|EXCLUDE) NULLS)?
    LeftParen oracleColumnOrTuple oraclePivotFor IN LeftParen oracleUnpivotValue (Comma oracleUnpivotValue)* RightParen RightParen;

oracleUnpivotValue: oracleColumnOrTuple (AS (literal|LeftParen literal (Comma literal)* RightParen))?;

oracleMatchRecognizeClause: {IsOracleSyntax()}? ORACLE_MATCH_RECOGNIZE LeftParen
    (PARTITION BY oracleExpressionList)? orderByClause?
    (ORACLE_MEASURES oracleMatchMeasure (Comma oracleMatchMeasure)*)?
    ((ORACLE_ONE ROW|ALL ROWS) {NextWordIs("PER")}? anyWord MATCH)?
    (AFTER MATCH SKIP_KW (ORACLE_PAST LAST ROW|TO (NEXT ROW|(FIRST|LAST)? identifier)))?
    ORACLE_PATTERN LeftParen oraclePatternTerm+ RightParen
    (ORACLE_DEFINE oraclePatternDefinition (Comma oraclePatternDefinition)*)? RightParen;

oracleMatchMeasure: valueExpression AS columnName;

oraclePatternTerm: (identifier|LeftParen oraclePatternTerm+ RightParen) (Asterisk|PlusSign|QuestionMark)? (VerticalBar oraclePatternTerm)?;

oraclePatternDefinition: identifier AS searchCondition;

oracleModelClause: {IsOracleSyntax()}? ORACLE_MODEL
    (PARTITION BY LeftParen oracleExpressionList RightParen)?
    ORACLE_DIMENSION BY LeftParen oracleExpressionList RightParen
    ORACLE_MEASURES LeftParen oracleExpressionList RightParen
    ORACLE_RULES? LeftParen oracleModelRule (Comma oracleModelRule)* RightParen;

oracleModelRule: columnReference LeftBracket oracleExpressionList RightBracket EqualsOperator valueExpression;

// Oracle aggregates, XML/JSON functions and row values.

oracleExpression: {IsOracleSyntax()}?
    ((ORACLE_CONNECT_BY_ROOT|PRIOR) valueExpressionPrimary
    | oracleListagg | oracleXmlFunction
    | oracleJsonValue | oracleJsonObject | oracleCursorAttribute);

oracleExpressionList: valueExpression (Comma valueExpression)*;

oracleListagg: ORACLE_LISTAGG LeftParen setQuantifier? valueExpression (Comma valueExpression)?
    (ON ORACLE_OVERFLOW (ORACLE_ERROR | ORACLE_TRUNCATE characterStringLiteral? ((WITH|WITHOUT) COUNT)?))? RightParen
    (WITHIN GROUP LeftParen orderByClause RightParen)? overClause?;

oracleKeepClause: {IsOracleSyntax()}? ORACLE_KEEP LeftParen ORACLE_DENSE_RANK (FIRST|LAST) orderByClause RightParen;

oracleNullTreatment: {IsOracleSyntax()}? (ORACLE_IGNORE|ORACLE_RESPECT) NULLS;

oracleXmlFunction: ORACLE_XMLELEMENT LeftParen identifier (Comma valueExpression)* RightParen
    | (ORACLE_XMLATTRIBUTES|ORACLE_XMLFOREST) LeftParen derivedColumn (Comma derivedColumn)* RightParen;

oracleJsonValue: (ORACLE_JSON_VALUE|ORACLE_JSON_EXISTS) LeftParen valueExpression Comma valueExpression
    (RETURNING dataType)? oracleJsonErrorClause* RightParen;

oracleJsonErrorClause: (NULL|ORACLE_ERROR|DEFAULT valueExpression|TRUE|FALSE) ON (ORACLE_ERROR|ORACLE_EMPTY);

oracleJsonObject: ORACLE_JSON_OBJECT LeftParen
    (oracleJsonObjectEntry (Comma oracleJsonObjectEntry)*)?
    ((NULL|ORACLE_ABSENT) ON NULL)? (RETURNING dataType)? oracleJsonErrorClause* RightParen;

oracleJsonObjectEntry: KEY? valueExpression (VALUE|Colon) valueExpression;

oracleJsonTable: {IsOracleSyntax()}? ORACLE_JSON_TABLE LeftParen valueExpression Comma valueExpression
    {NextWordIs("COLUMNS")}? anyWord LeftParen oracleJsonColumn (Comma oracleJsonColumn)* RightParen RightParen;

oracleJsonColumn: columnName (FOR ORACLE_ORDINALITY | dataType (ORACLE_PATH characterStringLiteral)? oracleJsonErrorClause*);

oracleCursorAttribute: qualifiedName OraclePercent
    ({NextWordIs("FOUND") || NextWordIs("NOTFOUND") || NextWordIs("ROWCOUNT") || NextWordIs("ISOPEN")}? anyWord);

// Oracle DML and schema statements.

oracleInsertStatement: {IsOracleSyntax()}? INSERT
    (ALL oracleInsertInto+ | (ALL|FIRST) oracleInsertWhen+ (ELSE oracleInsertInto+)?) queryExpression;

oracleInsertWhen: WHEN searchCondition THEN oracleInsertInto+;

oracleInsertInto: INTO tableName (LeftParen insertColumnList RightParen)? (VALUES LeftParen oracleExpressionList RightParen)?;

oracleReturningClause: {IsOracleSyntax()}? (RETURNING|RETURN) oracleExpressionList oracleIntoClause;

oracleIntoClause: (ORACLE_BULK ORACLE_COLLECT)? INTO oracleIntoTarget (Comma oracleIntoTarget)*;

oracleIntoTarget: variableExpression|qualifiedName;

oracleMergeUpdateTail: {IsOracleSyntax()}? (whereClause (DELETE whereClause)? | DELETE whereClause);

oracleAlterColumns: {IsOracleSyntax()}?
    (ADD LeftParen columnDefinition (Comma columnDefinition)* RightParen
    | ORACLE_MODIFY (columnDefinition|LeftParen columnDefinition (Comma columnDefinition)* RightParen));

oracleMaterializedViewOptions: {IsOracleSyntax()}?
    (ORACLE_BUILD (IMMEDIATE|DEFERRED) (ORACLE_REFRESH oracleMaterializedRefreshMode? oracleMaterializedRefreshTrigger?)?
    | ORACLE_REFRESH oracleMaterializedRefreshMode? oracleMaterializedRefreshTrigger?);

oracleMaterializedRefreshMode: ORACLE_FAST | {NextWordIs("COMPLETE") || NextWordIs("FORCE")}? anyWord;

oracleMaterializedRefreshTrigger: ON (COMMIT|ORACLE_DEMAND);

oracleDropTableOptions: {IsOracleSyntax()}? (CASCADE CONSTRAINTS ORACLE_PURGE? | ORACLE_PURGE);

oracleSchemaStatement: {IsOracleSyntax()}?
    (CREATE (OR REPLACE)? TYPE qualifiedName (AS|IS) oracleTypeDefinition
    | ORACLE_COMMENT ON (TABLE tableName|COLUMN qualifiedName) IS (characterStringLiteral|NULL)
    | ORACLE_TRUNCATE TABLE tableName ((DROP|{NextWordIs("REUSE")}? anyWord) ORACLE_STORAGE)?
    | ORACLE_GRANT oracleObjectPrivileges TO oracleGranteeList (WITH ORACLE_GRANT OPTION)?
    | ORACLE_REVOKE oracleObjectPrivileges FROM oracleGranteeList);

oracleTypeDefinition: ORACLE_OBJECT LeftParen oracleRecordField (Comma oracleRecordField)* RightParen | TABLE OF oracleDataType;

oracleRecordField: columnName oracleDataType;

oracleObjectPrivileges: oracleObjectPrivilege (Comma oracleObjectPrivilege)* ON tableName;

oracleObjectPrivilege: SELECT|INSERT|UPDATE|DELETE|REFERENCES|ALTER|INDEX|EXECUTE|ALL|READ;

oracleGranteeList: identifier (Comma identifier)*;

oracleSavepointStatement: {IsOracleSyntax()}? ORACLE_SAVEPOINT identifier;

oracleSessionStatement: {IsOracleSyntax()}? ALTER SESSION SET identifier EqualsOperator valueExpression;

// Oracle PL/SQL declarations and native statements.

oraclePackageDeclaration: {IsOracleSyntax()}? CREATE (OR REPLACE)? ORACLE_PACKAGE
    (qualifiedName (AS|IS) (oraclePackageSpecificationItem Semicolon)* END identifier?
    | ORACLE_BODY qualifiedName (AS|IS) (oracleDeclaration Semicolon)* (proceduralBlock|END identifier?));

oraclePackageSpecificationItem: oracleRoutineSignature | oracleDeclaration;

oracleRoutineSignature: PROCEDURE identifier routineSignature?
    | FUNCTION identifier routineSignature? RETURN oracleDataType;

oracleDataType: qualifiedName OraclePercent (TYPE|{NextWordIs("ROWTYPE")}? anyWord) | dataType;

oracleDeclaration: {IsOracleSyntax()}?
    (identifier ORACLE_CONSTANT? oracleDataType (NOT NULL)? oracleVariableDefault?
    | CURSOR identifier routineSignature? (RETURN oracleDataType)? IS directSqlDataStatement
    | TYPE identifier IS (ORACLE_RECORD LeftParen oracleRecordField (Comma oracleRecordField)* RightParen
        | TABLE OF oracleDataType (INDEX BY dataType)?)
    | oracleRoutineSignature (AS|IS) (oracleDeclaration Semicolon)* proceduralBlock);

oracleVariableDefault: (Colon EqualsOperator|DEFAULT) valueExpression;

oracleProceduralStatement: {IsOracleSyntax()}?
    (qualifiedName (LeftParen oracleExpressionList? RightParen)? (Period identifier (LeftParen oracleExpressionList? RightParen)?)* Colon EqualsOperator valueExpression
    | variableExpression Colon EqualsOperator valueExpression
    | qualifiedName (LeftParen oracleExpressionList? RightParen)?
    | (ORACLE_EXIT|ORACLE_CONTINUE) identifier? (WHEN searchCondition)?
    | OPEN identifier (LeftParen oracleExpressionList? RightParen)?
    | EXECUTE IMMEDIATE valueExpression selectIntoClause? (USING oracleExpressionList)? oracleReturningClause?
    | ORACLE_FORALL identifier IN (ORACLE_INDICES OF qualifiedName (BETWEEN valueExpression AND valueExpression)?|oracleLoopRange)
        (ORACLE_SAVE ORACLE_EXCEPTIONS)? directSqlDataStatement
    | RETURN valueExpression? | RAISE_KW qualifiedName? | NULL | sqlTransactionStatement);

oracleIfStatement: {IsOracleSyntax()}? IF searchCondition THEN proceduralStatements
    (ELSIF searchCondition THEN proceduralStatements)* (ELSE proceduralStatements)? END IF;

oracleLoopStatement: {IsOracleSyntax()}? (ShiftLeft identifier ShiftRight)?
    (WHILE searchCondition | FOR identifier IN (ORACLE_REVERSE? oracleLoopRange|qualifiedName|tableSubquery))?
    LOOP proceduralStatements END LOOP identifier?;

oracleLoopRange: valueExpression DoublePeriod valueExpression;

oracleNonReserved: {IsOracleSyntax()}?
    (ORACLE_ABSENT|ORACLE_APPLY|ORACLE_BITMAP|ORACLE_BODY|ORACLE_BREADTH|ORACLE_BUILD
    |ORACLE_BULK|ORACLE_COLLECT|ORACLE_CONNECT_BY_ROOT|ORACLE_CONSTANT|ORACLE_CONTINUE|ORACLE_CYCLE
    |ORACLE_DEFINE|ORACLE_DEMAND|ORACLE_DENSE_RANK|ORACLE_DEPTH|ORACLE_DIMENSION|ORACLE_EMPTY
    |ORACLE_ERROR|ORACLE_EXCEPTIONS|ORACLE_EXIT|ORACLE_FAST|ORACLE_FORALL|ORACLE_IGNORE
    |ORACLE_INCLUDE|ORACLE_INDICES|ORACLE_JSON_EXISTS|ORACLE_JSON_OBJECT|ORACLE_JSON_TABLE|ORACLE_JSON_VALUE
    |ORACLE_KEEP|ORACLE_LISTAGG|ORACLE_MATCH_RECOGNIZE|ORACLE_MAXVALUE|ORACLE_MEASURES|ORACLE_MINVALUE
    |ORACLE_MODEL|ORACLE_NOCYCLE|ORACLE_OBJECT|ORACLE_ONE|ORACLE_ORDINALITY|ORACLE_OVERFLOW
    |ORACLE_PACKAGE|ORACLE_PAST|ORACLE_PATH|ORACLE_PATTERN|ORACLE_PIVOT|ORACLE_PURGE
    |ORACLE_RECORD|ORACLE_REFRESH|ORACLE_RESPECT|ORACLE_REVERSE|ORACLE_RULES|ORACLE_SAMPLE
    |ORACLE_SAVE|ORACLE_SAVEPOINT|ORACLE_SCN|ORACLE_SEARCH|ORACLE_SEED|ORACLE_SIBLINGS
    |ORACLE_STORAGE|ORACLE_TRUNCATE|ORACLE_UNPIVOT|ORACLE_VERSIONS|ORACLE_XMLATTRIBUTES|ORACLE_XMLELEMENT
    |ORACLE_XMLFOREST);

oracleXmlTable: {IsOracleSyntax() && NextWordIs("XMLTABLE")}? anyWord LeftParen valueExpression
    ({NextWordIs("PASSING")}? anyWord valueExpression (AS identifier)? (Comma valueExpression (AS identifier)?)*)?
    ({NextWordIs("COLUMNS")}? anyWord oracleJsonColumn (Comma oracleJsonColumn)*)? RightParen;
