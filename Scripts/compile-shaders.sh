#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SHADERCROSS_CLI="$SCRIPT_DIR/native/install/bin/shadercross"
SHADER_SRC_DIR="$SCRIPT_DIR/Shaders"
SHADER_OUT_DIR="$SCRIPT_DIR/Shaders/compiled"

# Check shadercross is available
if [ ! -f "$SHADERCROSS_CLI" ]; then
    echo "ERROR: shadercross CLI not found at $SHADERCROSS_CLI"
    echo "Please run 'dotnet build' first to build the native dependencies."
    exit 1
fi

# Create output directories
mkdir -p "$SHADER_OUT_DIR/spirv"
mkdir -p "$SHADER_OUT_DIR/msl"
mkdir -p "$SHADER_OUT_DIR/dxil"

compile_shader() {
    local file="$1"
    local stage="$2"
    local filename
    filename="$(basename "$file")"

    echo "Compiling $filename ($stage)..."

    "$SHADERCROSS_CLI" "$file" -o "$SHADER_OUT_DIR/spirv/$filename.spv" --source HLSL --dest SPIRV --stage "$stage"
    "$SHADERCROSS_CLI" "$file" -o "$SHADER_OUT_DIR/msl/$filename.msl"  --source HLSL --dest MSL  --stage "$stage"
    "$SHADERCROSS_CLI" "$file" -o "$SHADER_OUT_DIR/dxil/$filename.dxil" --source HLSL --dest DXIL --stage "$stage"
}

# Find and compile all shaders
while IFS= read -r -d '' file; do
    compile_shader "$file" "vertex"
done < <(find "$SHADER_SRC_DIR" -name "*.vert.hlsl" -print0)

while IFS= read -r -d '' file; do
    compile_shader "$file" "fragment"
done < <(find "$SHADER_SRC_DIR" -name "*.frag.hlsl" -print0)

while IFS= read -r -d '' file; do
    compile_shader "$file" "compute"
done < <(find "$SHADER_SRC_DIR" -name "*.comp.hlsl" -print0)

echo "Shader compilation complete."
