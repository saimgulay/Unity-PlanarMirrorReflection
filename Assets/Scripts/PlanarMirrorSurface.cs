using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Optimised, self-contained planar mirror for URP using a secondary Camera.
/// Adds a plane orientation mode (Ground/Wall/Custom) without changing default behaviour.
/// Default remains Ground (transform.up), so existing scenes are unaffected.
///
/// Player-centric radius mask — when radius <= 0, behaviour is IDENTICAL to before.
/// When radius > 0, planar reflection is visible only within a disc around Player projected onto the plane.
///
/// Includes RT stability controls:
/// - lockRtSize: fix RT size to avoid frequent reallocations
/// - resizeHysteresis: only reallocate when size delta exceeds a ratio threshold
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class PlanarMirrorSurface : MonoBehaviour
{
    // --- plane mode ---
    public enum PlaneMode
    {
        Ground_UseUp,     // default - identical to previous behaviour (transform.up)
        Wall_UseForward,  // wall/vertical mirrors (transform.forward)
        CustomNormal      // supply a custom world-space normal
    }

    [Header("Plane Orientation")]
    [Tooltip("Ground = transform.up (default/unchanged). Wall = transform.forward. Custom = use Custom Normal WS.")]
    public PlaneMode planeMode = PlaneMode.Ground_UseUp;

    [Tooltip("Used only when PlaneMode = CustomNormal. Should be a normalized world-space direction.")]
    public Vector3 customNormalWS = Vector3.up;

    [Header("Quality")]
    [Range(0.25f, 2f)] public float resolutionScale = 0.75f;
    [Tooltip("MSAA samples for the reflection RenderTexture. 1 = off.")]
    [Range(1, 8)] public int msaa = 1;
    public bool allowHDR = true;

    [Header("Projection")]
    [Tooltip("Push the clip plane away from the mirror to avoid acne.")]
    public float clipPlaneOffset = 0.05f;
    [Tooltip("Match source camera's FOV/orthographic settings before applying oblique clip.")]
    public bool matchSourceProjection = true;

    [Header("Culling & Layers")]
    [Tooltip("Layers to render in the reflection.")]
    public LayerMask reflectionCullingMask = ~0;
    [Tooltip("Optional: disable shadows for the reflection to save cost.")]
    public bool disableShadowsInReflection = true;

    [Header("Performance")]
    [Tooltip("Render only if mirror is visible to the current camera.")]
    public bool requireVisibility = true;
    [Tooltip("Render only every N frames (1 = every frame).")]
    [Min(1)] public int updateEveryNFrames = 1;
    [Tooltip("Skip rendering when camera is further than this distance. 0 = disabled.")]
    [Min(0)] public float maxRenderDistance = 0f;

    [Header("Player Radius Mask")]
    [Tooltip("Player transform used as the centre of the reflection mask. If null, world origin is used.")]
    public Transform player;
    [Tooltip("When <= 0, mask is disabled and behaviour is identical. When > 0, reflection shows only within this radius around Player on the plane.")]
    [Min(0f)] public float radius = 0f;
    [Tooltip("Soft falloff width at the edge of the radius (world units along the plane).")]
    [Min(0f)] public float radiusFeather = 0.5f;

    [Header("RT Stability")]
    [Tooltip("If true, fixes RT size independent of source camera to avoid frequent reallocs.")]
    public bool lockRtSize = false;
    [Min(8)] public int lockedWidth = 1024;
    [Min(8)] public int lockedHeight = 1024;

    [Tooltip("If not locked, only reallocate RT when size delta exceeds this ratio.")]
    [Range(0.01f, 0.5f)] public float resizeHysteresis = 0.1f; // 10%

    [Header("Runtime (debug)")]
    [SerializeField, HideInInspector] Camera _reflectionCam;
    [SerializeField, HideInInspector] RenderTexture _rt;

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    private static bool s_IsRenderingReflection = false;
    private int _lastFrameRendered = -1;
    private Camera _lastSourceCam;
    private int _rtW = -1, _rtH = -1;

    void OnEnable()
    {
        _renderer = GetComponent<Renderer>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
#endif
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= OnEditorPlayModeChanged;
#endif
        ReleaseRT();
        DestroyReflectionCamera();
    }

#if UNITY_EDITOR
    void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingEditMode ||
            state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            ReleaseRT();
            DestroyReflectionCamera();
        }
    }
