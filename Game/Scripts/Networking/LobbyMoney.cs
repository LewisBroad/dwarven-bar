using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative currency shared by every client in the current lobby.
/// Place this beside a NetworkObject that is spawned with the scene.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class LobbyMoney : NetworkBehaviour
{
    [SerializeField, Min(0)] private int startingBalance = 100;

    public static LobbyMoney Instance { get; private set; }

    // Everyone may read the balance; only the host/server is allowed to write it.
    public NetworkVariable<int> Balance { get; } = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public event Action<int> BalanceChanged;

    private void Start()
    {
        // Makes the HUD visible while testing the scene before a host/client session
        // has been started. Network spawning will take ownership once it begins.
        if (!IsSpawned)
        {
            Balance.Value = startingBalance;
            LobbyMoneyHud.ShowFor(this);
        }
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        Balance.OnValueChanged += OnBalanceValueChanged;
        LobbyMoneyHud.ShowFor(this);

        // This runs once when the lobby wallet is spawned by the host.
        if (IsServer)
        {
            Balance.Value = startingBalance;
        }

        BalanceChanged?.Invoke(Balance.Value);
    }

    public override void OnNetworkDespawn()
    {
        Balance.OnValueChanged -= OnBalanceValueChanged;
        if (Instance == this) Instance = null;
    }

    /// <summary>Ask the server to spend from the single shared lobby balance.</summary>
    public void RequestSpend(int cost)
    {
        if (cost <= 0 || !IsSpawned) return;
        RequestSpendRpc(cost);
    }

    /// <summary>
    /// Server-only method for trusted gameplay, e.g. awarding money when a sale completes.
    /// Clients must not call this directly.
    /// </summary>
    public bool TryAddMoney(int amount)
    {
        // The local path is intentional for single-player/editor testing. Once this
        // NetworkObject is spawned, only the server may change the shared balance.
        if ((IsSpawned && !IsServer) || amount <= 0) return false;

        Balance.Value += amount;
        if (!IsSpawned) BalanceChanged?.Invoke(Balance.Value);
        return true;
    }

    [Rpc(SendTo.Server)]
    private void RequestSpendRpc(int cost, RpcParams rpcParams = default)
    {
        if (cost <= 0 || Balance.Value < cost)
        {
            return;
        }

        Balance.Value -= cost;
    }

    private void OnBalanceValueChanged(int previousValue, int newValue)
    {
        BalanceChanged?.Invoke(newValue);
    }
}
