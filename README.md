# VoxPopuli

A voxel world renderer built with C# and SDL3. Renders procedurally generated terrain using a greedy meshing algorithm and an SDL GPU backend.

## Installing from GitHub Releases

Pre-built binaries for Linux (x64), macOS (arm64), and Windows (x64) are published on the [Releases page](../../releases).

1. Download the zip for your platform (e.g. `VoxPopuli-v1.0.0-macos.zip`)
2. Extract it
3. Run the `VoxPopuli` executable inside the `publish/` folder

A `nightly` release is also published automatically on every push to `main`.

## Building from Source

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [CMake](https://cmake.org/download/) on your `PATH`
- Git
- Platform build tools (see below)

**Linux** — install these packages before building:
```sh
sudo apt-get install -y ninja-build pkg-config libasound2-dev libpulse-dev \
  libx11-dev libxext-dev libxrandr-dev libxcursor-dev libxfixes-dev \
  libxi-dev libxss-dev libxtst-dev libxkbcommon-dev libdrm-dev libgbm-dev \
  libgl1-mesa-dev libgles2-mesa-dev libegl1-mesa-dev libdbus-1-dev \
  libibus-1.0-dev libudev-dev libvulkan-dev libwayland-dev libdecor-0-dev
```

### Build

```sh
dotnet build
```

The first build will clone and compile SDL3 and SDL_shadercross (including LLVM/DXC). **This can take 30+ minutes.** Subsequent builds are fast thanks to caching.

## Running

```sh
dotnet run
```

Or run the compiled binary directly from the output directory.

## Controls

| Input | Action |
|---|---|
| `W` / `S` | Pan forward / backward |
| `A` / `D` | Pan left / right |
| `Q` / `E` | Rotate left / right |
| Mouse wheel | Zoom in / out |
| Right mouse button + drag | Look around |
| `P` | Toggle wireframe |
| `F` | Trigger voxel edit |
