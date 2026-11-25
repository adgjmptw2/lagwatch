param([int]$cycle=60,[string]$only="",[string]$out=$env:TEMP)
Set-Location $PSScriptRoot
$csc=(Get-ChildItem "$env:ProgramFiles\dotnet\sdk" -Directory | Where-Object { Test-Path "$($_.FullName)\Roslyn\bincore\csc.dll" } | Sort-Object { [version]($_.Name -replace '-.*','') } | Select-Object -Last 1).FullName + '\Roslyn\bincore\csc.dll'
& dotnet $csc '@csc_test.rsp' -target:exe -out:build\Desk2.exe tests\Desk2.cs | Where-Object { $_ -notmatch 'CS2023' }
& dotnet $csc '@csc.rsp' -target:winexe -out:build\LagWatch.exe -win32manifest:app.manifest -win32icon:app.ico '-resource:res\a.ttf,a.ttf' '-resource:res\b.ttf,b.ttf' @('Native', 'Sys', 'Lang', 'Model', 'Names', 'Clock', 'Store', 'History', 'Monitor', 'Rules', 'D2', 'Ui', 'Fx', 'View', 'Trace', 'Blame', 'Clean', 'Fix', 'Actions', 'Game', 'Doctor', 'Main', 'Flow', 'Pages', 'GamePage', 'Ask', 'Panel', 'Windows', 'Demo','Program' | ForEach-Object { "src\$_.cs" }) | Where-Object { $_ -notmatch 'CS2023' }
& build\Desk2.exe "$PSScriptRoot\build\LagWatch.exe" $out $cycle $(if($only){$only})
