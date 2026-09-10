using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider), typeof(NetworkObject))]
public class PintGlass : PhysicsItem
{
    [Header("Glass & Liquid Stats")]
    [Range(0f, 1f)] public float fillAmount = 1f;
    public string beerType = "Standard Ale";
    [SerializeField] private bool startsClean = true;
    public bool isUnbreakable = false;

    [Header("Cleanliness")]
    [Tooltip("The renderer for the physical glass, not the LiquidVisual child.")]
    [SerializeField] private Renderer glassRenderer;
    [Tooltip("Optional. If omitted, the glass renderer's starting material is used when clean.")]
    [SerializeField] private Material cleanGlassMaterial;
    [Tooltip("A stained/foggy version of the glass material used after a customer drinks from it.")]
    [SerializeField] private Material dirtyGlassMaterial;
    public DwarvenDishwasher DockedDishwasher { get; private set; }
    private readonly NetworkVariable<bool> _networkIsClean = new(true);
    private bool _offlineIsClean;
    public bool IsClean => IsSpawned ? _networkIsClean.Value : _offlineIsClean;

    [Header("Puddle Spawning & Throttling")]
    [Tooltip("Minimum seconds between puddle drops/expansions from this glass")]
    [Range(0.05f, 1.5f)] 
    [SerializeField] private float puddleInterval = 0.35f;

    [Tooltip("Minimum volume accumulated before triggering a puddle update")]
    [Range(0.01f, 0.2f)] 
    [SerializeField] private float minPuddleVolume = 0.04f;

    [Tooltip("Radius within which drops merge into an existing puddle instead of making a new one")]
    [Range(0.2f, 2.0f)] 
    [SerializeField] private float puddleMergeRadius = 0.75f;

    [Tooltip("Maximum scale the puddle can grow to")]
    [SerializeField] private float maxPuddleScale = 1.8f;

    [Tooltip("How much beer can overflow before a puddle drops (0.08 = ~1/4 sec of grace)")]
    [SerializeField] private float overflowGraceVolume = 0.08f;
    private float _overflowAccumulator = 0f;

    [Header("Physical Dimensions")]
    [SerializeField] private float glassHeight = 0.15f;
    [SerializeField] private float glassRadius = 0.042f;
    [SerializeField] private float minSpillAngle = 18f;

    [Header("Visual Pour Stream")]
    [SerializeField] private ParticleSystem pourParticles;

    [Header("Smooth Hold Settings")]
    [SerializeField] private float followSharpness = 18f;
    [SerializeField] private float maxHoldSpeed = 8f;
    [SerializeField] private float uprightStrength = 30f;
    [SerializeField] private float uprightDamping = 5f;
    [SerializeField] private float tiltInertiaMultiplier = 1.2f;

    [Header("Collision & Break Limits")]
    [SerializeField] private float breakImpactVelocity = 4.2f;
    [SerializeField] private GameObject glassShatterPrefab;
    [SerializeField] private GameObject beerSpillPrefab;

    [Header("Visual References")]
    [SerializeField] private Renderer liquidRenderer;

    // --- Private State & Hashes ---
    private Vector3 _lastVelocity;
    private float _currentAdjustedDistance;
    private MaterialPropertyBlock _propBlock;
    private float _spillAccumulator = 0f;
    private float _lastPuddleTime = 0f;

    // Slosh tracking fields
    private Vector2 _slosh;
    private Vector2 _sloshVelocity;

    private static readonly int FillLevelProp = Shader.PropertyToID("_FillLevel");
    private static readonly int TiltXProp     = Shader.PropertyToID("_TiltX");
    private static readonly int TiltZProp     = Shader.PropertyToID("_TiltZ");

