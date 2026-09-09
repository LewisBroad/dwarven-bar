using UnityEngine;

public class PuddleManager : MonoBehaviour
{
    public static PuddleManager Instance { get; private set; }

    [Header("Prefab & Spawning")]
    [SerializeField] private GameObject beerPuddlePrefab;
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private float mergeRadius = 1.0f;

    [Header("Volume Thresholds")]
    [Tooltip("Ignore tiny micro-droplets below this volume")]
    [SerializeField] private float minSpillThreshold = 0.03f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Spawns a puddle or merges into an existing nearby puddle.
    /// </summary>
    public void SpillBeer(Vector3 worldPosition, float pints)
    {
        if (pints < minSpillThreshold) return;

        // Snap to floor using floorLayer mask
        if (Physics.Raycast(worldPosition + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3.0f, floorLayer, QueryTriggerInteraction.Ignore))
        {
            Vector3 spawnPos = hit.point + (hit.normal * 0.003f);

            // 1. Check if an active (non-dying) puddle already exists nearby to expand
            Collider[] existing = Physics.OverlapSphere(spawnPos, mergeRadius, ~0, QueryTriggerInteraction.Collide);
            BeerPuddle bestPuddle = null;
            float closestDistSqr = float.MaxValue;

            foreach (var col in existing)
            {
                BeerPuddle p = col.GetComponent<BeerPuddle>();
                if (p != null && !p.IsBeingAbsorbed)
                {
                    float dSqr = (col.transform.position - spawnPos).sqrMagnitude;
                    if (dSqr < closestDistSqr)
                    {
                        closestDistSqr = dSqr;
                        bestPuddle = p;
                    }
                }
            }

            if (bestPuddle != null)
            {
                bestPuddle.AddBeer(pints);
                return;
            }

            // 2. Otherwise instantiate a fresh puddle aligned with floor normal
            if (beerPuddlePrefab != null)
            {
                // Align puddle surface flat against the hit floor normal
                Quaternion floorAlignRot = Quaternion.FromToRotation(Vector3.up, hit.normal);
                GameObject newPuddle = Instantiate(beerPuddlePrefab, spawnPos, floorAlignRot);

                if (newPuddle.TryGetComponent(out BeerPuddle puddle))
                {
                    // If you want pints to dictate the exact start volume rather than adding to the prefab default:
                    puddle.AddBeer(pints);
                }
            }
        }
    }
}