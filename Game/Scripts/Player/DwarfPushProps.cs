using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class DwarfPushProps : MonoBehaviour
{
    [Header("Push Settings")]
    [Tooltip("Force multiplier applied to loose props when walking into them")]
    [SerializeField] private float pushPower = 2.5f;

    [Tooltip("Maximum mass the dwarf can kick around effortlessly without slowing down")]
    [SerializeField] private float maxPushMass = 10f;

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        // No rigidbody, or kinematic (like static furniture) -> standard collision
        if (body == null || body.isKinematic) return;

        // 1. Don't push things we are standing on directly from above
        // If the hit normal points straight up, we stepped on top of it.
        // Nudge it outward horizontally so we slide off rather than crush into it.
        if (hit.moveDirection.y < -0.3f)
        {
            Vector3 slipDir = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            body.AddForce(slipDir * 2f, ForceMode.Impulse);
            return;
        }

        // Only push props within dwarf kick weight
        if (body.mass > maxPushMass) return;

        // 2. Calculate pure horizontal push direction from the dwarf's velocity
        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (pushDir.sqrMagnitude < 0.001f) return;

        // Apply impulse to the prop at the impact contact point
        body.AddForceAtPosition(pushDir * pushPower, hit.point, ForceMode.Impulse);
    }
}