param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repository
try {
    # Три API, JWT и PostgreSQL в собственном временном контейнере.
    # Рабочие базы compose не используются. Docker Desktop должен быть запущен.
    dotnet test ProjectWork.PresentationTests/ProjectWork.PresentationTests.csproj --configuration $Configuration --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "HTTP-проверки сервисов завершились с кодом $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
