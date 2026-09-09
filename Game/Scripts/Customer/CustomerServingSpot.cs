using UnityEngine;

/// <summary>Trigger placed on the bar where a waiting customer receives an order.</summary>
[RequireComponent(typeof(Collider))]
public class CustomerServingSpot : MonoBehaviour
{
    [SerializeField] private Transform customerWaitPoint;

    public CustomerOrder CurrentCustomer { get; private set; }
    public Transform CustomerWaitPoint => customerWaitPoint != null ? customerWaitPoint : transform;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    public bool Claim(CustomerOrder customer)
    {
        if (customer == null || (CurrentCustomer != null && CurrentCustomer != customer)) return false;
        CurrentCustomer = customer;
        return true;
    }

    public void Release(CustomerOrder customer)
    {
        if (CurrentCustomer == customer) CurrentCustomer = null;
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
