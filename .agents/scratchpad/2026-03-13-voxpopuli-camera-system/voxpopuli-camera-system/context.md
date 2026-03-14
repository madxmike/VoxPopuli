# Context: VoxPopuli Camera System

## Project Structure
- `Program.cs` — SDL init, event loop, calls `game.Tick()`
- `Game/Game.cs` — `VoxGame`: owns mesh, instances, calls `renderer.DrawFrame()`
- `Renderer/Renderer.cs` — `SdlRenderer`: `IRenderer` impl, `BuildMVP`, `DrawFrame`
- `Renderer/SdlGpuDevice.cs` — SDL3 GPU resources

## Task Set
Implement a god/overview camera (RTS/city-builder style) across 4 steps:
- Step 1: `CameraInput` struct + `Program.cs` input collection
- Step 2: `GodCamera` class
- Step 3: Wire view matrix through `IRenderer` and `SdlRenderer`
- Step 4: Integration verification

## Key Design Decisions
- Camera state lives in game layer (`VoxGame`), not renderer
- `CameraInput` is a plain readonly struct — no SDL types cross the boundary
- `IRenderer.DrawFrame` gains a `Matrix4x4 view` parameter
- `BuildMVP` receives view matrix instead of hardcoding it
- Smoothing via exponential decay: `Lerp(smoothed, goal, 1f - MathF.Exp(-SmoothFactor * dt))`
- Pan is camera-relative (forward/right derived from `YawGoal`)

## Existing Documentation
- No CODEASSIST.md found
- Design doc: `.agents/planning/2026-03-13-voxpopuli-camera-system/design/detailed-design.md`
