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
lexer grammar SQLStandardLexer;

@header {
    // Adapted from DBeaver SQLStandard grammar.
    // Copyright (C) 2010-2026 DBeaver Corp and others.
    // Licensed under the Apache License, Version 2.0.
}

tokens { CustomAnonymousParameterMark, CustomNamedParameterPrefix, MySqlScriptDelimiter, MySqlBodySemicolon }

MySqlDelimiterDirective: {IsMySqlDialect && StartsOnOwnLine()}? D E L I M I T E R [ \t]+
    ( '\'' ('\\' ~[\r\n] | ~['\\\r\n])+ '\''
    | '"' ('\\' ~[\r\n] | ~["\\\r\n])+ '"'
    | '`' ('\\' ~[\r\n] | ~[`\\\r\n])+ '`'
    | ~[ \t\r\n\\'"`]+) {SetMySqlScriptDelimiter();};

DelimitedIdentifier:
    DoubleQuote (~["] | DoubleQuote DoubleQuote)* DoubleQuote
    | {AllowsBackticks}? BackQuote (~[`] | BackQuote BackQuote)* BackQuote
    | {AllowsBrackets}? '[' (~[\]] | ']]')* ']';

MySqlSystemVariable: {IsMySqlDialect}? At At ((S E S S I O N | G L O B A L | L O C A L) Period)? IdentifierBody;

BatchVariableName: At At? IdentifierBody;

ClientVariableName: Dollar LeftBrace Identifier RightBrace;

// letters to support case-insensitivity for keywords

fragment A:[aA];

fragment B:[bB];

fragment C:[cC];

fragment D:[dD];

fragment E:[eE];

fragment F:[fF];

fragment G:[gG];

fragment H:[hH];

fragment I:[iI];

fragment J:[jJ];

fragment K:[kK];

fragment L:[lL];

fragment M:[mM];

fragment N:[nN];

fragment O:[oO];

fragment P:[pP];

fragment Q:[qQ];

fragment R:[rR];

fragment S:[sS];

fragment T:[tT];

fragment U:[uU];

fragment V:[vV];

fragment W:[wW];

fragment X:[xX];

fragment Y:[yY];

fragment Z:[zZ];


// keywords

ACTION: A C T I O N ;

ADD: A D D ;

AFTER: A F T E R ;

ALL: A L L ;

ALTER: A L T E R ;

AND: A N D ;

ANY: A N Y ;

ARRAY: A R R A Y;

AS: A S ;

ASC: A S C ;

ATOMIC: A T O M I C ;

AUTHORIZATION: A U T H O R I Z A T I O N ;

AUTO_INCREMENT: A U T O '_' I N C R E M E N T ;

BEFORE: B E F O R E ;

BETWEEN: B E T W E E N ;

BEGIN: B E G I N ;

BLOCK: B L O C K ;

BY: B Y ;

CALL: C A L L;

CASCADE: C A S C A D E ;

CASCADED: C A S C A D E D ;

CASE: C A S E ;

CAST: C A S T ;

CATALOG: C A T A L O G ;

CHARACTER: C H A R A C T E R ;

CHECK: C H E C K ;

COALESCE: C O A L E S C E ;

COLUMN: C O L U M N ;

COMMIT: C O M M I T ;

COMMITTED: C O M M I T T E D ;

CATCH: C A T C H ;

CLOSE: C L O S E ;

CONCURRENTLY: C O N C U R R E N T L Y ;

CONSTRAINT: C O N S T R A I N T ;

CONSTRAINTS: C O N S T R A I N T S ;

CORRESPONDING: C O R R E S P O N D I N G ;

COUNT: C O U N T ;

CREATE: C R E A T E ;

CURSOR: C U R S O R ;

CROSS: C R O S S ;

CURRENT_USER: C U R R E N T '_' U S E R ;

DATE: D A T E ;

DATABASE: D A T A B A S E ;

DAY: D A Y ;

DECLARE: D E C L A R E ;

DEALLOCATE: D E A L L O C A T E ;

DEFAULT: D E F A U L T ;

DEFINER: D E F I N E R ;

DEFERRABLE: D E F E R R A B L E ;

DEFERRED: D E F E R R E D ;

DELETE: D E L E T E ;

DESC: D E S C ;

DISTINCT: D I S T I N C T ;

DROP: D R O P ;

DO: D O ;

EACH: E A C H ;

FETCH: F E T C H ;

ELSE: E L S E ;

ELSEIF: E L S E I F ;

ELSIF: E L S I F ;

END: E N D ;

EXCEPTION: E X C E P T I O N ;

EXPLAIN: E X P L A I N ;

ESCAPE: E S C A P E ;

EXCEPT: E X C E P T ;

EXEC: E X E C;

EXECUTE: E X E C U T E;

EXISTS: E X I S T S ;

EXTRACT: E X T R A C T ;

FALSE: F A L S E ;

FIRST: F I R S T ;

FILTER: F I L T E R ;

FOREIGN: F O R E I G N ;

FOR: F O R ;

FROM: F R O M ;

FULL: F U L L ;

FUNCTION: F U N C T I O N ;

GENERATED: G E N E R A T E D;

GLOBAL: G L O B A L ;

GROUP: G R O U P ;

HAVING: H A V I N G ;

HANDLER: H A N D L E R ;

HOUR: H O U R ;

IF: I F ;

ILIKE: I L I K E ;

IMMEDIATE: I M M E D I A T E ;

IN: I N ;

INSTEAD: I N S T E A D ;

INDICATOR: I N D I C A T O R ;

INITIALLY: I N I T I A L L Y ;

INNER: I N N E R ;

INDEX: I N D E X ;

INSERT: I N S E R T ;

INTERSECT: I N T E R S E C T ;

INTERVAL: I N T E R V A L ;

INTO: I N T O ;

IS: I S ;

ISOLATION: I S O L A T I O N ;

JOIN: J O I N ;

KEY: K E Y ;

LATERAL: L A T E R A L;

LEFT: L E F T ;

LEVEL: L E V E L ;

LANGUAGE: L A N G U A G E ;

LAST: L A S T ;

LIKE: L I K E ;

LIMIT: L I M I T ;

LOCAL: L O C A L ;

LOOP: L O O P ;

MATCH: M A T C H ;

MATERIALIZED: M A T E R I A L I Z E D ;

MINUTE: M I N U T E ;

MONTH: M O N T H ;

NAMES: N A M E S ;

NATURAL: N A T U R A L ;

NEW: N E W ;

NEXT: N E X T ;

NO: N O ;

NOT: N O T ;

NOTNULL: N O T N U L L;

NULL: N U L L ;

NULLIF: N U L L I F ;

OF: O F;

OLD: O L D ;

OPEN: O P E N ;

OFFSET: O F F S E T;

ON: O N ;

ONLY: O N L Y ;

OPTION: O P T I O N ;

OR: O R ;

ORDER: O R D E R ;

OUTER: O U T E R ;

OVER: O V E R ;

OVERLAPS: O V E R L A P S ;

PARTIAL: P A R T I A L ;

PARTITION: P A R T I T I O N ;

PRESERVE: P R E S E R V E ;

PLAN: P L A N ;

PROCEDURE: P R O C E D U R E ;

PRIOR: P R I O R ;

PROC: P R O C ;

PRIMARY: P R I M A R Y ;

QUERY: Q U E R Y ;

RANGE: R A N G E ;

RELATIVE: R E L A T I V E ;

READ: R E A D ;

RECREATE: R E C R E A T E ;

RECURSIVE: R E C U R S I V E;

REFERENCING: R E F E R E N C I N G ;

REFERENCES: R E F E R E N C E S ;

REGEXP: R E G E X P;

RENAME : R E N A M E;

REPEATABLE: R E P E A T A B L E ;

REPEAT: R E P E A T ;

REPLACE: R E P L A C E ;

RETURN: R E T U R N ;

RETURNS: R E T U R N S ;

RESTRICT: R E S T R I C T ;

RIGHT: R I G H T ;

ROLE: R O L E ;

ROLLBACK: R O L L B A C K ;

ROW: R O W ;

ROWS: R O W S ;

SCHEMA: S C H E M A ;

SECOND: S E C O N D ;

SELECT: S E L E C T ;

Separator: (LiteralSeparator | '\u00A0' {Dialect == "tsql"}?)+ -> channel(HIDDEN);

SEQUENCE: S E Q U E N C E ;

SEPARATOR: S E P A R A T O R ;

SERIALIZABLE: S E R I A L I Z A B L E ;

SESSION: S E S S I O N ;

SESSION_USER: S E S S I O N '_'U S E R ;

SET: S E T ;

SIGNAL: S I G N A L ;

SOME: S O M E ;

STRAIGHT_JOIN: S T R A I G H T '_' J O I N ;

SYNONYM: S Y N O N Y M ;

SYSTEM_USER: S Y S T E M '_'U S E R ;

TABLE: T A B L E ;

TEMP: T E M P ;

TEMPORARY: T E M P O R A R Y ;

THEN: T H E N ;

TRIGGER: T R I G G E R ;

TRY: T R Y ;

TIME: T I M E ;

TIMESTAMP: T I M E S T A M P ;

TIMEZONE_HOUR: T I M E Z O N E '_'H O U R ;

TIMEZONE_MINUTE: T I M E Z O N E '_'M I N U T E ;

TO: T O ;

TRANSACTION: T R A N S A C T I O N ;

TRUE: T R U E ;

TYPE: T Y P E ;

UNCOMMITTED: U N C O M M I T T E D ;

UNION: U N I O N ;

UNIQUE: U N I Q U E ;

UNKNOWN: U N K N O W N ;

UNTIL: U N T I L ;

UPDATE: U P D A T E ;

USER: U S E R ;

USING: U S I N G ;

VALUE: V A L U E ;

VALUES: V A L U E S ;

VAR: V A R ;

VARIABLE: V A R I A B L E ;

VIEW: V I E W ;

WHEN: W H E N ;

WHERE: W H E R E ;

WHILE: W H I L E ;

WITH: W I T H ;

WITHIN: W I T H I N ;

WITHOUT: W I T H O U T ;

WORK: W O R K ;

WRITE: W R I T E ;

YEAR: Y E A R ;

ZONE: Z O N E ;

TOP: T O P ;

PERCENT: P E R C E N T ;

OraclePercent: {Dialect == "oracle"}? '%';

Percent: '%';

TIES: T I E S ;

GO: {IsBatchSeparatorAhead()}? G O ;

CONFLICT: C O N F L I C T;

MATCHED: M A T C H E D;

MERGE: M E R G E;

NOTHING: N O T H I N G;

OUTPUT: O U T P U T;

RETURNING: R E T U R N I N G;

LOCKED: L O C K E D;

SKIP_KW: S K I P;

WINDOW: W I N D O W;

QUALIFY: Q U A L I F Y;

QUEUE: Q U E U E;

RAISE_KW: R A I S E;

SQL_CALC_FOUND_ROWS: S Q L Underscore C A L C Underscore F O U N D Underscore R O W S;

RECEIVE: R E C E I V E;

WAITFOR: W A I T F O R;

CONVERSATION: C O N V E R S A T I O N;

TIMEOUT: T I M E O U T;

NULLS: N U L L S;

GROUPING: G R O U P I N G;

SETS: S E T S;

ROLLUP: R O L L U P;

CUBE: C U B E;

SQLITE_ABORT: {IsSqliteSyntax()}? A B O R T;

SQLITE_ATTACH: {IsSqliteSyntax()}? A T T A C H;

SQLITE_AUTOINCREMENT: {IsSqliteSyntax()}? A U T O I N C R E M E N T;

SQLITE_DETACH: {IsSqliteSyntax()}? D E T A C H;

SQLITE_EXCLUSIVE: {IsSqliteSyntax()}? E X C L U S I V E;

SQLITE_FAIL: {IsSqliteSyntax()}? F A I L;

SQLITE_GLOB: {IsSqliteSyntax()}? G L O B;

SQLITE_IGNORE: {IsSqliteSyntax()}? I G N O R E;

SQLITE_INDEXED: {IsSqliteSyntax()}? I N D E X E D;

SQLITE_PRAGMA: {IsSqliteSyntax()}? P R A G M A;

SQLITE_RELEASE: {IsSqliteSyntax()}? R E L E A S E;

SQLITE_SAVEPOINT: {IsSqliteSyntax()}? S A V E P O I N T;

SQLITE_VACUUM: {IsSqliteSyntax()}? V A C U U M;

SQLITE_VIRTUAL: {IsSqliteSyntax()}? V I R T U A L;

GROUPS: G R O U P S;

UNBOUNDED: U N B O U N D E D;

PRECEDING: P R E C E D I N G;

FOLLOWING: F O L L O W I N G;

CURRENT: C U R R E N T;

EXCLUDE: E X C L U D E;

OTHERS: O T H E R S;

// Dialect-specific keywords remain ordinary identifiers in other dialects.

MYSQL_XOR: X O R {IsMySqlDialect}?;

MYSQL_RLIKE: R L I K E {IsMySqlDialect}?;

MYSQL_USE: U S E {IsMySqlDialect}?;

MYSQL_FORCE: F O R C E {IsMySqlDialect}?;

MYSQL_IGNORE: I G N O R E {IsMySqlDialect}?;

MYSQL_SIGNED: S I G N E D {IsMySqlDialect}?;

MYSQL_UNSIGNED: U N S I G N E D {IsMySqlDialect}?;

MYSQL_INTEGER: I N T E G E R {IsMySqlDialect}?;

MYSQL_COLLATE: C O L L A T E {IsMySqlDialect}?;

MYSQL_ENGINE: E N G I N E {IsMySqlDialect}?;

MYSQL_CHARSET: C H A R S E T {IsMySqlDialect}?;

MYSQL_ALGORITHM: A L G O R I T H M {IsMySqlDialect}?;

MYSQL_UNDEFINED: U N D E F I N E D {IsMySqlDialect}?;

MYSQL_TEMPTABLE: T E M P T A B L E {IsMySqlDialect}?;

MYSQL_SQL: S Q L {IsMySqlDialect}?;

MYSQL_SECURITY: S E C U R I T Y {IsMySqlDialect}?;

MYSQL_INVOKER: I N V O K E R {IsMySqlDialect}?;

MYSQL_FULLTEXT: F U L L T E X T {IsMySqlDialect}?;

MYSQL_SPATIAL: S P A T I A L {IsMySqlDialect}?;

MYSQL_LOW_PRIORITY: L O W Underscore P R I O R I T Y {IsMySqlDialect}?;

MYSQL_DELAYED: D E L A Y E D {IsMySqlDialect}?;

MYSQL_HIGH_PRIORITY: H I G H Underscore P R I O R I T Y {IsMySqlDialect}?;

MYSQL_DUPLICATE: D U P L I C A T E {IsMySqlDialect}?;

MYSQL_START: S T A R T {IsMySqlDialect}?;

MYSQL_CONSISTENT: C O N S I S T E N T {IsMySqlDialect}?;

MYSQL_SNAPSHOT: S N A P S H O T {IsMySqlDialect}?;

MYSQL_SAVEPOINT: S A V E P O I N T {IsMySqlDialect}?;

MYSQL_RELEASE: R E L E A S E {IsMySqlDialect}?;

MYSQL_CHAIN: C H A I N {IsMySqlDialect}?;

MYSQL_CONVERT: C O N V E R T {IsMySqlDialect}?;

MYSQL_AGAINST: A G A I N S T {IsMySqlDialect}?;

MYSQL_BOOLEAN: B O O L E A N {IsMySqlDialect}?;

MYSQL_MODE: M O D E {IsMySqlDialect}?;

MYSQL_EXPANSION: E X P A N S I O N {IsMySqlDialect}?;

MYSQL_JSON_VALUE: J S O N Underscore V A L U E {IsMySqlDialect}?;

MYSQL_JSON_TABLE: J S O N Underscore T A B L E {IsMySqlDialect}?;

MYSQL_COLUMNS: C O L U M N S {IsMySqlDialect}?;

MYSQL_ORDINALITY: O R D I N A L I T Y {IsMySqlDialect}?;

MYSQL_PATH: P A T H {IsMySqlDialect}?;

MYSQL_NESTED: N E S T E D {IsMySqlDialect}?;

MYSQL_EMPTY: E M P T Y {IsMySqlDialect}?;

MYSQL_ERROR: E R R O R {IsMySqlDialect}?;

MYSQL_CONTINUE: C O N T I N U E {IsMySqlDialect}?;

MYSQL_EXIT: E X I T {IsMySqlDialect}?;

MYSQL_UNDO: U N D O {IsMySqlDialect}?;

MYSQL_FOUND: F O U N D {IsMySqlDialect}?;

MYSQL_SQLWARNING: S Q L W A R N I N G {IsMySqlDialect}?;

MYSQL_SQLEXCEPTION: S Q L E X C E P T I O N {IsMySqlDialect}?;

MYSQL_SQLSTATE: S Q L S T A T E {IsMySqlDialect}?;

MYSQL_LEAVE: L E A V E {IsMySqlDialect}?;

MYSQL_DESCRIBE: D E S C R I B E {IsMySqlDialect}?;

MYSQL_LOAD: L O A D {IsMySqlDialect}?;

MYSQL_DATA: D A T A {IsMySqlDialect}?;

MYSQL_CONCURRENT: C O N C U R R E N T {IsMySqlDialect}?;

MYSQL_INFILE: I N F I L E {IsMySqlDialect}?;

MYSQL_XML: X M L {IsMySqlDialect}?;

MYSQL_IDENTIFIED: I D E N T I F I E D {IsMySqlDialect}?;

MYSQL_FIELDS: F I E L D S {IsMySqlDialect}?;

MYSQL_TERMINATED: T E R M I N A T E D {IsMySqlDialect}?;

MYSQL_OPTIONALLY: O P T I O N A L L Y {IsMySqlDialect}?;

MYSQL_ENCLOSED: E N C L O S E D {IsMySqlDialect}?;

MYSQL_ESCAPED: E S C A P E D {IsMySqlDialect}?;

MYSQL_LINES: L I N E S {IsMySqlDialect}?;

MYSQL_STARTING: S T A R T I N G {IsMySqlDialect}?;

MYSQL_PREPARE: P R E P A R E {IsMySqlDialect}?;

MYSQL_LOCK: L O C K {IsMySqlDialect}?;

MYSQL_TABLES: T A B L E S {IsMySqlDialect}?;

MYSQL_UNLOCK: U N L O C K {IsMySqlDialect}?;

MYSQL_ANALYZE: A N A L Y Z E {IsMySqlDialect}?;

MYSQL_NO_WRITE_TO_BINLOG: N O Underscore W R I T E Underscore T O Underscore B I N L O G {IsMySqlDialect}?;

MYSQL_HISTOGRAM: H I S T O G R A M {IsMySqlDialect}?;

MYSQL_BUCKETS: B U C K E T S {IsMySqlDialect}?;

MYSQL_SHOW: S H O W {IsMySqlDialect}?;

MYSQL_EVENT: E V E N T {IsMySqlDialect}?;

MYSQL_FLUSH: F L U S H {IsMySqlDialect}?;

MYSQL_EXPORT: E X P O R T {IsMySqlDialect}?;

MYSQL_SCHEDULE: S C H E D U L E {IsMySqlDialect}?;

MYSQL_EVERY: E V E R Y {IsMySqlDialect}?;

MYSQL_STARTS: S T A R T S {IsMySqlDialect}?;

MYSQL_ENDS: E N D S {IsMySqlDialect}?;

MYSQL_COMPLETION: C O M P L E T I O N {IsMySqlDialect}?;

MYSQL_ENABLE: E N A B L E {IsMySqlDialect}?;

MYSQL_DISABLE: D I S A B L E {IsMySqlDialect}?;

MYSQL_COMMENT: C O M M E N T {IsMySqlDialect}?;

MYSQL_AT: A T {IsMySqlDialect}?;

MARIA_SYSTEM_TIME: {Dialect == "mariadb"}? S Y S T E M Underscore T I M E;

DK_ABORT: {Dialect is "vertica"}? A B O R T;

DK_COPY: {Dialect is "vertica"}? C O P Y;

DK_DELIMITER: {Dialect is "vertica"}? D E L I M I T E R;

DK_ERROR: {Dialect is "vertica"}? E R R O R;

DK_NODES: {Dialect is "vertica"}? N O D E S;

DK_PROJECTION: {Dialect is "vertica"}? P R O J E C T I O N;

DK_SEGMENTED: {Dialect is "vertica"}? S E G M E N T E D;

DK_STDIN: {Dialect is "vertica"}? S T D I N;

DK_ACCESS: {Dialect is "teradata"}? A C C E S S;

DK_LOCKING: {Dialect is "teradata"}? L O C K I N G;

DK_MULTISET: {Dialect is "teradata"}? M U L T I S E T;

DK_SEL: {Dialect is "teradata"}? S E L;

DK_VOLATILE: {Dialect is "teradata"}? V O L A T I L E;

DK_ASOF: {Dialect is "duckdb"}? A S O F;

DK_CLUSTER: {Dialect is "materialize"}? C L U S T E R;

DK_SHOW: {Dialect is "materialize"}? S H O W;

DK_VIEWS: {Dialect is "materialize"}? V I E W S;

DK_COMPUTE: {Dialect is "impala"}? C O M P U T E;

DK_INCREMENTAL: {Dialect is "impala"}? I N C R E M E N T A L;

DK_INVALIDATE: {Dialect is "impala"}? I N V A L I D A T E;

DK_METADATA: {Dialect is "impala"}? M E T A D A T A;

DK_STATS: {Dialect is "impala"}? S T A T S;

DK_CONNECT: {Dialect is "exasol"}? C O N N E C T;

DK_HIGH: {Dialect is "exasol"}? H I G H;

DK_LOW: {Dialect is "exasol"}? L O W;

DK_PREFERRING: {Dialect is "exasol"}? P R E F E R R I N G;

DK_START: {Dialect is "exasol"}? S T A R T;

DK_CS: {Dialect is "db2"}? C S;

DK_RR: {Dialect is "db2"}? R R;

DK_RS: {Dialect is "db2"}? R S;

DK_UR: {Dialect is "db2"}? U R;

DK_DESCRIBE: {Dialect is "databricks"}? D E S C R I B E;

DK_DETAIL: {Dialect is "databricks"}? D E T A I L;

DK_VERSION: {Dialect is "databricks"}? V E R S I O N;

DK_DISTRIBUTE: {Dialect is "sparksql" or "databricks" or "hive"}? D I S T R I B U T E;

DK_SORT: {Dialect is "sparksql" or "databricks" or "hive"}? S O R T;

DK_ENGINE: {Dialect is "clickhouse"}? E N G I N E;

DK_FINAL: {Dialect is "clickhouse"}? F I N A L;

DK_PREWHERE: {Dialect is "clickhouse"}? P R E W H E R E;

DK_FORCE: {Dialect is "doris"}? F O R C E;

DK_FORMAT: {Dialect is "greenplum" or "redshift"}? F O R M A T;

DK_HEADER: {Dialect is "greenplum"}? H E A D E R;

DK_READABLE: {Dialect is "greenplum"}? R E A D A B L E;

DK_IAM_ROLE: {Dialect is "redshift"}? I A M '_' R O L E;

DK_UNLOAD: {Dialect is "redshift"}? U N L O A D;

DK_LABEL: {Dialect is "doris" or "starrocks"}? L A B E L;

DK_EXTERNAL: {Dialect is "greenplum" or "hive" or "sparksql" or "impala"}? E X T E R N A L;

DK_LOCATION: {Dialect is "greenplum" or "hive" or "sparksql" or "impala"}? L O C A T I O N;

DK_ORDINALITY: {Dialect is "postgres" or "greenplum" or "trino" or "athena"}? O R D I N A L I T Y;

DK_OVERWRITE: {Dialect is "hive" or "sparksql" or "databricks" or "starrocks"}? O V E R W R I T E;

DK_PARTITIONED: {Dialect is "hive" or "sparksql" or "impala"}? P A R T I T I O N E D;

DK_STORED: {Dialect is "hive" or "sparksql" or "impala"}? S T O R E D;

DK_SYSTEM_TIME: {Dialect is "flink"}? S Y S T E M '_' T I M E;

DK_WAREHOUSE: {Dialect is "snowflake"}? W A R E H O U S E;

// symbols

At: '@';

DollarQuotedStringLiteral: {AllowsPostgresLiterals}? Dollar IdentifierBody? Dollar {ReadDollarQuotedString();};

DoubleDollar: '$$';

DollarAction: {Dialect == "tsql"}? Dollar A C T I O N {AtIdentifierEnd()}?;

Dollar: '$';

PositionalParameter: {AllowsPositionalParameters}? Dollar UnsignedInteger;

SqliteParameter: {IsSqliteSyntax()}? (QuestionMark UnsignedInteger | Dollar IdentifierBody);

EqualsOperator: '=' | {IsSqliteSyntax()}? '==';

NotEqualsOperator: '<>' | '!=';

MySqlNullSafeEquals: {IsMySqlDialect}? '<=>';

RightParen: ')';

LeftParen: '(';

SingleQuote: '\'';

BackQuote: '`';

Comma: ',';

TypeCast: '::';

Colon: ':';

Semicolon: ';' {MarkMySqlBodySemicolon();};

Ampersand: '&';

Asterisk: '*';

Solidus: '/';

ConcatenationOperator: '||';

Period: '.';

DoublePeriod: '..';

DoubleQuote: '"';

GreaterThanOperator: '>';

GreaterThanOrEqualsOperator: '>=' | {Dialect == "tsql"}? '!<';

LessThanOperator: '<';

LessThanOrEqualsOperator: '<=' | {Dialect == "tsql"}? '!>';

LeftBracket: '[';

RightBracket: ']';

LeftBrace: '{';

RightBrace: '}';

MinusSign: '-';

PlusSign: '+';

ShiftLeft: '<<';

ShiftRight: '>>';

JsonArrow: '->' '>'?;

PostgresJsonPathOperator: {AllowsPostgresLiterals}? '#>' '>'?;

PostgresRegexOperator: {AllowsPostgresLiterals}? ('~*' | '!~' '*'?);

PostgresPredicateOperator: {AllowsPostgresLiterals}? ('&&' | '@@' | '@>' | '<@');

NamedArgumentArrow: {Dialect == "snowflake"}? '=>';

CompoundAssignment: ('+'|'-'|'*'|'/'|'%'|'&'|'|'|'^') '=';

QuestionMark: '?';

Underscore: '_';

VerticalBar: '|';

Tilda: '~';

Hashtag: '#';


// characters

fragment SimpleLatinLetter: (SimpleLatinUpperCaseLetter|SimpleLatinLowerCaseLetter);

fragment SimpleLatinUpperCaseLetter: ('A'|'B'|'C'|'D'|'E'|'F'|'G'|'H'|'I'|'J'|'K'|'L'|'M'|'N'|'O'|'P'|'Q'|'R'|'S'|'T'|'U'|'V'|'W'|'X'|'Y'|'Z');

fragment SimpleLatinLowerCaseLetter: ('a'|'b'|'c'|'d'|'e'|'f'|'g'|'h'|'i'|'j'|'k'|'l'|'m'|'n'|'o'|'p'|'q'|'r'|'s'|'t'|'u'|'v'|'w'|'x'|'y'|'z');


// numeric fragments

fragment Digit: ('0'|'1'|'2'|'3'|'4'|'5'|'6'|'7'|'8'|'9');

fragment Hexit: (Digit|'A'|'B'|'C'|'D'|'E'|'F'|'a'|'b'|'c'|'d'|'e'|'f');

fragment Bit: ('0'|'1');


// numeric literals

DecimalLiteral: (UnsignedInteger Period UnsignedInteger)|(UnsignedInteger Period {Dialect != "oracle" || InputStream.LA(1) != '.'}?)|(Period UnsignedInteger);

UnsignedInteger: Digit (Digit | {AllowsPostgresLiterals || IsSqliteSyntax()}? '_' Digit)*;

ApproximateNumericLiteral: (UnsignedInteger|DecimalLiteral) E SignedInteger | {Dialect == "tsql"}? (UnsignedInteger|DecimalLiteral) E (PlusSign|MinusSign)? {InputStream.LA(1) is < '0' or > '9'}?;

SqliteHexInteger: {IsSqliteSyntax()}? '0' X Hexit (Hexit | '_' Hexit)*;

TsqlBinaryLiteral: {Dialect == "tsql"}? '0' X Hexit*;

MySqlHexInteger: {IsMySqlDialect}? '0x' Hexit+;

MySqlBinaryInteger: {IsMySqlDialect}? '0b' Bit+;

fragment SignedInteger: (PlusSign|MinusSign)? UnsignedInteger;


// special characters and character sequences

fragment NonquoteCharacter: {!AllowsBackslashEscapes}? ~'\'' | {AllowsBackslashEscapes}? ~['\\] | {AllowsBackslashEscapes}? '\\' .;

fragment QuoteSymbol: SingleQuote SingleQuote;

Introducer: Underscore;

fragment NewLine: ([\r][\n])|[\n]|[\r];

Space: [ \t]+;

fragment LiteralSeparator: NewLine | Space;

Comment: (LineComment | MultilineComment) -> channel (HIDDEN);

LineComment : LineCommentPrefix ~ [\r\n]*;

fragment LineCommentPrefix: '--' {!IsMySqlDialect || InputStream.LA(1) <= ' '}? | {AllowsHashComments}? '#';

MultilineComment: '/*' ({AllowsNestedComments}? MultilineComment | {AllowsNestedComments}? '/' ~[*] | {AllowsNestedComments}? '*' ~[/] | {AllowsNestedComments}? ~[*/] | {!AllowsNestedComments}? .)*? '*/';

ORACLE_ABSENT: {Dialect == "oracle"}? A B S E N T;

ORACLE_APPLY: {Dialect == "oracle"}? A P P L Y;

ORACLE_BITMAP: {Dialect == "oracle"}? B I T M A P;

ORACLE_BODY: {Dialect == "oracle"}? B O D Y;

ORACLE_BREADTH: {Dialect == "oracle"}? B R E A D T H;

ORACLE_BUILD: {Dialect == "oracle"}? B U I L D;

ORACLE_BULK: {Dialect == "oracle"}? B U L K;

ORACLE_COLLECT: {Dialect == "oracle"}? C O L L E C T;

ORACLE_COMMENT: {Dialect == "oracle"}? C O M M E N T;

ORACLE_CONNECT: {Dialect == "oracle"}? C O N N E C T;

ORACLE_CONNECT_BY_ROOT: {Dialect == "oracle"}? C O N N E C T '_' B Y '_' R O O T;

ORACLE_CONSTANT: {Dialect == "oracle"}? C O N S T A N T;

ORACLE_CONTINUE: {Dialect == "oracle"}? C O N T I N U E;

ORACLE_CYCLE: {Dialect == "oracle"}? C Y C L E;

ORACLE_DEFINE: {Dialect == "oracle"}? D E F I N E;

ORACLE_DEMAND: {Dialect == "oracle"}? D E M A N D;

ORACLE_DENSE_RANK: {Dialect == "oracle"}? D E N S E '_' R A N K;

ORACLE_DEPTH: {Dialect == "oracle"}? D E P T H;

ORACLE_DIMENSION: {Dialect == "oracle"}? D I M E N S I O N;

ORACLE_EMPTY: {Dialect == "oracle"}? E M P T Y;

ORACLE_ERROR: {Dialect == "oracle"}? E R R O R;

ORACLE_EXCEPTIONS: {Dialect == "oracle"}? E X C E P T I O N S;

ORACLE_EXIT: {Dialect == "oracle"}? E X I T;

ORACLE_FAST: {Dialect == "oracle"}? F A S T;

ORACLE_FORALL: {Dialect == "oracle"}? F O R A L L;

ORACLE_GRANT: {Dialect == "oracle"}? G R A N T;

ORACLE_IGNORE: {Dialect == "oracle"}? I G N O R E;

ORACLE_INCLUDE: {Dialect == "oracle"}? I N C L U D E;

ORACLE_INDICES: {Dialect == "oracle"}? I N D I C E S;

ORACLE_JSON_EXISTS: {Dialect == "oracle"}? J S O N '_' E X I S T S;

ORACLE_JSON_OBJECT: {Dialect == "oracle"}? J S O N '_' O B J E C T;

ORACLE_JSON_TABLE: {Dialect == "oracle"}? J S O N '_' T A B L E;

ORACLE_JSON_VALUE: {Dialect == "oracle"}? J S O N '_' V A L U E;

ORACLE_KEEP: {Dialect == "oracle"}? K E E P;

ORACLE_LISTAGG: {Dialect == "oracle"}? L I S T A G G;

ORACLE_MATCH_RECOGNIZE: {Dialect == "oracle"}? M A T C H '_' R E C O G N I Z E;

ORACLE_MAXVALUE: {Dialect == "oracle"}? M A X V A L U E;

ORACLE_MEASURES: {Dialect == "oracle"}? M E A S U R E S;

ORACLE_MINUS: {Dialect == "oracle"}? M I N U S;

ORACLE_MINVALUE: {Dialect == "oracle"}? M I N V A L U E;

ORACLE_MODEL: {Dialect == "oracle"}? M O D E L;

ORACLE_MODIFY: {Dialect == "oracle"}? M O D I F Y;

ORACLE_NOCYCLE: {Dialect == "oracle"}? N O C Y C L E;

ORACLE_OBJECT: {Dialect == "oracle"}? O B J E C T;

ORACLE_ONE: {Dialect == "oracle"}? O N E;

ORACLE_ORDINALITY: {Dialect == "oracle"}? O R D I N A L I T Y;

ORACLE_OVERFLOW: {Dialect == "oracle"}? O V E R F L O W;

ORACLE_PACKAGE: {Dialect == "oracle"}? P A C K A G E;

ORACLE_PAST: {Dialect == "oracle"}? P A S T;

ORACLE_PATH: {Dialect == "oracle"}? P A T H;

ORACLE_PATTERN: {Dialect == "oracle"}? P A T T E R N;

ORACLE_PIVOT: {Dialect == "oracle"}? P I V O T;

ORACLE_PURGE: {Dialect == "oracle"}? P U R G E;

ORACLE_RECORD: {Dialect == "oracle"}? R E C O R D;

ORACLE_REFRESH: {Dialect == "oracle"}? R E F R E S H;

ORACLE_RESPECT: {Dialect == "oracle"}? R E S P E C T;

ORACLE_REVERSE: {Dialect == "oracle"}? R E V E R S E;

ORACLE_REVOKE: {Dialect == "oracle"}? R E V O K E;

ORACLE_RULES: {Dialect == "oracle"}? R U L E S;

ORACLE_SAMPLE: {Dialect == "oracle"}? S A M P L E;

ORACLE_SAVE: {Dialect == "oracle"}? S A V E;

ORACLE_SAVEPOINT: {Dialect == "oracle"}? S A V E P O I N T;

ORACLE_SCN: {Dialect == "oracle"}? S C N;

ORACLE_SEARCH: {Dialect == "oracle"}? S E A R C H;

ORACLE_SEED: {Dialect == "oracle"}? S E E D;

ORACLE_SIBLINGS: {Dialect == "oracle"}? S I B L I N G S;

ORACLE_START: {Dialect == "oracle"}? S T A R T;

ORACLE_STORAGE: {Dialect == "oracle"}? S T O R A G E;

ORACLE_TRUNCATE: {Dialect == "oracle"}? T R U N C A T E;

ORACLE_UNPIVOT: {Dialect == "oracle"}? U N P I V O T;

ORACLE_VERSIONS: {Dialect == "oracle"}? V E R S I O N S;

ORACLE_XMLATTRIBUTES: {Dialect == "oracle"}? X M L A T T R I B U T E S;

ORACLE_XMLELEMENT: {Dialect == "oracle"}? X M L E L E M E N T;

ORACLE_XMLFOREST: {Dialect == "oracle"}? X M L F O R E S T;

Identifier: IdentifierBody ({AllowsPostgresLiterals}? Dollar IdentifierPart*)*;

fragment IdentifierBody:  IdentifierStart IdentifierPart*;

fragment IdentifierStart: [\p{L}\p{Nl}]|Underscore|{AllowsTempTables}? Hashtag;

fragment IdentifierPart: [\p{L}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}] | Underscore;

// string literals

fragment CharacterRepresentation: (NonquoteCharacter|QuoteSymbol);

NationalCharacterStringLiteral: N SingleQuote CharacterRepresentation* SingleQuote (LiteralSeparator+ SingleQuote CharacterRepresentation* SingleQuote)*;

BitStringLiteral: B SingleQuote Bit* SingleQuote (LiteralSeparator+ SingleQuote Bit* SingleQuote)*;

HexStringLiteral: X SingleQuote Hexit* SingleQuote (LiteralSeparator+ SingleQuote Hexit* SingleQuote)*;

MySqlIntroducedLiteral: {IsMySqlDialect}? Underscore [a-zA-Z] [a-zA-Z0-9_]* StringLiteralContent;

OracleAlternativeLiteral:
    {AllowsOracleAlternativeLiteral}? N? Q SingleQuote
    ('[' .*? ']' | '(' .*? ')' | '{' .*? '}' | '<' .*? '>') SingleQuote;

PostgresEscapeStringLiteral: {AllowsPostgresLiterals}? E PostgresEscapeStringPart (LiteralSeparator+ PostgresEscapeStringPart)*;

PostgresUnicodeStringLiteral: {AllowsPostgresLiterals}? U Ampersand StringLiteralContent;

PostgresUnicodeIdentifier: {AllowsPostgresLiterals}? U Ampersand DoubleQuote ('""' | ~["])* DoubleQuote;

fragment PostgresEscapeStringPart: SingleQuote (~['\\] | '\\' . | QuoteSymbol)* SingleQuote;

StringLiteralContent: SingleQuote CharacterRepresentation* SingleQuote (LiteralSeparator+ SingleQuote CharacterRepresentation* SingleQuote)*;

WS: Separator;


// quotted something

Quotted: {false}? DoubleQuote;
