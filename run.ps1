dotnet test
if ($LASTEXITCODE -eq 0) {
    Write-Host "All tests passed. Starting API..." -ForegroundColor Green
    dotnet run --project src/TallgrassAgentApi/TallgrassAgentApi.csproj
} else {
    Write-Host "Tests failed. API not started." -ForegroundColor Red
    exit 1
}