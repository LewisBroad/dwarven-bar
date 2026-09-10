using System.Collections;
using UnityEngine;

public enum RagdollCameraMode
{
    FirstPerson,
    ThirdPerson
}

public class DwarfRagdoll : MonoBehaviour
{
    [Header("Camera Mode (Settings Hook)")]
    [Tooltip("Switch between first-person tumble and third-person spectator view during ragdoll.")]
    [SerializeField] private RagdollCameraMode cameraMode = RagdollCameraMode.ThirdPerson;

    [Header("Bone & Physics References")]
    [Tooltip("The root bone of the character's armature (typically Hips / Pelvis).")]
    [SerializeField] private Transform hipsBone;
    [Tooltip("The head bone of the armature.")]
    [SerializeField] private Transform headBone;
    [SerializeField] private Animator animator;

    [Header("Third-Person Orbit Settings")]
    [SerializeField] private float thirdPersonDistance = 2.4f;
    [SerializeField] private float thirdPersonHeight = 0.8f;
    [SerializeField] private float cameraSmoothSpeed = 10f;
    [SerializeField] private LayerMask obstacleLayers; // Environment/Default layers to prevent wall clipping

    [Header("Recovery Settings")]
    [SerializeField] private float minRagdollDuration = 2.0f;
    [SerializeField] private float getUpVelocityThreshold = 0.2f;

    [Header("Camera & Input Link")]
    [SerializeField] private Transform cameraHolder;

    public bool IsRagdolled { get; private set; } = false;
    public RagdollCameraMode CameraMode
    {
        get => cameraMode;
        set => cameraMode = value;
    }

    private CharacterController _characterController;
    private DwarfController _dwarfController;
    private DwarfGrabber _dwarfGrabber;
    private DwarfBodyVisibility _bodyVisibility;

    private Rigidbody[] _ragdollRigidbodies;
    private Collider[] _ragdollColliders;
    private Vector3 _originalCamLocalPos;
    private Quaternion _originalCamLocalRot;
    private Coroutine _ragdollRoutine;
    private Vector3 _thirdPersonVelocity;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _dwarfController = GetComponent<DwarfController>();
        _dwarfGrabber = GetComponentInChildren<DwarfGrabber>();
        _bodyVisibility = GetComponent<DwarfBodyVisibility>();

        if (animator == null) animator = GetComponentInChildren<Animator>();

        _ragdollRigidbodies = hipsBone != null ? hipsBone.GetComponentsInChildren<Rigidbody>() : GetComponentsInChildren<Rigidbody>();
        _ragdollColliders = hipsBone != null ? hipsBone.GetComponentsInChildren<Collider>() : GetComponentsInChildren<Collider>();

        if (cameraHolder != null)
        {
            _originalCamLocalPos = cameraHolder.localPosition;
            _originalCamLocalRot = cameraHolder.localRotation;
        }

