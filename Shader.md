# Shader Reference

Shader:
- `EndlessOcean/URP/MirrorSurfaceSRP_RayPlane`

## Overview
- Computes a reflection ray and intersects it with the mirror plane.
- Projects the intersection point using `_MirrorVP` into reflection UV.
- Samples `_MirrorTex` and blends with URP PBR lighting.

## Reflection Blending
`_ReflectionBlend`:
- 0 = LERP
- 1 = SCREEN
- 2 = ADD
