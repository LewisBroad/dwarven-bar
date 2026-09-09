using UnityEngine;
using TMPro;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class BeerKeg : PhysicsItem
{
    [Header("Keg Contents")]
    [SerializeField] private string beerName = "Dwarven Stout";
    [SerializeField] private float maxPints = 40f;
    [SerializeField] private float currentPints = 40f;

    [Header("Dynamic Weights (kg)")]
    [Tooltip("Weight of empty metal shell (~12kg: easy solo carry).")]
    [SerializeField] private float emptyMass = 12.0f;
    [Tooltip("Weight when completely full (~50kg: heavy drag / 2-dwarf carry).")]
    [SerializeField] private float fullMass = 50.0f;

    [Header("Top Valve Spray Hazard")]
    [Tooltip("Particle system placed at the top bung hole of the keg")]
    [SerializeField] private ParticleSystem topValveSprayVfx;
    [SerializeField] private GameObject beerSpillPrefab;
    [SerializeField] private AudioSource sprayAudio;
    [SerializeField] private float sprayRagdollForce = 18f;
    [SerializeField] private float sprayHitCooldown = 1.0f; // Prevents re-triggering ragdoll every particle frame

    [Header("Audio")]
    [SerializeField] private AudioSource dragAudio;

    [Header("Debug Display (Optional)")]
    [SerializeField] private TextMeshPro debugText;

    public string BeerName => beerName;
    public float CurrentPints => currentPints;
    public float MaxPints => maxPints;
    public float FillRatio => Mathf.Clamp01(currentPints / maxPints);
    public bool IsEmpty => currentPints <= 0.01f;
    public bool IsDocked { get; set; }
    public BasementKegBay DockedBay { get; set; }
    public bool NeedsTriggerExitToDock { get; set; } = false;

    private bool _isVentingPressure;
    private float _ventTimer;
    private float _ventDrainRate;
    private float _lastHitTime;

    protected override void Awake()
    {
        base.Awake();
        UpdateDynamicMass();
    }

    public override bool OnGrab(Transform cameraTransform, Vector3 worldHitPoint, Collider playerCollider, DwarfGrabber grabber)
    {
        if (IsDocked && DockedBay != null && DockedBay.IsPumpActive)
        {
            return false;
        }

        if (IsDocked)
        {
            Rb.isKinematic = false;
        }

        return base.OnGrab(cameraTransform, worldHitPoint, playerCollider, grabber);
    }

    public void DrainBeer(float pintsToDrain)
    {
        if (currentPints <= 0f) return;

        currentPints = Mathf.Max(0f, currentPints - pintsToDrain);
        UpdateDynamicMass();
    }

    public void UpdateDynamicMass()
    {
        float targetMass = Mathf.Lerp(emptyMass, fullMass, FillRatio);
        SetMass(targetMass);
        UpdateDebugLabel();
    }

    private void UpdateDebugLabel()
    {
        if (debugText != null)
        {
            debugText.text = $"{beerName}\n{currentPints:F1} / {maxPints:F0} Pts\nWeight: {Rb.mass:F1} kg";
        }
    }

    public void TriggerPressureVent(float duration, float drainRate)
    {
        if (IsEmpty) return;

        _isVentingPressure = true;
        _ventTimer = duration;
        _ventDrainRate = drainRate;

        if (topValveSprayVfx != null)
        {
            topValveSprayVfx.gameObject.SetActive(true);
            topValveSprayVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            topValveSprayVfx.Play(true);
        }

        if (sprayAudio != null)
        {
            sprayAudio.Play();
        }

        if (beerSpillPrefab != null && Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2f))
        {
            Instantiate(beerSpillPrefab, hit.point + Vector3.up * 0.01f, Quaternion.identity);
        }
    }

    /// <summary>
/// Called by ParticleDamageRelay when beer stream particles hit any collider.
/// </summary>
public void HandleParticleCollision(GameObject other)
{
    if (!_isVentingPressure) return;
    if (Time.time < _lastHitTime + sprayHitCooldown) return;

    // Check if the hit object or its parents have a DwarfRagdoll
    DwarfRagdoll ragdoll = other.GetComponentInParent<DwarfRagdoll>();
    if (ragdoll != null && !ragdoll.IsRagdolled)
    {
        _lastHitTime = Time.time;

        Vector3 blastDirection = topValveSprayVfx != null 
            ? topValveSprayVfx.transform.forward 
            : (other.transform.position - transform.position).normalized;

        Vector3 forceVector = (blastDirection + Vector3.up * 0.35f).normalized * sprayRagdollForce;

        Debug.Log($"[BeerKeg] Beer jet hit {other.name}! Triggering ragdoll with force: {forceVector}");
        ragdoll.TriggerRagdoll(forceVector, 2.5f);
    }
}

    private void Update()
    {
        if (_isVentingPressure)
        {
            _ventTimer -= Time.deltaTime;
            DrainBeer(_ventDrainRate * Time.deltaTime);

            if (_ventTimer <= 0f || IsEmpty)
            {
                _isVentingPressure = false;
                if (topValveSprayVfx != null) topValveSprayVfx.Stop();
                if (sprayAudio != null) sprayAudio.Stop();
            }
        }

        if (dragAudio != null)
        {
            bool isScraping = IsHeld && Rb.linearVelocity.magnitude > 0.25f && Physics.Raycast(transform.position, Vector3.down, 0.45f);
            if (isScraping && !dragAudio.isPlaying) dragAudio.Play();
            else if (!isScraping && dragAudio.isPlaying) dragAudio.Stop();
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        if (!IsHeld && !IsDocked)
        {
            Vector3 v = Rb.linearVelocity;
            if (Mathf.Abs(v.y) > 5.0f)
            {
                v.y = Mathf.Clamp(v.y, -5.0f, 5.0f);
                Rb.linearVelocity = v;
            }

            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            if (horiz.magnitude > 6.0f)
            {
                horiz = horiz.normalized * 6.0f;
                Rb.linearVelocity = new Vector3(horiz.x, v.y, horiz.z);
            }
        }
    }
}