using UnityEngine;

[RequireComponent(typeof(Collider))]
public class KegBayLeaveArea : MonoBehaviour
{
    [SerializeField] private BasementKegBay parentBay;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;

        if (parentBay == null)
        {
            parentBay = GetComponentInParent<BasementKegBay>();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        BeerKeg keg = other.GetComponentInParent<BeerKeg>();
        if (parentBay != null && keg != null)
        {
            parentBay.NotifyKegLeftLeaveArea(keg);
        }
    }
}