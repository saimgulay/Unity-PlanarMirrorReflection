# Performance Guide

Planar reflections cost an extra camera render per mirror.

## Recommended Optimisations
- Enable `requireVisibility`
- Increase `updateEveryNFrames` (2–3 is often acceptable)
- Use `maxRenderDistance`
- Disable reflection shadows via `disableShadowsInReflection`
- Lower `resolutionScale` on constrained platforms

## RenderTexture Stability
If you see realloc churn:
- Enable `lockRtSize` (e.g. 1024x1024)
- Or increase `resizeHysteresis`
