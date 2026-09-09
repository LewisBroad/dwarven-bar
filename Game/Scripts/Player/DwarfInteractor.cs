using UnityEngine;
using UnityEngine.InputSystem;

public class DwarfInteractor : MonoBehaviour
{
    [Header("Interact Config")]
    [SerializeField] private float interactRange = 2.6f;
    [SerializeField] private LayerMask interactLayer;

    private PlayerInputActions _inputActions;
    private IInteractable _currentHovered;
    private string _interactBindingDisplay;

    public DwarfController Dwarf { get; private set; }

    private void Awake()
    {
        _inputActions = new PlayerInputActions();
        Dwarf = GetComponentInParent<DwarfController>();

        // Resolves the current keyboard/gamepad binding string (e.g. "[E]" or "(X)")
        _interactBindingDisplay = _inputActions.Player.Interact.GetBindingDisplayString();
    }

    private void OnEnable()
    {
        _inputActions.Player.Enable();
        _inputActions.Player.Interact.performed += ctx => TryInteract();
    }

    private void OnDisable()
    {
        _inputActions.Player.Disable();
        ClearCurrentHover();
    }

    private void Update()
    {
        CheckForInteractable();
    }

    private void CheckForInteractable()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactLayer))
        {
            if (hit.collider.TryGetComponent(out IInteractable interactable) ||
               (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.TryGetComponent(out interactable)))
            {
                if (_currentHovered != interactable)
                {
                    ClearCurrentHover();
                    _currentHovered = interactable;
                    SetHoveredPromptCamera(_currentHovered);
                    _currentHovered.OnHoverEnter(_interactBindingDisplay);
                }
                return;
            }
        }

        ClearCurrentHover();
    }

    private void UpdatePromptUI()
    {
        if (_currentHovered != null && InteractionPromptUI.Instance != null)
        {
            // Replaces the placeholder {KEY} in prompts like "{KEY} to Pour"
            string prompt = _currentHovered.GetInteractionPrompt();
            prompt = prompt.Replace("{KEY}", $"[{_interactBindingDisplay}]");
            InteractionPromptUI.Instance.ShowPrompt(prompt);
        }
    }

    private void SetHoveredPromptCamera(IInteractable interactable)
    {
        if (!(interactable is Component component)) return;

        FloatingPromptAnchor promptAnchor = component.GetComponent<FloatingPromptAnchor>();
        Camera viewerCamera = GetComponent<Camera>();
        if (promptAnchor != null && viewerCamera != null)
        {
            promptAnchor.SetViewerCamera(viewerCamera);
        }
    }

    private void ClearCurrentHover()
    {
        if (_currentHovered != null)
        {
            _currentHovered.OnHoverExit();
            _currentHovered = null;

            if (InteractionPromptUI.Instance != null)
            {
                InteractionPromptUI.Instance.HidePrompt();
            }
        }
    }

    private void TryInteract()
    {
        if (_currentHovered != null)
        {
            _currentHovered.Interact(this);
            // Immediately update the prompt text so it switches to "{KEY} to Close Tap"
            _currentHovered.OnHoverEnter(_interactBindingDisplay);
        }
    }
}
