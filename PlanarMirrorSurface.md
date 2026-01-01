# PlanarMirrorSurface Component

`PlanarMirrorSurface` renders planar reflections in URP using a hidden secondary camera and a RenderTexture.

## Key Features
- Visibility gating (`requireVisibility`)
- Frame skipping (`updateEveryNFrames`)
- Distance culling (`maxRenderDistance`)
- RenderTexture stability (`lockRtSize`, `resizeHysteresis`)
- Optional player-centric radius mask (`player`, `radius`, `radiusFeather`)
- Plane orientation modes (ground/wall/custom normal)

## Script → Shader Data (MaterialPropertyBlock)
The script pushes these properties per-renderer:
- `_MirrorTex`
- `_PlanePosWS`
- `_PlaneNormalWS`
- `_MirrorVP`
- `_PlayerPosWS`, `_Radius`, `_RadiusFeather` (mask)