    protected override void Awake()
    {
        base.Awake();
        _offlineIsClean = startsClean;
        SetMass(0.4f);

        Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Rb.interpolation = RigidbodyInterpolation.Interpolate;

        _propBlock = new MaterialPropertyBlock();

        if (liquidRenderer == null)
        {
            Transform liquidChild = transform.Find("LiquidVisual");
            if (liquidChild != null)
            {
                liquidRenderer = liquidChild.GetComponent<Renderer>();
            }
        }

        if (glassRenderer == null)
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != liquidRenderer)
                {
                    glassRenderer = renderer;
                    break;
                }
            }
        }
        if (cleanGlassMaterial == null && glassRenderer != null)
            cleanGlassMaterial = glassRenderer.sharedMaterial;

        UpdateLiquidVisual();
        UpdateCleanlinessVisual();
    }

    private void OnValidate()
    {
        if (Application.isPlaying && liquidRenderer != null)
        {
            UpdateLiquidVisual();
        }
    }

    public override bool OnGrab(Transform cameraTransform, Vector3 worldHitPoint, Collider playerCollider, DwarfGrabber grabber)
    {
        if (DockedDishwasher != null && !DockedDishwasher.TryReleaseGlass(this)) return false;
        bool grabbed = base.OnGrab(cameraTransform, worldHitPoint, playerCollider, grabber);
        if (grabbed && _activeGrabs.Count > 0)
        {
            _currentAdjustedDistance = _activeGrabs[0].GrabDistance;
            Rb.useGravity = false;
            Rb.linearDamping = 3f;
            Rb.angularDamping = 4f;
        }
        return grabbed;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer) _networkIsClean.Value = startsClean;
        _networkIsClean.OnValueChanged += OnCleanlinessChanged;
        UpdateCleanlinessVisual();
    }

    public override void OnNetworkDespawn()
    {
        _networkIsClean.OnValueChanged -= OnCleanlinessChanged;
        base.OnNetworkDespawn();
    }

    public void MakeDirty()
    {
        if (IsSpawned && !IsServer) return;
        fillAmount = 0f;
        beerType = string.Empty;
        SetCleanState(false);
        UpdateLiquidVisual();
        UpdateCleanlinessVisual();
    }

    public void Clean()
    {
        if (IsSpawned && !IsServer) return;
        fillAmount = 0f;
        beerType = string.Empty;
        SetCleanState(true);
        UpdateLiquidVisual();
        UpdateCleanlinessVisual();
    }

    public void SetDockedDishwasher(DwarvenDishwasher dishwasher)
    {
        DockedDishwasher = dishwasher;
    }

    private void UpdateCleanlinessVisual()
    {
        if (glassRenderer == null) return;
        Material targetMaterial = IsClean ? cleanGlassMaterial : dirtyGlassMaterial;
        if (targetMaterial != null) glassRenderer.sharedMaterial = targetMaterial;
    }

    private void SetCleanState(bool clean)
    {
        if (IsSpawned) _networkIsClean.Value = clean;
        else _offlineIsClean = clean;
    }

    private void OnCleanlinessChanged(bool previousValue, bool newValue) => UpdateCleanlinessVisual();

    public override void OnRelease(Collider playerCollider, DwarfGrabber grabber)
    {
        base.OnRelease(playerCollider, grabber);
        Rb.useGravity = true;
        Rb.linearDamping = 0.05f;
        Rb.angularDamping = 0.05f;
        StopPourParticles();
    }

    private void Update()
    {
        // 1. PHYSICAL SLOSH WITH DEADZONE (Prevents jitter when standing still)
        Vector3 localVel = transform.InverseTransformDirection(Rb.linearVelocity);
        Vector3 localAngVel = transform.InverseTransformDirection(Rb.angularVelocity);

        // Filter out physics noise below 0.05 m/s
        if (localVel.magnitude < 0.05f) localVel = Vector3.zero;
        if (localAngVel.magnitude < 0.1f) localAngVel = Vector3.zero;

        Vector2 targetSlosh = new Vector2(
            Mathf.Clamp(-localVel.x * 0.06f - localAngVel.z * 0.12f, -0.4f, 0.4f),
            Mathf.Clamp(-localVel.z * 0.06f + localAngVel.x * 0.12f, -0.4f, 0.4f)
        );

        // Smoothly settle slosh back to zero
        _slosh = Vector2.SmoothDamp(_slosh, targetSlosh, ref _sloshVelocity, 0.12f);

        UpdateLiquidVisual();

        // 2. Dynamic geometric spill check
        if (fillAmount > 0.005f)
        {
            float currentTilt = Vector3.Angle(transform.up, Vector3.up);
            float criticalAngle = CalculateCriticalSpillAngle(fillAmount);

            if (currentTilt > criticalAngle)
            {
                SpillBeer(currentTilt, criticalAngle);
            }
            else
            {
                StopPourParticles();
            }
        }
        else
        {
            StopPourParticles();
        }
    }

    private float CalculateCriticalSpillAngle(float fill)
    {
        fill = Mathf.Clamp(fill, 0.01f, 1f);
        float aspect = (2f * glassRadius) / Mathf.Max(0.01f, glassHeight);

        float tanAngle = (fill >= 0.5f)
            ? aspect * (1f - fill) / fill
            : aspect / Mathf.Sqrt(2f * fill);

        return Mathf.Max(minSpillAngle, Mathf.Atan(tanAngle) * Mathf.Rad2Deg);
    }

