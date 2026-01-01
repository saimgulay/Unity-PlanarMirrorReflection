# Troubleshooting

## Reflection is black
- Ensure URP is active and the material uses the correct shader.
- Check `reflectionCullingMask` is not empty.

## Hall of mirrors / feedback
- Put the mirror object on its own layer and exclude it from `reflectionCullingMask`.

## Flickering / reallocations
- Enable `lockRtSize` or increase `resizeHysteresis`.

## Artifacts near the plane
- Adjust `clipPlaneOffset` (e.g. 0.02–0.1).
