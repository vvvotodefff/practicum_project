#requires -Version 7.0
param([int]$Port = 5260)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$baseUrl = "http://127.0.0.1:$Port"
$createdIds = [System.Collections.Generic.List[string]]::new()
$apiProcess = $null
$logBase = Join-Path ([System.IO.Path]::GetTempPath()) ("eventapi-check-" + [guid]::NewGuid())
$stdoutPath = "$logBase.stdout.log"
$stderrPath = "$logBase.stderr.log"

function Assert-Check($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Request([string]$method, [string]$path, $body = $null) {
    $parameters = @{
        Uri = "$baseUrl$path"; Method = $method
        SkipHttpErrorCheck = $true; TimeoutSec = 15
    }
    if ($null -ne $body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $body | ConvertTo-Json -Depth 5 -Compress
    }
    $response = Invoke-WebRequest @parameters
    # application/problem+json может возвращаться PowerShell как byte[], а не строка.
    $content = if ($response.Content -is [byte[]]) {
        [System.Text.Encoding]::UTF8.GetString($response.Content)
    } else { [string]$response.Content }
    $json = if ($content) {
        try { $content | ConvertFrom-Json } catch { $null }
    } else { $null }
    return [pscustomobject]@{ Code = [int]$response.StatusCode; Json = $json; Response = $response }
}

function Start-Api {
    $arguments = @('run', '--no-build', '--no-launch-profile', '--project', 'ProjectWork.Presentation/ProjectWork.Presentation.csproj', '--', '--urls', $baseUrl)
    $script:apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($script:apiProcess.HasExited) {
            throw "API terminated: $(Get-Content $stderrPath -Raw) $(Get-Content $stdoutPath -Raw)"
        }
        try {
            $result = Request GET '/swagger/v1/swagger.json'
            if ($result.Code -eq 200) { return $result.Json }
        } catch { }
        Start-Sleep -Milliseconds 200
    }
    throw "API did not start. Logs: $stdoutPath, $stderrPath"
}

function Stop-Api {
    if ($null -ne $script:apiProcess -and -not $script:apiProcess.HasExited) {
        $script:apiProcess.Kill($true)
        $script:apiProcess.WaitForExit()
    }
    $script:apiProcess = $null
}

function New-TestEvent([int]$seats) {
    $title = "EventApi-check-$([guid]::NewGuid())"
    $request = @{
        title = $title; description = 'HTTP verification'
        startAt = '2026-11-01T12:00:00Z'; endAt = '2026-11-01T13:00:00Z'; totalSeats = $seats
    }
    $result = Request POST '/events' $request
    Assert-Check ($result.Code -eq 201) "Create failed: $($result.Response.Content)"
    $createdIds.Add([string]$result.Json.id)
    Assert-Check ($result.Response.Headers.Location -match "/events/$($result.Json.id)") 'Invalid Location'
    Assert-Check ($null -eq $result.Json.PSObject.Properties['bookings']) 'Event response leaks navigation properties'
    return $result.Json
}

