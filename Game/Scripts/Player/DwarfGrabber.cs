using UnityEngine;
using UnityEngine.InputSystem;

public class DwarfGrabber : MonoBehaviour
{
    [Header("Grab Settings")]
    [SerializeField] private float grabRange = 2.8f;
    [SerializeField] private LayerMask interactableLayer;
    
    [Header("Equipment Sockets")]
    [Tooltip("Socket parented to the camera/arms for local first-person view")]
    [SerializeField] private Transform firstPersonSocket;
    [Tooltip("Socket parented to the character skeleton's hand bone for third-person / multiplayer view")]
    [SerializeField] private Transform thirdPersonSocket;

    [Header("Layer Settings")]
    [SerializeField] private string firstPersonLayerName = "FirstPersonOnly";
    [SerializeField] private string defaultLayerName = "Default";
    [SerializeField] private bool isLocalPlayer = true; // Set to false if this is a remote network avatar

    private PlayerInputActions _inputActions;
    private PhysicsItem _currentHeldItem;
    private IMappableTool _currentEquippedTool;
    
    private Collider _playerCollider;
    private DwarfController _dwarfController;
    private float _scrollInput;

    public DwarfController Dwarf => _dwarfController;
    public PhysicsItem HeldItem => _currentHeldItem;
    public bool HasItem => _currentHeldItem != null || _currentEquippedTool != null;

    private void Awake()
    {
        _inputActions = new PlayerInputActions();
        _playerCollider = GetComponentInParent<Collider>();
        _dwarfController = GetComponentInParent<DwarfController>();
    }

    /// <label>Call from network manager for remote player instances</label>
    public void InitializeAsRemotePlayer()
    {
        isLocalPlayer = false;
    }

    private void OnEnable()
    {
        _inputActions.Player.Enable();
        
        // Existing physics grab bindings
        _inputActions.Player.Grab.performed += ctx => TryGrabPhysicsItem();
        _inputActions.Player.Grab.canceled += ctx => ReleasePhysicsItem();
        _inputActions.Player.AdjustGrabDistance.performed += ctx => _scrollInput = ctx.ReadValue<float>();
        _inputActions.Player.AdjustGrabDistance.canceled += _ => _scrollInput = 0f;

        // Tool bindings (Attack = Left Click, Drop = R)
        _inputActions.Player.Attack.performed += ctx => OnAttackInput();
        _inputActions.Player.Drop.performed += ctx => OnDropInput();
    }

    private void OnDisable()
    {
        _inputActions.Player.Disable();
    }

    private void Update()
    {
        if (_currentHeldItem != null)
        {
            if (Mathf.Abs(_scrollInput) > 0.001f)
            {
                _currentHeldItem.AdjustDistance(this, _scrollInput);
            }

            if (_dwarfController != null)
            {
                float sharedMass = _currentHeldItem.ItemMass / Mathf.Max(1, _currentHeldItem.GrabberCount);
                float speedMod = Mathf.Lerp(1.0f, 0.4f, Mathf.Clamp01((sharedMass - 10f) / 40f));
                _dwarfController.SpeedMultiplier = speedMod;
            }
        }
    }

    private void TryGrabPhysicsItem()
    {
        // Cannot grab physics items if already holding a tool or another item
        if (HasItem) return;

        Ray ray = new Ray(transform.position, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, grabRange, interactableLayer))
        {
            if (hit.collider.TryGetComponent(out PhysicsItem item))
            {
                if (item.OnGrab(transform, hit.point, _playerCollider, this))
                {
                    _currentHeldItem = item;
                }
            }
        }
    }

    private void ReleasePhysicsItem()
    {
        if (_currentHeldItem == null) return;
        ForceReleaseItem(_currentHeldItem);
    }

    private void OnAttackInput()
    {
        if (_currentEquippedTool != null)
        {
            // Use the equipped tool (e.g. swing mop)
            _currentEquippedTool.UseTool();
        }
        else if (!HasItem)
        {
            // Try to pick up an equippable tool from the floor
            Ray ray = new Ray(transform.position, transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, grabRange, interactableLayer))
            {
                if (hit.collider.TryGetComponent(out IMappableTool toolItem))
                {
                    EquipTool(toolItem);
                }
            }
        }
    }

    private void OnDropInput()
    {
        if (_currentEquippedTool != null)
        {
            UnequipTool();
        }
    }

private void EquipTool(IMappableTool tool)
    {
        _currentEquippedTool = tool;

        // Determine if we are using First Person or Third Person socket
        bool useFP = isLocalPlayer && firstPersonSocket != null;
        Transform targetSocket = useFP ? firstPersonSocket : thirdPersonSocket;
        if (targetSocket == null) targetSocket = thirdPersonSocket != null ? thirdPersonSocket : transform;

        // If local player uses third-person socket (e.g. spectating or full body visible), match "LocalPlayerBody" layer
        // If remote player, use "Default" layer so others can see it.
        string layerName = useFP ? firstPersonLayerName : (isLocalPlayer ? "LocalPlayerBody" : "Default");
        int targetLayer = LayerMask.NameToLayer(layerName);

        tool.OnEquip(targetSocket, targetLayer, useFP, this);
    }

    private void UnequipTool()
    {
        if (_currentEquippedTool != null)
        {
            _currentEquippedTool.OnUnequip();
            _currentEquippedTool = null;
        }
    }

    public void ForceReleaseItem(PhysicsItem item)
    {
        if (_currentHeldItem != item) return;

        _currentHeldItem.OnRelease(_playerCollider, this);
        _currentHeldItem = null;

        if (_dwarfController != null)
        {
            _dwarfController.SpeedMultiplier = 1.0f;
        }
    }

    public void ForceDrop()
    {
        if (_currentHeldItem != null)
        {
            _currentHeldItem.OnRelease(_playerCollider, this);
            _currentHeldItem = null;
        }

        if (_currentEquippedTool != null)
        {
            UnequipTool();
        }

        if (_dwarfController != null)
        {
            _dwarfController.SpeedMultiplier = 1.0f;
        }
    }
}

public interface IMappableTool
{
    void UseTool();
    void OnEquip(Transform socket, int targetLayer, bool isFirstPerson, DwarfGrabber grabber);
    void OnUnequip();
}