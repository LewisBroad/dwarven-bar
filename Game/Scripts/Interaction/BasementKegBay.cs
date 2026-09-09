using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BasementKegBay : MonoBehaviour
{
    [Header("Beer Line Settings")]
    [SerializeField] private string assignedBeerType = "Dwarven Stout";
    [SerializeField] private Transform snapTransform;

    [Header("Leave Area & Safety")]
    [Tooltip("Leave Area trigger volume that the keg must completely leave before it can re-dock.")]
    [SerializeField] private Collider leaveAreaCollider;
    [Tooltip("Distance threshold to pop the latch loose when pulling.")]
    [SerializeField] private float pullBreakawayThreshold = 0.12f;
    [Tooltip("Failsafe: If the keg travels this far from the snap center, automatically clear the lockout even if OnTriggerExit missed.")]
    [SerializeField] private float autoClearDistanceFailsafe = 1.6f;

    [Header("Hazard Configuration")]
    [SerializeField] private float ventDuration = 4.0f;
    [SerializeField] private float ventDrainRate = 8.0f;
    [SerializeField] private float knockbackForce = 16f;

    [Header("Dock Visuals / Clamps")]
    [SerializeField] private Transform clampHinge;
    [SerializeField] private Vector3 clampLockedRot = new Vector3(0, 0, 0);
    [SerializeField] private Vector3 clampOpenRot = new Vector3(-80, 0, 0);

    public BeerKeg DockedKeg { get; private set; }
    public bool IsPumpActive { get; private set; } = false;
    public string AssignedBeerType => assignedBeerType;

    private bool _hasBeenPressurized = false;
    private BeerKeg _pendingExitKeg = null;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
        UpdateClampVisual(false);
    }

    public void SetPumpState(bool active)
    {
        IsPumpActive = active;

        if (IsPumpActive && DockedKeg != null)
        {
            _hasBeenPressurized = true;
        }
    }

    private void FixedUpdate()
    {
        CheckPhysicalUndockTug();
        CheckDistanceFailsafe();
    }

    /// <summary>
    /// Failsafe: Guarantees the keg is re-armed if dragged outside, even if Unity misses an OnTriggerExit event.
    /// </summary>
    private void CheckDistanceFailsafe()
    {
        if (_pendingExitKeg == null) return;

        Vector3 dockPos = snapTransform != null ? snapTransform.position : transform.position;
        float dist = Vector3.Distance(_pendingExitKeg.transform.position, dockPos);

        if (dist >= autoClearDistanceFailsafe)
        {
            Debug.Log($"[KegBay] Failsafe: Keg cleared {dist:F2}m. Re-arming dock!");
            _pendingExitKeg.NeedsTriggerExitToDock = false;
            _pendingExitKeg = null;
        }
    }

    public void NotifyKegLeftLeaveArea(BeerKeg keg)
    {
        if (keg == _pendingExitKeg || (keg != null && keg.NeedsTriggerExitToDock))
        {
            Debug.Log("[KegBay] Keg left Leave Area trigger. Re-armed!");
            keg.NeedsTriggerExitToDock = false;
            _pendingExitKeg = null;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAutoDock(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryAutoDock(other);
    }

    private void OnTriggerExit(Collider other)
    {
        BeerKeg keg = other.GetComponentInParent<BeerKeg>();
        if (keg != null && (keg == _pendingExitKeg || keg.NeedsTriggerExitToDock))
        {
            // If leaveAreaCollider isn't assigned, use the main bay's trigger exit
            if (leaveAreaCollider == null)
            {
                keg.NeedsTriggerExitToDock = false;
                _pendingExitKeg = null;
            }
        }
    }

    private void TryAutoDock(Collider other)
    {
        // 1. Bay occupied or pump running
        if (DockedKeg != null || IsPumpActive) return;

        BeerKeg keg = other.GetComponentInParent<BeerKeg>();
        if (keg == null) return;

        // 2. Reject empty kegs
        if (keg.IsEmpty)
        {
            return;
        }

        // 3. Reject if pending exit
        if (keg.IsDocked || keg.NeedsTriggerExitToDock || keg == _pendingExitKeg)
        {
            return;
        }

        // 4. Match type
        if (keg.BeerName == assignedBeerType)
        {
            DockKeg(keg);
        }
    }

    private void DockKeg(BeerKeg keg)
    {
        DockedKeg = keg;
        keg.IsDocked = true;
        keg.DockedBay = this;
        keg.NeedsTriggerExitToDock = false;
        _hasBeenPressurized = false;
        _pendingExitKeg = null;

        // Release player grabs cleanly
        var grabbers = FindObjectsByType<DwarfGrabber>(FindObjectsSortMode.None);
        foreach (var grabber in grabbers)
        {
            keg.OnRelease(null, grabber);
        }

        // Freeze physics & lock upright
        keg.Rb.linearVelocity = Vector3.zero;
        keg.Rb.angularVelocity = Vector3.zero;
        keg.Rb.isKinematic = true;

        Vector3 targetPosition = snapTransform != null ? snapTransform.position : transform.position;
        Quaternion targetRotation = snapTransform != null 
            ? Quaternion.Euler(0f, snapTransform.eulerAngles.y, 0f) 
            : Quaternion.identity;

        keg.transform.position = targetPosition;
        keg.transform.rotation = targetRotation;

        UpdateClampVisual(true);
        Debug.Log("[KegBay] Successfully docked!");
    }

    private void CheckPhysicalUndockTug()
    {
        if (DockedKeg == null) return;

        // Pump is ON: clamps keep it pinned
        if (IsPumpActive)
        {
            DockedKeg.transform.position = snapTransform != null ? snapTransform.position : transform.position;
            return;
        }

        // Pump is OFF: check for pull
        if (DockedKeg.IsHeld)
        {
            Vector3 dockPos = snapTransform != null ? snapTransform.position : transform.position;
            float pullDist = Vector3.Distance(DockedKeg.transform.position, dockPos);

            if (pullDist >= pullBreakawayThreshold)
            {
                DetachDockedKeg();
            }
        }
    }

    private void DetachDockedKeg()
    {
        if (DockedKeg == null) return;

        BeerKeg ejected = DockedKeg;
        DockedKeg = null;

        ejected.IsDocked = false;
        ejected.DockedBay = null;
        ejected.NeedsTriggerExitToDock = true;
        _pendingExitKeg = ejected;

        UpdateClampVisual(false);

        // Turn physics dynamic so the keg drops or moves naturally
        ejected.Rb.isKinematic = false;

        // If pressurized, trigger the spray on the keg and release hand grip
        if (_hasBeenPressurized && !ejected.IsEmpty)
        {
            // Drop player hands so they are free to get hit by the stream
            var grabbers = FindObjectsByType<DwarfGrabber>(FindObjectsSortMode.None);
            foreach (var grabber in grabbers)
            {
                grabber.ForceDrop();
            }

            // Start the vent — the particle collisions handle the ragdoll and force
            ejected.TriggerPressureVent(ventDuration, ventDrainRate);
        }

        _hasBeenPressurized = false;
    }

    private void ResetDwarfSpeed()
    {
        var dwarfs = FindObjectsByType<DwarfController>(FindObjectsSortMode.None);
        foreach (var dwarf in dwarfs)
        {
            dwarf.SpeedMultiplier = 1.0f;
        }
    }

    private void UpdateClampVisual(bool locked)
    {
        if (clampHinge != null)
        {
            clampHinge.localRotation = Quaternion.Euler(locked ? clampLockedRot : clampOpenRot);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 pos = snapTransform != null ? snapTransform.position : transform.position;
        Gizmos.DrawWireSphere(pos, autoClearDistanceFailsafe);
    }
}