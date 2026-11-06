param(
    [Parameter(Mandatory = $true)]
    [string]$PostgreSqlBin,
    [string]$Filter = 'Category=PostgreSQL',
    [switch]$AllTests,
    [switch]$Coverage
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$binaryPath = (Resolve-Path -LiteralPath $PostgreSqlBin).Path
$initDb = Join-Path $binaryPath 'initdb.exe'
$pgCtl = Join-Path $binaryPath 'pg_ctl.exe'
$postgres = Join-Path $binaryPath 'postgres.exe'
$pgReady = Join-Path $binaryPath 'pg_isready.exe'
if (!(Test-Path -LiteralPath $initDb) -or !(Test-Path -LiteralPath $pgCtl) -or
    !(Test-Path -LiteralPath $postgres) -or !(Test-Path -LiteralPath $pgReady)) {
    throw 'PostgreSqlBin must contain the PostgreSQL server and command-line tools.'
}

# Allocate a port independently of any existing PostgreSQL service.
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$testPort = $listener.LocalEndpoint.Port
$listener.Stop()
$runPath = Join-Path $repoPath ('TestResults\postgresql-' + [Guid]::NewGuid().ToString('N'))
$dataPath = Join-Path $runPath 'data'
$logPath = Join-Path $runPath 'server.log'
New-Item -ItemType Directory -Path $runPath | Out-Null
$previousConnection = [Environment]::GetEnvironmentVariable('EVENTLAUSCHER_TEST_POSTGRES', 'Process')
$initialized = $false
$server = $null

try {
    # Trust applies only to this temporary cluster, bound to loopback with test data.
    & $initDb -D $dataPath -U test_admin --auth=trust --encoding=UTF8 --locale=C
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize temporary PostgreSQL cluster.' }
    $initialized = $true
    $server = Start-Process -FilePath $postgres -ArgumentList @('-D', ('"' + $dataPath + '"'), '-h', '127.0.0.1', '-p', $testPort, '-F', '-N', '20', '-B', '16MB') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runPath 'stdout.log') -RedirectStandardError $logPath
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        & $pgReady -h 127.0.0.1 -p $testPort -U test_admin -d postgres -q
        if ($LASTEXITCODE -eq 0) { break }
        if ($server.HasExited -or [DateTime]::UtcNow -gt $deadline) {
            throw "Could not start PostgreSQL. See $logPath"
        }
        Start-Sleep -Milliseconds 200
    } while ($true)
    $env:EVENTLAUSCHER_TEST_POSTGRES = "Host=127.0.0.1;Port=$testPort;Database=postgres;Username=test_admin;Pooling=false"
    $testArgs = @('test', (Join-Path $repoPath 'EventLauscherApi.sln'), '-c', 'Release', '--logger', 'console;verbosity=quiet')
    if (!$AllTests -and $Filter) { $testArgs += @('--filter', $Filter) }
    if ($Coverage) {
        $testArgs += @('--collect', 'XPlat Code Coverage', '--settings', (Join-Path $repoPath 'Tests\coverage.runsettings'), '--results-directory', $runPath)
    }
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally {
    [Environment]::SetEnvironmentVariable('EVENTLAUSCHER_TEST_POSTGRES', $previousConnection, 'Process')
    if ($initialized -and $null -ne $server -and !$server.HasExited) {
        & $pgCtl -D $dataPath -m fast -w -t 20 stop
    }
    Write-Output "PostgreSQL test logs: $runPath"
}
