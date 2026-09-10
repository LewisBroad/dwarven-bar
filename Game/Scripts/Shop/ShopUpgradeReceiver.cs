using UnityEngine;
using UnityEngine.Events;

/// <summary>Connects a catalog upgrade ID to arbitrary scene changes through UnityEvents.</summary>
public class ShopUpgradeReceiver : MonoBehaviour
{
    [SerializeField] private string upgradeId;
    [SerializeField] private UnityEvent onPurchased;
    private bool _purchased;

    public string UpgradeId => upgradeId;
    public bool CanPurchase => !_purchased;

    public bool TryPurchase()
    {
        if (_purchased) return false;
        _purchased = true;
        onPurchased?.Invoke();
        return true;
    }
}
