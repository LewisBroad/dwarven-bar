using UnityEngine;

public class DwarfPerks : MonoBehaviour
{
    [Header("Floor Grip & Footwear")]
    [Tooltip("0 = normal slips, 0.5 = 50% chance/higher speed needed, 1.0 = completely immune to slipping.")]
    [Range(0f, 1f)]
    [SerializeField] private float slipResistance = 0f;

    [Tooltip("Multiplier applied to the minimum speed required to trigger a slip.")]
    [SerializeField] private float slipSpeedThresholdMultiplier = 1.0f;

    public float SlipResistance
    {
        get => slipResistance;
        set => slipResistance = Mathf.Clamp01(value);
    }

    public float SlipSpeedThresholdMultiplier
    {
        get => slipSpeedThresholdMultiplier;
        set => slipSpeedThresholdMultiplier = Mathf.Max(0.1f, value);
    }

    public bool IsSlipImmune => slipResistance >= 1.0f;
}