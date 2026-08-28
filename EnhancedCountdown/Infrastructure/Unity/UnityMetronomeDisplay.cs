using System;
using System.IO;
using System.Reflection;
using EnhancedCountdown.Domain.MidRun;
using UnityEngine;
using UnityEngine.UI;

namespace EnhancedCountdown.Infrastructure.Unity;

internal sealed class UnityMetronomeDisplay
{
  private const string IconResourceName = "EnhancedCountdown.Resources.metronome-icon-200.png";
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
    if (canvas == null)
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
    iconSprite.name = "EnhancedCountdown Metronome Icon";

    root = new GameObject("EnhancedCountdown Metronome UI", typeof(RectTransform), typeof(CanvasGroup));
    root.SetActive(false);
    root.layer = countdown.gameObject.layer;
    var rootTransform = (RectTransform)root.transform;
    rootTransform.SetParent(canvas.transform, worldPositionStays: false);
    rootTransform.anchorMin = new Vector2(0.5f, 0.5f);
    rootTransform.anchorMax = new Vector2(0.5f, 0.5f);
    rootTransform.pivot = new Vector2(0.5f, 0.5f);
    rootTransform.anchoredPosition = Vector2.zero;
    rootTransform.sizeDelta = new Vector2(240f, 240f);
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
    iconTransform.anchoredPosition = Vector2.zero;
    iconTransform.sizeDelta = new Vector2(200f, 200f);
    Image image = iconObject.GetComponent<Image>();
    image.sprite = iconSprite;
    image.preserveAspect = true;
    image.raycastTarget = false;

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

  internal void SetPlayback(MetronomePlayback value)
  {
    playback = value;
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
      throw new InvalidOperationException(string.Concat("Embedded resource '", IconResourceName, "' was not found."));
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
    {
      name = "EnhancedCountdown Metronome Icon Texture",
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
