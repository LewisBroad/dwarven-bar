using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>Server-authoritative day loop. It also works in an offline editor scene.</summary>
[RequireComponent(typeof(NetworkObject))]
public class QuotaGameManager : NetworkBehaviour
{
    public enum DayState { Setup, Open, LastCall, Transitioning }

    [Header("Day Rules")]
    [SerializeField, Min(0.1f)] private float realMinutesPerDay = 14f;
    [SerializeField, Min(0)] private int startingQuota = 100;
    [SerializeField, Min(0)] private int quotaIncreasePerDay = 50;
    [SerializeField] private bool useDynamicQuotaScaling;
    [SerializeField, Min(0)] private int minimumDynamicQuota = 100;
    [SerializeField, Min(0f)] private float previousDayRevenueQuotaMultiplier = 1f;
    [SerializeField] private bool useTwentyFourHourClock = true;
    [SerializeField] private bool automaticallyStartFirstSetup = true;

    [Header("World References")]
    [SerializeField] private CustomerManager customerManager;
    [SerializeField] private Light mainDayLight;
    [SerializeField, Min(0f)] private float morningLightIntensity = 1.2f;
    [SerializeField, Min(0f)] private float closingLightIntensity = 0.35f;
    [SerializeField] private TextMeshPro quotaBoardText;
    [SerializeField] private AudioSource lastCallBell;
    [SerializeField, Min(0f)] private float transitionFadeSeconds = 1.5f;
    [SerializeField, Min(0f)] private float blackScreenHoldSeconds = 2f;

    public static QuotaGameManager Instance { get; private set; }
    private readonly NetworkVariable<int> _state = new((int)DayState.Setup);
    private readonly NetworkVariable<int> _dayNumber = new(1);
    private readonly NetworkVariable<int> _quota = new(100);
    private readonly NetworkVariable<int> _dailyRevenue = new();
    private readonly NetworkVariable<int> _previousDayRevenue = new();
    private readonly NetworkVariable<float> _finalElapsedSeconds = new();
    private readonly NetworkVariable<double> _dayStartedAtServerTime = new(-1d);

    private DayState _offlineState = DayState.Setup;
    private int _offlineDayNumber = 1, _offlineQuota, _offlineDailyRevenue, _offlinePreviousDayRevenue;
    private float _offlineElapsedSeconds;
    private int _lastWalletBalance;
    private LobbyMoney _wallet;
    private QuotaDayHud _hud;

    public DayState State => IsSpawned ? (DayState)_state.Value : _offlineState;
    public int DayNumber => IsSpawned ? _dayNumber.Value : _offlineDayNumber;
    public int CurrentQuota => IsSpawned ? _quota.Value : _offlineQuota;
    public int DailyRevenue => IsSpawned ? _dailyRevenue.Value : _offlineDailyRevenue;
    public int PreviousDayRevenue => IsSpawned ? _previousDayRevenue.Value : _offlinePreviousDayRevenue;
    public bool IsBarOpen => State == DayState.Open;
    public bool IsInputLocked => State == DayState.Transitioning;
    public bool UseTwentyFourHourClock { get => useTwentyFourHourClock; set { useTwentyFourHourClock = value; UpdateDisplays(); } }
    public bool CanCloseForNextDay => State == DayState.LastCall && (customerManager == null || !customerManager.HasCustomersNeedingService);
    public float DayProgress => Mathf.Clamp01(ElapsedDaySeconds / DayDurationSeconds);

    private float DayDurationSeconds => realMinutesPerDay * 60f;
    private bool IsGameAuthority => !IsSpawned || IsServer;
    private float ElapsedDaySeconds => !IsSpawned ? _offlineElapsedSeconds :
        State == DayState.Open && _dayStartedAtServerTime.Value >= 0d && NetworkManager != null
            ? Mathf.Clamp((float)(NetworkManager.ServerTime.Time - _dayStartedAtServerTime.Value), 0f, DayDurationSeconds)
            : _finalElapsedSeconds.Value;

    private void Awake()
    {
        Instance = this;
        _offlineQuota = startingQuota;
        if (customerManager == null) customerManager = CustomerManager.Instance;
        _hud = QuotaDayHud.CreateOrGet();
    }

