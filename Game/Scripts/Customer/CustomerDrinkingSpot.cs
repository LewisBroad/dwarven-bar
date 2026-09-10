using UnityEngine;

/// <summary>A table, stool, or standing location that can be claimed by one customer.</summary>
public class CustomerDrinkingSpot : MonoBehaviour
{
    [SerializeField] private Transform standPoint;
    [Header("Used Glasses")]
    [SerializeField] private Transform dirtyGlassDropPoint;
    [SerializeField, Min(0f)] private float dirtyGlassScatterRadius = 0.16f;
    public CustomerOrder CurrentCustomer { get; private set; }
    public Transform StandPoint => standPoint != null ? standPoint : transform;

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

    /// <summary>Returns a customer's used glass to their table/standing area.</summary>
    public void LeaveDirtyGlass(PintGlass glass)
    {
        if (glass == null) return;

        Transform drop = dirtyGlassDropPoint != null ? dirtyGlassDropPoint : StandPoint;
        Vector2 scatter = Random.insideUnitCircle * dirtyGlassScatterRadius;
        glass.transform.SetParent(null, true);
        glass.transform.SetPositionAndRotation(drop.position + new Vector3(scatter.x, 0.05f, scatter.y), Quaternion.identity);
        glass.Rb.linearVelocity = Vector3.zero;
        glass.Rb.angularVelocity = Vector3.zero;
        glass.Rb.isKinematic = false;
        glass.Rb.useGravity = true;
        glass.Col.enabled = true;
    }
}