private void SpillBeer(float currentTilt, float criticalAngle)
    {
        float degreesPastBrim = currentTilt - criticalAngle;
        float drainSpeed = Mathf.Lerp(0.35f, 1.2f, Mathf.Clamp01(degreesPastBrim / 40f));

        float lostBeer = drainSpeed * Time.deltaTime;
        fillAmount = Mathf.Max(0f, fillAmount - lostBeer);

        _spillAccumulator += lostBeer;

        // Particle stream visual
        if (pourParticles != null)
        {
            Vector3 localDown = transform.InverseTransformDirection(Vector3.down);
            localDown.y = 0f;
            if (localDown.sqrMagnitude > 0.001f)
            {
                var shape = pourParticles.shape;
                shape.rotation = Quaternion.LookRotation(localDown.normalized).eulerAngles;
            }

            if (!pourParticles.isPlaying)
            {
                pourParticles.Play();
            }
        }

            if (Time.time - _lastPuddleTime >= puddleInterval && _spillAccumulator >= minPuddleVolume)
    {
        _lastPuddleTime = Time.time;
        float volumeToDeposit = _spillAccumulator;
        _spillAccumulator = 0f;

        Vector3 lowestLipDir = Vector3.ProjectOnPlane(-Vector3.up, transform.up).normalized;
        Vector3 rimPosWS = transform.position + (transform.up * (glassHeight * 0.5f));
        Vector3 rayOrigin = rimPosWS + (lowestLipDir * (glassRadius + 0.015f));

        int layerMask = ~LayerMask.GetMask("Player", "LocalPlayerBody", "Debris");

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 3.5f, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != Col && !hit.collider.transform.IsChildOf(transform))
            {
                SpawnPuddleAt(hit.point, hit.normal, volumeToDeposit);
            }
        }
    }
    }

    private void StopPourParticles()
    {
        if (pourParticles != null && pourParticles.isPlaying)
        {
            pourParticles.Stop();
        }
    }

   private void UpdateLiquidVisual()
    {
        if (liquidRenderer == null) return;

        if (fillAmount <= 0.005f)
        {
            liquidRenderer.enabled = false;
            return;
        }

        liquidRenderer.enabled = true;

        // 1. Get the direction of world gravity in the glass's LOCAL space
        Vector3 localDown = transform.InverseTransformDirection(Vector3.down);

        // Project onto the horizontal plane of the cylinder (X and Z)
        Vector2 tiltDir = new Vector2(localDown.x, localDown.z);
        float tiltMagnitude = tiltDir.magnitude; // 0 when upright, 1 when horizontal (90 deg)

        if (tiltMagnitude > 0.001f)
        {
            tiltDir /= tiltMagnitude; // Normalized local direction of the lean
        }

        // 2. Calculate the tilt slope
        // A cylinder's local radius is 0.5. To reach the brim (1.0) from the current fill level:
        // Slope required = (1.0 - fillAmount) / 0.5 = 2.0 * (1.0 - fillAmount)
        float maxTiltSlope = 2.0f * (1.0f - fillAmount);

        // Scale the tilt slope based on how far the glass is tipped
        float currentTiltSlope = Mathf.Clamp(tiltMagnitude * 2.2f, 0f, maxTiltSlope);

        // 3. Slosh inertia from dwarf movement (safely check Rb for OnValidate / Editor use)
        Vector3 currentVelocity = (Rb != null) ? Rb.linearVelocity : Vector3.zero;
        Vector3 localVel = transform.InverseTransformDirection(currentVelocity);

        Vector2 sloshTarget = Vector2.zero;
        if (localVel.sqrMagnitude > 0.04f) // Deadzone for micro-jitter
            {
                sloshTarget = new Vector2(-localVel.x, -localVel.z) * 0.08f;
            }
        _slosh = Vector2.SmoothDamp(_slosh, sloshTarget, ref _sloshVelocity, 0.1f);

        // 4. Combine gravity lean with slosh inertia
        float finalTiltX = (tiltDir.x * currentTiltSlope) + _slosh.x;
        float finalTiltZ = (tiltDir.y * currentTiltSlope) + _slosh.y;

        // 5. Volume-conserving base fill adjustment
        // When tilted, pivoting the surface around the center ensures the fluid never shrinks
        float adjustedFill = fillAmount;

        // Push values to MaterialPropertyBlock
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        liquidRenderer.GetPropertyBlock(_propBlock);

        _propBlock.SetFloat(FillLevelProp, adjustedFill);
        _propBlock.SetFloat(TiltXProp, finalTiltX);
        _propBlock.SetFloat(TiltZProp, finalTiltZ);

        liquidRenderer.SetPropertyBlock(_propBlock);
    }
    protected override void FixedUpdate()
    {
        if (!IsHeld || _activeGrabs.Count == 0 || _activeGrabs[0].CameraTransform == null)
        {
            base.FixedUpdate();
            return;
        }

        GrabInstance grab = _activeGrabs[0];
        Transform cam = grab.CameraTransform;

        float targetDistance = grab.GrabDistance;
        float glassSweepRadius = 0.12f;

        Ray cameraRay = new Ray(cam.position, cam.forward);
        int layerMask = ~LayerMask.GetMask("Player", "LocalPlayerBody");

        if (Physics.SphereCast(cameraRay, glassSweepRadius, out RaycastHit hit, grab.GrabDistance, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != Col)
            {
                targetDistance = Mathf.Max(0.45f, hit.distance);
            }
        }

        _currentAdjustedDistance = Mathf.Lerp(_currentAdjustedDistance, targetDistance, Time.fixedDeltaTime * 15f);
        Vector3 targetWorldPos = cam.position + (cam.forward * _currentAdjustedDistance);

        Vector3 posError = targetWorldPos - Rb.position;
        Vector3 targetLinearVel = Vector3.ClampMagnitude(posError * followSharpness, maxHoldSpeed);
        Rb.linearVelocity = Vector3.MoveTowards(Rb.linearVelocity, targetLinearVel, 60f * Time.fixedDeltaTime);

        Vector3 currentAcceleration = (Rb.linearVelocity - _lastVelocity) / Time.fixedDeltaTime;
        _lastVelocity = Rb.linearVelocity;

        Vector3 desiredUp = (Vector3.up - (currentAcceleration * (0.012f * tiltInertiaMultiplier))).normalized;

        Quaternion targetRot = Quaternion.FromToRotation(transform.up, desiredUp) * transform.rotation;
        Quaternion rotDiff = targetRot * Quaternion.Inverse(transform.rotation);

        rotDiff.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);
        if (angleInDegrees > 180f) angleInDegrees -= 360f;

        if (Mathf.Abs(angleInDegrees) > 0.1f && !float.IsNaN(rotationAxis.x))
        {
            Vector3 targetAngVel = rotationAxis.normalized * (angleInDegrees * Mathf.Deg2Rad * uprightStrength);
            Rb.angularVelocity = Vector3.MoveTowards(Rb.angularVelocity, targetAngVel, uprightDamping * 10f * Time.fixedDeltaTime);
        }
    }

   public void PourBeer(string type, float amount)
    {
        beerType = type;
        
        float spaceRemaining = 1f - fillAmount;
        
        if (amount <= spaceRemaining)
        {
            fillAmount += amount;
            _overflowAccumulator = 0f; 
        }
        else
        {
            fillAmount = 1f;
            float excess = amount - spaceRemaining;
            
            _overflowAccumulator += excess;
            
            if (_overflowAccumulator >= overflowGraceVolume)
            {
                float spillVolume = _overflowAccumulator;
                _overflowAccumulator = 0f; 
                
                int layerMask = ~LayerMask.GetMask("Player", "LocalPlayerBody", "Debris");
                
                // FIXED: Start the raycast slightly ABOVE the base of the glass 
                // so it doesn't get trapped inside the table's surface boundary.
                Vector3 rayOrigin = transform.position + (Vector3.up * 0.1f);
                
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 2.5f, layerMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider != Col && !hit.collider.transform.IsChildOf(transform))
                    {
                        SpawnPuddleAt(hit.point, hit.normal, spillVolume);
                    }
                }
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isUnbreakable) return;

        if (collision.relativeVelocity.magnitude >= breakImpactVelocity)
        {
            ContactPoint contact = collision.contacts[0];
            Vector3 impactPoint = contact.point;
            Vector3 surfaceNormal = contact.normal; // Normal pointing out from hit wall/floor
            
            // Use Rb.linearVelocity or relativeVelocity so direction is preserved
            Vector3 incomingVelocity = (Rb != null) ? Rb.linearVelocity : -collision.relativeVelocity;

            Shatter(impactPoint, incomingVelocity, surfaceNormal);
        }
    }

    public void Shatter(Vector3 impactPoint, Vector3 incomingVelocity, Vector3 surfaceNormal)
    {
        StopPourParticles();

        // --- FIXED SHATTER PUDDLE ---
        if (fillAmount > 0.05f)
        {
            // Raycast down from the impact point to find the true floor under the shattered glass
            int layerMask = ~LayerMask.GetMask("Player", "LocalPlayerBody", "Debris");
            Vector3 floorCheckOrigin = impactPoint + (surfaceNormal * 0.05f);

            if (Physics.Raycast(floorCheckOrigin, Vector3.down, out RaycastHit floorHit, 4.0f, layerMask, QueryTriggerInteraction.Ignore))
            {
                SpawnPuddleAt(floorHit.point, floorHit.normal, fillAmount);
            }
            else
            {
                // Fallback: spawn on the surface we struck (e.g. table top or wall splash)
                SpawnPuddleAt(impactPoint, surfaceNormal, fillAmount);
            }
        }

        // Spawn shatter debris
        if (glassShatterPrefab != null)
        {
            GameObject shatterObj = Instantiate(glassShatterPrefab, transform.position, transform.rotation);
            if (shatterObj.TryGetComponent(out ShatteredGlass sg))
            {
                sg.Initialize(incomingVelocity, impactPoint, surfaceNormal);
            }
        }

        Destroy(gameObject);
    }
    private void OnDrawGizmosSelected()
    {
        // Draws a blue wire disc showing the calculated top rim and radius
        Vector3 rimPosWS = transform.position + (transform.up * (glassHeight * 0.5f));
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(rimPosWS, glassRadius);

        // Draws the lowest rim point where beer will exit when tipped
        Vector3 lowestLipDir = Vector3.ProjectOnPlane(-Vector3.up, transform.up).normalized;
        Vector3 lowestRimPoint = rimPosWS + (lowestLipDir * glassRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(lowestRimPoint, 0.01f);
    }
private void SpawnPuddleAt(Vector3 point, Vector3 normal, float beerVolume)
    {
        if (PuddleManager.Instance != null)
        {
            PuddleManager.Instance.SpillBeer(point, beerVolume);
        }
        else if (beerSpillPrefab != null)
        {
            // Fallback: search for nearby BeerPuddle and call AddBeer directly
            Collider[] existingPuddles = Physics.OverlapSphere(point, puddleMergeRadius, ~0, QueryTriggerInteraction.Collide);
            BeerPuddle targetPuddle = null;

            foreach (var col in existingPuddles)
            {
                BeerPuddle bp = col.GetComponent<BeerPuddle>();
                if (bp != null && !bp.IsBeingAbsorbed)
                {
                    targetPuddle = bp;
                    break;
                }
            }

            if (targetPuddle != null)
            {
                targetPuddle.AddBeer(beerVolume);
            }
            else
            {
                Quaternion puddleRot = Quaternion.FromToRotation(Vector3.up, normal);
                GameObject newPuddle = Instantiate(beerSpillPrefab, point + (normal * 0.003f), puddleRot);
                if (newPuddle.TryGetComponent(out BeerPuddle bp))
                {
                    bp.AddBeer(beerVolume);
                }
            }
        }
    }
}
