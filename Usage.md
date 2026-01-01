# Usage

## Quick Start
1. Create a plane/mesh for your mirror/water surface (must have a **Renderer**).
2. Apply a material using shader:
   - `EndlessOcean/URP/MirrorSurfaceSRP_RayPlane`
3. Add the component:
   - `PlanarMirrorSurface`
4. Configure:
   - Set `reflectionCullingMask` (exclude mirror layer)
   - Tune `resolutionScale`, `msaa`, and performance gates.

## Plane Orientation
- **Ground_UseUp** (default): uses `transform.up`
- **Wall_UseForward**: uses `transform.forward`
- **CustomNormal**: uses `customNormalWS` (world space)

## Player Radius Mask (Optional)
To show reflections only around the player (disc on the plane):
- Assign `player`
- Set `radius > 0`
- Optionally set `radiusFeather` for a soft edge

When `radius <= 0`, behaviour matches the default (unmasked).

## Common Presets
### High quality mirror (PC)
- `resolutionScale`: 1.0
- `msaa`: 2 or 4
- `updateEveryNFrames`: 1

### Mobile-friendly water reflection
- `resolutionScale`: 0.5–0.75
- `msaa`: 1
- `updateEveryNFrames`: 2–3
- `requireVisibility`: true
- `disableShadowsInReflection`: true
- Consider `maxRenderDistance`
