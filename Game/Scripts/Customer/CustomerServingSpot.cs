using System.Collections.Generic;
using UnityEngine;

/// <summary>Trigger placed on the bar where a waiting customer receives an order.</summary>
[RequireComponent(typeof(Collider))]
public class CustomerServingSpot : MonoBehaviour
{
    [SerializeField] private Transform customerWaitPoint;
    [Header("Queue")]
    [SerializeField] private Transform queueAnchor;
    [SerializeField, Min(0.1f)] private float queueSpacing = 1.2f;

    private readonly List<CustomerOrder> _queue = new();

    public CustomerOrder CurrentCustomer => _queue.Count > 0 ? _queue[0] : null;
    public int QueueCount => _queue.Count;
    public Transform CustomerWaitPoint => customerWaitPoint != null ? customerWaitPoint : transform;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    public void JoinQueue(CustomerOrder customer)
    {
        if (customer != null && !_queue.Contains(customer)) _queue.Add(customer);
    }

    public void Release(CustomerOrder customer)
    {
        _queue.Remove(customer);
    }

    public bool IsFrontOfQueue(CustomerOrder customer)
    {
        return CurrentCustomer == customer;
    }

    public Vector3 GetQueuePosition(CustomerOrder customer)
    {
        int queueIndex = _queue.IndexOf(customer);
        if (queueIndex < 0) queueIndex = 0;

        Transform anchor = queueAnchor != null ? queueAnchor : CustomerWaitPoint;
        return anchor.position - anchor.forward * (queueIndex * queueSpacing);
    }

    private void OnTriggerEnter(Collider other) => TryServe(other);
    private void OnTriggerStay(Collider other) => TryServe(other);

    private void TryServe(Collider other)
    {
        if (CurrentCustomer == null) return;

        PintGlass glass = other.GetComponentInParent<PintGlass>();
        if (glass != null)
        {
            CurrentCustomer.TryServe(glass);
            return;
        }

        ServeableItem snack = other.GetComponentInParent<ServeableItem>();
        if (snack != null) CurrentCustomer.TryServe(snack);
    }
}
