using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Creates local, visual-only lobby dwarves: the host stands behind the bar and
/// each connected client occupies the next stool. Use a model-only prefab here,
/// not the controllable player prefab.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LobbyPresentationController : NetworkBehaviour
{
    [Header("Lobby Layout")]
    [SerializeField] private GameObject idleDwarfPrefab;
    [SerializeField] private Transform hostBehindBarAnchor;
    [SerializeField] private Transform[] joinerStoolAnchors = new Transform[3];
    [Tooltip("One state per lobby slot: Host, Joiner 1, Joiner 2, Joiner 3. Leave an entry empty to use Default Idle State.")]
    [SerializeField] private string[] idleAnimationStates = { "Idle", "Idle", "Idle", "Idle" };
    [SerializeField] private string defaultIdleAnimationState = "Idle";

    [Header("Offline Preview")]
    [SerializeField, Range(1, 4)] private int previewPlayerCount = 4;

    private readonly NetworkVariable<int> _connectedPlayerCount = new(1);
    private readonly List<GameObject> _visualDwarves = new();

    public int PlayerCount => IsSpawned ? _connectedPlayerCount.Value : previewPlayerCount;
    public event Action<int> PlayerCountChanged;

    private void Start()
    {
        // Lets the layout be polished without having to start networking.
        if (!IsSpawned) RefreshVisuals(previewPlayerCount);
    }

    public override void OnNetworkSpawn()
    {
        _connectedPlayerCount.OnValueChanged += OnPlayerCountChanged;
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            SyncPlayerCount();
        }
        RefreshVisuals(_connectedPlayerCount.Value);
    }

    public override void OnNetworkDespawn()
    {
        _connectedPlayerCount.OnValueChanged -= OnPlayerCountChanged;
        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnNetworkDespawn();
    }

    private void OnClientConnected(ulong _) => SyncPlayerCount();
    private void OnClientDisconnected(ulong _) => SyncPlayerCount();

    private void SyncPlayerCount()
    {
        if (!IsServer || NetworkManager == null) return;
        _connectedPlayerCount.Value = Mathf.Clamp(NetworkManager.ConnectedClientsIds.Count, 1, 4);
    }

    private void OnPlayerCountChanged(int _, int current)
    {
        RefreshVisuals(current);
        PlayerCountChanged?.Invoke(current);
    }

    private void RefreshVisuals(int playerCount)
    {
        ClearVisuals();
        if (idleDwarfPrefab == null)
        {
            Debug.LogWarning("LobbyPresentationController needs a visual-only dwarf prefab.", this);
            return;
        }

        int visualCount = Mathf.Clamp(playerCount, 1, 1 + joinerStoolAnchors.Length);
        for (int slot = 0; slot < visualCount; slot++)
        {
            Transform anchor = slot == 0 ? hostBehindBarAnchor : joinerStoolAnchors[slot - 1];
            if (anchor == null)
            {
                Debug.LogWarning($"Lobby slot {slot} has no anchor assigned.", this);
                continue;
            }

            GameObject dwarf = Instantiate(idleDwarfPrefab, anchor.position, anchor.rotation, transform);
            dwarf.name = slot == 0 ? "Lobby Host" : $"Lobby Joiner {slot}";
            SetIdle(dwarf, GetIdleStateForSlot(slot));
            _visualDwarves.Add(dwarf);
        }
    }

    private void ClearVisuals()
    {
        foreach (GameObject dwarf in _visualDwarves)
            if (dwarf != null) Destroy(dwarf);
        _visualDwarves.Clear();
    }

    private string GetIdleStateForSlot(int slot)
    {
        if (idleAnimationStates != null && slot >= 0 && slot < idleAnimationStates.Length &&
            !string.IsNullOrWhiteSpace(idleAnimationStates[slot]))
            return idleAnimationStates[slot];
        return defaultIdleAnimationState;
    }

    private void SetIdle(GameObject dwarf, string stateName)
    {
        Animator animator = dwarf.GetComponentInChildren<Animator>();
        if (animator == null || string.IsNullOrWhiteSpace(stateName)) return;
        int stateHash = Animator.StringToHash(stateName);
        if (animator.HasState(0, stateHash)) animator.CrossFade(stateHash, 0.05f, 0, UnityEngine.Random.value);
    }

    public override void OnDestroy()
    {
        ClearVisuals();
        base.OnDestroy();
    }
}
