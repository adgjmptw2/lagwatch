param([string]$out='LagWatchCli',[string]$extra='src\Program.cs')
Set-Location $PSScriptRoot
$csc=(Get-ChildItem "$env:ProgramFiles\dotnet\sdk" -Directory | Where-Object { Test-Path "$($_.FullName)\Roslyn\bincore\csc.dll" } | Sort-Object { [version]($_.Name -replace '-.*','') } | Select-Object -Last 1).FullName + '\Roslyn\bincore\csc.dll'
$core = 'Native', 'Sys', 'Lang', 'Model', 'Names', 'Clock', 'Store', 'History', 'Monitor', 'Rules', 'D2', 'Ui', 'View', 'Trace', 'Blame', 'Clean', 'Fix', 'Actions', 'Game', 'Doctor', 'Main', 'Flow', 'Pages', 'GamePage', 'Ask', 'Panel', 'Windows', 'Demo' | ForEach-Object { "src\$_.cs" }
$rsp = if ($extra -match 'tests') { '@csc_test.rsp' } else { '@csc.rsp' }
& dotnet $csc $rsp -target:exe "-out:build\$out.exe" -win32manifest:app.manifest '-resource:res\a.ttf,a.ttf' '-resource:res\b.ttf,b.ttf' @core @($extra -split ",") | Where-Object { $_ -notmatch 'CS2023' } | Select-Object -First 40