        SetRagdollState(false);
    }

    private void LateUpdate()
    {
        if (!IsRagdolled || cameraHolder == null) return;

        Transform trackingBone = hipsBone != null ? hipsBone : transform;

        if (cameraMode == RagdollCameraMode.FirstPerson)
        {
            // 1. FIRST-PERSON: Mount to head, tumble with the skull
            Transform anchor = headBone != null ? headBone : hipsBone;
            if (anchor != null)
            {
                cameraHolder.position = anchor.position;
                cameraHolder.rotation = Quaternion.Slerp(cameraHolder.rotation, anchor.rotation, Time.deltaTime * 15f);
            }
        }
        else
        {
            // 2. THIRD-PERSON: Orbit behind the tumbling dwarf with anti-clipping
            Vector3 targetCenter = trackingBone.position + Vector3.up * thirdPersonHeight;
            Vector3 backDir = -trackingBone.forward;
            backDir.y = 0.2f;
            backDir.Normalize();

            Vector3 desiredPos = targetCenter + (backDir * thirdPersonDistance);

            // Sphere cast to prevent camera pushing through tavern walls/floor
            if (Physics.SphereCast(targetCenter, 0.25f, (desiredPos - targetCenter).normalized, out RaycastHit hit, thirdPersonDistance, obstacleLayers))
            {
                desiredPos = hit.point + hit.normal * 0.15f;
            }

            cameraHolder.position = Vector3.SmoothDamp(cameraHolder.position, desiredPos, ref _thirdPersonVelocity, 1f / cameraSmoothSpeed);
            cameraHolder.LookAt(targetCenter);
        }
    }

    public void TriggerRagdoll(Vector3 impactForce, float duration = 2.0f)
    {
        if (IsRagdolled) return;

        // A ragdoll changes the camera holder and disables the controller, so the
        // book must always restore its normal camera state before the tumble starts.
        MagicOrderBookUI.ForceClose(immediate: true);

        if (_ragdollRoutine != null) StopCoroutine(_ragdollRoutine);
        _ragdollRoutine = StartCoroutine(RagdollRoutine(impactForce, duration));
    }

    private IEnumerator RagdollRoutine(Vector3 force, float duration)
    {
        IsRagdolled = true;

        if (_dwarfGrabber != null)
        {
            _dwarfGrabber.ForceDrop();
        }

        SetRagdollState(true);

        foreach (var rb in _ragdollRigidbodies)
        {
            rb.linearVelocity = force * 0.4f;
            rb.AddForce(force + (Vector3.up * 2f), ForceMode.Impulse);
        }

        yield return new WaitForSeconds(Mathf.Max(minRagdollDuration, duration));

        Rigidbody hipsRb = hipsBone != null ? hipsBone.GetComponent<Rigidbody>() : null;
        if (hipsRb != null)
        {
            while (hipsRb.linearVelocity.magnitude > getUpVelocityThreshold)
            {
                yield return new WaitForSeconds(0.2f);
            }
        }

        RecoverFromRagdoll();
    }

    private void RecoverFromRagdoll()
    {
        if (hipsBone != null)
        {
            Vector3 hipsPos = hipsBone.position;
            if (Physics.Raycast(hipsPos + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 2.5f))
            {
                transform.position = hit.point + Vector3.up * 0.05f;
            }
            else
            {
                transform.position = hipsPos;
            }
        }

        SetRagdollState(false);

        // Snap camera back to the original head/eyes view
        if (cameraHolder != null)
        {
            cameraHolder.localPosition = _originalCamLocalPos;
            cameraHolder.localRotation = _originalCamLocalRot;
        }

        IsRagdolled = false;
        _ragdollRoutine = null;
    }

    private void SetRagdollState(bool active)
    {
        if (_characterController != null) _characterController.enabled = !active;
        if (_dwarfController != null) _dwarfController.enabled = !active;
        if (animator != null) animator.enabled = !active;

        foreach (var rb in _ragdollRigidbodies)
        {
            if (rb.gameObject == gameObject) continue;
            rb.isKinematic = !active;
            rb.useGravity = active;
            rb.collisionDetectionMode = active ? CollisionDetectionMode.Continuous : CollisionDetectionMode.Discrete;
        }

        foreach (var col in _ragdollColliders)
        {
            if (col.gameObject == gameObject) continue;
            col.enabled = active;
        }

        // MESH VISIBILITY (Safe: only touches layers, never disables the camera)
        if (_bodyVisibility != null && _bodyVisibility.IsLocalPlayer)
        {
            if (active)
            {
                // In 3rd person ragdoll: show the body so you see the dwarf wipe out
                // In 1st person ragdoll: keep it hidden so eyes don't clip through the head
                bool showBody = (cameraMode == RagdollCameraMode.ThirdPerson);
                _bodyVisibility.SetMeshVisible(showBody);
            }
            else
            {
                // Recovered: hide body again for regular 1st person gameplay
                _bodyVisibility.SetMeshVisible(false);
            }
        }
    }
}
