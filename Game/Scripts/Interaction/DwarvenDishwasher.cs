using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A fantasy "steam-rune washer": load dirty glasses into its rack, then interact
/// to seal the lid and clean them. It protects glasses only during the wash cycle.
/// </summary>
[RequireComponent(typeof(Collider), typeof(NetworkObject))]
public class DwarvenDishwasher : NetworkBehaviour, IInteractable
{
    [Header("Rack")]
    [SerializeField] private Transform[] glassSlots;
    [SerializeField] private Transform lidVisual;
    [SerializeField] private Vector3 openLidRotation = Vector3.zero;
    [SerializeField] private Vector3 closedLidRotation = new(-80f, 0f, 0f);

    [Header("Wash Cycle")]
    [SerializeField, Min(0.1f)] private float washDuration = 8f;
    [SerializeField] private ParticleSystem steamVfx;
    [SerializeField] private AudioSource washAudio;

    private readonly List<PintGlass> _loadedGlasses = new();
    private readonly NetworkVariable<bool> _networkWashing = new();
    private bool _offlineWashing;
    private FloatingPromptAnchor _prompt;
    private InteractableHighlight _highlight;

    public bool IsWashing => IsSpawned ? _networkWashing.Value : _offlineWashing;
    private bool IsWashAuthority => !IsSpawned || IsServer;
    private int DirtyGlassCount => _loadedGlasses.FindAll(glass => glass != null && !glass.IsClean).Count;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        _prompt = GetComponent<FloatingPromptAnchor>();
        if (_prompt == null) _prompt = gameObject.AddComponent<FloatingPromptAnchor>();
        _highlight = GetComponent<InteractableHighlight>();
        if (_highlight == null) _highlight = gameObject.AddComponent<InteractableHighlight>();
        UpdateVisuals();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkWashing.OnValueChanged += OnWashingChanged;
        UpdateVisuals();
    }

    public override void OnNetworkDespawn()
    {
        _networkWashing.OnValueChanged -= OnWashingChanged;
        base.OnNetworkDespawn();
    }

    private void OnTriggerEnter(Collider other) => TryLoad(other.GetComponentInParent<PintGlass>());
    private void OnTriggerStay(Collider other) => TryLoad(other.GetComponentInParent<PintGlass>());

    public void Interact(DwarfInteractor interactor)
    {
        if (IsSpawned && !IsServer) RequestStartWashServerRpc();
        else StartWashAsAuthority();
    }

    [Rpc(SendTo.Server)] private void RequestStartWashServerRpc() => StartWashAsAuthority();

    private void StartWashAsAuthority()
    {
        if (IsWashAuthority && !IsWashing && DirtyGlassCount > 0) StartCoroutine(Wash());
    }

    public string GetInteractionPrompt()
    {
        if (IsWashing) return "Washing...";
        return DirtyGlassCount > 0 ? "{KEY} Start Steam Wash" : "Load dirty glasses";
    }

    public void OnHoverEnter(string key)
    {
        _highlight.SetHighlight(true);
        _prompt.Show(GetInteractionPrompt().Replace("{KEY}", $"[{key}]"));
    }

    public void OnHoverExit()
    {
        _highlight.SetHighlight(false);
        _prompt.Hide();
    }

    private void TryLoad(PintGlass glass)
    {
        if (!IsWashAuthority || IsWashing || glass == null || glass.IsClean || _loadedGlasses.Contains(glass)) return;
        int slotIndex = _loadedGlasses.Count;
        if (glassSlots == null || slotIndex >= glassSlots.Length || glassSlots[slotIndex] == null) return;

        foreach (DwarfGrabber grabber in FindObjectsByType<DwarfGrabber>(FindObjectsSortMode.None))
            grabber.ForceReleaseItem(glass);

        Transform slot = glassSlots[slotIndex];
        // Never parent a physics/network glass to a rack transform. Parenting it
        // with a local (0,0,0) pose is what can strand it at world origin when a
        // NetworkTransform corrects its hierarchy. The rack slot is a world pose.
        glass.transform.SetParent(null, true);
        glass.transform.SetPositionAndRotation(slot.position, slot.rotation);
        glass.Rb.linearVelocity = Vector3.zero;
        glass.Rb.angularVelocity = Vector3.zero;
        glass.Rb.isKinematic = true;
        glass.Rb.useGravity = false;
        // Keep the collider enabled before a wash so the player can still grab it
        // and remove it from the rack. The trigger ignores already-loaded glasses.
        glass.Col.enabled = true;
        glass.SetDockedDishwasher(this);
        _loadedGlasses.Add(glass);
    }

    /// <summary>Called by PintGlass when a player attempts to grab a loaded glass.</summary>
    public bool TryReleaseGlass(PintGlass glass)
    {
        if (!IsWashAuthority || IsWashing || glass == null || !_loadedGlasses.Remove(glass)) return false;
        if (!glass.IsSpawned) glass.transform.SetParent(null, true);
        glass.Rb.isKinematic = false;
        glass.Rb.useGravity = true;
        glass.Col.enabled = true;
        glass.SetDockedDishwasher(null);
        return true;
    }

    private IEnumerator Wash()
    {
        SetWashing(true);
        SetLoadedGlassColliders(false);
        UpdateVisuals();
        if (steamVfx != null) steamVfx.Play();
        if (washAudio != null) washAudio.Play();
        yield return new WaitForSeconds(washDuration);

        foreach (PintGlass glass in _loadedGlasses)
            if (glass != null) glass.Clean();

        // The completed cycle presents clean glasses back to the player. They must
        // have colliders enabled before they can be raycast/grabbed again.
        ReleaseCleanGlasses();
        SetWashing(false);
        UpdateVisuals();
        if (steamVfx != null) steamVfx.Stop();
        if (washAudio != null) washAudio.Stop();
    }

    private void UpdateVisuals()
    {
        if (lidVisual != null) lidVisual.localRotation = Quaternion.Euler(IsWashing ? closedLidRotation : openLidRotation);
    }

    private void ReleaseCleanGlasses()
    {
        foreach (PintGlass glass in _loadedGlasses)
        {
            if (glass == null) continue;
            glass.Rb.linearVelocity = Vector3.zero;
            glass.Rb.angularVelocity = Vector3.zero;
            glass.Rb.isKinematic = false;
            glass.Rb.useGravity = true;
            glass.Col.enabled = true;
            glass.SetDockedDishwasher(null);
        }
        _loadedGlasses.Clear();
    }

    private void SetLoadedGlassColliders(bool enabled)
    {
        foreach (PintGlass glass in _loadedGlasses)
            if (glass != null) glass.Col.enabled = enabled;
    }

    private void SetWashing(bool value)
    {
        if (IsSpawned) _networkWashing.Value = value;
        else _offlineWashing = value;
    }

    private void OnWashingChanged(bool previousValue, bool newValue)
    {
        UpdateVisuals();
        if (steamVfx != null)
        {
            if (newValue) steamVfx.Play(); else steamVfx.Stop();
        }
        if (washAudio != null)
        {
            if (newValue) washAudio.Play(); else washAudio.Stop();
        }
    }
}
