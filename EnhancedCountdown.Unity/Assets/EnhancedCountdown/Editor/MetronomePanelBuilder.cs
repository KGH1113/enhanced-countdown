using System;
using System.Collections.Generic;
using System.IO;
using EnhancedCountdown;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace EnhancedCountdown.Editor
{
    internal static class MetronomePanelBuilder
    {
        private const string AssetRoot = "Assets/EnhancedCountdown";
        private const string ArtRoot = AssetRoot + "/Art";
        private const string FontRoot = AssetRoot + "/Font";
        private const string PrefabRoot = AssetRoot + "/Prefabs";
        private const string ResourcesRoot = "Assets/TextMesh Pro/Resources";
        private const string PanelSpritePath = ArtRoot + "/rounded-panel.png";
        private const string ToggleTrackSpritePath = ArtRoot + "/toggle-pill.png";
        private const string ToggleKnobSpritePath = ArtRoot + "/toggle-knob.png";
        private const string FontSourcePath = FontRoot + "/MAPLESTORY_OTF_BOLD.OTF";
        private const string FontAssetPath = FontRoot + "/MAPLESTORY_OTF_BOLD Dynamic SDF.asset";
        private const string PrefabPath = PrefabRoot + "/MetronomeControlPanel.prefab";
        private const string PreviewScenePath = "Assets/Scenes/SampleScene.unity";
        private const string BundleName = "enhancedcountdown_ui.bundle";

        private static readonly Color32 PanelColor = new Color32(7, 7, 10, 245);
        private static readonly Color32 SurfaceColor = new Color32(24, 26, 33, 245);
        private static readonly Color32 AccentColor = new Color32(68, 191, 255, 255);
        private static readonly Color32 PrimaryTextColor = new Color32(246, 246, 248, 255);
        private static readonly Color32 SecondaryTextColor = new Color32(174, 177, 187, 255);

        [InitializeOnLoadMethod]
        private static void CreateInitialAssets()
        {
            if (!File.Exists(Path.Combine(ProjectRoot, PrefabPath)))
            {
                EditorApplication.delayCall += RebuildPanelAndPreview;
            }
        }

        [MenuItem("Enhanced Countdown/UI/Rebuild Metronome Panel")]
        public static void RebuildPanelAndPreview()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RebuildPanelAndPreview;
                return;
            }

            EnsureAssetFolders();
            EnsureRoundedPanelArt();
            EnsureToggleArt();
            TMP_FontAsset font = EnsureFontAsset();
            CreatePanelPrefab(font);
            CreatePreviewScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Enhanced Countdown metronome panel rebuilt at {PrefabPath}");
        }

        [MenuItem("Enhanced Countdown/UI/Open Metronome Preview")]
        public static void OpenPreview()
        {
            if (!File.Exists(Path.Combine(ProjectRoot, PreviewScenePath)))
            {
                RebuildPanelAndPreview();
            }
            EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Enhanced Countdown/UI/Build All UI Bundles")]
        public static void BuildAllBundles()
        {
            RebuildPanelAndPreview();
            BuildBundle(BuildTarget.StandaloneOSX, "mac");
            BuildBundle(BuildTarget.StandaloneWindows64, "win");
            BuildBundle(BuildTarget.StandaloneLinux64, "linux");
        }

        [MenuItem("Enhanced Countdown/UI/Build macOS UI Bundle")]
        public static void BuildMacBundle()
        {
            RebuildPanelAndPreview();
            BuildBundle(BuildTarget.StandaloneOSX, "mac");
        }

        [MenuItem("Enhanced Countdown/UI/Build Windows UI Bundle")]
        public static void BuildWindowsBundle()
        {
            RebuildPanelAndPreview();
            BuildBundle(BuildTarget.StandaloneWindows64, "win");
        }

        [MenuItem("Enhanced Countdown/UI/Build Linux UI Bundle")]
        public static void BuildLinuxBundle()
        {
            RebuildPanelAndPreview();
            BuildBundle(BuildTarget.StandaloneLinux64, "linux");
        }

        private static void BuildBundle(BuildTarget target, string platformFolder)
        {
            string outputDirectory = Path.Combine(ProjectRoot, "Build", "AssetBundles", platformFolder);
            Directory.CreateDirectory(outputDirectory);
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                BuildAssetBundleOptions.ChunkBasedCompression,
                target);
            if (manifest == null)
            {
                throw new InvalidOperationException($"Failed to build the {platformFolder} UI bundle.");
            }
            string builtBundle = Path.Combine(outputDirectory, BundleName);
            string runtimeDirectory = Path.Combine(RuntimeAssetRoot, platformFolder);
            Directory.CreateDirectory(runtimeDirectory);
            string runtimeBundle = Path.Combine(runtimeDirectory, BundleName);
            File.Copy(builtBundle, runtimeBundle, true);
            Debug.Log($"Built {target} UI bundle at {builtBundle} and copied it to {runtimeBundle}");
        }

        private static void CreatePanelPrefab(TMP_FontAsset font)
        {
            Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
            Sprite toggleTrackSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ToggleTrackSpritePath);
            Sprite toggleKnobSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ToggleKnobSpritePath);
            if (panelSprite == null || toggleTrackSprite == null || toggleKnobSprite == null)
            {
                throw new InvalidOperationException("One or more metronome UI sprites are unavailable.");
            }

            GameObject overlay = new GameObject(
                "MetronomeControlOverlay",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            try
            {
                Canvas canvas = overlay.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10001;

                CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                Image panel = CreateImage("MetronomeControlPanel", overlay.transform, panelSprite, PanelColor);
                SetRect(
                    panel.rectTransform,
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(-24f, 24f),
                    new Vector2(360f, 285f));
                panel.type = Image.Type.Sliced;
                panel.raycastTarget = true;

                Outline outline = panel.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 1f, 1f, 0.13f);
                outline.effectDistance = new Vector2(1f, -1f);

                Image topSheen = CreateImage("TopSheen", panel.transform, null, new Color(1f, 1f, 1f, 0.13f));
                SetRect(
                    topSheen.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, -1f),
                    new Vector2(-30f, 1f));

                CreateText(
                    "HeaderText",
                    panel.transform,
                    font,
                    "METRONOME",
                    15f,
                    FontStyles.Bold,
                    AccentColor,
                    TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(21f, -15f),
                    new Vector2(240f, 21f));

                CreateEnabledToggle(
                    panel.transform,
                    toggleTrackSprite,
                    toggleKnobSprite,
                    new Vector2(292f, -12.5f));

                Image headerDivider = CreateImage("HeaderDivider", panel.transform, null, new Color(1f, 1f, 1f, 0.09f));
                SetRect(
                    headerDivider.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(21f, -46.5f),
                    new Vector2(318f, 1f));

                CreateText(
                    "BpmLabel",
                    panel.transform,
                    font,
                    "CLICK BPM",
                    12.75f,
                    FontStyles.Bold,
                    SecondaryTextColor,
                    TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(21f, -57f),
                    new Vector2(318f, 16.5f));

                CreateBpmInput(panel.transform, panelSprite, font);

                CreateActionButton("Divide2Button", panel.transform, panelSprite, font, "÷2", new Vector2(21f, -141f));
                CreateActionButton("Multiply2Button", panel.transform, panelSprite, font, "×2", new Vector2(102f, -141f));
                CreateActionButton("Divide3Button", panel.transform, panelSprite, font, "÷3", new Vector2(183f, -141f));
                CreateActionButton("Multiply3Button", panel.transform, panelSprite, font, "×3", new Vector2(264f, -141f));

                Image sectionDivider = CreateImage("SectionDivider", panel.transform, null, new Color(1f, 1f, 1f, 0.09f));
                SetRect(
                    sectionDivider.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(21f, -190.5f),
                    new Vector2(318f, 1f));

                CreateText(
                    "TimeSignatureLabel",
                    panel.transform,
                    font,
                    "TIME SIGNATURE",
                    12.75f,
                    FontStyles.Bold,
                    SecondaryTextColor,
                    TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(21f, -205.5f),
                    new Vector2(318f, 16.5f));

                CreateDropdown("NumeratorDropdown", panel.transform, panelSprite, font, new Vector2(39f, -229.5f));
                CreateText(
                    "TimeSignatureSlash",
                    panel.transform,
                    font,
                    "/",
                    25.5f,
                    FontStyles.Normal,
                    PrimaryTextColor,
                    TextAlignmentOptions.Center,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(165f, -229.5f),
                    new Vector2(30f, 39f));
                CreateDropdown("DenominatorDropdown", panel.transform, panelSprite, font, new Vector2(201f, -229.5f));

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(overlay, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException("Failed to save the metronome control panel prefab.");
                }

                AssetImporter importer = AssetImporter.GetAtPath(PrefabPath);
                importer.assetBundleName = BundleName;
                importer.SaveAndReimport();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(overlay);
            }
        }

        private static void CreateBpmInput(Transform parent, Sprite panelSprite, TMP_FontAsset font)
        {
            Image background = CreateImage("BpmInput", parent, panelSprite, SurfaceColor);
            SetRect(
                background.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(21f, -76.5f),
                new Vector2(318f, 55.5f));
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            TMP_InputField input = background.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 6;
            input.onFocusSelectAll = true;
            input.selectionColor = new Color(AccentColor.r / 255f, AccentColor.g / 255f, AccentColor.b / 255f, 0.42f);
            input.caretColor = AccentColor;
            input.customCaretColor = true;
            input.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(background.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            SetRect(
                viewportRect,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(-31.5f, 0f),
                new Vector2(-81f, -9f));

            TextMeshProUGUI inputText = CreateText(
                "Text",
                viewport.transform,
                font,
                "240.0",
                31.5f,
                FontStyles.Normal,
                PrimaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(3f, 0f),
                new Vector2(-6f, 0f));
            inputText.textWrappingMode = TextWrappingModes.NoWrap;

            TextMeshProUGUI placeholder = CreateText(
                "Placeholder",
                viewport.transform,
                font,
                "240.0",
                31.5f,
                FontStyles.Normal,
                new Color32(128, 132, 143, 150),
                TextAlignmentOptions.MidlineLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(3f, 0f),
                new Vector2(-6f, 0f));

            input.textViewport = viewportRect;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.text = "240.0";

            CreateText(
                "BpmSuffix",
                background.transform,
                font,
                "BPM",
                16.5f,
                FontStyles.Bold,
                AccentColor,
                TextAlignmentOptions.MidlineRight,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(-13.5f, 0f),
                new Vector2(60f, 0f));
        }

        private static void CreateActionButton(
            string name,
            Transform parent,
            Sprite panelSprite,
            TMP_FontAsset font,
            string label,
            Vector2 position)
        {
            Image image = CreateImage(name, parent, panelSprite, SurfaceColor);
            SetRect(
                image.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                new Vector2(75f, 37.5f));
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateControlColors();
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            CreateText(
                "Label",
                image.transform,
                font,
                label,
                18.75f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
        }

        private static void CreateEnabledToggle(
            Transform parent,
            Sprite trackSprite,
            Sprite knobSprite,
            Vector2 position)
        {
            var toggleObject = new GameObject("EnabledToggle", typeof(RectTransform), typeof(Toggle));
            toggleObject.transform.SetParent(parent, false);
            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            SetRect(
                toggleRect,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                new Vector2(48f, 26f));

            Image track = CreateImage("ToggleTrack", toggleObject.transform, trackSprite, AccentColor);
            SetRect(
                track.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            track.type = Image.Type.Simple;
            track.preserveAspect = true;
            track.raycastTarget = true;

            Image knob = CreateImage("ToggleKnob", toggleObject.transform, knobSprite, PrimaryTextColor);
            SetRect(
                knob.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(11f, 0f),
                new Vector2(19.5f, 19.5f));
            knob.raycastTarget = false;
            knob.type = Image.Type.Simple;
            knob.preserveAspect = true;

            Toggle toggle = toggleObject.GetComponent<Toggle>();
            toggle.targetGraphic = track;
            toggle.graphic = null;
            toggle.isOn = true;
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.colors = CreateControlColors();
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        private static void CreateDropdown(
            string name,
            Transform parent,
            Sprite panelSprite,
            TMP_FontAsset font,
            Vector2 position)
        {
            Image background = CreateImage(name, parent, panelSprite, SurfaceColor);
            SetRect(
                background.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                new Vector2(120f, 39f));
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            TMP_Dropdown dropdown = background.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.colors = CreateControlColors();
            dropdown.navigation = new Navigation { mode = Navigation.Mode.None };

            TextMeshProUGUI caption = CreateText(
                "Label",
                background.transform,
                font,
                "4",
                19.5f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(-9f, 0f),
                new Vector2(-33f, -6f));

            CreateText(
                "Arrow",
                background.transform,
                font,
                "▼",
                12f,
                FontStyles.Normal,
                AccentColor,
                TextAlignmentOptions.Center,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(-13.5f, 0f),
                new Vector2(21f, 0f));

            RectTransform template = CreateDropdownTemplate(background.transform, panelSprite, font, out TextMeshProUGUI itemText);
            dropdown.template = template;
            dropdown.captionText = caption;
            dropdown.itemText = itemText;
            dropdown.options = CreateMeterOptions();
            dropdown.SetValueWithoutNotify(3);
            dropdown.RefreshShownValue();
            template.gameObject.SetActive(false);
        }

        private static RectTransform CreateDropdownTemplate(
            Transform parent,
            Sprite panelSprite,
            TMP_FontAsset font,
            out TextMeshProUGUI itemText)
        {
            Image templateImage = CreateImage("Template", parent, panelSprite, PanelColor);
            RectTransform template = templateImage.rectTransform;
            SetRect(
                template,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 3f),
                new Vector2(0f, 177f));
            templateImage.type = Image.Type.Sliced;
            templateImage.raycastTarget = true;

            ScrollRect scrollRect = templateImage.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 25.5f;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(template, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            SetRect(viewportRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-6f, -6f));

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportRect, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            SetRect(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 432f));

            Image itemBackground = CreateImage("Item", content, null, Color.clear);
            SetRect(
                itemBackground.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(0f, 27f));
            itemBackground.raycastTarget = true;

            Toggle toggle = itemBackground.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = itemBackground;
            toggle.transition = Selectable.Transition.ColorTint;
            ColorBlock itemColors = CreateControlColors();
            itemColors.normalColor = Color.clear;
            itemColors.highlightedColor = new Color32(55, 63, 76, 255);
            itemColors.selectedColor = new Color32(42, 76, 96, 255);
            toggle.colors = itemColors;

            itemText = CreateText(
                "Item Label",
                itemBackground.transform,
                font,
                "1",
                16.5f,
                FontStyles.Normal,
                PrimaryTextColor,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-9f, 0f));

            scrollRect.viewport = viewportRect;
            scrollRect.content = content;
            return template;
        }

        private static List<TMP_Dropdown.OptionData> CreateMeterOptions()
        {
            var options = new List<TMP_Dropdown.OptionData>(16);
            for (int value = 1; value <= 16; value++)
            {
                options.Add(new TMP_Dropdown.OptionData(value.ToString()));
            }
            return options;
        }

        private static ColorBlock CreateControlColors()
        {
            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color32(207, 242, 255, 255),
                pressedColor = new Color32(157, 220, 246, 255),
                selectedColor = new Color32(207, 242, 255, 255),
                disabledColor = new Color32(100, 103, 112, 170),
                colorMultiplier = 1f,
                fadeDuration = 0.12f,
            };
        }

        private static void CreatePreviewScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(18, 19, 24, 255);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject preview = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (preview == null)
            {
                throw new InvalidOperationException("Failed to instantiate the metronome panel preview.");
            }
            preview.name = "MetronomePanelPreview";
            preview.AddComponent<MetronomePanelPreviewHarness>();

            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
        }

        private static void EnsureAssetFolders()
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ArtRoot));
            Directory.CreateDirectory(Path.Combine(ProjectRoot, FontRoot));
            Directory.CreateDirectory(Path.Combine(ProjectRoot, PrefabRoot));
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ResourcesRoot));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void EnsureRoundedPanelArt()
        {
            string absolutePath = Path.Combine(ProjectRoot, PanelSpritePath);
            if (!File.Exists(absolutePath))
            {
                const int size = 64;
                const float radius = 14f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float distanceX = Mathf.Max(radius - x, 0f, x - (size - 1f - radius));
                        float distanceY = Mathf.Max(radius - y, 0f, y - (size - 1f - radius));
                        float distance = Mathf.Sqrt(distanceX * distanceX + distanceY * distanceY);
                        byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius + 0.5f - distance) * 255f);
                        pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(PanelSpritePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = new Vector4(24f, 24f, 24f, 24f);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static void EnsureToggleArt()
        {
            EnsureRoundedSpriteFile(ToggleTrackSpritePath, 192, 104, 184f, 96f, 48f);
            EnsureRoundedSpriteFile(ToggleKnobSpritePath, 104, 104, 96f, 96f, 48f);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureSpriteImporter(ToggleTrackSpritePath, Vector4.zero);
            ConfigureSpriteImporter(ToggleKnobSpritePath, Vector4.zero);
        }

        private static void EnsureRoundedSpriteFile(
            string assetPath,
            int width,
            int height,
            float shapeWidth,
            float shapeHeight,
            float radius)
        {
            string absolutePath = Path.Combine(ProjectRoot, assetPath);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float halfShapeWidth = shapeWidth * 0.5f;
            float halfShapeHeight = shapeHeight * 0.5f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float distanceX = Mathf.Abs(x + 0.5f - halfWidth) - (halfShapeWidth - radius);
                    float distanceY = Mathf.Abs(y + 0.5f - halfHeight) - (halfShapeHeight - radius);
                    float outsideDistance = Mathf.Sqrt(
                        Mathf.Max(distanceX, 0f) * Mathf.Max(distanceX, 0f)
                        + Mathf.Max(distanceY, 0f) * Mathf.Max(distanceY, 0f));
                    float signedDistance = outsideDistance + Mathf.Min(Mathf.Max(distanceX, distanceY), 0f) - radius;
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - signedDistance) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void ConfigureSpriteImporter(string assetPath, Vector4 border)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Could not import generated UI sprite: {assetPath}");
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            var textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(textureSettings);
            importer.SaveAndReimport();
        }

        private static TMP_FontAsset EnsureFontAsset()
        {
            EnsureTmpSettings();
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
            {
                AssignDefaultFontAsset(existing);
                return existing;
            }

            Font source = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (source == null)
            {
                throw new FileNotFoundException("MapleStory Bold font source was not found.", FontSourcePath);
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                source,
                72,
                8,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                throw new InvalidOperationException("Failed to create the dynamic MapleStory TMP font asset.");
            }

            fontAsset.name = "MAPLESTORY_OTF_BOLD Dynamic SDF";
            fontAsset.atlasTextures[0].name = "MAPLESTORY_OTF_BOLD Atlas";
            fontAsset.material.name = "MAPLESTORY_OTF_BOLD Material";
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssignDefaultFontAsset(fontAsset);
            return fontAsset;
        }

        private static void EnsureTmpSettings()
        {
            string settingsPath = ResourcesRoot + "/TMP Settings.asset";
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TMP_Settings>();
                settings.name = "TMP Settings";
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty version = serialized.FindProperty("assetVersion");
            if (version != null)
            {
                version.stringValue = "2";
            }
            SerializedProperty clearDynamicData = serialized.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearDynamicData != null)
            {
                clearDynamicData.boolValue = false;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void AssignDefaultFontAsset(TMP_FontAsset fontAsset)
        {
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(ResourcesRoot + "/TMP Settings.asset");
            if (settings == null)
            {
                return;
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty defaultFont = serialized.FindProperty("m_defaultFontAsset");
            if (defaultFont != null)
            {
                defaultFont.objectReferenceValue = fontAsset;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string value,
            float size,
            FontStyles style,
            Color color,
            TextAlignmentOptions alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 dimensions)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            gameObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            SetRect(text.rectTransform, anchorMin, anchorMax, pivot, position, dimensions);
            return text;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 position,
            Vector2 dimensions)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Unable to locate the Unity project root.");

        private static string RuntimeAssetRoot => Path.GetFullPath(
            Path.Combine(ProjectRoot, "..", "EnhancedCountdown", "Assets"));
    }
}
