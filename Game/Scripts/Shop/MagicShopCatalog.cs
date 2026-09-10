using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Dwarf Bar/Magic Shop Catalog", fileName = "MagicShopCatalog")]
public class MagicShopCatalog : ScriptableObject
{
    [SerializeField] private List<ShopItemDefinition> items = new();
    public IReadOnlyList<ShopItemDefinition> Items => items;
}
