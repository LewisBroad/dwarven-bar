using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Represents one customer's order. Attach it to a customer prefab with an optional
/// NavMeshAgent, then assign a CustomerServingSpot and exit point.
/// </summary>
[RequireComponent(typeof(NetworkObject), typeof(CustomerRagdoll))]
public class CustomerOrder : NetworkBehaviour
{
    public enum OrderKind { Drink, Snack }

    [Header("Order")]
    [SerializeField] private OrderKind orderKind = OrderKind.Drink;
    [SerializeField] private string requestedItemId = "Dwarven Stout";
    [SerializeField, Range(0.05f, 1f)] private float minimumQuality = 0.75f;
    [SerializeField, Min(0)] private int basePayment = 10;
    [SerializeField, Min(0)] private int maximumTip = 6;
    [SerializeField, Min(1f)] private float targetServiceSeconds = 30f;

    [Header("Dynamic Selection")]
    [SerializeField] private bool chooseRandomUnlockedBeer = true;
    [SerializeField] private bool chooseRandomServingSpot = true;
    [SerializeField] private bool chooseRandomDrinkingSpot = true;

    [Header("Route")]
    [SerializeField] private CustomerServingSpot servingSpot;
    [Tooltip("Seat or standing area the customer visits after collecting a served drink.")]
    [SerializeField] private Transform drinkingPoint;
    private CustomerDrinkingSpot _claimedDrinkingSpot;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private bool destroyAfterLeaving = true;

    [Header("Served Drink")]
    [Tooltip("Optional child transform on the customer, normally positioned at their hand.")]
    [SerializeField] private Transform drinkSocket;
    [Tooltip("Small local adjustment from the assigned Drink Socket.")]
    [SerializeField] private Vector3 drinkSocketOffset = Vector3.zero;
    [SerializeField, Min(0f)] private float minimumDrinkingDuration = 25f;
    [SerializeField, Min(0f)] private float maximumDrinkingDuration = 60f;
    [SerializeField] private bool destroyDrinkWhenLeaving = true;

    [Header("Slip & Mess Behaviour")]
    [SerializeField, Range(0f, 1f)] private float accidentalSpillChance = 0.12f;
    [SerializeField, Min(1f)] private float accidentalSpillCheckSeconds = 8f;
    [SerializeField, Min(0.03f)] private float accidentalSpillPints = 0.15f;

    private bool _waitingForOrder;
    private bool _hasBeenPaid;
    private bool _isLeaving;
    private bool _isDrinking;
    private bool _isSlipping;
    private bool _demandingFreePint;
    private float _waitingStartedAt;
    private float _drinkingEndsAt;
    private PhysicsItem _servedItem;
    private float _nextAccidentalSpillTime;

