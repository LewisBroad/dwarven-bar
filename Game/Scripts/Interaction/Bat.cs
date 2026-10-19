using System.Collections.Generic;
using UnityEngine;

/// <summary>A melee tool using the same equip, prompt, and safe socket-follow system as Mop.</summary>
public class Bat : PhysicsItem, IMappableTool, IInteractable
{
    [Header("Bat Hit")]
    [SerializeField] private Transform hitOrigin;
    [SerializeField, Min(0.05f)] private float hitRadius = 0.45f;
    [SerializeField, Min(0.05f)] private float hitReach = 1.15f;
    [SerializeField] private LayerMask hitLayers = ~0;
    [SerializeField, Min(0.1f)] private float ragdollForce = 12f;
    [SerializeField, Min(0.1f)] private float ragdollDuration = 2f;
    [SerializeField, Min(0f)] private float swingCooldown = 0.45f;
    [SerializeField] private Animator batAnimator;
    [SerializeField] private string swingAnimTrigger = "Swing";
    [SerializeField] private AudioSource hitAudio;

    [Header("First-Person Offsets")]
    [SerializeField] private Vector3 fpLocalPosition;
    [SerializeField] private Vector3 fpLocalRotation;
    [Header("Third-Person Offsets")]
    [SerializeField] private Vector3 tpLocalPosition;
    [SerializeField] private Vector3 tpLocalRotation;
    [Header("Layer Setup")]
    [SerializeField] private string droppedLayerName = "Interactable";

    private bool _isEquipped;
    private Transform _equippedSocket;
    private Vector3 _equippedLocalPosition;
    private Quaternion _equippedLocalRotation;
    private Rigidbody _rb;
    private Collider _col;
    private DwarfGrabber _owner;
    private float _nextSwingTime;
    private InteractableHighlight _highlighter;
    private FloatingPromptAnchor _promptAnchor;

    protected override void Awake()
    {
        base.Awake();
        SetMass(1.1f);
        _rb = Rb;
        _col = Col;
        _highlighter = GetComponent<InteractableHighlight>() ?? gameObject.AddComponent<InteractableHighlight>();
        _promptAnchor = GetComponent<FloatingPromptAnchor>() ?? gameObject.AddComponent<FloatingPromptAnchor>();
    }

    public void OnEquip(Transform socket, int targetLayer, bool isFirstPerson, DwarfGrabber grabber)
    {
        _isEquipped = true;
        _owner = grabber;
        _highlighter.SetHighlight(false);
        _promptAnchor.Hide();
        _rb.isKinematic = true;
        _col.enabled = false;
        _equippedSocket = socket;
        _equippedLocalPosition = isFirstPerson ? fpLocalPosition : tpLocalPosition;
        _equippedLocalRotation = Quaternion.Euler(isFirstPerson ? fpLocalRotation : tpLocalRotation);
        transform.SetParent(null, true);
        SnapToEquippedSocket();
        SetLayerRecursively(gameObject, targetLayer);
    }

    public void OnUnequip()
    {
        _isEquipped = false;
        _equippedSocket = null;
        _owner = null;
        _rb.isKinematic = false;
        _col.enabled = true;
        int layer = LayerMask.NameToLayer(droppedLayerName);
        SetLayerRecursively(gameObject, layer >= 0 ? layer : 0);
        _rb.linearVelocity = transform.forward * 2f + Vector3.up * 2f;
    }

    private void LateUpdate()
    {
        if (_isEquipped) SnapToEquippedSocket();
    }

    public void UseTool()
    {
        if (!_isEquipped || Time.time < _nextSwingTime) return;
        _nextSwingTime = Time.time + swingCooldown;
        if (batAnimator != null) batAnimator.SetTrigger(swingAnimTrigger);

        Transform origin = hitOrigin != null ? hitOrigin : transform;
        Vector3 center = origin.position + origin.forward * hitReach;
        HashSet<Component> hitTargets = new();
        foreach (Collider hit in Physics.OverlapSphere(center, hitRadius, hitLayers, QueryTriggerInteraction.Collide))
        {
            if (_owner != null && hit.transform.IsChildOf(_owner.transform.root)) continue;

            CustomerOrder customer = hit.GetComponentInParent<CustomerOrder>();
            if (customer != null)
            {
                if (hitTargets.Add(customer)) HitCustomer(customer, origin.forward);
                continue;
            }

            DwarfRagdoll dwarf = hit.GetComponentInParent<DwarfRagdoll>();
            if (dwarf != null && hitTargets.Add(dwarf))
            {
                dwarf.TriggerRagdoll(BuildImpact(origin.forward), ragdollDuration);
                PlayHitAudio();
            }
        }
    }

    private void HitCustomer(CustomerOrder customer, Vector3 direction)
    {
        customer.HitByBat(BuildImpact(direction), ragdollDuration);
        PlayHitAudio();
    }

    private Vector3 BuildImpact(Vector3 direction) => direction.normalized * ragdollForce + Vector3.up * (ragdollForce * 0.16f);
    private void PlayHitAudio() { if (hitAudio != null) hitAudio.Play(); }

    private void SnapToEquippedSocket()
    {
        if (_equippedSocket != null)
            transform.SetPositionAndRotation(_equippedSocket.TransformPoint(_equippedLocalPosition), _equippedSocket.rotation * _equippedLocalRotation);
    }

    private void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform) SetLayerRecursively(child.gameObject, layer);
    }

    public string GetInteractionPrompt() => _isEquipped ? string.Empty : "[Left Click] to Equip Bat";
    public void Interact(DwarfInteractor interactor) { }
    public void OnHoverEnter(string key) { if (!_isEquipped) { _highlighter.SetHighlight(true); _promptAnchor.Show(GetInteractionPrompt()); } }
    public void OnHoverExit() { _highlighter.SetHighlight(false); _promptAnchor.Hide(); }

    private void OnDrawGizmosSelected()
    {
        Transform origin = hitOrigin != null ? hitOrigin : transform;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(origin.position + origin.forward * hitReach, hitRadius);
    }
}
