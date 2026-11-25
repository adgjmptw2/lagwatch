# 빌드: pwsh -File build.ps1   → dist\LagWatch.exe
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $sdk = Get-ChildItem "$env:ProgramFiles\dotnet\sdk" -Directory | Where-Object { Test-Path "$($_.FullName)\Roslyn\bincore\csc.dll" } | Sort-Object { [version]($_.Name -replace '-.*', '') } | Select-Object -Last 1
    $csc = "$($sdk.FullName)\Roslyn\bincore\csc.dll"
    New-Item -ItemType Directory -Force dist, build | Out-Null
    $core = 'Native', 'Sys', 'Lang', 'Model', 'Names', 'Clock', 'Store', 'History', 'Monitor', 'Rules', 'D2', 'Ui', 'Fx', 'View', 'Trace', 'Blame', 'Clean', 'Fix', 'Actions', 'Game', 'Doctor', 'Main', 'Flow', 'Pages', 'GamePage', 'Ask', 'Panel', 'Windows', 'Demo' | ForEach-Object { "src\$_.cs" }
    $res = '-resource:res\a.ttf,a.ttf', '-resource:res\b.ttf,b.ttf'
    $ico = @(if (Test-Path app.ico) { '-win32icon:app.ico' })
    & dotnet $csc '@csc.rsp' -target:winexe -out:build\LagWatch.exe -win32manifest:app.manifest @ico @res @core src\Program.cs | Where-Object { $_ -notmatch 'CS2023' }
    if ($LASTEXITCODE) { throw 'build failed' }
    # 창 없는 확인용(콘솔 출력)
    & dotnet $csc '@csc.rsp' -target:exe -out:build\LagWatchCli.exe -win32manifest:app.manifest @res @core src\Program.cs | Where-Object { $_ -notmatch 'CS2023' }
    if ($LASTEXITCODE) { throw 'cli build failed' }
    & dotnet $csc '@csc_test.rsp' -target:exe -out:build\Tests.exe @res @core tests\Tests.cs tests\Tests2.cs tests\Tests3.cs tests\Tests4.cs | Where-Object { $_ -notmatch 'CS2023' }
    if ($LASTEXITCODE) { throw 'test build failed' }
    try { Copy-Item build\LagWatch.exe dist\LagWatch.exe -Force } catch { 'dist 는 실행 중이라 그대로 둠' }
    'built'
}
finally { Pop-Location }
