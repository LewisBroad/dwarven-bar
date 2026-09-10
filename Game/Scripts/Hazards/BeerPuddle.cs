using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CapsuleCollider))]
public class BeerPuddle : MonoBehaviour
{
    [Header("Puddle Dimensions")]
    [SerializeField] private float currentVolumePints = 1.0f;
    [SerializeField] private float maxVolumePints = 5.0f;
    [SerializeField] private float minRadius = 0.5f;
    [SerializeField] private float maxRadius = 1.8f;
    [SerializeField] private Transform visualTransform;

    [Header("Collider Tuning")]
    [Tooltip("Fixed vertical height of the slip detection zone (keeps it flat on the floor)")]
    [SerializeField] private float triggerHeight = 0.12f; 
    [Tooltip("Adjusts physical trigger radius to match the visible beer shader edge (e.g., 0.85 = trigger is 85% of visual mesh)")]
    [Range(0.5f, 1.2f)]
    [SerializeField] private float visualToColliderRatio = 0.9f;

    [Header("Merge & Growth Animation")]
    [SerializeField] private float expansionDampTime = 0.2f;
    [SerializeField] private float absorbDuration = 0.3f;

    [Header("Slip Rules")]
    [SerializeField] private float baseSlipSpeedThreshold = 3.2f;
    [SerializeField] private float slipImpulseForce = 8.0f;
    [SerializeField] private float ragdollDuration = 1.6f;
    [SerializeField] private float perPlayerSlipCooldown = 2.5f;
    [Tooltip("Customers commonly walk more slowly than players, so this is intentionally lower.")]
    [SerializeField] private float customerSlipSpeedThreshold = 0.8f;

    [Header("Audio")]
    [SerializeField] private AudioSource slipAudio;

    private readonly Dictionary<int, float> _playerCooldowns = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _customerCooldowns = new Dictionary<int, float>();
    private readonly HashSet<CustomerOrder> _slipImmuneCustomers = new HashSet<CustomerOrder>();
    private CapsuleCollider _triggerCollider;

    private float _targetRadius;
    private float _currentVisualRadius;
    private float _radiusVelocity;
    private Vector3 _targetPosition;
    private Vector3 _positionVelocity;
    private bool _isBeingAbsorbed;

    public float CurrentVolume => currentVolumePints;
    public bool IsBeingAbsorbed => _isBeingAbsorbed;

    /// <summary>Customers that created/contributed to this puddle do not slip on it.</summary>
    public void AddSlipImmuneCustomer(CustomerOrder customer)
    {
        if (customer != null) _slipImmuneCustomers.Add(customer);
    }

    private void Awake()
    {
        _triggerCollider = GetComponent<CapsuleCollider>();
        if (_triggerCollider != null)
        {
            _triggerCollider.isTrigger = true;
            _triggerCollider.direction = 1; // 1 = Y-axis (vertical)
            _triggerCollider.height = triggerHeight;
            // Center the capsule so its bottom is on the floor (Y=0) and it reaches up to triggerHeight
            _triggerCollider.center = new Vector3(0f, triggerHeight * 0.5f, 0f);
        }

        _targetPosition = transform.position;
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        if (visualTransform != null)
        {
            Renderer r = visualTransform.GetComponent<Renderer>();
            if (r != null)
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetFloat("_Seed", Random.Range(0f, 100f));
                r.SetPropertyBlock(block);
            }
        }

