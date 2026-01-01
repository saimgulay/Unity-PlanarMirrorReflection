# Installation

## Requirements
- Unity project using **Universal Render Pipeline (URP)**.
- The system renders reflections via URP callbacks and `UniversalRenderPipeline.RenderSingleCamera`.

## Option A — Copy into an existing URP project
1. Clone the repository or download as ZIP.
2. Copy these folders into your project:
   - `Assets/Scripts/`
   - `Assets/Shaders/`
   - `Assets/Materials/`
   - `Assets/Scenes/` (optional demo)
3. Ensure URP is configured (Pipeline Asset assigned in Project Settings).

## Option B — Use the demo project
Open the repository folder as a Unity project and run:
- `Assets/Scenes/SampleScene.unity`

## Recommended Layer Setup (Important)
To avoid recursive reflections:
1. Create a layer (e.g. `Mirror`).
2. Put the mirror surface object on that layer.
3. In `PlanarMirrorSurface.reflectionCullingMask`, **exclude** the `Mirror` layer.
