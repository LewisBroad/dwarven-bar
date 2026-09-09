using UnityEngine;
using System.Collections.Generic;

public class BeerTap : MonoBehaviour, IInteractable
{
    [Header("Line Connection")]
    [SerializeField] private BasementKegBay basementBay;
    [SerializeField] private float pintsPerSecond = 0.35f;

    [Header("Visuals & FX")]
    [SerializeField] private Transform tapHandle;
    [SerializeField] private Transform nozzlePoint;
    [SerializeField] private Transform fluidStreamCylinder;
    [Tooltip("Particle system for the foam splash at the bottom")]
    [SerializeField] private ParticleSystem splashVfx;
    
    [Tooltip("How wide the 3D fluid mesh looks (e.g. 0.035 = 3.5cm wide)")]
    [SerializeField] private float visualStreamWidth = 0.035f;
    [Tooltip("How fast the liquid falls out of the tap (m/s)")]
    [SerializeField] private float streamFallSpeed = 4.5f;

    [SerializeField] private Vector3 openHandleRotation = new Vector3(30f, 0, 0);
    [SerializeField] private Vector3 closedHandleRotation = Vector3.zero;

    [Header("Pouring Physics (Forgiveness)")]
    [Tooltip("How far down the beer can fall")]
    [SerializeField] private float maxStreamDistance = 1.5f;
    [Tooltip("How thick the physical catch zone is. (Keep this larger than the visual width!)")]
    [SerializeField] private float streamRadius = 0.08f; 
    [Tooltip("Volume of missed beer before a puddle forms (0.1 = ~0.3s of grace)")]
    [SerializeField] private float tapMissGraceVolume = 0.1f;
    [SerializeField] private LayerMask collisionMask = ~0;

    public bool IsTapOpen { get; private set; }

    private Quaternion _targetHandleRotation;
    private InteractableHighlight _highlighter;
    [SerializeField] private FloatingPromptAnchor promptAnchor;
    
    // Physics tracking
    private float _spillAccumulator = 0f;
    private float _lastPuddleTime = 0f;
    private float _tapMissedGrace = 0f;

    // Animated Stream tracking
    private Vector3 _currentStreamTop;
    private Vector3 _currentStreamBottom;
    private bool _isFlowingVisually;

    private void Awake()
    {
        _targetHandleRotation = Quaternion.Euler(closedHandleRotation);
        _highlighter = GetComponent<InteractableHighlight>();
        if (_highlighter == null) _highlighter = gameObject.AddComponent<InteractableHighlight>();
        if (promptAnchor == null) promptAnchor = GetComponentInChildren<FloatingPromptAnchor>();
        if (tapHandle != null) tapHandle.localRotation = _targetHandleRotation;

        Vector3 startPos = nozzlePoint != null ? nozzlePoint.position : transform.position;
        _currentStreamTop = startPos;
        _currentStreamBottom = startPos;
    }


    public void Interact(DwarfInteractor interactor)
    {
        IsTapOpen = !IsTapOpen;
        _targetHandleRotation = Quaternion.Euler(IsTapOpen ? openHandleRotation : closedHandleRotation);
    }

    private void Update()
    {
        // 1. Animate the physical handle
        if (tapHandle != null)
        {
            tapHandle.localRotation = Quaternion.Lerp(tapHandle.localRotation, _targetHandleRotation, Time.deltaTime * 12f);
        }

        bool hasBeerFlow = IsTapOpen 
            && basementBay != null 
            && basementBay.IsPumpActive 
            && basementBay.DockedKeg != null 
            && !basementBay.DockedKeg.IsEmpty;

        Vector3 origin = nozzlePoint != null ? nozzlePoint.position : transform.position;

       // 2. Locate the physical target (Glass or Floor)
        RaycastHit[] hits = Physics.SphereCastAll(origin, streamRadius, Vector3.down, maxStreamDistance, collisionMask, QueryTriggerInteraction.Ignore);
        
        Vector3 targetBottom = origin + (Vector3.down * maxStreamDistance);
        float closestDist = maxStreamDistance;
        
        foreach (var hit in hits)
        {
            if (hit.distance < closestDist)
            {
                closestDist = hit.distance;
                
                // UNITY BUG FIX: If SphereCast starts inside a collider, it returns Vector3.zero.
                if (hit.distance == 0 && hit.point == Vector3.zero)
                {
                    targetBottom = origin; // Glass is touching the nozzle. Stop the stream right here.
                }
                else
                {
                    // Forcing X and Z to match the nozzle ensures the stream ALWAYS falls perfectly straight down.
                    targetBottom = new Vector3(origin.x, hit.point.y, origin.z);
                }
            }
        }

        // 3. Animate the 3D Liquid Stream
        UpdateStreamVisuals(hasBeerFlow, origin, targetBottom);

// 4. Process pouring physics ONLY if the liquid has visibly reached the target
        bool streamHasReachedTarget = Vector3.Distance(_currentStreamBottom, targetBottom) < 0.05f;

        // --- NEW SPLASH LOGIC ---
        if (splashVfx != null)
        {
            if (streamHasReachedTarget && _isFlowingVisually)
            {
                splashVfx.transform.position = _currentStreamBottom;
                if (!splashVfx.isPlaying) splashVfx.Play();
            }
            else
            {
                if (splashVfx.isPlaying) splashVfx.Stop();
            }
        }
        // ------------------------

        if (hasBeerFlow && streamHasReachedTarget)
        {
            ProcessPouringPhysics(hits, targetBottom);
        }
    }

