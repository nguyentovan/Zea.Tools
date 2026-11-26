param(
    [string] $ParserPath = (Join-Path $PSScriptRoot 'Generated/SQLStandardParser.cs')
)

$ErrorActionPreference = 'Stop'
$path = (Resolve-Path -LiteralPath $ParserPath).Path
$source = [IO.File]::ReadAllText($path)
$marker = '// Generated T-SQL predicate metadata.'
$position = $source.IndexOf($marker, [StringComparison]::Ordinal)
if ($position -ge 0) {
    $source = $source.Substring(0, $position)
}

$start = $source.IndexOf('public override bool Sempred', [StringComparison]::Ordinal)
if ($start -lt 0) {
    throw "Missing the generated parser predicate dispatch in $path"
}

$fixedMethods = @(
    'AllowsAggregateFilter', 'AllowsArrayLiteral', 'AllowsConflictClause', 'AllowsDistinctComparison',
    'AllowsGroupingSets', 'AllowsLimit', 'AllowsMerge', 'AllowsMergeByTarget', 'AllowsNullOrder',
    'AllowsReturning', 'AllowsTop', 'AllowsTypeCast', 'AllowsWindowExclusion', 'TrailingSelectIntoAllowed'
)
$fixedMethodPattern = '^!?(?:' + ($fixedMethods -join '|') + ')\(\)$'
$fixedDialect = '^!?Is(?:Oracle|Postgres|MySql|Sqlite|TsqlProcedural|FirebirdProcedural|GenericProcedural)Syntax\(\)$'
$comparison = '(?:NextWordIs\("[A-Z0-9_]+"(?:, [0-9]+)?\)|TokenStream\.LA\([0-9]+\) == [A-Z_]+|IsRowLevelSecurityAhead\(\)|!IsOracleSyntax\(\))'
$falseTsqlPrefix = '^(?:Is(?:Oracle|Postgres|MySql|Sqlite|FirebirdProcedural|GenericProcedural)Syntax\(\)|Dialect == "(?!tsql")[a-z]+") && ' +
    $comparison + '(?: && ' + $comparison + ')*$'
$dialectLiteral = '^Dialect (?:== |is )"[a-z]+"(?: or "[a-z]+")*$'
$flags = '^!?(?:HasValueExpressionOperators|AllowsNamedParameters|AllowsAnonymousParameters)$'
$predicates = [regex]::Matches($source.Substring($start), '(?m)case (\d+): return ([^\r\n]+);\r?$')
$indices = foreach ($predicate in $predicates) {
    $expression = $predicate.Groups[2].Value
    if ($expression -cmatch $fixedMethodPattern -or $expression -cmatch $fixedDialect -or
        $expression -cmatch $falseTsqlPrefix -or $expression -cmatch $dialectLiteral -or
        $expression -cmatch $flags) {
        [int] $predicate.Groups[1].Value
    }
}

if (!$indices -or @($indices | Sort-Object -Unique).Count -ne $indices.Count) {
    throw "Missing or duplicate generated predicate indices in $path"
}

$lines = for ($index = 0; $index -lt $indices.Count; $index += 12) {
    $last = [Math]::Min($index + 11, $indices.Count - 1)
    $indices[$index..$last] -join ' or '
}
$lineBreak = [string] [char] 10
$table = $lines -join (' or' + $lineBreak + '                ')
$template = @(
    $marker,
    'namespace Zea.SqlFormat.Parsing',
    '{',
    '    partial class SQLStandardParser',
    '    {',
    '        partial void ClassifyTsqlPredicate(int predicateIndex, ref bool result)',
    '        {',
    '            result = predicateIndex is',
    "                $table;",
    '        }',
    '    }',
    '}'
)
[IO.File]::WriteAllText($path, $source.TrimEnd() + $lineBreak + $lineBreak + ($template -join $lineBreak) + $lineBreak)

