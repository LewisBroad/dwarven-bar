using System.Collections.Generic;
using UnityEngine;

/// <summary>Validates purchases, debits the lobby wallet, and dispatches deliveries/upgrades.</summary>
public class ShopOrderManager : MonoBehaviour
{
    [SerializeField] private MagicShopCatalog catalog;
    [SerializeField] private bool automaticallyFindDeliveryPoints = true;
    [SerializeField] private List<ShopDeliveryPoint> deliveryPoints = new();

    private readonly HashSet<string> _purchasedOnce = new();
    public static ShopOrderManager Instance { get; private set; }
    public MagicShopCatalog Catalog => catalog;

    private void Awake()
    {
        Instance = this;
        if (automaticallyFindDeliveryPoints) RefreshDeliveryPoints();
    }

    public bool TryOrder(ShopItemDefinition item, out string result)
    {
        result = "";
        if (item == null) { result = "That item is unavailable."; return false; }
        if (!item.CanPurchaseMoreThanOnce && _purchasedOnce.Contains(item.ItemId)) { result = "Already purchased."; return false; }

        ShopDeliveryPoint deliveryPoint = null;
        ShopUpgradeReceiver upgrade = null;
        if (item.Kind == ShopItemDefinition.PurchaseKind.DeliveredItem)
        {
            deliveryPoint = deliveryPoints.Find(point => point != null && point.DeliveryPointId == item.DeliveryPointId);
            if (deliveryPoint == null) { result = $"No delivery point named '{item.DeliveryPointId}'."; return false; }
            if (item.DeliveryPrefab == null) { result = "This item has no delivery prefab."; return false; }
            if (!deliveryPoint.CanDeliver()) { result = "All delivery slots are occupied."; return false; }
        }
        else
        {
            foreach (ShopUpgradeReceiver receiver in FindObjectsByType<ShopUpgradeReceiver>(FindObjectsSortMode.None))
            {
                if (receiver.UpgradeId == item.UpgradeId && receiver.CanPurchase)
                {
                    upgrade = receiver;
                    break;
                }
            }
            if (upgrade == null) { result = "That upgrade is unavailable."; return false; }
        }

        if (LobbyMoney.Instance == null || !LobbyMoney.Instance.TrySpendMoney(item.Price))
        {
            result = "Not enough gold.";
            return false;
        }

        bool completed = item.Kind == ShopItemDefinition.PurchaseKind.DeliveredItem
            ? deliveryPoint.TryDeliver(item.DeliveryPrefab, out _)
            : upgrade.TryPurchase();

        if (!completed)
        {
            // This should only be reachable when the world changes between
            // validation and execution; restore the local/host wallet cleanly.
            LobbyMoney.Instance.TryAddMoney(item.Price);
            result = "Order failed. Your gold was returned.";
            return false;
        }

        if (!item.CanPurchaseMoreThanOnce) _purchasedOnce.Add(item.ItemId);
        result = item.Kind == ShopItemDefinition.PurchaseKind.DeliveredItem
            ? $"Ordered {item.DisplayName}. Delivery is on its way!"
            : $"Purchased {item.DisplayName}!";
        return true;
    }

    [ContextMenu("Refresh Delivery Points")]
    public void RefreshDeliveryPoints()
    {
        deliveryPoints = new List<ShopDeliveryPoint>(FindObjectsByType<ShopDeliveryPoint>(FindObjectsSortMode.None));
    }
}
