using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central lobby manager for customer spawning and available service/drinking spots.
/// It can discover spots automatically or use the explicit Inspector lists.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CustomerManager : NetworkBehaviour
{
    [Header("Spot Lists")]
    [SerializeField] private bool automaticallyFindSceneSpots = true;
    [SerializeField] private List<CustomerServingSpot> servingSpots = new();
    [SerializeField] private List<CustomerDrinkingSpot> drinkingSpots = new();

    [Header("Optional Customer Spawning")]
    [SerializeField] private CustomerOrder customerPrefab;
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Shared destination outside the bar for customers leaving after their drink.")]
    [SerializeField] private Transform exitPoint;
    [SerializeField, Min(1)] private int maximumCustomers = 4;
    [SerializeField, Min(0f)] private float spawnInterval = 12f;
    [SerializeField] private bool spawnCustomersAutomatically;

    [Header("Quota Mode Scaling")]
    [Tooltip("Extra simultaneous customers permitted for each day after day one.")]
    [SerializeField, Min(0)] private int additionalMaximumCustomersPerDay = 1;
    [Tooltip("Seconds removed from the spawn interval for each day after day one.")]
    [SerializeField, Min(0f)] private float spawnIntervalReductionPerDay = 1f;
    [SerializeField, Min(0.5f)] private float minimumSpawnInterval = 3f;

    private readonly HashSet<CustomerOrder> _activeCustomers = new();
    private float _nextSpawnTime;
    private bool _customerArrivalsEnabled;
    private int _currentDay = 1;

    public static CustomerManager Instance { get; private set; }
    public Transform ExitPoint => exitPoint;
    private bool IsSpawnAuthority => !IsSpawned || IsServer;

    private void Awake()
    {
        Instance = this;
        if (automaticallyFindSceneSpots) RefreshSpotLists();
    }

    private void Update()
    {
        // Customer selection and spawning are host-only. Clients receive the spawned
        // NetworkObject instead of independently rolling a different customer/queue.
        if (!IsSpawnAuthority) return;
        if (!spawnCustomersAutomatically || !_customerArrivalsEnabled || Time.time < _nextSpawnTime) return;
        if (_activeCustomers.Count >= CurrentMaximumCustomers) return;
        if (customerPrefab == null || spawnPoint == null) return;

        CustomerOrder customer = Instantiate(customerPrefab, spawnPoint.position, spawnPoint.rotation);
        customer.SetExitPointIfMissing(exitPoint);
        RegisterCustomer(customer);
        if (IsSpawned)
        {
            NetworkObject customerNetworkObject = customer.GetComponent<NetworkObject>();
            if (customerNetworkObject == null)
            {
                Debug.LogError("Customer prefab needs a NetworkObject to be spawned in an online game.", customer);
                Destroy(customer.gameObject);
                return;
            }
            customerNetworkObject.Spawn(true);
        }
        _nextSpawnTime = Time.time + CurrentSpawnInterval;
    }

    public bool HasCustomersNeedingService
    {
        get
        {
            foreach (CustomerOrder customer in _activeCustomers)
            {
                if (customer != null && customer.NeedsService) return true;
            }
            return false;
        }
    }

    private int CurrentMaximumCustomers => maximumCustomers + ((_currentDay - 1) * additionalMaximumCustomersPerDay);
    private float CurrentSpawnInterval => Mathf.Max(minimumSpawnInterval, spawnInterval - ((_currentDay - 1) * spawnIntervalReductionPerDay));

    /// <summary>Stops or starts new arrivals without disturbing customers already in the bar.</summary>
    public void SetCustomerArrivalsEnabled(bool enabled)
    {
        if (!IsSpawnAuthority) return;
        _customerArrivalsEnabled = enabled;
        if (enabled) _nextSpawnTime = Time.time + CurrentSpawnInterval;
    }

    /// <summary>Applies the current quota-day difficulty to future arrivals.</summary>
    public void SetDayNumber(int dayNumber)
    {
        if (!IsSpawnAuthority) return;
        _currentDay = Mathf.Max(1, dayNumber);
    }

    public void RegisterCustomer(CustomerOrder customer)
    {
        if (customer != null) _activeCustomers.Add(customer);
    }

    public void UnregisterCustomer(CustomerOrder customer)
    {
        if (customer != null) _activeCustomers.Remove(customer);
    }

    public CustomerServingSpot GetRandomAvailableServingSpot()
    {
        List<CustomerServingSpot> validSpots = servingSpots.FindAll(spot => spot != null);
        if (validSpots.Count == 0) return null;

        int shortestQueue = int.MaxValue;
        foreach (CustomerServingSpot spot in validSpots)
        {
            shortestQueue = Mathf.Min(shortestQueue, spot.QueueCount);
        }

        List<CustomerServingSpot> shortestQueues = validSpots.FindAll(spot => spot.QueueCount == shortestQueue);
        return shortestQueues[Random.Range(0, shortestQueues.Count)];
    }

    public CustomerDrinkingSpot ClaimRandomAvailableDrinkingSpot(CustomerOrder customer)
    {
        List<CustomerDrinkingSpot> available = drinkingSpots.FindAll(spot => spot != null && spot.CurrentCustomer == null);
        if (available.Count == 0) return null;

        CustomerDrinkingSpot chosenSpot = available[Random.Range(0, available.Count)];
        return chosenSpot.Claim(customer) ? chosenSpot : null;
    }

    [ContextMenu("Refresh Spot Lists")]
    public void RefreshSpotLists()
    {
        servingSpots = new List<CustomerServingSpot>(FindObjectsByType<CustomerServingSpot>(FindObjectsSortMode.None));
        drinkingSpots = new List<CustomerDrinkingSpot>(FindObjectsByType<CustomerDrinkingSpot>(FindObjectsSortMode.None));
    }
}
