using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class ParticleDamageRelay : MonoBehaviour
{
    [SerializeField] private BeerKeg parentKeg;

    private void Awake()
    {
        if (parentKeg == null)
        {
            parentKeg = GetComponentInParent<BeerKeg>();
        }
    }

    private void OnParticleCollision(GameObject other)
    {
        if (parentKeg != null)
        {
            parentKeg.HandleParticleCollision(other);
        }
    }
}