        CalculateTargetRadius();
        _currentVisualRadius = _targetRadius;
        ApplyScaleToVisual(_currentVisualRadius);
    }

    private void Update()
    {
        if (_isBeingAbsorbed) return;

        if (Mathf.Abs(_currentVisualRadius - _targetRadius) > 0.005f)
        {
            _currentVisualRadius = Mathf.SmoothDamp(_currentVisualRadius, _targetRadius, ref _radiusVelocity, expansionDampTime);
            ApplyScaleToVisual(_currentVisualRadius);
        }

        if ((transform.position - _targetPosition).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.SmoothDamp(transform.position, _targetPosition, ref _positionVelocity, expansionDampTime);
        }

        // NavMeshAgents often have no Rigidbody, so Unity trigger callbacks are not
        // guaranteed for them. Actively sample the shallow puddle area instead.
        CheckCustomersStandingInPuddle();
    }

    public void AddBeer(float pints)
    {
        if (_isBeingAbsorbed) return;

        currentVolumePints = Mathf.Min(maxVolumePints, currentVolumePints + pints);
        CalculateTargetRadius();
        CheckForNearbyPuddlesToAbsorb();
    }

    public void CleanPuddle(float pintsCleaned)
    {
        if (_isBeingAbsorbed) return;

        currentVolumePints -= pintsCleaned;

        if (currentVolumePints <= 0.02f)
        {
            Destroy(gameObject);
        }
        else
        {
            CalculateTargetRadius();
        }
    }

    private void CalculateTargetRadius()
    {
        float t = Mathf.Clamp01(currentVolumePints / maxVolumePints);
        _targetRadius = Mathf.Lerp(minRadius, maxRadius, t);

        if (_triggerCollider != null)
        {
            // Lock the height to triggerHeight, only expand horizontally
            _triggerCollider.radius = _targetRadius * visualToColliderRatio;
            _triggerCollider.height = triggerHeight;
            _triggerCollider.center = new Vector3(0f, triggerHeight * 0.5f, 0f);
        }
    }

    private void ApplyScaleToVisual(float radius)
    {
        if (visualTransform != null)
        {
            float diameter = radius * 2f;
            // Assumes visualTransform is a horizontal disc/quad: (X: width, Y: length, Z: 1) or (X, Z, Y) depending on mesh orientation
            visualTransform.localScale = new Vector3(diameter, diameter, 1f);
        }
    }

    private void CheckForNearbyPuddlesToAbsorb()
    {
        if (_isBeingAbsorbed) return;

        // Use OverlapSphere or OverlapCapsule for puddle-to-puddle merging
        Vector3 pointBottom = transform.position;
        Vector3 pointTop = transform.position + (Vector3.up * triggerHeight);
        Collider[] overlaps = Physics.OverlapCapsule(pointBottom, pointTop, _targetRadius * 0.85f);

        foreach (var col in overlaps)
        {
            if (col.gameObject == gameObject) continue;

            BeerPuddle otherPuddle = col.GetComponent<BeerPuddle>();
            if (otherPuddle != null && !otherPuddle.IsBeingAbsorbed)
            {
                bool weAreDominant = currentVolumePints > otherPuddle.currentVolumePints ||
                                    (Mathf.Approximately(currentVolumePints, otherPuddle.currentVolumePints) && GetInstanceID() < otherPuddle.GetInstanceID());

                if (weAreDominant)
                {
                    AbsorbPuddle(otherPuddle);
                }
                break;
            }
        }
    }

    private void AbsorbPuddle(BeerPuddle other)
    {
        foreach (CustomerOrder customer in other._slipImmuneCustomers)
            if (customer != null) _slipImmuneCustomers.Add(customer);

        float totalVol = currentVolumePints + other.currentVolumePints;
        float weightOther = other.currentVolumePints / totalVol;
        _targetPosition = Vector3.Lerp(transform.position, other.transform.position, weightOther * 0.6f);

        currentVolumePints = Mathf.Min(maxVolumePints, totalVol);
        CalculateTargetRadius();

        other.StartBeingAbsorbedInto(this, absorbDuration);
    }

    public void StartBeingAbsorbedInto(BeerPuddle host, float duration)
    {
        _isBeingAbsorbed = true;

        if (_triggerCollider != null)
        {
            _triggerCollider.enabled = false;
        }

        StartCoroutine(AbsorbSlideRoutine(host, duration));
    }

    private IEnumerator AbsorbSlideRoutine(BeerPuddle host, float duration)
    {
        Vector3 startPos = transform.position;
        float startRadius = _currentVisualRadius > 0.01f ? _currentVisualRadius : minRadius;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float pullCurve = t * t;

            if (host != null)
            {
                transform.position = Vector3.Lerp(startPos, host.transform.position, pullCurve);
            }

            float currentR = Mathf.Lerp(startRadius, 0f, pullCurve);
            ApplyScaleToVisual(currentR);

            yield return null;
        }

        Destroy(gameObject);
    }

    private void OnTriggerStay(Collider other)
    {
        if (_isBeingAbsorbed) return;

        DwarfController dwarf = other.GetComponentInParent<DwarfController>();
        if (dwarf == null)
        {
            TrySlipCustomer(other);
            return;
        }

        int dwarfId = dwarf.gameObject.GetInstanceID();

        if (_playerCooldowns.TryGetValue(dwarfId, out float lastSlipTime))
        {
            if (Time.time < lastSlipTime + perPlayerSlipCooldown)
            {
                return;
            }
        }

        DwarfPerks perks = dwarf.GetComponent<DwarfPerks>();
        if (perks != null && perks.IsSlipImmune)
        {
            return;
        }

        float dwarfSpeed = dwarf.CurrentHorizontalSpeed;
        if (dwarfSpeed <= 0.01f)
        {
            CharacterController cc = dwarf.GetComponent<CharacterController>();
            if (cc != null)
            {
                dwarfSpeed = new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude;
            }
        }

        float effectiveThreshold = baseSlipSpeedThreshold;
        if (perks != null)
        {
            effectiveThreshold *= perks.SlipSpeedThresholdMultiplier;

            if (Random.value < perks.SlipResistance)
            {
                return;
            }
        }

        if (dwarfSpeed < effectiveThreshold)
        {
            return;
        }

        Vector3 slipDirection = dwarf.transform.forward;
        CharacterController controller = dwarf.GetComponent<CharacterController>();
        if (controller != null && controller.velocity.sqrMagnitude > 0.1f)
        {
            slipDirection = new Vector3(controller.velocity.x, 0f, controller.velocity.z).normalized;
        }

        _playerCooldowns[dwarfId] = Time.time;
        ExecuteSlip(dwarf, slipDirection * dwarfSpeed);

        return;
    }

    private void OnTriggerEnter(Collider other) => TrySlipCustomer(other);

    private void CheckCustomersStandingInPuddle()
    {
        if (_triggerCollider == null) return;
        Vector3 center = transform.TransformPoint(_triggerCollider.center);
        Collider[] overlaps = Physics.OverlapCapsule(
            center - Vector3.up * (_triggerCollider.height * 0.35f),
            center + Vector3.up * (_triggerCollider.height * 0.35f),
            _triggerCollider.radius,
            ~0,
            QueryTriggerInteraction.Collide);

        foreach (Collider overlap in overlaps) TrySlipCustomer(overlap);
    }

    private void TrySlipCustomer(Collider other)
    {
        if (_isBeingAbsorbed) return;
        CustomerOrder customer = other.GetComponentInParent<CustomerOrder>();
        if (customer == null || _slipImmuneCustomers.Contains(customer) || customer.IsSlipping) return;

        int customerId = customer.GetInstanceID();
        if (_customerCooldowns.TryGetValue(customerId, out float lastSlip)
            && Time.time < lastSlip + perPlayerSlipCooldown) return;

        if (customer.CurrentMoveSpeed < customerSlipSpeedThreshold) return;
        _customerCooldowns[customerId] = Time.time;
        Vector3 direction = customer.transform.forward;
        customer.SlipOnPuddle(direction * Mathf.Max(customer.CurrentMoveSpeed, 1f), ragdollDuration);
    }

    private void ExecuteSlip(DwarfController dwarf, Vector3 horizontalVel)
    {
        if (slipAudio != null)
        {
            slipAudio.pitch = Random.Range(0.85f, 1.15f);
            slipAudio.Play();
        }

        DwarfRagdoll ragdoll = dwarf.GetComponent<DwarfRagdoll>();
        if (ragdoll != null && !ragdoll.IsRagdolled)
        {
            Vector3 slipForce = horizontalVel.normalized * slipImpulseForce + (Vector3.up * 1.5f);
            ragdoll.TriggerRagdoll(slipForce, ragdollDuration);
        }
    }
}
