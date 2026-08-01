using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using RemoveCountdown.Domain.MidRun;
using UnityEngine;
using UnityEngine.UI;

namespace RemoveCountdown.Infrastructure.Unity;

internal sealed class UnityMetronomeDisplay
{
  private const string IconResourceName = "RemoveCountdown.Resources.metronome-icon-200.png";
  private const float PulseAmplitude = 0.22f;
  private const double RecoveryIntervalRatio = 0.65;
  private GameObject root;
  private RectTransform iconTransform;
  private Sprite iconSprite;
  private Texture2D iconTexture;
  private MetronomePlayback playback;

  private UnityMetronomeDisplay() { }

  internal static UnityMetronomeDisplay Create(MetronomePlayback playback)
  {
    var display = new UnityMetronomeDisplay();
    try
    {
      display.Initialize(playback);
      return display;
    }
    catch
    {
      display.Dispose();
      throw;
    }
  }

  private void Initialize(MetronomePlayback playback)
  {
    scrCountdown countdown = UnityEngine.Object.FindAnyObjectByType<scrCountdown>();
    Canvas canvas = countdown?.GetComponentInParent<Canvas>();
    Text countdownText = countdown?.GetComponent<Text>();
    if (canvas == null || countdownText == null)
    {
      throw new InvalidOperationException("The countdown canvas is unavailable.");
    }

    this.playback = playback;
    iconTexture = LoadIconTexture();
    iconSprite = Sprite.Create(
      iconTexture,
      new Rect(0f, 0f, iconTexture.width, iconTexture.height),
      new Vector2(0.5f, 0.5f),
      100f
    );
    iconSprite.name = "RemoveCountdown Metronome Icon";

    root = new GameObject("RemoveCountdown Metronome UI", typeof(RectTransform), typeof(CanvasGroup));
    root.SetActive(false);
    root.layer = countdown.gameObject.layer;
    var rootTransform = (RectTransform)root.transform;
    rootTransform.SetParent(canvas.transform, worldPositionStays: false);
    rootTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rootTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rootTransform.pivot = new Vector2(0.5f, 0.5f);
    rootTransform.anchoredPosition = Vector2.zero;
    rootTransform.sizeDelta = new Vector2(480f, 280f);
    rootTransform.SetAsLastSibling();

    CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
    canvasGroup.interactable = false;
    canvasGroup.blocksRaycasts = false;

    var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    iconObject.layer = root.layer;
    iconTransform = (RectTransform)iconObject.transform;
    iconTransform.SetParent(rootTransform, worldPositionStays: false);
    iconTransform.anchorMin = new Vector2(0.5f, 0.5f);
    iconTransform.anchorMax = new Vector2(0.5f, 0.5f);
    iconTransform.pivot = new Vector2(0.5f, 0.5f);
    iconTransform.anchoredPosition = new Vector2(0f, -39f);
    iconTransform.sizeDelta = new Vector2(200f, 200f);
    Image image = iconObject.GetComponent<Image>();
    image.sprite = iconSprite;
    image.preserveAspect = true;
    image.raycastTarget = false;

    var textObject = new GameObject("BPM", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
    textObject.layer = root.layer;
    var textTransform = (RectTransform)textObject.transform;
    textTransform.SetParent(rootTransform, worldPositionStays: false);
    textTransform.anchorMin = new Vector2(0.5f, 0.5f);
    textTransform.anchorMax = new Vector2(0.5f, 0.5f);
    textTransform.pivot = new Vector2(0.5f, 0.5f);
    textTransform.anchoredPosition = new Vector2(0f, 103f);
    textTransform.sizeDelta = new Vector2(440f, 72f);

    Text bpmText = textObject.GetComponent<Text>();
    bpmText.font = countdownText.font;
    bpmText.fontStyle = countdownText.fontStyle;
    bpmText.material = countdownText.material;
    bpmText.color = countdownText.color;
    bpmText.fontSize = 60;
    bpmText.alignment = TextAnchor.MiddleCenter;
    bpmText.horizontalOverflow = HorizontalWrapMode.Overflow;
    bpmText.verticalOverflow = VerticalWrapMode.Overflow;
    bpmText.raycastTarget = false;
    bpmText.supportRichText = false;
    bpmText.text = playback.OriginalBpm.ToString("0.0", CultureInfo.InvariantCulture) + " BPM";

    iconTransform.localScale = Vector3.one;
    root.SetActive(true);
  }

  internal void Update(int timeSamples, bool isPlaying)
  {
    if (iconTransform == null || playback.ClickFrames <= 0)
    {
      return;
    }

    if (!isPlaying)
    {
      iconTransform.localScale = Vector3.one;
      return;
    }

    int currentSample = Math.Max(0, timeSamples);
    int beatIndex = currentSample / playback.ClickFrames;
    int samplesSinceClick = currentSample % playback.ClickFrames;
    double beatPhase = (double)samplesSinceClick / playback.ClickFrames;
    float magnitude = 1f;
    if (beatPhase < RecoveryIntervalRatio)
    {
      double normalizedTime = beatPhase / RecoveryIntervalRatio;
      magnitude += PulseAmplitude * (float)Math.Pow(2.0, -10.0 * normalizedTime);
    }

    float direction = (beatIndex & 1) == 0 ? -1f : 1f;
    iconTransform.localScale = new Vector3(direction * magnitude, magnitude, 1f);
  }

  internal void Dispose()
  {
    if (root != null)
    {
      root.SetActive(false);
      UnityEngine.Object.Destroy(root);
    }
    if (iconSprite != null)
    {
      UnityEngine.Object.Destroy(iconSprite);
    }
    if (iconTexture != null)
    {
      UnityEngine.Object.Destroy(iconTexture);
    }

    root = null;
    iconTransform = null;
    iconSprite = null;
    iconTexture = null;
  }

  private static Texture2D LoadIconTexture()
  {
    using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName);
    if (stream == null)
    {
      throw new InvalidOperationException($"Embedded resource '{IconResourceName}' was not found.");
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
    {
      name = "RemoveCountdown Metronome Icon Texture",
      filterMode = FilterMode.Bilinear,
      wrapMode = TextureWrapMode.Clamp,
    };
    if (!ImageConversion.LoadImage(texture, buffer.ToArray(), markNonReadable: true))
    {
      UnityEngine.Object.Destroy(texture);
      throw new InvalidOperationException("The embedded metronome icon could not be decoded.");
    }
    return texture;
  }
}
