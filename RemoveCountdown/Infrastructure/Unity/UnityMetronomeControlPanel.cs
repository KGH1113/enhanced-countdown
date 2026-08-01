using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using RemoveCountdown.Domain.MidRun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RemoveCountdown.Infrastructure.Unity;

internal sealed class UnityMetronomeControlPanel : IDisposable
{
  private const string BundleName = "enhancedcountdown_ui.bundle";
  private const string PrefabSuffix = "/metronomecontrolpanel.prefab";
  private readonly AssetBundle bundle;
  private readonly GameObject root;
  private readonly RectTransform panel;
  private readonly TMP_InputField bpmInput;
  private readonly TMP_Text bpmPlaceholder;
  private readonly TMP_Dropdown numeratorDropdown;
  private readonly TMP_Dropdown denominatorDropdown;
  private readonly Toggle enabledToggle;
  private readonly Image toggleTrack;
  private readonly RectTransform toggleKnob;
  private readonly Action<MetronomeSettings> settingsChanged;
  private readonly Action disableRequested;
  private readonly double placeholderBpm;
  private MetronomeSettings settings;
  private int suppressInputThroughFrame = -1;

  private UnityMetronomeControlPanel(
    AssetBundle bundle,
    GameObject root,
    MetronomeSettings settings,
    double placeholderBpm,
    Action<MetronomeSettings> settingsChanged,
    Action disableRequested
  )
  {
    this.bundle = bundle;
    this.root = root;
    this.settings = settings;
    this.placeholderBpm = placeholderBpm;
    this.settingsChanged = settingsChanged;
    this.disableRequested = disableRequested;
    panel = RequireDescendant<RectTransform>(root.transform, "MetronomeControlPanel");
    bpmInput = Require<TMP_InputField>(panel, "BpmInput");
    bpmPlaceholder = bpmInput.placeholder as TMP_Text;
    numeratorDropdown = Require<TMP_Dropdown>(panel, "NumeratorDropdown");
    denominatorDropdown = Require<TMP_Dropdown>(panel, "DenominatorDropdown");
    enabledToggle = Require<Toggle>(panel, "EnabledToggle");
    toggleTrack = Require<Image>(enabledToggle.transform, "ToggleTrack");
    toggleKnob = Require<RectTransform>(enabledToggle.transform, "ToggleKnob");

    BindMultiplier("Divide2Button", 0.5m);
    BindMultiplier("Multiply2Button", 2m);
    BindMultiplier("Divide3Button", 1m / 3m);
    BindMultiplier("Multiply3Button", 3m);
    bpmInput.onEndEdit.AddListener(CommitBpm);
    numeratorDropdown.onValueChanged.AddListener(CommitNumerator);
    denominatorDropdown.onValueChanged.AddListener(CommitDenominator);
    enabledToggle.onValueChanged.AddListener(CommitEnabled);
    enabledToggle.SetIsOnWithoutNotify(true);
    RefreshToggle();
    Refresh();
    root.SetActive(true);
  }

