# Run the latest migration against the PostgreSQL database
# Usage: .\run_migration.ps1
# Or for Docker: Get-Content .\2026_02_13_notifications_and_permissions.sql | docker exec -i gisv2-postgres-1 psql -U postgres -d gis_v2

param(
    [string]$Host = "localhost",
    [int]$Port = 5433,
    [string]$Database = "gis_v2",
    [string]$User = "postgres",
    [string]$Password = "postgres",
    [switch]$Docker
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlFile = Join-Path $scriptDir "2026_02_13_notifications_and_permissions.sql"

if ($Docker) {
    Write-Host "Running migration via Docker..." -ForegroundColor Cyan
    Get-Content $sqlFile | docker exec -i gisv2-postgres-1 psql -U $User -d $Database -v ON_ERROR_STOP=1
} else {
    Write-Host "Running migration directly..." -ForegroundColor Cyan
    $env:PGPASSWORD = $Password
    psql -h $Host -p $Port -U $User -d $Database -f $sqlFile -v ON_ERROR_STOP=1
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nMigration completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`nMigration failed!" -ForegroundColor Red
}