    public bool IsWaitingForOrder => _waitingForOrder;
    /// <summary>True until this customer has received their order or has left.</summary>
    public bool NeedsService => (!_hasBeenPaid || _demandingFreePint) && !_isLeaving;
    public string RequestedItemId => requestedItemId;
    public bool IsSlipping => _isSlipping;
    public bool IsDemandingFreePint => _demandingFreePint;
    public float CurrentMoveSpeed
    {
        get
        {
            if (agent == null) return 0f;
            Vector3 velocity = agent.velocity.sqrMagnitude > 0.001f ? agent.velocity : agent.desiredVelocity;
            return new Vector3(velocity.x, 0f, velocity.z).magnitude;
        }
    }

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        // The host owns customer decisions and navigation. Add a NetworkTransform to
        // the prefab so the host-driven movement is shown to every client.
        if (IsSpawned && !IsServer) return;
        if (CustomerManager.Instance != null) CustomerManager.Instance.RegisterCustomer(this);
        if (exitPoint == null && CustomerManager.Instance != null) exitPoint = CustomerManager.Instance.ExitPoint;
        ChooseOrderAndServingSpot();
        if (servingSpot != null) servingSpot.JoinQueue(this);
        GoToServingSpot();
    }

    public override void OnDestroy()
    {
        if (CustomerManager.Instance != null) CustomerManager.Instance.UnregisterCustomer(this);
        if (servingSpot != null) servingSpot.Release(this);
        base.OnDestroy();
    }

    private void Update()
    {
        if (IsSpawned && !IsServer) return;
        if (_isSlipping) return;
        if (_isDrinking)
        {
            if (Time.time >= _drinkingEndsAt) LeaveBar();
            return;
        }

        if (agent == null || !agent.isOnNavMesh)
        {
            // Keeps the queue functional during simple scene testing without a
            // baked NavMesh: only the front customer may enter the serving state.
            if (!_waitingForOrder && !_isLeaving && !_hasBeenPaid
                && servingSpot != null && servingSpot.IsFrontOfQueue(this))
            {
                BeginWaitingForOrder();
            }
            return;
        }

        TryCreateAccidentalSpill();

        if (!_waitingForOrder && !_isLeaving)
        {
            if (_hasBeenPaid && !_demandingFreePint && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            {
                BeginDrinking();
            }
            else if ((!_hasBeenPaid || _demandingFreePint) && servingSpot != null)
            {
                agent.SetDestination(servingSpot.GetQueuePosition(this));
                if (servingSpot.IsFrontOfQueue(this) && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
                {
                    BeginWaitingForOrder();
                }
            }
        }
        else if (_isLeaving && exitPoint != null && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance && destroyAfterLeaving)
        {
            Destroy(gameObject);
        }
    }

    private void LateUpdate()
    {
        // Do not parent a spawned glass to a non-networked bone/socket: Netcode can
        // reject that relationship and reset the object to its origin. Keeping its
        // network root unparented while pinning its world pose works in both offline
        // play and multiplayer, and still follows the animated hand exactly.
        if (_servedItem != null && drinkSocket != null)
        {
            _servedItem.transform.SetPositionAndRotation(
                drinkSocket.TransformPoint(drinkSocketOffset),
                drinkSocket.rotation);
        }
    }

    public void ConfigureRoute(CustomerServingSpot spot, Transform exit)
    {
        servingSpot = spot;
        exitPoint = exit;
    }

    public void SetExitPointIfMissing(Transform fallbackExit)
    {
        if (exitPoint == null) exitPoint = fallbackExit;
    }

    private void ChooseOrderAndServingSpot()
    {
        if (chooseRandomUnlockedBeer && orderKind == OrderKind.Drink
            && BeerUnlockRegistry.Instance != null
            && BeerUnlockRegistry.Instance.TryGetRandomUnlockedBeer(out string selectedBeer))
        {
            requestedItemId = selectedBeer;
        }

        if (chooseRandomServingSpot)
        {
            if (CustomerManager.Instance != null)
            {
                CustomerServingSpot selectedSpot = CustomerManager.Instance.GetRandomAvailableServingSpot();
                if (selectedSpot != null) servingSpot = selectedSpot;
            }
            else
            {
                CustomerServingSpot[] spots = FindObjectsByType<CustomerServingSpot>(FindObjectsSortMode.None);
                List<CustomerServingSpot> availableSpots = new();
                foreach (CustomerServingSpot spot in spots)
                {
                    availableSpots.Add(spot);
                }
                if (availableSpots.Count > 0) servingSpot = availableSpots[Random.Range(0, availableSpots.Count)];
            }
        }

        Debug.Log($"[CustomerOrder] {name} ordered {requestedItemId} at {servingSpot?.name ?? "no serving spot"}.", this);
    }

    public bool TryServe(PintGlass glass)
    {
        if (!_waitingForOrder || (_hasBeenPaid && !_demandingFreePint) || orderKind != OrderKind.Drink || glass == null) return false;
        if (!string.Equals(glass.beerType, requestedItemId, System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[CustomerOrder] Rejected drink: ordered '{requestedItemId}', received '{glass.beerType}'.", this);
            return false;
        }
        return CompleteOrder(glass.fillAmount, glass);
    }

    public bool TryServe(ServeableItem snack)
    {
        if (!_waitingForOrder || (_hasBeenPaid && !_demandingFreePint) || orderKind != OrderKind.Snack || snack == null) return false;
        if (!string.Equals(snack.ItemId, requestedItemId, System.StringComparison.OrdinalIgnoreCase)) return false;
        return CompleteOrder(snack.Quality, snack);
    }

    private void GoToServingSpot()
    {
        if (servingSpot == null)
        {
            Debug.LogWarning($"{name} has no CustomerServingSpot assigned.", this);
            return;
        }

        if (agent != null && agent.isOnNavMesh)
        {
            agent.SetDestination(servingSpot.GetQueuePosition(this));
        }
        else
        {
            BeginWaitingForOrder();
        }
    }

    private void BeginWaitingForOrder()
    {
        if (_waitingForOrder || servingSpot == null || !servingSpot.IsFrontOfQueue(this)) return;

        _waitingForOrder = true;
        _waitingStartedAt = Time.time;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
    }

    private bool CompleteOrder(float quality, PhysicsItem servedItem)
    {
        if (quality < minimumQuality)
        {
            Debug.Log($"[CustomerOrder] Rejected order: quality {quality:P0}; minimum is {minimumQuality:P0}.", this);
            return false;
        }

        float qualityScore = Mathf.InverseLerp(minimumQuality, 1f, quality);
        float efficiencyScore = 1f - Mathf.Clamp01((Time.time - _waitingStartedAt) / targetServiceSeconds);
        int tip = Mathf.RoundToInt(maximumTip * Mathf.Clamp01((qualityScore * 0.65f) + (efficiencyScore * 0.35f)));
        int totalPayment = basePayment + tip;

        bool freePint = _demandingFreePint;
        if (!freePint && (LobbyMoney.Instance == null || !LobbyMoney.Instance.TryAddMoney(totalPayment)))
        {
            Debug.LogWarning("Customer payment could not be added: the local player is not the money authority.", this);
            return false;
        }

        _hasBeenPaid = true;
        _demandingFreePint = false;
        _waitingForOrder = false;
        servingSpot.Release(this);
        TakeServedItem(servedItem);
        Debug.Log(freePint ? "[CustomerOrder] Angry customer received their free replacement pint." : $"[CustomerOrder] Order served. Paid {totalPayment} gold ({basePayment} base + {tip} tip).", this);

        ChooseDrinkingSpot();
        if (drinkingPoint != null && agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(drinkingPoint.position);
        }
        else
        {
            BeginDrinking();
        }
        return true;
    }

    private void TakeServedItem(PhysicsItem servedItem)
    {
        if (servedItem == null) return;
        _servedItem = servedItem;

        // A glass may still be held when it crosses the serving trigger. Release it
        // before making it the customer's carried prop.
        foreach (DwarfGrabber grabber in FindObjectsByType<DwarfGrabber>(FindObjectsSortMode.None))
        {
            grabber.ForceReleaseItem(servedItem);
        }

        servedItem.Rb.linearVelocity = Vector3.zero;
        servedItem.Rb.angularVelocity = Vector3.zero;
        servedItem.Rb.isKinematic = true;
        servedItem.Col.enabled = false;

        if (drinkSocket != null)
        {
            servedItem.transform.SetParent(null, true);
            servedItem.transform.SetPositionAndRotation(
                drinkSocket.TransformPoint(drinkSocketOffset),
                drinkSocket.rotation);
        }
        else
        {
            // Safe temporary fallback for customers without a hand socket. It uses
            // the visible model's bounds, rather than assuming the root pivot is at
            // the character's feet.
            Renderer customerRenderer = GetComponentInChildren<Renderer>();
            Bounds bounds = customerRenderer != null
                ? customerRenderer.bounds
                : new Bounds(transform.position + Vector3.up, Vector3.one * 2f);

            Vector3 carryPosition = bounds.center + transform.forward * 0.25f + Vector3.up * (bounds.extents.y * 0.3f);
            servedItem.transform.SetPositionAndRotation(carryPosition, Quaternion.LookRotation(transform.forward, Vector3.up));
            servedItem.transform.SetParent(transform, true);
        }
    }

    private void BeginDrinking()
    {
        if (_isDrinking || _isLeaving) return;

        _isDrinking = true;
        float minDuration = Mathf.Min(minimumDrinkingDuration, maximumDrinkingDuration);
        float maxDuration = Mathf.Max(minimumDrinkingDuration, maximumDrinkingDuration);
        _drinkingEndsAt = Time.time + Random.Range(minDuration, maxDuration);
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
    }

    private void TryCreateAccidentalSpill()
    {
        if (_isLeaving || _isDrinking || agent == null || agent.velocity.sqrMagnitude < 0.5f) return;
        if (Time.time < _nextAccidentalSpillTime) return;
        _nextAccidentalSpillTime = Time.time + accidentalSpillCheckSeconds;

        if (Random.value <= accidentalSpillChance && PuddleManager.Instance != null)
        {
            PuddleManager.Instance.SpillBeer(transform.position, accidentalSpillPints, this);
        }
    }

    /// <summary>Called by BeerPuddle. The host owns the customer state transition.</summary>
    public void SlipOnPuddle(Vector3 force, float ragdollDuration)
    {
        if ((IsSpawned && !IsServer) || _isSlipping || _isLeaving) return;
        _isSlipping = true;
        bool hadAlreadyOrdered = _hasBeenPaid;

        CustomerRagdoll ragdoll = GetComponent<CustomerRagdoll>();
        if (ragdoll != null)
        {
            ragdoll.TriggerRagdoll(force, ragdollDuration, () => RecoverFromSlip(hadAlreadyOrdered));
        }
        else
        {
            StartCoroutine(RecoverFromSlipAfterDelay(hadAlreadyOrdered, ragdollDuration));
        }
    }

    private System.Collections.IEnumerator RecoverFromSlipAfterDelay(bool hadAlreadyOrdered, float duration)
    {
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        yield return new WaitForSeconds(duration);
        RecoverFromSlip(hadAlreadyOrdered);
    }

    private void RecoverFromSlip(bool hadAlreadyOrdered)
    {
        _isSlipping = false;
        if (!hadAlreadyOrdered)
        {
            if (servingSpot != null) servingSpot.Release(this);
            _waitingForOrder = false;
            LeaveBar();
            return;
        }

        // They spill the drink, then rejoin the service queue as an angry customer.
        DropDrinkAfterSlip();
        if (_claimedDrinkingSpot != null)
        {
            _claimedDrinkingSpot.Release(this);
            _claimedDrinkingSpot = null;
        }
        _isDrinking = false;
        _demandingFreePint = true;
        if (CustomerManager.Instance != null)
        {
            CustomerServingSpot replacementSpot = CustomerManager.Instance.GetRandomAvailableServingSpot();
            if (replacementSpot != null) servingSpot = replacementSpot;
        }
        if (servingSpot != null)
        {
            servingSpot.JoinQueue(this);
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(servingSpot.GetQueuePosition(this));
            }
        }
    }

    private void DropDrinkAfterSlip()
    {
        PintGlass glass = _servedItem as PintGlass;
        if (glass == null) return;
        float spilledBeer = Mathf.Max(0.1f, glass.fillAmount);
        glass.MakeDirty();
        glass.transform.SetParent(null, true);
        glass.transform.SetPositionAndRotation(transform.position + transform.forward * 0.35f + Vector3.up * 0.15f, Quaternion.identity);
        glass.Rb.isKinematic = false;
        glass.Rb.useGravity = true;
        glass.Col.enabled = true;
        glass.Rb.linearVelocity = Vector3.zero;
        glass.Rb.angularVelocity = Vector3.zero;
        if (PuddleManager.Instance != null) PuddleManager.Instance.SpillBeer(glass.transform.position, spilledBeer, this);
        _servedItem = null;
    }

    private void ChooseDrinkingSpot()
    {
        if (!chooseRandomDrinkingSpot) return;

        if (CustomerManager.Instance != null)
        {
            _claimedDrinkingSpot = CustomerManager.Instance.ClaimRandomAvailableDrinkingSpot(this);
            if (_claimedDrinkingSpot != null) drinkingPoint = _claimedDrinkingSpot.StandPoint;
            return;
        }

        CustomerDrinkingSpot[] spots = FindObjectsByType<CustomerDrinkingSpot>(FindObjectsSortMode.None);
        List<CustomerDrinkingSpot> availableSpots = new();
        foreach (CustomerDrinkingSpot spot in spots)
        {
            if (spot.CurrentCustomer == null) availableSpots.Add(spot);
        }

        if (availableSpots.Count == 0) return;

        _claimedDrinkingSpot = availableSpots[Random.Range(0, availableSpots.Count)];
        _claimedDrinkingSpot.Claim(this);
        drinkingPoint = _claimedDrinkingSpot.StandPoint;
    }

    private void LeaveBar()
    {
        _isDrinking = false;
        _isLeaving = true;

        bool leftUsedGlass = false;
        PintGlass usedGlass = _servedItem as PintGlass;
        if (usedGlass != null)
        {
            usedGlass.MakeDirty();
            if (_claimedDrinkingSpot != null)
            {
                _claimedDrinkingSpot.LeaveDirtyGlass(usedGlass);
                leftUsedGlass = true;
            }
            else
            {
                // A customer without a configured drinking spot still leaves their
                // glass in the world rather than silently deleting it.
                usedGlass.transform.SetParent(null, true);
                usedGlass.transform.SetPositionAndRotation(transform.position + transform.forward * 0.35f + Vector3.up * 0.2f, Quaternion.identity);
                usedGlass.Rb.linearVelocity = Vector3.zero;
                usedGlass.Rb.angularVelocity = Vector3.zero;
                usedGlass.Rb.isKinematic = false;
                usedGlass.Rb.useGravity = true;
                usedGlass.Col.enabled = true;
                leftUsedGlass = true;
            }
        }

        if (_claimedDrinkingSpot != null)
        {
            _claimedDrinkingSpot.Release(this);
            _claimedDrinkingSpot = null;
        }

        // Stop LateUpdate from moving a dropped glass back to the departing hand.
        if (leftUsedGlass) _servedItem = null;

        if (destroyDrinkWhenLeaving && _servedItem != null)
        {
            Destroy(_servedItem.gameObject);
            _servedItem = null;
        }

        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            if (exitPoint != null) agent.SetDestination(exitPoint.position);
            else Debug.LogWarning($"{name} has no exit point. Assign Customer Manager > Exit Point so customers visibly leave.", this);
        }
        else if (destroyAfterLeaving)
        {
            Destroy(gameObject, 1f);
        }
    }
}