  internal bool IsConsumingInput
  {
    get
    {
      if (root == null || !root.activeInHierarchy)
      {
        return false;
      }
      if (
        Time.frameCount <= suppressInputThroughFrame
        || bpmInput.isFocused
        || numeratorDropdown.IsExpanded
        || denominatorDropdown.IsExpanded
      )
      {
        return true;
      }

      Canvas canvas = root.GetComponent<Canvas>();
      Camera eventCamera =
        canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
      bool pointerOverPanel = RectTransformUtility.RectangleContainsScreenPoint(panel, Input.mousePosition, eventCamera);
      bool pointerPressed = Input.GetMouseButton(0) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0);
      if (pointerOverPanel && pointerPressed)
      {
        return true;
      }

      ClearTransientSelection();
      return false;
    }
  }

  internal static UnityMetronomeControlPanel Load(
    MetronomeSettings settings,
    double placeholderBpm,
    Action<MetronomeSettings> settingsChanged,
    Action disableRequested
  )
  {
    string path = Path.Combine(RuntimeDirectory(), "Assets", PlatformFolder(), BundleName);
    if (!File.Exists(path))
    {
      throw new FileNotFoundException("The metronome control panel AssetBundle was not found.", path);
    }

    AssetBundle bundle = AssetBundle.LoadFromFile(path);
    if (bundle == null)
    {
      throw new InvalidOperationException($"Failed to load the metronome control panel AssetBundle: {path}");
    }

    GameObject root = null;
    try
    {
      string prefabName = bundle
        .GetAllAssetNames()
        .FirstOrDefault(name => name.EndsWith(PrefabSuffix, StringComparison.OrdinalIgnoreCase));
      GameObject prefab = string.IsNullOrEmpty(prefabName) ? null : bundle.LoadAsset<GameObject>(prefabName);
      if (prefab == null)
      {
        throw new InvalidOperationException("MetronomeControlPanel prefab was not found in the UI AssetBundle.");
      }

      root = UnityEngine.Object.Instantiate(prefab);
      root.name = "RemoveCountdown Metronome Control Panel";
      UnityEngine.Object.DontDestroyOnLoad(root);
      return new UnityMetronomeControlPanel(
        bundle,
        root,
        settings,
        placeholderBpm,
        settingsChanged,
        disableRequested
      );
    }
    catch
    {
      if (root != null)
      {
        UnityEngine.Object.Destroy(root);
      }
      bundle.Unload(true);
      throw;
    }
  }

  internal void SetSettings(MetronomeSettings value)
  {
    settings = value;
    Refresh();
  }

  public void Dispose()
  {
    if (bpmInput != null)
    {
      bpmInput.onEndEdit.RemoveListener(CommitBpm);
    }
    if (numeratorDropdown != null)
    {
      numeratorDropdown.onValueChanged.RemoveListener(CommitNumerator);
    }
    if (denominatorDropdown != null)
    {
      denominatorDropdown.onValueChanged.RemoveListener(CommitDenominator);
    }
    if (enabledToggle != null)
    {
      enabledToggle.onValueChanged.RemoveListener(CommitEnabled);
    }
    if (root != null)
    {
      root.SetActive(false);
      UnityEngine.Object.Destroy(root);
    }
    bundle?.Unload(false);
  }

  private void BindMultiplier(string buttonName, decimal multiplier)
  {
    Button button = Require<Button>(panel, buttonName);
    button.onClick.AddListener(() => ApplyMultiplier(multiplier));
  }

  private void ApplyMultiplier(decimal multiplier)
  {
    CommitBpm(bpmInput.text, notify: false);
    decimal value = (decimal)settings.ClickBpm * multiplier;
    Apply(settings.WithClickBpm((double)value));
    ClearTransientSelection(force: true);
  }

  private void CommitBpm(string value)
  {
    CommitBpm(value, notify: true);
    if (!Input.GetMouseButton(0))
    {
      ClearTransientSelection(force: true);
    }
  }

  private void CommitBpm(string value, bool notify)
  {
    if (
      !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
      && !decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
    )
    {
      Refresh();
      SuppressInput();
      return;
    }

    MetronomeSettings updated = settings.WithClickBpm((double)parsed);
    if (notify)
    {
      Apply(updated);
    }
    else
    {
      settings = updated;
      Refresh();
    }
  }

  private void CommitNumerator(int optionIndex)
  {
    Apply(settings.WithNumerator(optionIndex + 1));
    ClearTransientSelection(force: true);
  }

  private void CommitDenominator(int optionIndex)
  {
    Apply(settings.WithDenominator(optionIndex + 1));
    ClearTransientSelection(force: true);
  }

  private void CommitEnabled(bool enabled)
  {
    RefreshToggle();
    SuppressInput();
    ClearTransientSelection(force: true);
    if (!enabled)
    {
      disableRequested?.Invoke();
    }
  }

  private void Apply(MetronomeSettings updated)
  {
    settings = updated;
    Refresh();
    SuppressInput();
    settingsChanged?.Invoke(settings);
  }

  private void Refresh()
  {
    string bpmText = settings.ClickBpm.ToString("0.0", CultureInfo.InvariantCulture);
    bpmInput.SetTextWithoutNotify(bpmText);
    if (bpmPlaceholder != null)
    {
      bpmPlaceholder.text = placeholderBpm.ToString("0.0", CultureInfo.InvariantCulture);
    }
    numeratorDropdown.SetValueWithoutNotify(settings.Numerator - 1);
    numeratorDropdown.RefreshShownValue();
    denominatorDropdown.SetValueWithoutNotify(settings.Denominator - 1);
    denominatorDropdown.RefreshShownValue();
  }

  private void SuppressInput()
  {
    suppressInputThroughFrame = Time.frameCount + 1;
  }

  private void RefreshToggle()
  {
    bool enabled = enabledToggle.isOn;
    toggleTrack.color = enabled ? new Color32(68, 191, 255, 255) : new Color32(70, 73, 82, 255);
    float padding = Math.Max(0f, (toggleTrack.rectTransform.rect.height - toggleKnob.rect.height) * 0.5f);
    float travel = Math.Max(0f, (toggleTrack.rectTransform.rect.width - toggleKnob.rect.width) * 0.5f - padding);
    Vector2 position = toggleKnob.anchoredPosition;
    position.x = enabled ? travel : -travel;
    toggleKnob.anchoredPosition = position;
  }

  private void ClearTransientSelection(bool force = false)
  {
    EventSystem eventSystem = EventSystem.current;
    GameObject selected = eventSystem?.currentSelectedGameObject;
    if (selected == null || !selected.transform.IsChildOf(root.transform))
    {
      return;
    }
    if (!force && (bpmInput.isFocused || numeratorDropdown.IsExpanded || denominatorDropdown.IsExpanded))
    {
      return;
    }
    eventSystem.SetSelectedGameObject(null);
  }

  private static T Require<T>(Transform parent, string name)
    where T : Component
  {
    Transform descendant = FindDescendant(parent, name);
    if (descendant == null || !descendant.TryGetComponent(out T component))
    {
      throw new InvalidOperationException($"Required UI component '{name}' ({typeof(T).Name}) was not found.");
    }
    return component;
  }

  private static T RequireDescendant<T>(Transform parent, string name)
    where T : Component
  {
    for (int index = 0; index < parent.childCount; index++)
    {
      Transform descendant = FindDescendant(parent.GetChild(index), name);
      if (descendant != null && descendant.TryGetComponent(out T component))
      {
        return component;
      }
    }
    throw new InvalidOperationException($"Required child UI component '{name}' ({typeof(T).Name}) was not found.");
  }

  private static Transform FindDescendant(Transform parent, string name)
  {
    if (parent.name == name)
    {
      return parent;
    }
    for (int index = 0; index < parent.childCount; index++)
    {
      Transform found = FindDescendant(parent.GetChild(index), name);
      if (found != null)
      {
        return found;
      }
    }
    return null;
  }

  private static string PlatformFolder()
  {
    return UnityEngine.Application.platform switch
    {
      RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "mac",
      RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor => "win",
      RuntimePlatform.LinuxPlayer or RuntimePlatform.LinuxEditor => "linux",
      _ => throw new PlatformNotSupportedException($"Unsupported platform: {UnityEngine.Application.platform}"),
    };
  }

  private static string RuntimeDirectory()
  {
    string assemblyPath = Assembly.GetExecutingAssembly().Location;
    string directory = string.IsNullOrWhiteSpace(assemblyPath)
      ? Directory.GetCurrentDirectory()
      : Path.GetDirectoryName(assemblyPath);
    return directory ?? Directory.GetCurrentDirectory();
  }
}
