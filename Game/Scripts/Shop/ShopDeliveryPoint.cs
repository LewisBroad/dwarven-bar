using System.Collections.Generic;
using UnityEngine;

/// <summary>Named destination for an ordered physical item, with independent shelf/drop slots.</summary>
public class ShopDeliveryPoint : MonoBehaviour
{
    [SerializeField] private string deliveryPointId = "Basement";
    [SerializeField] private Transform[] deliverySlots;
    [SerializeField, Min(0.05f)] private float slotClearDistance = 0.35f;

    private readonly Dictionary<Transform, GameObject> _slotContents = new();
    public string DeliveryPointId => deliveryPointId;

    public bool CanDeliver()
    {
        return FindFreeSlot() != null;
    }

    public bool TryDeliver(GameObject prefab, out GameObject deliveredObject)
    {
        deliveredObject = null;
        if (prefab == null) return false;

        Transform slot = FindFreeSlot();
        if (slot == null) return false;

        deliveredObject = Instantiate(prefab, slot.position, slot.rotation);
        _slotContents[slot] = deliveredObject;

        if (deliveredObject.TryGetComponent(out Rigidbody rigidbody))
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }
        return true;
    }

    private Transform FindFreeSlot()
    {
        foreach (Transform slot in deliverySlots)
        {
            if (slot == null) continue;

            if (_slotContents.TryGetValue(slot, out GameObject item))
            {
                if (item == null || Vector3.Distance(item.transform.position, slot.position) > slotClearDistance)
                {
                    _slotContents.Remove(slot);
                }
                else
                {
                    continue;
                }
            }
            return slot;
        }
        return null;
    }
}