/// <summary>Optional ragdoll controller for a customer prefab.</summary>
public class CustomerRagdoll : MonoBehaviour
{
    [SerializeField] private Transform hipsBone;
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField, Min(0.1f)] private float minimumDuration = 1.5f;

    private Rigidbody[] _bodies;
    private Collider[] _ragdollColliders;
    private bool _isRagdolled;
    public bool IsRagdolled => _isRagdolled;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (hipsBone == null && animator != null && animator.isHuman)
            hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        _bodies = hipsBone != null ? hipsBone.GetComponentsInChildren<Rigidbody>(true) : System.Array.Empty<Rigidbody>();
        _ragdollColliders = hipsBone != null ? hipsBone.GetComponentsInChildren<Collider>(true) : System.Array.Empty<Collider>();
        SetRagdollState(false);
    }

    public void TriggerRagdoll(Vector3 force, float duration, System.Action onRecovered)
    {
        if (!_isRagdolled) StartCoroutine(RagdollRoutine(force, duration, onRecovered));
    }

    private System.Collections.IEnumerator RagdollRoutine(Vector3 force, float duration, System.Action onRecovered)
    {
        _isRagdolled = true;
        bool hasRig = _bodies.Length > 0;
        if (hasRig)
        {
            SetRagdollState(true);
            foreach (Rigidbody body in _bodies)
            {
                body.linearVelocity = force * 0.35f;
                body.AddForce(force + Vector3.up * 1.5f, ForceMode.Impulse);
            }
        }
        else
        {
            if (agent != null) agent.isStopped = true;
            if (animator != null) animator.enabled = false;
        }

        yield return new WaitForSeconds(Mathf.Max(minimumDuration, duration));
        if (hasRig && hipsBone != null && Physics.Raycast(hipsBone.position + Vector3.up, Vector3.down, out RaycastHit hit, 3f))
            transform.position = hit.point + Vector3.up * 0.05f;

        SetRagdollState(false);
        _isRagdolled = false;
        onRecovered?.Invoke();
    }

    private void SetRagdollState(bool active)
    {
        if (agent != null) agent.enabled = !active;
        if (animator != null) animator.enabled = !active;
        foreach (Rigidbody body in _bodies) { body.isKinematic = !active; body.useGravity = active; }
        foreach (Collider collider in _ragdollColliders) collider.enabled = active;
    }
}
