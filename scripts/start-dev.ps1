$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:DATABASE_URL)) {
    $env:DATABASE_URL = Read-Host 'DATABASE_URL'
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:PORT = '5088'
dotnet run --project .\src\Mirage.Api\Mirage.Api.csproj
