using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class DwarfController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 4.5f;
    [SerializeField] private float jumpHeight = 1.0f;
    [SerializeField] private float gravity = -18.0f;

    [Header("References")]
    [SerializeField] private FirstPersonLook playerCamera;
    [SerializeField] private Animator animator;
    [SerializeField] private DwarfGrabber grabber;

    [Header("Push Settings")]
    [SerializeField] private float pushPower = 1.2f; // Scales how easily dwarf pushes objects

    [Header("Loose Item Priority")]
    [Tooltip("Extra horizontal clearance kept between the character and loose rigidbody items.")]
    [SerializeField] private float itemSeparationPadding = 0.03f;

    private CharacterController _controller;
    private PlayerInputActions _inputActions;
    private Vector2 _moveInput;
    private Vector2 _lookInput;
    private Vector3 _velocity;
    private bool _isGrounded;
    private bool _isCurrentLookFromGamepad;
    private float _airTime;

    // Animator Hashes
    private static readonly int ForwardHash = Animator.StringToHash("Forward");
    private static readonly int StrafeHash = Animator.StringToHash("Strafe");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int TurnHash = Animator.StringToHash("Turn");
    private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
    private static readonly int VerticalVelocityHash = Animator.StringToHash("VerticalVelocity");
    private static readonly int IsCarryingHash = Animator.StringToHash("IsCarrying");
    private static readonly int IsFallingHash = Animator.StringToHash("IsFalling");


    public float SpeedMultiplier { get; set; } = 1f;
    public float CurrentHorizontalSpeed { get; private set; }

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _inputActions = new PlayerInputActions();

        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (grabber == null) grabber = GetComponentInChildren<DwarfGrabber>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnEnable()
    {
        _inputActions.Player.Enable();

        // Move
        _inputActions.Player.Move.performed += ctx => _moveInput = ctx.ReadValue<Vector2>();
        _inputActions.Player.Move.canceled += _ => _moveInput = Vector2.zero;

        // Look
        _inputActions.Player.Look.performed += OnLookPerformed;
        _inputActions.Player.Look.canceled += _ => _lookInput = Vector2.zero;

        // Jump
        _inputActions.Player.Jump.performed += _ => TryJump();
    }

    private void OnDisable()
    {
        _inputActions.Player.Disable();
    }

    private void OnLookPerformed(InputAction.CallbackContext context)
    {
        _lookInput = context.ReadValue<Vector2>();
        _isCurrentLookFromGamepad = context.control.device is Gamepad;
    }

    private void Update()
    {
        if (MagicOrderBookUI.IsOpen || (QuotaGameManager.Instance != null && QuotaGameManager.Instance.IsInputLocked))
        {
            _moveInput = Vector2.zero;
            _lookInput = Vector2.zero;
            CurrentHorizontalSpeed = 0f;
            return;
        }

        _isGrounded = _controller.isGrounded;

        if (_isGrounded)
        {
            _airTime = 0f;
            if (_velocity.y < 0)
            {
                _velocity.y = -2f;
            }
        }
        else
        {
            _airTime += Time.deltaTime;
        }

        // Camera Look
        if (playerCamera != null)
        {
            playerCamera.HandleLook(_lookInput, _isCurrentLookFromGamepad);
        }

        // Calculate Movement & Track Speed for Hazards
        Vector3 move = transform.right * _moveInput.x + transform.forward * _moveInput.y;
        float inputMagnitude = Mathf.Clamp01(_moveInput.magnitude);

        CurrentHorizontalSpeed = inputMagnitude * walkSpeed * SpeedMultiplier;
        _controller.Move(move * (walkSpeed * SpeedMultiplier * Time.deltaTime));

        // Gravity & Jump
        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);

        // Drive character model animations
        UpdateAnimations();
    }

    private void UpdateAnimations()
    {
        if (animator == null || !animator.enabled) return;

        animator.SetFloat(ForwardHash, _moveInput.y, 0.08f, Time.deltaTime);
        animator.SetFloat(StrafeHash, _moveInput.x, 0.08f, Time.deltaTime);
        animator.SetFloat(SpeedHash, CurrentHorizontalSpeed, 0.08f, Time.deltaTime);
        animator.SetFloat(TurnHash, _lookInput.x, 0.08f, Time.deltaTime);
        animator.SetFloat(VerticalVelocityHash, _velocity.y);
        animator.SetBool(IsGroundedHash, _isGrounded);

        // ONLY trigger true falling if airborne for > 0.22s AND actually descending rapidly
        // Stepping off a 30cm keg takes ~0.12s, so this completely filters out little step-downs
        bool isTrulyFalling = !_isGrounded && _airTime > 0.22f && _velocity.y < -3.5f;
        animator.SetBool(IsFallingHash, isTrulyFalling);

        bool carrying = grabber != null && grabber.HeldItem != null;
        animator.SetBool(IsCarryingHash, carrying);
    }

    private void TryJump()
    {
        if (_isGrounded)
        {
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        // Ignore static environment and kinematic/docked items
        if (body == null || body.isKinematic) return;

        // Loose items must yield to the character controller. Moving the item out
        // horizontally avoids the controller being squeezed downward into the floor.
        GivePlayerPriorityOverItem(body, hit.collider);

        // --- 1. PREVENT PINCHING / ROCKETING WHEN JUMPING ON TOP ---
        if (hit.point.y < transform.position.y + 0.15f && hit.moveDirection.y < -0.1f)
        {
            if (body.linearVelocity.y < 0f)
            {
                Vector3 vel = body.linearVelocity;
                vel.y = 0f;
                body.linearVelocity = vel;
            }

            Vector3 pushItemAway = hit.collider.bounds.center - transform.position;
            pushItemAway.y = 0f;
            if (pushItemAway.sqrMagnitude < 0.001f) pushItemAway = transform.forward;
            body.AddForce(pushItemAway.normalized * pushPower, ForceMode.Impulse);
            return;
        }

        // --- 2. RELIABLE HORIZONTAL PUSHING ---
        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (pushDir.sqrMagnitude < 0.001f) return;
        pushDir.Normalize();

        float pushImpulse = pushPower * (walkSpeed / Mathf.Max(1f, body.mass * 0.2f));
        body.AddForce(pushDir * pushImpulse, ForceMode.Impulse);

        // --- 3. HARD VELOCITY CLAMP ---
        Vector3 horizLinearVel = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
        float maxAllowedSpeed = walkSpeed * 0.8f;

        if (horizLinearVel.magnitude > maxAllowedSpeed)
        {
            horizLinearVel = horizLinearVel.normalized * maxAllowedSpeed;
            body.linearVelocity = new Vector3(horizLinearVel.x, body.linearVelocity.y, horizLinearVel.z);
        }

        if (body.angularVelocity.magnitude > 3.0f)
        {
            body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, 3.0f);
        }
    }

    private void GivePlayerPriorityOverItem(Rigidbody itemBody, Collider itemCollider)
    {
        Vector3 itemAwayFromPlayer = itemBody.worldCenterOfMass - transform.position;
        itemAwayFromPlayer.y = 0f;
        if (itemAwayFromPlayer.sqrMagnitude < 0.001f) itemAwayFromPlayer = transform.forward;
        itemAwayFromPlayer.Normalize();

        // ComputePenetration gives the direction that would move the controller out
        // of the item. We instead move the item sideways, keeping the floor as the
        // player's reliable support surface.
        if (Physics.ComputePenetration(
                _controller, transform.position, transform.rotation,
                itemCollider, itemCollider.transform.position, itemCollider.transform.rotation,
                out _, out float penetrationDepth))
        {
            itemBody.position += itemAwayFromPlayer * (penetrationDepth + itemSeparationPadding);
        }

        // Stop an item that is already travelling into the player from shoving the
        // controller around on the following physics step.
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(itemBody.linearVelocity, Vector3.up);
        float velocityIntoPlayer = Vector3.Dot(horizontalVelocity, -itemAwayFromPlayer);
        if (velocityIntoPlayer > 0f)
        {
            itemBody.linearVelocity += itemAwayFromPlayer * velocityIntoPlayer;
        }
    }
}
