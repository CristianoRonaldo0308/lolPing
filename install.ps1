# Cài lolPing lên máy: build bản Release, chép vào %LOCALAPPDATA%\Programs\lolPing,
# tạo lối tắt trên Desktop và Start menu rồi mở app.
# Chạy lại file này mỗi khi có bản cập nhật. Cài đặt, âm thanh và ảnh của bạn (%AppData%\lolPing) được giữ nguyên.

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\LolPing\LolPing.csproj'
$dest    = Join-Path $env:LOCALAPPDATA 'Programs\lolPing'
$exe     = Join-Path $dest 'lolPing.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'Chưa có .NET SDK. Cài bằng lệnh: winget install Microsoft.DotNet.SDK.10' -ForegroundColor Red
    exit 1
}

Write-Host '1/4  Tắt lolPing nếu đang chạy...'
Get-Process lolPing -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

Write-Host '2/4  Build bản Release (lần đầu có thể mất vài phút)...'
dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $dest
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build thất bại. Hãy gửi phần báo lỗi màu đỏ ở trên.' -ForegroundColor Red
    exit 1
}

Write-Host '3/4  Tạo lối tắt trên Desktop và Start menu...'
$shell = New-Object -ComObject WScript.Shell
$places = @(
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('Programs')
)
foreach ($place in $places) {
    $lnk = $shell.CreateShortcut((Join-Path $place 'lolPing.lnk'))
    $lnk.TargetPath       = $exe
    $lnk.WorkingDirectory = $dest
    $lnk.IconLocation     = "$exe,0"
    $lnk.Description      = 'lolPing - Phát tín hiệu mọi nơi'
    $lnk.Save()
}

Write-Host '4/4  Mở lolPing...'
Start-Process $exe
Write-Host "Xong! App đã cài tại $dest. Từ giờ mở bằng biểu tượng lolPing trên Desktop." -ForegroundColor Green
