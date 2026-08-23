using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnhancedCountdown
{
    public sealed class MetronomePanelPreviewHarness : MonoBehaviour
    {
        private const decimal MinimumBpm = 20.0m;
        private const decimal MaximumBpm = 999.0m;
        private const decimal InitialBpm = 240.0m;

        private TMP_InputField bpmInput;
        private TMP_Text bpmPlaceholder;
        private Toggle enabledToggle;
        private Image toggleTrack;
        private RectTransform toggleKnob;
        private Slider volumeSlider;
        private TMP_Text volumeValue;
        private Button muteButton;
        private Image muteButtonImage;
        private Image volumeIcon;
        private Image volumeXIcon;
        private decimal currentBpm = InitialBpm;
        private bool isMuted;

        private void Awake()
        {
            Transform panel = FindDescendant(transform, "MetronomeControlPanel");
            if (panel == null)
            {
                Debug.LogError("MetronomeControlPanel was not found in the preview hierarchy.");
                return;
            }

            bpmInput = Require<TMP_InputField>(panel, "BpmInput");
            bpmPlaceholder = bpmInput.placeholder as TMP_Text;
            BindMultiplier(panel, "Divide2Button", 0.5m);
            BindMultiplier(panel, "Multiply2Button", 2m);
            BindMultiplier(panel, "Divide3Button", 1m / 3m);
            BindMultiplier(panel, "Multiply3Button", 3m);

            TMP_Dropdown numerator = Require<TMP_Dropdown>(panel, "NumeratorDropdown");
            TMP_Dropdown denominator = Require<TMP_Dropdown>(panel, "DenominatorDropdown");
            enabledToggle = Require<Toggle>(panel, "EnabledToggle");
            toggleTrack = Require<Image>(enabledToggle.transform, "ToggleTrack");
            toggleKnob = Require<RectTransform>(enabledToggle.transform, "ToggleKnob");
            volumeSlider = Require<Slider>(panel, "VolumeSlider");
            volumeValue = Require<TMP_Text>(panel, "VolumeValue");
            muteButton = Require<Button>(panel, "MuteButton");
            muteButtonImage = muteButton.GetComponent<Image>();
            volumeIcon = Require<Image>(muteButton.transform, "VolumeIcon");
            volumeXIcon = Require<Image>(muteButton.transform, "VolumeXIcon");
            numerator.SetValueWithoutNotify(3);
            denominator.SetValueWithoutNotify(3);

            bpmInput.onEndEdit.AddListener(CommitBpm);
            enabledToggle.onValueChanged.AddListener(RefreshToggle);
            volumeSlider.onValueChanged.AddListener(RefreshVolume);
            muteButton.onClick.AddListener(ToggleMute);
            RefreshBpmText();
            RefreshToggle(enabledToggle.isOn);
            RefreshVolume(volumeSlider.value);
            RefreshMute();
        }

        private void OnDestroy()
        {
            if (bpmInput != null)
            {
                bpmInput.onEndEdit.RemoveListener(CommitBpm);
            }
            if (enabledToggle != null)
            {
                enabledToggle.onValueChanged.RemoveListener(RefreshToggle);
            }
            if (volumeSlider != null)
            {
                volumeSlider.onValueChanged.RemoveListener(RefreshVolume);
            }
            if (muteButton != null)
            {
                muteButton.onClick.RemoveListener(ToggleMute);
            }
        }

        private void BindMultiplier(Transform panel, string buttonName, decimal multiplier)
        {
            Button button = Require<Button>(panel, buttonName);
            button.onClick.AddListener(() => ApplyMultiplier(multiplier));
        }

        private void ApplyMultiplier(decimal multiplier)
        {
            CommitBpm(bpmInput.text);
            currentBpm = NormalizeBpm(currentBpm * multiplier);
            RefreshBpmText();
        }

        private void CommitBpm(string value)
        {
            if (!TryParseBpm(value, out decimal parsed))
            {
                RefreshBpmText();
                return;
            }

            currentBpm = NormalizeBpm(parsed);
            RefreshBpmText();
        }

        private void RefreshBpmText()
        {
            if (bpmInput != null)
            {
                string bpmText = currentBpm.ToString("0.0", CultureInfo.InvariantCulture);
                bpmInput.SetTextWithoutNotify(bpmText);
                if (bpmPlaceholder != null)
                {
                    bpmPlaceholder.text = InitialBpm.ToString("0.0", CultureInfo.InvariantCulture);
                }
            }
        }

        private void RefreshToggle(bool enabled)
        {
            toggleTrack.color = enabled ? new Color32(68, 191, 255, 255) : new Color32(70, 73, 82, 255);
            float padding = Mathf.Max(0f, (toggleTrack.rectTransform.rect.height - toggleKnob.rect.height) * 0.5f);
            float travel = Mathf.Max(0f, (toggleTrack.rectTransform.rect.width - toggleKnob.rect.width) * 0.5f - padding);
            Vector2 position = toggleKnob.anchoredPosition;
            position.x = enabled ? travel : -travel;
            toggleKnob.anchoredPosition = position;
        }

        private void RefreshVolume(float value)
        {
            volumeValue.text = $"{Mathf.RoundToInt(value)}%";
        }

        private void ToggleMute()
        {
            isMuted = !isMuted;
            RefreshMute();
        }

        private void RefreshMute()
        {
            muteButtonImage.color = isMuted ? new Color32(68, 191, 255, 255) : new Color32(24, 26, 33, 245);
            volumeIcon.gameObject.SetActive(!isMuted);
            volumeXIcon.gameObject.SetActive(isMuted);
        }

        private static bool TryParseBpm(string value, out decimal bpm)
        {
            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out bpm)
                || decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out bpm);
        }

        private static decimal NormalizeBpm(decimal bpm)
        {
            return Math.Clamp(Math.Round(bpm, 1, MidpointRounding.AwayFromZero), MinimumBpm, MaximumBpm);
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
    }
}
