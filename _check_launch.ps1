$proc = Get-Process Atlas_v2 -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "ALREADY_RUNNING PID=$($proc.Id) Title=$($proc.MainWindowTitle) Path=$($proc.Path)"
} else {
    Write-Host "NOT_RUNNING - launching now"
    Start-Process -FilePath "D:\My Apps\AOS\New build\Atlas.OS\bin\x64\Atlas_v2.exe" -WorkingDirectory "D:\My Apps\AOS\New build\Atlas.OS\bin\x64"
    Start-Sleep -Seconds 4
    $proc2 = Get-Process Atlas_v2 -ErrorAction SilentlyContinue
    if ($proc2) {
        Write-Host "LAUNCHED PID=$($proc2.Id) Path=$($proc2.Path)"
    } else {
        Write-Host "LAUNCH_FAILED - process not found after 4 seconds"
    }
}
