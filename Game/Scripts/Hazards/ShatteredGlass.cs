using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShatteredGlass : MonoBehaviour
{
    [Header("Audio Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] shatterClips;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.85f, 1.25f);

    [Header("Dynamic Impulse Tuning")]
    [Tooltip("How much the shards carry the glass's pre-impact momentum")]
    [Range(0f, 1f)] [SerializeField] private float momentumPreservation = 0.55f;

    [Tooltip("How hard shards bounce backward off the struck surface")]
    [SerializeField] private float reboundForce = 2.4f;

    [Tooltip("Cone spray spread angle in degrees (0 = laser beam, 90 = hemisphere)")]
    [Range(15f, 85f)] [SerializeField] private float scatterConeAngle = 45f;

    [Tooltip("Additional outward radial burst from the exact crack center")]
    [SerializeField] private float crackBurstForce = 1.8f;

    [Header("Lifecycle & Cleanup")]
    [SerializeField] private float settleDelay = 3.5f;
    [SerializeField] private float shrinkDuration = 0.8f;

    private readonly List<Rigidbody> _shardRbs = new List<Rigidbody>();
    private readonly List<Transform> _shardTransforms = new List<Transform>();

    private void Awake()
    {
        Rigidbody[] rbs = GetComponentsInChildren<Rigidbody>();
        foreach (var rb in rbs)
        {
            _shardRbs.Add(rb);
            _shardTransforms.Add(rb.transform);
        }
    }

    /// <summary>
    /// Initializes shards with dynamic momentum, surface deflection, and impact point burst.
    /// </summary>
    /// <param name="incomingVelocity">Velocity of the glass right before impact.</param>
    /// <param name="impactPoint">World point where the glass struck.</param>
    /// <param name="surfaceNormal">The normal of the surface struck (pointing outward from wall/floor).</param>
    public void Initialize(Vector3 incomingVelocity, Vector3 impactPoint, Vector3 surfaceNormal)
    {
        // 1. Play impact sound (volume/pitch scales slightly with violent throws)
        if (audioSource != null && shatterClips != null && shatterClips.Length > 0)
        {
            AudioClip clip = shatterClips[Random.Range(0, shatterClips.Length)];
            float speedFactor = Mathf.InverseLerp(4f, 14f, incomingVelocity.magnitude);
            audioSource.pitch = Mathf.Lerp(pitchRange.y, pitchRange.x, speedFactor); // Harder hits sound deeper/crunchier
            audioSource.PlayOneShot(clip, Mathf.Lerp(0.7f, 1.0f, speedFactor));
        }

        // 2. Calculate primary spray direction:
        // Reflect incoming motion off the surface (like a ricochet), blended with pure surface bounce
        Vector3 reflectDir = Vector3.Reflect(incomingVelocity.normalized, surfaceNormal);
        Vector3 primarySprayDir = Vector3.Slerp(surfaceNormal, reflectDir, 0.5f).normalized;

        float impactSpeed = incomingVelocity.magnitude;

        foreach (var rb in _shardRbs)
        {
            if (rb == null) continue;

            // A: Tangential forward throw momentum
            Vector3 forwardCarry = incomingVelocity * momentumPreservation;

            // B: Rebound spray outward from the wall/floor within a conical spread
            Vector3 randomConeDir = DirInCone(primarySprayDir, scatterConeAngle);
            Vector3 bounceKick = randomConeDir * (reboundForce + (impactSpeed * 0.25f));

            // C: Local radial explosion away from the contact point
            Vector3 fromContact = (rb.worldCenterOfMass - impactPoint).normalized;
            Vector3 localBurst = fromContact * crackBurstForce;

            // Combined dynamic velocity
            rb.linearVelocity = forwardCarry + bounceKick + localBurst;

            // Spin shards around the axis of collision
            Vector3 tumbleAxis = Vector3.Cross(incomingVelocity.normalized, surfaceNormal);
            if (tumbleAxis.sqrMagnitude < 0.01f) tumbleAxis = Random.insideUnitSphere;
            rb.angularVelocity = (tumbleAxis.normalized + Random.insideUnitSphere * 0.4f) * (impactSpeed * 3.5f);
        }

        StartCoroutine(SettleAndCleanupRoutine());
    }

    private Vector3 DirInCone(Vector3 forward, float maxAngle)
    {
        Quaternion randomRot = Quaternion.AngleAxis(Random.Range(0f, maxAngle), Random.onUnitSphere);
        return (randomRot * forward).normalized;
    }

    private IEnumerator SettleAndCleanupRoutine()
    {
        yield return new WaitForSeconds(settleDelay);

        // Disable physics solver for all shards to free PhysX ticks
        for (int i = 0; i < _shardRbs.Count; i++)
        {
            if (_shardRbs[i] != null)
            {
                _shardRbs[i].isKinematic = true;
                if (_shardRbs[i].TryGetComponent(out Collider col))
                {
                    col.enabled = false;
                }
            }
        }

        // Smooth shrink-out
        float elapsed = 0f;
        Vector3[] baseScales = new Vector3[_shardTransforms.Count];
        for (int i = 0; i < _shardTransforms.Count; i++)
        {
            if (_shardTransforms[i] != null) baseScales[i] = _shardTransforms[i].localScale;
        }

        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / shrinkDuration;
            float scale = Mathf.Lerp(1f, 0f, t * t);

            for (int i = 0; i < _shardTransforms.Count; i++)
            {
                if (_shardTransforms[i] != null) _shardTransforms[i].localScale = baseScales[i] * scale;
            }
            yield return null;
        }

        Destroy(gameObject);
    }
}