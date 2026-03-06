$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ShadercrossCLI = "$ScriptDir\native\install\bin\shadercross.exe"
$ShaderSrcDir = "$ScriptDir\Shaders"
$ShaderOutDir = "$ScriptDir\Shaders\compiled"

if (-not (Test-Path $ShadercrossCLI)) {
    Write-Error "shadercross CLI not found at $ShadercrossCLI. Please run 'dotnet build' first to build the native dependencies."
    exit 1
}

New-Item -ItemType Directory -Force -Path "$ShaderOutDir\spirv" | Out-Null
New-Item -ItemType Directory -Force -Path "$ShaderOutDir\msl"   | Out-Null
New-Item -ItemType Directory -Force -Path "$ShaderOutDir\dxil"  | Out-Null

function Compile-Shader($file, $stage) {
    $filename = Split-Path -Leaf $file
    Write-Host "Compiling $filename ($stage)..."

    & $ShadercrossCLI $file -o "$ShaderOutDir\spirv\$filename.spv"  --source HLSL --dest SPIRV --stage $stage
    & $ShadercrossCLI $file -o "$ShaderOutDir\msl\$filename.msl"    --source HLSL --dest MSL   --stage $stage
    & $ShadercrossCLI $file -o "$ShaderOutDir\dxil\$filename.dxil"  --source HLSL --dest DXIL  --stage $stage
}

Get-ChildItem -Recurse -Filter "*.vert.hlsl" $ShaderSrcDir | ForEach-Object { Compile-Shader $_.FullName "vertex" }
Get-ChildItem -Recurse -Filter "*.frag.hlsl" $ShaderSrcDir | ForEach-Object { Compile-Shader $_.FullName "fragment" }
Get-ChildItem -Recurse -Filter "*.comp.hlsl" $ShaderSrcDir | ForEach-Object { Compile-Shader $_.FullName "compute" }

Write-Host "Shader compilation complete."
