// File: FreeFlyCamera.cs
// Unity 2020+ (Legacy Input). New Input System kullanıyorsanız Player ▸ Active Input Handling = Both.
// -----------------------------------------------------------------------------
// Look modes:
// - Always: Her zaman mouse ile bak, cursor Play’de kilitli.
// - HoldToLook: Mouse1 basılıyken bak (önceki davranış).
// - ToggleLook: Mouse1’e basınca aç/kapat, cursor lock durumunu değiştirir.
// -----------------------------------------------------------------------------
// WASD hareket, Space/E yukarı, C/Q aşağı, Shift hızlı, Ctrl yavaş,
// Mouse tekerlek hız ölçekler.

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class FreeFlyCamera : MonoBehaviour
{
    public enum LookMode { Always, HoldToLook, ToggleLook }

    [Header("Look")]
    public LookMode lookMode = LookMode.Always;
    public float lookSensitivity = 1.2f;
    public float maxPitch = 89f;
    public bool invertY = false;
    public float lookDamping = 0f; // 0 = anlık

    [Header("Move")]
    public float moveSpeed = 6f;
    public float fastMultiplier = 3f;
    public float slowMultiplier = 0.25f;
    public float wheelScale = 1.15f;
    public Vector2 speedClamp = new Vector2(0.5f, 200f);
    public float moveDamping = 0.05f; // 0 = anlık

    [Header("Keys")]
    public KeyCode keyForward = KeyCode.W;
    public KeyCode keyBackward = KeyCode.S;
    public KeyCode keyLeft = KeyCode.A;
    public KeyCode keyRight = KeyCode.D;
    public KeyCode keyUp = KeyCode.Space; // ayrıca E
    public KeyCode keyDown = KeyCode.C;   // ayrıca Q
    public KeyCode keyFast = KeyCode.LeftShift;
    public KeyCode keySlow = KeyCode.LeftControl;
    public KeyCode lookButton = KeyCode.Mouse1; // Hold/Toggle için

    private float _yaw, _pitch;
    private Vector3 _moveVel;
    private Vector2 _lookVel;
    private bool _lookArmed; // Toggle için
    private bool _cursorLockedByMe;

    void OnEnable()
    {
        var e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = Mathf.DeltaAngle(0f, e.x);
        _lookArmed = (lookMode == LookMode.Always);
        if (Application.isPlaying && lookMode == LookMode.Always) LockCursor();
    }

    void OnDisable() => UnlockCursor();

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Hız tekerleği
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            moveSpeed *= Mathf.Pow(wheelScale, Mathf.Sign(wheel));
            moveSpeed = Mathf.Clamp(moveSpeed, speedClamp.x, speedClamp.y);
        }

        // Look mode state
        if (lookMode == LookMode.HoldToLook)
        {
            bool wantLook = Input.GetKey(lookButton);
            if (wantLook && !_cursorLockedByMe) LockCursor();
            if (!wantLook && _cursorLockedByMe) UnlockCursor();
            _lookArmed = wantLook;
        }
        else if (lookMode == LookMode.ToggleLook)
        {
            if (Input.GetKeyDown(lookButton))
            {
                _lookArmed = !_lookArmed;
                if (_lookArmed) LockCursor(); else UnlockCursor();
            }
        }
        else // Always
        {
            if (Application.isPlaying && !_cursorLockedByMe) LockCursor();
            _lookArmed = true;
        }

        // Mouse look
        if (_lookArmed)
        {
            float mx = Input.GetAxisRaw("Mouse X");
            float my = Input.GetAxisRaw("Mouse Y");

            if (lookDamping > 0f)
            {
                float a = 1f - Mathf.Exp(-dt / lookDamping);
                _lookVel.x = Mathf.Lerp(_lookVel.x, mx, a);
                _lookVel.y = Mathf.Lerp(_lookVel.y, my, a);
                mx = _lookVel.x; my = _lookVel.y;
            }

            float inv = invertY ? 1f : -1f;
            _yaw   += mx * lookSensitivity;
            _pitch += my * lookSensitivity * inv;
            _pitch = Mathf.Clamp(_pitch, -maxPitch, maxPitch);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
        else
        {
            _lookVel = Vector2.zero;
        }

        // Hareket
        Vector3 wish = Vector3.zero;
        if (Input.GetKey(keyForward))  wish += Vector3.forward;
        if (Input.GetKey(keyBackward)) wish += Vector3.back;
        if (Input.GetKey(keyRight))    wish += Vector3.right;
        if (Input.GetKey(keyLeft))     wish += Vector3.left;
        if (Input.GetKey(keyUp)   || Input.GetKey(KeyCode.E)) wish += Vector3.up;
        if (Input.GetKey(keyDown) || Input.GetKey(KeyCode.Q)) wish += Vector3.down;

        if (wish.sqrMagnitude > 1e-6f) wish.Normalize();

        float mult = 1f;
        if (Input.GetKey(keyFast)) mult *= fastMultiplier;
        if (Input.GetKey(keySlow)) mult *= slowMultiplier;

        Vector3 targetVel = transform.TransformDirection(wish) * (moveSpeed * mult);
        _moveVel = (moveDamping > 0f)
            ? Vector3.Lerp(_moveVel, targetVel, 1f - Mathf.Exp(-dt / moveDamping))
            : targetVel;

        transform.position += _moveVel * dt;

        // Güvenlik
        if (!IsFinite(transform.position))
        {
            Debug.LogWarning("[FreeFlyCamera] Non-finite position — resetting.");
            transform.position = Vector3.zero;
            _moveVel = Vector3.zero;
        }
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        _cursorLockedByMe = true;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _cursorLockedByMe = false;
    }

    private static bool IsFinite(Vector3 v)
        => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    [ContextMenu("Reset Orientation")]
    private void ResetOrientation()
    {
        _yaw = 0f; _pitch = 0f;
        transform.rotation = Quaternion.identity;
    }
}
