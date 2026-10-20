$ErrorActionPreference = 'Stop'
$env:CLOUDSDK_PYTHON = 'C:\Users\user1\AppData\Local\Programs\Python\Python312\python.exe'

Write-Host ''
Write-Host 'Summit business ID (a NUMBER, from Summit: Settings > API). It is stored in Secret Manager only.'
$id = (Read-Host 'Paste the numeric business ID and press Enter').Trim()

if ($id -notmatch '^[0-9]+$') {
    Write-Host 'This is not a number. Nothing was changed.' -ForegroundColor Red
    exit 1
}

$tmp = Join-Path $env:TEMP 'summit-company-id.txt'
[System.IO.File]::WriteAllText($tmp, $id)
try {
    gcloud secrets versions add summit-company-id --data-file=$tmp --project rent-photo
}
finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}

Write-Host 'Saved. Restarting the server so it picks up the new value...'
$stamp = (Get-Date).ToString('yyyyMMddHHmmss')
gcloud run services update photorental-api --region me-west1 --project rent-photo --update-env-vars "SECRETS_REFRESH=$stamp" --quiet
Write-Host 'Done. (A 418 error at the very end is only NetFree blocking the status check - it is fine.)'
