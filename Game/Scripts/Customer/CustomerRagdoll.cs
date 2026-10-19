using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class CustomerRagdoll : MonoBehaviour
{
    [SerializeField] private Transform hipsBone;
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField, Min(0.1f)] private float minimumDuration = 1.5f;
    [Tooltip("Enable only after this prefab has a complete, tested Unity Ragdoll Wizard setup. Disabled uses a safe stumble/recovery instead.")]
    [SerializeField] private bool usePhysicalRagdoll;

    private Rigidbody[] _bodies;
    private Collider[] _ragdollColliders;
    private bool _isRagdolled;
    private bool _hasUsableRagdoll;
    private Coroutine _ragdollRoutine;
    public bool IsRagdolled => _isRagdolled;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (hipsBone == null && animator != null && animator.isHuman)
            hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        _bodies = hipsBone != null ? hipsBone.GetComponentsInChildren<Rigidbody>(true) : System.Array.Empty<Rigidbody>();
        _ragdollColliders = hipsBone != null ? hipsBone.GetComponentsInChildren<Collider>(true) : System.Array.Empty<Collider>();
        _hasUsableRagdoll = usePhysicalRagdoll
            && _bodies.Length > 1
            && hipsBone != null
            && hipsBone.GetComponentsInChildren<Joint>(true).Length > 0;
        SetRagdollState(false);
    }

    public void TriggerRagdoll(Vector3 force, float duration, System.Action onRecovered)
    {
        // A fresh bat hit while down should add another impulse and reset recovery,
        // rather than being ignored until the customer has already stood back up.
        if (_ragdollRoutine != null) StopCoroutine(_ragdollRoutine);
        _ragdollRoutine = StartCoroutine(RagdollRoutine(force, duration, onRecovered));
    }

    private System.Collections.IEnumerator RagdollRoutine(Vector3 force, float duration, System.Action onRecovered)
    {
        _isRagdolled = true;
        bool hasRig = _hasUsableRagdoll;
        if (hasRig)
        {
            SetRagdollState(true);
            foreach (Rigidbody body in _bodies)
            {
                body.linearVelocity = force * 0.35f;
                body.AddForce(force + Vector3.up * 1.5f, ForceMode.Impulse);
            }
        }
        else
        {
            if (agent != null) agent.isStopped = true;
            if (animator != null) animator.enabled = false;
        }

        yield return new WaitForSeconds(Mathf.Max(minimumDuration, duration));
        if (hasRig && hipsBone != null && Physics.Raycast(hipsBone.position + Vector3.up, Vector3.down, out RaycastHit hit, 3f))
            transform.position = hit.point + Vector3.up * 0.05f;

        SetRagdollState(false);
        _isRagdolled = false;
        _ragdollRoutine = null;
        onRecovered?.Invoke();
    }

    private void SetRagdollState(bool active)
    {
        if (agent != null)
        {
            agent.enabled = !active;
            if (!active && agent.enabled) agent.isStopped = false;
        }
        if (animator != null) animator.enabled = !active;
        foreach (Rigidbody body in _bodies) { body.isKinematic = !active; body.useGravity = active; }
        foreach (Collider collider in _ragdollColliders) collider.enabled = active;
    }
}