try {
    # Не подключаемся случайно к чужому экземпляру API на выбранном порту.
    $portCheck = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try { $portCheck.Start() } finally { $portCheck.Stop() }
    $swagger = Start-Api
    $expectedOperations = @{
        '/events' = @('get', 'post')
        '/events/{id}' = @('get', 'put', 'delete')
        '/events/{id}/book' = @('post')
        '/bookings/{id}' = @('get')
    }
    foreach ($path in $expectedOperations.Keys) {
        foreach ($method in $expectedOperations[$path]) {
            Assert-Check ($null -ne $swagger.paths.$path.$method) "Swagger operation missing: $method $path"
        }
    }
    Assert-Check ((Request GET '/swagger/index.html').Code -eq 200) 'Swagger UI unavailable'
    Assert-Check (-not [string]::IsNullOrWhiteSpace($swagger.components.schemas.EventInfo.properties.title.description)) 'EventInfo XML documentation missing after moving DTOs'
    Assert-Check (-not [string]::IsNullOrWhiteSpace($swagger.components.schemas.UpdateEvent.properties.totalSeats.description)) 'UpdateEvent XML documentation missing after moving DTOs'
    $invalid = Request POST '/events' @{
        title = 'Invalid'; startAt = '2026-11-01T13:00:00Z'
        endAt = '2026-11-01T12:00:00Z'; totalSeats = 1
    }
    Assert-Check ($invalid.Code -eq 400) 'Invalid dates must return 400'
    Assert-Check ($invalid.Json.status -eq 400) 'Domain validation must return Problem Details with status 400'
    Assert-Check ($invalid.Response.Headers['Content-Type'] -match 'application/problem\+json') 'Domain error must use application/problem+json'
    $missingDates = Request POST '/events' @{ title = 'Missing dates'; totalSeats = 1 }
    Assert-Check ($missingDates.Code -eq 400) 'Missing dates must return 400'
    Assert-Check ((Request POST '/events' @{ title = 'Missing fields' }).Code -eq 400) 'Missing fields must return 400'
    $missing = [guid]::NewGuid()
    Assert-Check ((Request GET "/events/$missing").Code -eq 404) 'Missing event must return 404'
    Assert-Check ((Request GET "/bookings/$missing").Code -eq 404) 'Missing booking must return 404'
    Assert-Check ((Request POST "/events/$missing/book").Code -eq 404) 'Booking a missing event must return 404'
    foreach ($query in @('page=0', 'pageSize=0')) {
        $invalidPage = Request GET "/events?$query"
        Assert-Check ($invalidPage.Code -eq 400 -and $null -ne $invalidPage.Json.errors) 'Automatic query validation must return 400 with errors'
    }

    $event = New-TestEvent 5
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    try {
        $tasks = [System.Collections.Generic.List[System.Threading.Tasks.Task[System.Net.Http.HttpResponseMessage]]]::new()
        for ($i = 0; $i -lt 20; $i++) {
            $tasks.Add($client.PostAsync("$baseUrl/events/$($event.id)/book", $null))
        }
        $accepted = [System.Collections.Generic.List[object]]::new()
        $conflicts = 0
        foreach ($task in $tasks) {
            $response = $task.GetAwaiter().GetResult()
            try {
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                if ([int]$response.StatusCode -eq 202) {
                    $bookingDto = $body | ConvertFrom-Json
                    $accepted.Add($bookingDto)
                    Assert-Check ($null -eq $bookingDto.PSObject.Properties['event']) 'Booking response leaks navigation properties'
                    Assert-Check ($response.Headers.Location.ToString() -match "/bookings/$($bookingDto.id)") 'Invalid booking Location'
                }
                elseif ([int]$response.StatusCode -eq 409) {
                    Assert-Check ($response.Content.Headers.ContentType.MediaType -eq 'application/problem+json') 'Booking conflict must use application/problem+json'
                    $conflicts++
                }
                else { throw "Booking failed: $($response.StatusCode) $body" }
            } finally { $response.Dispose() }
        }
        Assert-Check ($accepted.Count -eq 5 -and $conflicts -eq 15) 'Concurrent bookings exceeded seats or failed'
    } finally { $client.Dispose() }

    $savedEvent = Request GET "/events/$($event.id)"
    Assert-Check ($savedEvent.Code -eq 200 -and $savedEvent.Json.availableSeats -eq 0) 'Seat counter not saved'
    Assert-Check ($null -eq $savedEvent.Json.PSObject.Properties['bookings']) 'GET event leaks navigation properties'
    $invalidUpdate = Request PUT "/events/$($event.id)" @{
        title = 'Invalid capacity'; startAt = '2026-11-01T12:00:00Z'
        endAt = '2026-11-01T13:00:00Z'; totalSeats = 4
    }
    Assert-Check ($invalidUpdate.Code -eq 400) 'Capacity below occupied seats must return 400'
    $unchanged = Request GET "/events/$($event.id)"
    Assert-Check ($unchanged.Json.title -eq $event.title -and $unchanged.Json.totalSeats -eq 5) 'Invalid update changed the event'
    $updated = Request PUT "/events/$($event.id)" @{
        title = $event.title; startAt = '2026-11-01T12:00:00Z'
        endAt = '2026-11-01T13:00:00Z'; totalSeats = 7
    }
    Assert-Check ($updated.Code -eq 204) 'Update failed'
    Assert-Check ((Request GET "/events/$($event.id)").Json.availableSeats -eq 2) 'Update lost reserved seats'
    $filter = [uri]::EscapeDataString($event.title.ToUpperInvariant())
    $page = Request GET "/events?title=$filter&page=1&pageSize=10&from=2026-11-01T00:00:00Z"
    Assert-Check ($page.Code -eq 200 -and $page.Json.totalCount -eq 1) 'SQL filters or pagination failed'
    Assert-Check ($null -eq $page.Json.items[0].PSObject.Properties['bookings']) 'Event page leaks navigation properties'

    $restartEvent = New-TestEvent 1
    $pending = Request POST "/events/$($restartEvent.id)/book"
    Assert-Check ($pending.Code -eq 202) 'Booking before restart failed'
    Stop-Api
    $null = Start-Api

    Assert-Check ((Request GET "/events/$($event.id)").Json.availableSeats -eq 2) 'Event lost after restart'
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $booking = Request GET "/bookings/$($pending.Json.id)"
        if ($booking.Json.status -eq 'Confirmed') { break }
        Start-Sleep -Milliseconds 200
    }
    Assert-Check ($booking.Code -eq 200 -and $booking.Json.status -eq 'Confirmed') 'Pending booking not processed after restart'
    Assert-Check ($null -ne $booking.Json.processedAt) 'Processing timestamp missing'
    Assert-Check ($null -eq $booking.Json.PSObject.Properties['event']) 'GET booking leaks navigation properties'
    Assert-Check ((Request DELETE "/events/$($event.id)").Code -eq 204) 'Delete failed'
    $createdIds.Remove([string]$event.id) | Out-Null
    Assert-Check ((Request GET "/bookings/$($accepted[0].id)").Code -eq 404) 'Cascade delete failed'
    Write-Output 'PASS: Swagger, DTO contracts, domain validation, CRUD, SQL filters, 20 concurrent bookings, restart and cascade delete.'
}
finally {
    foreach ($id in $createdIds) {
        try {
            if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
                $result = Request DELETE "/events/$id"
                if ($result.Code -notin @(204, 404)) { Write-Warning "Cleanup failed for event $id" }
            } else { Write-Warning "Test event $id may remain in the database." }
        } catch { Write-Warning "Cleanup failed for event $id" }
    }
    Stop-Api
    Write-Output "API logs: $stdoutPath, $stderrPath"
}
