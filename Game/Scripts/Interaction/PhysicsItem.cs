using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class PhysicsItem : MonoBehaviour
{
    [System.Serializable]
    public class GrabInstance
    {
        public Transform CameraTransform;
        public Collider PlayerCollider;
        public DwarfGrabber Grabber;
        public float GrabDistance;
        public Vector3 LocalGrabPoint; // Exact local offset on the item that was grabbed
        public Quaternion InitialRelativeRot;
    }

    [Header("Physics & Mass")]
    [Tooltip("Weight in kg. Glass ~0.5kg, Empty Keg ~12kg, Full Keg ~50kg")]
    [SerializeField] protected float itemMass = 1.0f;
    [SerializeField] private bool allowMultiGrab = true;

    [Header("Per-Dwarf Pull Force")]
    [Tooltip("Max upward/pull force a single dwarf can exert (280N allows tipping/dragging 50kg, but not lifting off ground).")]
    [SerializeField] private float maxForcePerDwarf = 300f;
    [SerializeField] private float springStrength = 480f;
    [SerializeField] private float damper = 45f;
    [SerializeField] private float rotateSpeed = 6f;

    [Header("Grab Distance Range")]
    [SerializeField] private float minGrabDistance = 0.8f;
    [SerializeField] private float maxGrabDistance = 2.6f;
    [SerializeField] private float scrollSpeed = 2.0f;

    public Rigidbody Rb { get; private set; }
    public Collider Col { get; private set; }
    public float ItemMass => itemMass;
    public bool AllowMultiGrab => allowMultiGrab;
    public int GrabberCount => _activeGrabs.Count;
    public bool IsHeld => _activeGrabs.Count > 0;

    protected readonly List<GrabInstance> _activeGrabs = new List<GrabInstance>();

    protected virtual void Awake()
    {
        Rb = GetComponent<Rigidbody>();
        Col = GetComponent<Collider>();
        SetMass(itemMass);
    }

    public void SetMass(float newMass)
    {
        itemMass = Mathf.Max(0.1f, newMass);
        if (Rb != null)
        {
            Rb.mass = itemMass;
        }
    }

    public virtual bool OnGrab(Transform cameraTransform, Vector3 worldHitPoint, Collider playerCollider, DwarfGrabber grabber)
    {
        if (!allowMultiGrab && IsHeld) return false;
        if (_activeGrabs.Exists(g => g.Grabber == grabber)) return false;

        float hitDistance = Vector3.Distance(cameraTransform.position, worldHitPoint);

        GrabInstance grab = new GrabInstance
        {
            CameraTransform = cameraTransform,
            PlayerCollider = playerCollider,
            Grabber = grabber,
            GrabDistance = Mathf.Clamp(hitDistance, minGrabDistance, maxGrabDistance),
            // Convert world hit position into the item's local coordinate space
            LocalGrabPoint = transform.InverseTransformPoint(worldHitPoint),
            InitialRelativeRot = Quaternion.Inverse(cameraTransform.rotation) * transform.rotation
        };

        _activeGrabs.Add(grab);

        if (playerCollider != null && Col != null)
        {
            Physics.IgnoreCollision(Col, playerCollider, true);
        }

        Rb.useGravity = true;
        Rb.linearDamping = 0.8f;
        Rb.angularDamping = 1.2f;

        return true;
    }

    public virtual void OnRelease(Collider playerCollider, DwarfGrabber grabber)
    {
        GrabInstance grab = _activeGrabs.Find(g => g.Grabber == grabber);
        if (grab != null)
        {
            if (playerCollider != null && Col != null)
            {
                Physics.IgnoreCollision(Col, playerCollider, false);
            }
            _activeGrabs.Remove(grab);
        }

        if (_activeGrabs.Count == 0)
        {
            Rb.linearDamping = 0.05f;
            Rb.angularDamping = 0.05f;

            float maxReleaseSpeed = Mathf.Lerp(8f, 1.2f, Mathf.Clamp01(Rb.mass / 50f));
            Rb.linearVelocity = Vector3.ClampMagnitude(Rb.linearVelocity, maxReleaseSpeed);
        }
    }

    public void AdjustDistance(DwarfGrabber grabber, float scrollDelta)
    {
        GrabInstance grab = _activeGrabs.Find(g => g.Grabber == grabber);
        if (grab != null)
        {
            float input = Mathf.Clamp(scrollDelta, -1f, 1f);
            grab.GrabDistance = Mathf.Clamp(grab.GrabDistance + (input * scrollSpeed * Time.deltaTime * 10f), minGrabDistance, maxGrabDistance);
        }
    }

    protected virtual void FixedUpdate()
    {
        if (_activeGrabs.Count == 0) return;

        foreach (var grab in _activeGrabs)
        {
            if (grab.CameraTransform == null) continue;

            // 1. Where the grabbed handle point is in world space right now
            Vector3 worldGrabPoint = transform.TransformPoint(grab.LocalGrabPoint);

            // 2. Where the player's camera look ray wants that grab point to be
            Vector3 targetGrabPoint = grab.CameraTransform.position + (grab.CameraTransform.forward * grab.GrabDistance);
            Vector3 posError = targetGrabPoint - worldGrabPoint;

            // 3. Velocity at that specific point on the rigid body
            Vector3 pointVelocity = Rb.GetPointVelocity(worldGrabPoint);

            // 4. Spring-damper force calculated specifically for that grab point
            Vector3 pullForce = (posError * springStrength) - (pointVelocity * (damper / _activeGrabs.Count));
            pullForce = Vector3.ClampMagnitude(pullForce, maxForcePerDwarf);

            // Applying force at the contact position introduces natural torque (leverage)
            Rb.AddForceAtPosition(pullForce, worldGrabPoint, ForceMode.Force);
        }

        // Apply a gentle rotation stabilizer so lighter items (glasses) don't spin wildly
        if (_activeGrabs.Count == 1 && _activeGrabs[0].CameraTransform != null)
        {
            Quaternion targetRot = _activeGrabs[0].CameraTransform.rotation * _activeGrabs[0].InitialRelativeRot;
            Quaternion rotDiff = targetRot * Quaternion.Inverse(Rb.rotation);
            rotDiff.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);

            if (angleInDegrees > 180f) angleInDegrees -= 360f;

            if (Mathf.Abs(angleInDegrees) > 0.5f && !float.IsNaN(rotationAxis.x))
            {
                Vector3 targetAngVel = rotationAxis.normalized * (angleInDegrees * Mathf.Deg2Rad * rotateSpeed);
                Vector3 torque = (targetAngVel - Rb.angularVelocity) * (Rb.mass * 0.4f);
                Rb.AddTorque(Vector3.ClampMagnitude(torque, 15f), ForceMode.Force);
            }
        }
    }
}