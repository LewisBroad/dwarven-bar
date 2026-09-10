using UnityEngine;

[CreateAssetMenu(menuName = "Dwarf Bar/Shop Item", fileName = "ShopItem_")]
public class ShopItemDefinition : ScriptableObject
{
    public enum PurchaseKind { DeliveredItem, Upgrade }

    [Header("Display")]
    [SerializeField] private string itemId;
    [SerializeField] private string displayName = "New Item";
    [TextArea] [SerializeField] private string description;
    [SerializeField] private Sprite icon;
    [SerializeField, Min(0)] private int price;

    [Header("Purchase Result")]
    [SerializeField] private PurchaseKind purchaseKind;
    [SerializeField] private GameObject deliveryPrefab;
    [Tooltip("Must match a Delivery Point ID for delivered items.")]
    [SerializeField] private string deliveryPointId;
    [Tooltip("Must match an Upgrade Receiver ID for upgrades.")]
    [SerializeField] private string upgradeId;
    [SerializeField] private bool canPurchaseMoreThanOnce = true;

    public string ItemId => string.IsNullOrWhiteSpace(itemId) ? name : itemId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public int Price => price;
    public PurchaseKind Kind => purchaseKind;
    public GameObject DeliveryPrefab => deliveryPrefab;
    public string DeliveryPointId => deliveryPointId;
    public string UpgradeId => upgradeId;
    public bool CanPurchaseMoreThanOnce => canPurchaseMoreThanOnce;
}