    private void UpdateStreamVisuals(bool hasBeerFlow, Vector3 origin, Vector3 targetBottom)
    {
        if (hasBeerFlow)
        {
            _isFlowingVisually = true;
            _currentStreamTop = origin;

            // Stream visibly shoots down from the nozzle
            _currentStreamBottom = Vector3.MoveTowards(_currentStreamBottom, targetBottom, streamFallSpeed * Time.deltaTime);

            // If the player raises the glass into the stream, snap the bottom up so it doesn't clip through
            if (_currentStreamBottom.y < targetBottom.y)
            {
                _currentStreamBottom.y = targetBottom.y;
            }
        }
        else
        {
            if (_isFlowingVisually)
            {
                // Tap closed: The top detaches from the nozzle and falls!
                _currentStreamTop = Vector3.MoveTowards(_currentStreamTop, _currentStreamBottom, streamFallSpeed * 1.5f * Time.deltaTime);
                
                // The bottom continues to fall if it hasn't hit the target yet
                _currentStreamBottom = Vector3.MoveTowards(_currentStreamBottom, targetBottom, streamFallSpeed * Time.deltaTime);

                // If the top catches up to the bottom, the liquid has fully landed
                if (Vector3.Distance(_currentStreamTop, _currentStreamBottom) < 0.02f)
                {
                    _isFlowingVisually = false;
                    _currentStreamTop = origin;
                    _currentStreamBottom = origin;
                    _spillAccumulator = 0f;
                    _tapMissedGrace = 0f; 
                }
            }
        }

        float dist = Vector3.Distance(_currentStreamTop, _currentStreamBottom);

        // Render the fluid cylinder only if there's actual space to draw it
        if (_isFlowingVisually && fluidStreamCylinder != null && dist > 0.02f)
        {
            fluidStreamCylinder.gameObject.SetActive(true);
            Vector3 midPoint = (_currentStreamTop + _currentStreamBottom) * 0.5f;

            fluidStreamCylinder.position = midPoint;
            fluidStreamCylinder.localScale = new Vector3(visualStreamWidth, dist * 0.5f, visualStreamWidth);
        }
        else if (fluidStreamCylinder != null)
        {
            fluidStreamCylinder.gameObject.SetActive(false);
        }
    }
    private void ProcessPouringPhysics(RaycastHit[] hits, Vector3 bestHitPoint)
    {
        float amountPouredThisFrame = pintsPerSecond * Time.deltaTime;
        basementBay.DockedKeg.DrainBeer(amountPouredThisFrame);

        List<PintGlass> caughtGlasses = new List<PintGlass>();
        foreach (var hit in hits)
        {
            if (hit.collider.TryGetComponent(out PintGlass glass) || 
               (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.TryGetComponent(out glass)))
            {
                if (!caughtGlasses.Contains(glass)) caughtGlasses.Add(glass);
            }
        }

        if (caughtGlasses.Count > 0)
        {
            _tapMissedGrace = 0f; // Player successfully caught it, forgive the miss grace
            float splitAmount = amountPouredThisFrame / caughtGlasses.Count;
            string beerType = basementBay.DockedKeg.BeerName;

            foreach (var g in caughtGlasses)
            {
                g.PourBeer(beerType, splitAmount);
            }
        }
        else
        {
            if (_tapMissedGrace < tapMissGraceVolume)
            {
                _tapMissedGrace += amountPouredThisFrame;
            }
            else
            {
                _spillAccumulator += amountPouredThisFrame;
                if (Time.time - _lastPuddleTime >= 0.25f && _spillAccumulator >= 0.04f)
                {
                    _lastPuddleTime = Time.time;
                    float volumeToDeposit = _spillAccumulator;
                    _spillAccumulator = 0f;

                    if (PuddleManager.Instance != null)
                    {
                        PuddleManager.Instance.SpillBeer(bestHitPoint, volumeToDeposit);
                    }
                }
            }
        }
    }
    public string GetInteractionPrompt()
    {
        return IsTapOpen ? "{KEY} to Close Tap" : "{KEY} to Pour Beer";
    }

    public void OnHoverEnter(string boundKeyName)
    {
        if (_highlighter != null) _highlighter.SetHighlight(true);

        if (promptAnchor != null)
        {
            string formattedPrompt = GetInteractionPrompt().Replace("{KEY}", $"[{boundKeyName}]");
            promptAnchor.Show(formattedPrompt);
        }
    }

    public void OnHoverExit()
    {
        if (_highlighter != null) _highlighter.SetHighlight(false);

        if (promptAnchor != null)
        {
            promptAnchor.Hide();
        }
    }
}
