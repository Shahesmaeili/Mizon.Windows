param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vc = Get-ChildItem "$vs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10"
$sdk = Get-ChildItem "$sdkRoot\Include" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$inc = $sdk.FullName
$env:PATH = "$($vc.FullName)\bin\Hostx64\x64;$env:PATH"
$env:INCLUDE = "$($vc.FullName)\include;$inc\ucrt;$inc\shared;$inc\um;$inc\winrt;$inc\cppwinrt"
$env:LIB = "$($vc.FullName)\lib\x64;$sdkRoot\Lib\$($sdk.Name)\um\x64;$sdkRoot\Lib\$($sdk.Name)\ucrt\x64"
$obj = Join-Path $PSScriptRoot 'obj\native'
New-Item $obj -ItemType Directory -Force | Out-Null
if (!(Test-Path "$inc\cppwinrt\winrt\Windows.UI.Xaml.h")) {
    if (!(Test-Path "$obj\include\winrt\Windows.UI.Xaml.h")) {
        & "$sdkRoot\bin\$($sdk.Name)\x64\cppwinrt.exe" -input sdk -output "$obj\include"
        if ($LASTEXITCODE) { throw 'C++/WinRT projection generation failed.' }
    }
    $env:INCLUDE += ";$obj\include"
}
& cl.exe /nologo /O2 /MT /c "/Fo$obj\" "$PSScriptRoot\Native\MinHook\src\buffer.c" "$PSScriptRoot\Native\MinHook\src\hook.c" "$PSScriptRoot\Native\MinHook\src\trampoline.c" "$PSScriptRoot\Native\MinHook\src\hde\hde64.c"
if ($LASTEXITCODE) { throw "MinHook compilation failed." }
$dll = Join-Path $OutputDirectory 'Mizon.Widgets.Native.dll'
& cl.exe /nologo /std:c++20 /EHsc /O2 /MT /LD /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX "$PSScriptRoot\NativeWidget.cpp" "/Fo$obj\NativeWidget.obj" "$obj\buffer.obj" "$obj\hook.obj" "$obj\trampoline.obj" "$obj\hde64.obj" /link "/DEF:$PSScriptRoot\NativeWidget.def" "/OUT:$dll" "/IMPLIB:$obj\NativeWidget.lib" windowsapp.lib user32.lib shell32.lib ole32.lib dbghelp.lib
if ($LASTEXITCODE) { throw 'Native Widgets module build failed.' }

Copy-Item "$sdkRoot\Debuggers\x64\dbghelp.dll","$sdkRoot\Debuggers\x64\symsrv.dll" -Destination $OutputDirectory -Force