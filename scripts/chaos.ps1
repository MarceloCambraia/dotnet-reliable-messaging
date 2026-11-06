<#
.SYNOPSIS
    Reproduces the failure scenarios live against `docker compose up` (Windows PowerShell 5.1+ / PowerShell 7).
.EXAMPLE
    ./scripts/chaos.ps1 broker-down
    ./scripts/chaos.ps1 duplicate
    ./scripts/chaos.ps1 dlq
#>
param(
    [ValidateSet('broker-down', 'duplicate', 'dlq')]
    [string] $Scenario = 'broker-down',
    [string] $Api = 'http://localhost:8080',
    [string] $Management = 'http://localhost:15672'
)

$ErrorActionPreference = 'Stop'
$queue = 'inventory.order-created'
$auth = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('app:app')) }

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Info($text) { Write-Host "    $text" }
function OutboxStats { Invoke-RestMethod "$Api/diagnostics/outbox" }
function Inventory($sql) { docker compose exec -T postgres psql -U postgres -d inventory -At -c $sql }
function QueueDepth($name) { (Invoke-RestMethod "$Management/api/queues/%2F/$name" -Headers $auth).messages }

function New-Order($sku, $correlationId) {
    $body = @{ customerId = 'chaos-demo'; items = @(@{ sku = $sku; quantity = 1; unitPrice = 10 }) } | ConvertTo-Json -Depth 5
    $response = Invoke-WebRequest "$Api/orders" -Method Post -Body $body -ContentType 'application/json' `
        -Headers @{ 'X-Correlation-Id' = $correlationId } -UseBasicParsing
    $response.StatusCode
}

switch ($Scenario) {
    'broker-down' {
        Step 'Outbox before the outage'; Info (OutboxStats | ConvertTo-Json -Compress)
        Step 'Stopping RabbitMQ'; docker compose stop rabbitmq | Out-Null
        Step 'Creating 5 orders while the broker is down'
        1..5 | ForEach-Object { Info "POST /orders -> HTTP $(New-Order 'SKU-1' "chaos-outage-$_")" }
        Step 'Health: readiness stays green, RabbitMQ shows as Degraded'
        Info ((Invoke-RestMethod "$Api/health").checks | ConvertTo-Json -Compress)
        Step 'Events wait in the outbox'; Start-Sleep 3; Info (OutboxStats | ConvertTo-Json -Compress)
        Step 'Starting RabbitMQ again'; docker compose start rabbitmq | Out-Null
        Step 'Waiting for the publisher to drain the outbox'
        for ($i = 0; $i -lt 90 -and (OutboxStats).pending -gt 0; $i++) { Start-Sleep 1 }
        Info (OutboxStats | ConvertTo-Json -Compress)
        Info 'Open Jaeger (http://localhost:16686) to see the request and the late publish in one trace.'
    }
    'duplicate' {
        $messageId = [guid]::NewGuid(); $orderId = [guid]::NewGuid(); $now = (Get-Date).ToUniversalTime().ToString('o')
        $envelope = @{
            messageId = $messageId; type = 'orders.order-created.v1'; correlationId = 'chaos-duplicate'; occurredAt = $now
            payload = @{ orderId = $orderId; customerId = 'chaos-demo'; items = @(@{ sku = 'SKU-2'; quantity = 1 }); total = 10; placedAt = $now }
        } | ConvertTo-Json -Depth 5 -Compress
        $publish = @{
            properties = @{ delivery_mode = 2; content_type = 'application/json' }
            routing_key = 'orders.order-created.v1'; payload = $envelope; payload_encoding = 'string'
        } | ConvertTo-Json -Depth 5

        Step "Stock of SKU-2 before: $(Inventory "select available_quantity from products where sku = 'SKU-2'")"
        Step "Publishing the SAME message ($messageId) twice"
        1..2 | ForEach-Object { Info (Invoke-RestMethod "$Management/api/exchanges/%2F/orders.events/publish" -Method Post -Headers $auth -Body $publish -ContentType 'application/json' | ConvertTo-Json -Compress) }
        Start-Sleep 3
        Step 'Result: one reservation, stock decremented once, one inbox row'
        Info "reservations: $(Inventory "select count(*) from stock_reservations where order_id = '$orderId'")"
        Info "inbox rows:   $(Inventory "select count(*) from inbox_messages where message_id = '$messageId'")"
        Info "SKU-2 stock:  $(Inventory "select available_quantity from products where sku = 'SKU-2'")"
    }
    'dlq' {
        Step "DLQ depth before: $(QueueDepth "$queue.dlq")"
        Step 'Creating an order with SKU-FAIL (the worker is configured to fail on it)'
        Info "POST /orders -> HTTP $(New-Order 'SKU-FAIL' 'chaos-dlq')"
        Step 'Watching retries (2s, 5s, 10s) and the dead-letter queue'
        for ($i = 0; $i -lt 30; $i++) {
            Write-Host ("    main={0} retry.1={1} retry.2={2} retry.3={3} dlq={4}   " -f (QueueDepth $queue), (QueueDepth "$queue.retry.1"), (QueueDepth "$queue.retry.2"), (QueueDepth "$queue.retry.3"), (QueueDepth "$queue.dlq")) -NoNewline
            Write-Host "`r" -NoNewline
            Start-Sleep 1
        }
        Step "DLQ depth after: $(QueueDepth "$queue.dlq")"
    }
}
