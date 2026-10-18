# Сквозная проверка системы, запущенной через docker compose up -d --build.
# Users -> Events -> Bookings -> Kafka -> Events: места у события уменьшаются
# без HTTP-вызовов между сервисами.
param(
    [string]$UsersUrl = "http://localhost:5250",
    [string]$EventsUrl = "http://localhost:5251",
    [string]$BookingsUrl = "http://localhost:5252",
    [int]$TimeoutSeconds = 30
)
$ErrorActionPreference = "Stop"

function Get-StatusCode([scriptblock]$Request) {
    try { & $Request | Out-Null; return 200 }
    catch { return [int]$_.Exception.Response.StatusCode }
}

function New-Token([string]$Role) {
    $login = "verify-$($Role.ToLower())-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
    $credentials = @{ login = $login; password = "password" }
    Invoke-RestMethod -Method Post -Uri "$UsersUrl/auth/register" -ContentType "application/json" `
        -Body (($credentials + @{ role = $Role }) | ConvertTo-Json) | Out-Null
    $token = (Invoke-RestMethod -Method Post -Uri "$UsersUrl/auth/login" -ContentType "application/json" `
        -Body ($credentials | ConvertTo-Json)).token
    Write-Host "Users: зарегистрирован и вошёл $login ($Role)"
    return $token
}

$admin = New-Token "Admin"
$user = New-Token "User"

$start = (Get-Date).ToUniversalTime().AddDays(1)
$newEvent = @{
    title = "Docker verify"; startAt = $start.ToString("o"); endAt = $start.AddHours(2).ToString("o"); totalSeats = 5
} | ConvertTo-Json

$userPost = Get-StatusCode { Invoke-RestMethod -Method Post -Uri "$EventsUrl/events" -ContentType "application/json" `
    -Body $newEvent -Headers @{ Authorization = "Bearer $user" } }
if ($userPost -ne 403) { throw "POST /events с ролью User: ожидался 403, получен $userPost." }
$anonymousBook = Get-StatusCode { Invoke-RestMethod -Method Post -Uri "$BookingsUrl/events/$([guid]::NewGuid())/book" }
if ($anonymousBook -ne 401) { throw "Бронирование без токена: ожидался 401, получен $anonymousBook." }
Write-Host "Права: User на POST /events -> 403, бронь без токена -> 401"

$created = Invoke-RestMethod -Method Post -Uri "$EventsUrl/events" -ContentType "application/json" `
    -Body $newEvent -Headers @{ Authorization = "Bearer $admin" }
Write-Host "Events: создано событие $($created.id), свободно мест: $($created.availableSeats)"

$booking = Invoke-RestMethod -Method Post -Uri "$BookingsUrl/events/$($created.id)/book" `
    -Headers @{ Authorization = "Bearer $user" }
Write-Host "Bookings: создана бронь $($booking.id), статус $($booking.status)"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Seconds 1
    $status = (Invoke-RestMethod -Uri "$BookingsUrl/bookings/$($booking.id)" -Headers @{ Authorization = "Bearer $user" }).status
    $seats = (Invoke-RestMethod -Uri "$EventsUrl/events/$($created.id)").availableSeats
} while (($status -ne "Confirmed" -or $seats -ne $created.availableSeats - 1) -and (Get-Date) -lt $deadline)

Write-Host "Итог: бронь $status, свободно мест $seats"
if ($status -ne "Confirmed" -or $seats -ne $created.availableSeats - 1) {
    throw "За $TimeoutSeconds с бронь не подтверждена или места не уменьшились через Kafka."
}
Write-Host "Проверка пройдена" -ForegroundColor Green
