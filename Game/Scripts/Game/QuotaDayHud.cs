using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Small temporary day HUD and fullscreen transition overlay for quota mode.</summary>
public class QuotaDayHud : MonoBehaviour
{
    private static QuotaDayHud _instance;

    private TextMeshProUGUI _label;
    private Image _fade;

    public static QuotaDayHud CreateOrGet()
    {
        if (_instance != null) return _instance;

        GameObject root = new("Quota Day HUD");
        DontDestroyOnLoad(root);
        _instance = root.AddComponent<QuotaDayHud>();
        _instance.Build();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<GraphicRaycaster>().enabled = false;

        GameObject labelObject = new("Debug Day Timer", typeof(RectTransform));
        labelObject.transform.SetParent(transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 1f);
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -18f);
        labelRect.sizeDelta = new Vector2(850f, 75f);
        _label = labelObject.AddComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.font = TMP_Settings.defaultFontAsset;
        _label.fontSize = 28f;
        _label.color = Color.white;
        _label.enableWordWrapping = false;
        _label.outlineWidth = 0.2f;

        GameObject fadeObject = new("Day Transition Fade", typeof(RectTransform));
        fadeObject.transform.SetParent(transform, false);
        RectTransform fadeRect = fadeObject.GetComponent<RectTransform>();
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = Vector2.zero;
        fadeRect.offsetMax = Vector2.zero;
        _fade = fadeObject.AddComponent<Image>();
        _fade.color = new Color(0f, 0f, 0f, 0f);
        _fade.raycastTarget = false;
    }

    public void SetDay(int day, string state, float elapsedSeconds, float durationSeconds, int revenue, int quota, int startHour, bool useTwentyFourHourClock)
    {
        if (_label == null) Build();
        // Quota mode uses one real second per in-game minute. Integer minutes keep
        // the clock visibly stepping from 10:00 through midnight without drift.
        int totalMinutes = (startHour * 60) + Mathf.FloorToInt(elapsedSeconds);
        int hour = (totalMinutes / 60) % 24;
        int minute = totalMinutes % 60;
        string time = useTwentyFourHourClock
            ? $"{hour:00}:{minute:00}"
            : $"{((hour + 11) % 12) + 1}:{minute:00} {(hour < 12 ? "AM" : "PM")}";
        _label.text = $"DAY {day}  |  {state}  |  {time}  |  TODAY {revenue:N0} / {quota:N0} GOLD";
    }

    public static IEnumerator FadeToBlack(float duration)
    {
        QuotaDayHud hud = CreateOrGet();
        yield return hud.Fade(1f, duration);
    }

    public static IEnumerator FadeFromBlack(float duration)
    {
        QuotaDayHud hud = CreateOrGet();
        yield return hud.Fade(0f, duration);
    }

    public static IEnumerator PlayDayTransition(float fadeSeconds, float holdSeconds)
    {
        QuotaDayHud hud = CreateOrGet();
        yield return hud.Fade(1f, fadeSeconds);
        yield return new WaitForSecondsRealtime(holdSeconds);
        yield return hud.Fade(0f, fadeSeconds);
    }

    private IEnumerator Fade(float targetAlpha, float duration)
    {
        if (_fade == null) Build();
        float startAlpha = _fade.color.a;
        if (duration <= 0f)
        {
            SetFade(targetAlpha);
            yield break;
        }

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            SetFade(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            yield return null;
        }
        SetFade(targetAlpha);
    }

    private void SetFade(float alpha)
    {
        Color color = _fade.color;
        color.a = alpha;
        _fade.color = color;
    }
}
