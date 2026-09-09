using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Represents one customer's order. Attach it to a customer prefab with an optional
/// NavMeshAgent, then assign a CustomerServingSpot and exit point.
/// </summary>
public class CustomerOrder : MonoBehaviour
{
    public enum OrderKind { Drink, Snack }

    [Header("Order")]
    [SerializeField] private OrderKind orderKind = OrderKind.Drink;
    [SerializeField] private string requestedItemId = "Standard Ale";
    [SerializeField, Range(0.05f, 1f)] private float minimumQuality = 0.75f;
    [SerializeField, Min(0)] private int basePayment = 10;
    [SerializeField, Min(0)] private int maximumTip = 6;
    [SerializeField, Min(1f)] private float targetServiceSeconds = 30f;

    [Header("Route")]
    [SerializeField] private CustomerServingSpot servingSpot;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private bool destroyAfterLeaving = true;

    private bool _waitingForOrder;
    private bool _hasBeenPaid;
    private bool _isLeaving;
    private float _waitingStartedAt;

    public bool IsWaitingForOrder => _waitingForOrder;
    public string RequestedItemId => requestedItemId;

    private void Awake()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        GoToServingSpot();
    }

    private void Update()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        if (!_waitingForOrder && !_isLeaving && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            BeginWaitingForOrder();
        }
        else if (_isLeaving && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance && destroyAfterLeaving)
        {
            Destroy(gameObject);
        }
    }

    public void ConfigureRoute(CustomerServingSpot spot, Transform exit)
    {
        servingSpot = spot;
        exitPoint = exit;
    }

    public bool TryServe(PintGlass glass)
    {
        if (!_waitingForOrder || _hasBeenPaid || orderKind != OrderKind.Drink || glass == null) return false;
        if (!string.Equals(glass.beerType, requestedItemId, System.StringComparison.OrdinalIgnoreCase)) return false;
        return CompleteOrder(glass.fillAmount);
    }

    public bool TryServe(ServeableItem snack)
    {
        if (!_waitingForOrder || _hasBeenPaid || orderKind != OrderKind.Snack || snack == null) return false;
        if (!string.Equals(snack.ItemId, requestedItemId, System.StringComparison.OrdinalIgnoreCase)) return false;
        return CompleteOrder(snack.Quality);
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
            agent.SetDestination(servingSpot.CustomerWaitPoint.position);
        }
        else
        {
            BeginWaitingForOrder();
        }
    }

    private void BeginWaitingForOrder()
    {
        if (_waitingForOrder || servingSpot == null || !servingSpot.Claim(this)) return;

        _waitingForOrder = true;
        _waitingStartedAt = Time.time;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
    }

    private bool CompleteOrder(float quality)
    {
        if (quality < minimumQuality) return false;

        float qualityScore = Mathf.InverseLerp(minimumQuality, 1f, quality);
        float efficiencyScore = 1f - Mathf.Clamp01((Time.time - _waitingStartedAt) / targetServiceSeconds);
        int tip = Mathf.RoundToInt(maximumTip * Mathf.Clamp01((qualityScore * 0.65f) + (efficiencyScore * 0.35f)));
        int totalPayment = basePayment + tip;

        if (LobbyMoney.Instance == null || !LobbyMoney.Instance.TryAddMoney(totalPayment))
        {
            Debug.LogWarning("Customer payment could not be added: the local player is not the money authority.", this);
            return false;
        }

        _hasBeenPaid = true;
        _waitingForOrder = false;
        servingSpot.Release(this);
        LeaveBar();
        return true;
    }

    private void LeaveBar()
    {
        _isLeaving = true;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            if (exitPoint != null) agent.SetDestination(exitPoint.position);
        }
        else if (destroyAfterLeaving)
        {
            Destroy(gameObject, 1f);
        }
    }
}
