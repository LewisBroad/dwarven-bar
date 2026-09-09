using UnityEngine;

/// <summary>Add to a physical snack/food item so a customer can validate it at a serving spot.</summary>
public class ServeableItem : PhysicsItem
{
    [SerializeField] private string itemId = "Snack";
    [Range(0f, 1f)] [SerializeField] private float quality = 1f;

    public string ItemId => itemId;
    public float Quality => quality;
}
