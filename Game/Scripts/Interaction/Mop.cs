using UnityEngine;

public class Mop : PhysicsItem, IMappableTool, IInteractable
{
    [Header("Mop Cleaning Stats")]
    [SerializeField] private float cleanRadius = 1.5f;
    [SerializeField] private float cleanPower = 0.8f;
    [SerializeField] private LayerMask spillZoneLayer;
    [SerializeField] private Animator mopAnimator;
    [SerializeField] private string swingAnimTrigger = "Swing";

    [Header("First-Person Offsets (Arms/Camera View)")]
    [SerializeField] private Vector3 fpLocalPosition;
    [SerializeField] private Vector3 fpLocalRotation;

    [Header("Third-Person Offsets (Hand Bone / Multiplayer)")]
    [SerializeField] private Vector3 tpLocalPosition;
    [SerializeField] private Vector3 tpLocalRotation;

    [Header("Layer Setup")]
    [SerializeField] private string droppedLayerName = "Interactable";

    private bool _isEquipped = false;
    private Transform _equippedSocket;
    private Vector3 _equippedLocalPosition;
    private Quaternion _equippedLocalRotation;
    private Rigidbody _rb;
    private Collider _col;
    private InteractableHighlight _highlighter;
    private FloatingPromptAnchor _promptAnchor;

    protected override void Awake()
    {
        base.Awake();
        SetMass(0.8f);
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        _highlighter = GetComponent<InteractableHighlight>();
        if (_highlighter == null) _highlighter = gameObject.AddComponent<InteractableHighlight>();

        _promptAnchor = GetComponent<FloatingPromptAnchor>();
        if (_promptAnchor == null) _promptAnchor = gameObject.AddComponent<FloatingPromptAnchor>();
    }

    public void OnEquip(Transform socket, int targetLayer, bool isFirstPerson, DwarfGrabber grabber)
    {
        _isEquipped = true;
        if (_highlighter != null) _highlighter.SetHighlight(false);
        if (_promptAnchor != null) _promptAnchor.Hide();
        _rb.isKinematic = true;
        _col.enabled = false;

        // A NetworkObject cannot safely be parented to a regular hand/camera bone.
        // Keep it unparented and follow the socket in world space, just like a
        // customer-held glass. This avoids Netcode resetting it to world origin.
        _equippedSocket = socket;
        _equippedLocalPosition = isFirstPerson ? fpLocalPosition : tpLocalPosition;
        _equippedLocalRotation = Quaternion.Euler(isFirstPerson ? fpLocalRotation : tpLocalRotation);
        transform.SetParent(null, true);
        SnapToEquippedSocket();

        // Apply layer recursively so visibility matches owner settings
        SetLayerRecursively(gameObject, targetLayer);
    }

    public void OnUnequip()
    {
        _isEquipped = false;
        _equippedSocket = null;
        _rb.isKinematic = false;
        _col.enabled = true;

        // Set to "Interactable" so the player's raycast can detect and pick it up again
        int interactableLayer = LayerMask.NameToLayer(droppedLayerName);
        if (interactableLayer != -1)
        {
            SetLayerRecursively(gameObject, interactableLayer);
        }
        else
        {
            Debug.LogWarning($"[Mop] Layer '{droppedLayerName}' not found in project settings! Defaulting to 0.");
            SetLayerRecursively(gameObject, 0);
        }

        // Toss forward slightly when dropped
        _rb.linearVelocity = transform.forward * 2f + Vector3.up * 2f;
    }

    private void LateUpdate()
    {
        if (_isEquipped) SnapToEquippedSocket();
    }

    private void SnapToEquippedSocket()
    {
        if (_equippedSocket == null) return;
        transform.SetPositionAndRotation(
            _equippedSocket.TransformPoint(_equippedLocalPosition),
            _equippedSocket.rotation * _equippedLocalRotation);
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    public void UseTool()
    {
        if (!_isEquipped) return;

        if (mopAnimator != null)
        {
            mopAnimator.SetTrigger(swingAnimTrigger);
        }

        Vector3 checkCenter = transform.position + (transform.forward * 0.6f);
        Collider[] hits = Physics.OverlapSphere(checkCenter, cleanRadius, spillZoneLayer, QueryTriggerInteraction.Collide);

        foreach (var hit in hits)
        {
            if (hit.TryGetComponent(out BeerPuddle puddle))
            {
                puddle.CleanPuddle(cleanPower);
            }
        }
    }

    public string GetInteractionPrompt()
    {
        return _isEquipped ? string.Empty : "[Left Click] to Equip Mop";
    }

    // Mop equipment is intentionally handled by DwarfGrabber's Attack binding.
    // Implementing IInteractable here makes it discoverable by the hover/prompt UI.
    public void Interact(DwarfInteractor interactor) { }

    public void OnHoverEnter(string boundKeyName)
    {
        if (_isEquipped) return;

        if (_highlighter != null) _highlighter.SetHighlight(true);
        if (_promptAnchor != null) _promptAnchor.Show(GetInteractionPrompt());
    }

    public void OnHoverExit()
    {
        if (_highlighter != null) _highlighter.SetHighlight(false);
        if (_promptAnchor != null) _promptAnchor.Hide();
    }
}