#endif

    void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (s_IsRenderingReflection) return;
        if (_reflectionCam != null && cam == _reflectionCam) return;
        if (_renderer == null || !_renderer.enabled) return;
        if (!isActiveAndEnabled) return;

        if (requireVisibility && !IsVisibleByCamera(cam)) return;

        if (maxRenderDistance > 0f)
        {
            float dist = Vector3.Distance(cam.transform.position, transform.position);
            if (dist > maxRenderDistance) return;
        }

        if (updateEveryNFrames > 1)
        {
            if (Time.renderedFrameCount % updateEveryNFrames != 0)
                return;
        }

        if (Time.renderedFrameCount == _lastFrameRendered && cam == _lastSourceCam)
            return;

        RenderOncePerFrame(ctx, cam);

        _lastFrameRendered = Time.renderedFrameCount;
        _lastSourceCam = cam;
    }

    void RenderOncePerFrame(ScriptableRenderContext ctx, Camera srcCam)
    {
        EnsureReflectionCamera(srcCam);
        EnsureRenderTexture(srcCam);

        // Plane definition
        Vector3 planePosWS = transform.position;
        Vector3 planeNormalWS = GetPlaneNormalWS();

        var planeWS4 = new Vector4(planeNormalWS.x, planeNormalWS.y, planeNormalWS.z, -Vector3.Dot(planeNormalWS, planePosWS));
        Matrix4x4 reflectionM = CalculateReflectionMatrix(planeWS4);

        CopyCommonCameraSettings(srcCam, _reflectionCam);

        _reflectionCam.worldToCameraMatrix = srcCam.worldToCameraMatrix * reflectionM;

        Vector3 srcPos = srcCam.transform.position;
        Vector3 reflPos = ReflectPointAcrossPlane(srcPos, planePosWS, planeNormalWS);
        _reflectionCam.transform.position = reflPos;
        _reflectionCam.transform.forward = ReflectVectorAcrossPlane(srcCam.transform.forward, planeNormalWS);
        _reflectionCam.transform.up = ReflectVectorAcrossPlane(srcCam.transform.up, planeNormalWS);

        if (matchSourceProjection)
        {
            if (srcCam.orthographic)
            {
                _reflectionCam.orthographic = true;
                _reflectionCam.orthographicSize = srcCam.orthographicSize;
            }
            else
            {
                _reflectionCam.orthographic = false;
                _reflectionCam.fieldOfView = srcCam.fieldOfView;
            }
        }

        _reflectionCam.targetTexture = _rt;

        Vector4 clipPlaneCameraSpace = CameraSpacePlane(_reflectionCam, planePosWS, planeNormalWS, 1.0f, clipPlaneOffset);
        _reflectionCam.projectionMatrix = srcCam.CalculateObliqueMatrix(clipPlaneCameraSpace);

        bool prevInvert = GL.invertCulling;
        GL.invertCulling = !prevInvert;

        try
        {
            s_IsRenderingReflection = true;
            UniversalRenderPipeline.RenderSingleCamera(ctx, _reflectionCam);
        }
        finally
        {
            s_IsRenderingReflection = false;
            GL.invertCulling = prevInvert;
        }

        PushMaterialProperties(planePosWS, planeNormalWS, _reflectionCam);
    }

    Vector3 GetPlaneNormalWS()
    {
        switch (planeMode)
        {
            case PlaneMode.Wall_UseForward:
                return transform.forward.sqrMagnitude > 1e-6f ? transform.forward.normalized : Vector3.forward;

            case PlaneMode.CustomNormal:
            {
                Vector3 n = customNormalWS;
                if (n.sqrMagnitude < 1e-6f) n = Vector3.up; // safe fallback
                return n.normalized;
            }

            case PlaneMode.Ground_UseUp:
            default:
                return transform.up.sqrMagnitude > 1e-6f ? transform.up.normalized : Vector3.up;
        }
    }

    bool IsVisibleByCamera(Camera cam)
    {
        if (_renderer == null) return false;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        return GeometryUtility.TestPlanesAABB(planes, _renderer.bounds);
    }

    void EnsureReflectionCamera(Camera src)
    {
        if (_reflectionCam != null) return;

        var go = new GameObject($"__ReflectionCam__{gameObject.GetInstanceID()}");
        go.hideFlags = HideFlags.HideAndDontSave;
        _reflectionCam = go.AddComponent<Camera>();
        _reflectionCam.enabled = false;
        _reflectionCam.gameObject.SetActive(true);

        CopyCommonCameraSettings(src, _reflectionCam);
        _reflectionCam.cullingMask = reflectionCullingMask;
        _reflectionCam.clearFlags = src.clearFlags;
        _reflectionCam.backgroundColor = src.backgroundColor;
        _reflectionCam.allowHDR = allowHDR;
        _reflectionCam.allowMSAA = (msaa > 1);

        var acd = _reflectionCam.GetUniversalAdditionalCameraData();
        if (acd != null)
        {
            acd.renderShadows = !disableShadowsInReflection;
            acd.renderPostProcessing = false;
            acd.requiresColorOption = CameraOverrideOption.Off;
            acd.requiresDepthOption = CameraOverrideOption.Off;
        }
    }

    void EnsureRenderTexture(Camera src)
    {
        int targetW, targetH;

        if (lockRtSize)
        {
            targetW = Mathf.Max(8, lockedWidth);
            targetH = Mathf.Max(8, lockedHeight);
        }
        else
        {
            int baseW = Mathf.Max(8, src.pixelWidth);
            int baseH = Mathf.Max(8, src.pixelHeight);
            targetW = Mathf.Max(8, Mathf.RoundToInt(baseW * Mathf.Clamp(resolutionScale, 0.25f, 2f)));
            targetH = Mathf.Max(8, Mathf.RoundToInt(baseH * Mathf.Clamp(resolutionScale, 0.25f, 2f)));
        }

        var fmt = allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        int aa = Mathf.Max(1, msaa);

        if (_rt != null)
        {
            bool fmtSame = (_rt.format == fmt) && (_rt.antiAliasing == aa);

            if (fmtSame)
            {
                if (lockRtSize)
                {
                    if (_rt.width == targetW && _rt.height == targetH) return;
                }
                else
                {
                    // Hysteresis: ignore small fluctuations to avoid churn
                    float dw = _rtW > 0 ? Mathf.Abs(targetW - _rtW) / (float)_rtW : 1f;
                    float dh = _rtH > 0 ? Mathf.Abs(targetH - _rtH) / (float)_rtH : 1f;
                    if (dw < resizeHysteresis && dh < resizeHysteresis) return;
                }
            }

            ReleaseRT();
        }

        _rt = new RenderTexture(targetW, targetH, 16, fmt)
        {
            antiAliasing = aa,
            useMipMap = false,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,
            name = $"MirrorRT_{targetW}x{targetH}"
        };
        _rt.Create();
        _rtW = targetW; _rtH = targetH;
    }

    void ReleaseRT()
    {
        if (_rt != null)
        {
            if (Application.isPlaying) Destroy(_rt);
            else DestroyImmediate(_rt);
            _rt = null;
            _rtW = -1; _rtH = -1;
        }
    }

    void DestroyReflectionCamera()
    {
        if (_reflectionCam != null)
        {
            if (Application.isPlaying) Destroy(_reflectionCam.gameObject);
            else DestroyImmediate(_reflectionCam.gameObject);
            _reflectionCam = null;
        }
    }

    void PushMaterialProperties(Vector3 planePosWS, Vector3 planeNormalWS, Camera reflCam)
    {
        if (_renderer == null) return;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetTexture("_MirrorTex", _rt);
        _mpb.SetVector("_PlanePosWS", new Vector4(planePosWS.x, planePosWS.y, planePosWS.z, 0));
        _mpb.SetVector("_PlaneNormalWS", new Vector4(planeNormalWS.x, planeNormalWS.y, planeNormalWS.z, 0));
        Matrix4x4 mirrorVP = reflCam.projectionMatrix * reflCam.worldToCameraMatrix;
        _mpb.SetMatrix("_MirrorVP", mirrorVP);

        // player radius mask params
        Vector3 playerPos = player ? player.position : Vector3.zero;
        _mpb.SetVector("_PlayerPosWS", new Vector4(playerPos.x, playerPos.y, playerPos.z, 0));
        _mpb.SetFloat("_Radius", Mathf.Max(0f, radius));
        _mpb.SetFloat("_RadiusFeather", Mathf.Max(0f, radiusFeather));

        _renderer.SetPropertyBlock(_mpb);
    }

    static void CopyCommonCameraSettings(Camera src, Camera dst)
    {
        dst.cameraType = CameraType.Game;
        dst.forceIntoRenderTexture = true;
        dst.useOcclusionCulling = src.useOcclusionCulling;
        dst.nearClipPlane = src.nearClipPlane;
        dst.farClipPlane = src.farClipPlane;
        dst.clearFlags = src.clearFlags;
        dst.backgroundColor = src.backgroundColor;

        var srcACD = src.GetUniversalAdditionalCameraData();
        var dstACD = dst.GetUniversalAdditionalCameraData();
        if (srcACD != null && dstACD != null)
        {
            dstACD.renderPostProcessing = srcACD.renderPostProcessing;
            dstACD.antialiasing = srcACD.antialiasing;
            dstACD.antialiasingQuality = srcACD.antialiasingQuality;
            dstACD.renderShadows = srcACD.renderShadows && !dst.orthographic;
        }
    }

    static Vector3 ReflectPointAcrossPlane(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        Vector3 v = point - planePoint;
        float dist = Vector3.Dot(v, planeNormal);
        return point - 2f * dist * planeNormal;
    }

    static Vector3 ReflectVectorAcrossPlane(Vector3 dir, Vector3 planeNormal)
    {
        return Vector3.Reflect(dir, planeNormal);
    }

    static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
    {
        Matrix4x4 m = Matrix4x4.identity;

        m.m00 = 1F - 2F * plane.x * plane.x;
        m.m01 = -2F * plane.x * plane.y;
        m.m02 = -2F * plane.x * plane.z;
        m.m03 = -2F * plane.w * plane.x;

        m.m10 = -2F * plane.y * plane.x;
        m.m11 = 1F - 2F * plane.y * plane.y;
        m.m12 = -2F * plane.y * plane.z;
        m.m13 = -2F * plane.w * plane.y;

        m.m20 = -2F * plane.z * plane.x;
        m.m21 = -2F * plane.z * plane.y;
        m.m22 = 1F - 2F * plane.z * plane.z;
        m.m23 = -2F * plane.w * plane.z;

        return m;
    }

    static Vector4 CameraSpacePlane(Camera cam, Vector3 planePointWS, Vector3 planeNormalWS, float sideSign, float clipPlaneOffset)
    {
        Vector3 offsetPos = planePointWS + planeNormalWS * clipPlaneOffset;
        Matrix4x4 worldToCamera = cam.worldToCameraMatrix;
        Vector3 cPos = worldToCamera.MultiplyPoint(offsetPos);
        Vector3 cNormal = worldToCamera.MultiplyVector(planeNormalWS).normalized * sideSign;
        return new Vector4(cNormal.x, cNormal.y, cNormal.z, -Vector3.Dot(cPos, cNormal));
    }
}

/// <summary>
/// Small helpers to get/add UniversalAdditionalCameraData safely.
/// </summary>
static class URPExt
{
    public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera cam)
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        cam.gameObject.TryGetComponent(out UniversalAdditionalCameraData acd);
        if (acd == null) acd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        return acd;
#else
        var acd = cam.gameObject.GetComponent<UniversalAdditionalCameraData>();
        if (acd == null) acd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        return acd;
#endif
    }
}