    private void Start()
    {
        BindWallet();
        if (customerManager == null) customerManager = CustomerManager.Instance;
        if (!IsSpawned && automaticallyStartFirstSetup) BeginSetupDay();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && automaticallyStartFirstSetup)
        {
            SetDayNumber(1);
            SetQuota(startingQuota);
            BeginSetupDay();
        }
        UpdateDisplays();
    }

    private void Update()
    {
        if (_wallet == null) BindWallet();
        if (customerManager == null) customerManager = CustomerManager.Instance;
        if (IsGameAuthority && State == DayState.Open && ElapsedDaySeconds >= DayDurationSeconds) BeginLastCall();
        UpdateLighting();
        UpdateDisplays();
    }

    public override void OnDestroy()
    {
        if (_wallet != null) _wallet.BalanceChanged -= OnWalletChanged;
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public void RequestToggleBar()
    {
        if (IsSpawned && !IsServer) RequestToggleBarServerRpc(); else ToggleBarAsAuthority();
    }

    [Rpc(SendTo.Server)] private void RequestToggleBarServerRpc() => ToggleBarAsAuthority();

    private void ToggleBarAsAuthority()
    {
        if (!IsGameAuthority) return;
        if (State == DayState.Setup) OpenBar();
        else if (State == DayState.Open || State == DayState.LastCall) CloseBar();
    }

    public void OpenBar()
    {
        if (!IsGameAuthority || State != DayState.Setup) return;
        SetState(DayState.Open); SetDailyRevenue(0); SetFinalElapsed(0f);
        if (IsSpawned && NetworkManager != null) _dayStartedAtServerTime.Value = NetworkManager.ServerTime.Time;
        else _offlineElapsedSeconds = 0f;
        customerManager?.SetCustomerArrivalsEnabled(true);
    }

    public void CloseBar()
    {
        if (!IsGameAuthority) return;
        if (State == DayState.Open) { BeginLastCall(); StartCoroutine(BeginNextDay(true)); }
        else if (State == DayState.LastCall && CanCloseForNextDay) StartCoroutine(BeginNextDay(false));
    }

    public string GetSignPrompt() => State switch
    {
        DayState.Setup => "{KEY} Open Bar", DayState.Open => "{KEY} Close Early",
        DayState.LastCall when CanCloseForNextDay => "{KEY} Close Bar",
        DayState.LastCall => "Finish serving the last customer", _ => "Closing..."
    };

    private void BeginLastCall()
    {
        if (!IsGameAuthority || State != DayState.Open) return;
        SetFinalElapsed(DayDurationSeconds); SetState(DayState.LastCall);
        customerManager?.SetCustomerArrivalsEnabled(false);
        if (lastCallBell != null) lastCallBell.Play();
    }

    private IEnumerator BeginNextDay(bool ignoreQueue)
    {
        if (!IsGameAuthority || State == DayState.Transitioning || (!ignoreQueue && customerManager != null && customerManager.HasCustomersNeedingService)) yield break;
        SetState(DayState.Transitioning);
        // Editor/offline play has no NetworkManager, so a ClientRpc would throw and
        // abort this coroutine before the fade and next-day reset can occur.
        if (IsSpawned && NetworkManager != null && NetworkManager.IsListening)
            PlayTransitionClientRpc(transitionFadeSeconds, blackScreenHoldSeconds);
        else
            StartCoroutine(QuotaDayHud.PlayDayTransition(transitionFadeSeconds, blackScreenHoldSeconds));
        yield return new WaitForSecondsRealtime((transitionFadeSeconds * 2f) + blackScreenHoldSeconds);
        if (_wallet != null && !_wallet.TrySpendMoney(CurrentQuota)) Debug.LogWarning($"[Quota Mode] Could not pay day {DayNumber} quota of {CurrentQuota} gold.", this);
        SetPreviousRevenue(DailyRevenue); SetDayNumber(DayNumber + 1); SetQuota(CalculateQuota());
        BeginSetupDay();
    }

    [ClientRpc] private void PlayTransitionClientRpc(float fadeSeconds, float holdSeconds) => StartCoroutine(QuotaDayHud.PlayDayTransition(fadeSeconds, holdSeconds));

    private void BeginSetupDay()
    {
        if (!IsGameAuthority) return;
        SetState(DayState.Setup); SetFinalElapsed(0f); SetDailyRevenue(0); _dayStartedAtServerTime.Value = -1d;
        customerManager?.SetCustomerArrivalsEnabled(false); customerManager?.SetDayNumber(DayNumber);
    }

    private int CalculateQuota() => DayNumber <= 1 ? startingQuota : useDynamicQuotaScaling
        ? Mathf.Max(minimumDynamicQuota, Mathf.RoundToInt(PreviousDayRevenue * previousDayRevenueQuotaMultiplier))
        : startingQuota + ((DayNumber - 1) * quotaIncreasePerDay);

    private void UpdateLighting()
    {
        if (mainDayLight != null) mainDayLight.intensity = Mathf.Lerp(morningLightIntensity, closingLightIntensity, DayProgress);
    }

    private void BindWallet()
    {
        if (LobbyMoney.Instance == null || _wallet == LobbyMoney.Instance) return;
        if (_wallet != null) _wallet.BalanceChanged -= OnWalletChanged;
        _wallet = LobbyMoney.Instance; _lastWalletBalance = _wallet.Balance.Value; _wallet.BalanceChanged += OnWalletChanged;
    }

    private void OnWalletChanged(int balance)
    {
        int change = balance - _lastWalletBalance;
        if (IsGameAuthority && State == DayState.Open && change > 0) SetDailyRevenue(DailyRevenue + change);
        _lastWalletBalance = balance;
    }

    private void UpdateDisplays()
    {
        string stateText = State == DayState.Setup ? "SETUP" : State == DayState.Open ? "OPEN" : State == DayState.LastCall ? "LAST CALL" : "CLOSING";
        _hud?.SetDay(DayNumber, stateText, ElapsedDaySeconds, DayDurationSeconds, DailyRevenue, CurrentQuota, 10, useTwentyFourHourClock);
        if (quotaBoardText != null) quotaBoardText.text = $"DAY {DayNumber}\nQUOTA: {CurrentQuota:N0} GOLD\nTODAY: {DailyRevenue:N0} / {CurrentQuota:N0}";
    }

    private void SetState(DayState value) { if (IsSpawned) _state.Value = (int)value; else _offlineState = value; }
    private void SetDayNumber(int value) { if (IsSpawned) _dayNumber.Value = value; else _offlineDayNumber = value; }
    private void SetQuota(int value) { if (IsSpawned) _quota.Value = value; else _offlineQuota = value; }
    private void SetDailyRevenue(int value) { if (IsSpawned) _dailyRevenue.Value = value; else _offlineDailyRevenue = value; }
    private void SetPreviousRevenue(int value) { if (IsSpawned) _previousDayRevenue.Value = value; else _offlinePreviousDayRevenue = value; }
    private void SetFinalElapsed(float value) { if (IsSpawned) _finalElapsedSeconds.Value = value; else _offlineElapsedSeconds = value; }
}
