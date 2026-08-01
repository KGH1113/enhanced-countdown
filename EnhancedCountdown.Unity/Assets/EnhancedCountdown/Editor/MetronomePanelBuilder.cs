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
            Debug.Log($"Built {target} UI bundle at {Path.Combine(outputDirectory, BundleName)}");
        }

        private static void CreatePanelPrefab(TMP_FontAsset font)
        {
            Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
            if (panelSprite == null)
            {
                throw new InvalidOperationException("The rounded panel sprite is unavailable.");
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
                    new Vector2(-32f, 32f),
                    new Vector2(480f, 380f));
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
                    new Vector2(-40f, 1f));

                CreateText(
                    "HeaderText",
                    panel.transform,
                    font,
                    "METRONOME",
                    20f,
                    FontStyles.Bold,
                    AccentColor,
                    TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(28f, -20f),
                    new Vector2(424f, 28f));

                Image headerDivider = CreateImage("HeaderDivider", panel.transform, null, new Color(1f, 1f, 1f, 0.09f));
                SetRect(
                    headerDivider.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(28f, -62f),
                    new Vector2(424f, 1f));

                CreateText(
                    "BpmLabel",
                    panel.transform,
                    font,
                    "CLICK BPM",
                    17f,
                    FontStyles.Bold,
                    SecondaryTextColor,
                    TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(28f, -76f),
                    new Vector2(424f, 22f));

                CreateBpmInput(panel.transform, panelSprite, font);

                CreateActionButton("Divide2Button", panel.transform, panelSprite, font, "÷2", new Vector2(28f, -188f));
                CreateActionButton("Multiply2Button", panel.transform, panelSprite, font, "×2", new Vector2(136f, -188f));
                CreateActionButton("Divide3Button", panel.transform, panelSprite, font, "÷3", new Vector2(244f, -188f));
                CreateActionButton("Multiply3Button", panel.transform, panelSprite, font, "×3", new Vector2(352f, -188f));

                Image sectionDivider = CreateImage("SectionDivider", panel.transform, null, new Color(1f, 1f, 1f, 0.09f));
                SetRect(
                    sectionDivider.rectTransform,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(28f, -254f),
                    new Vector2(424f, 1f));

                CreateText(
                    "TimeSignatureLabel",
                    panel.transform,
                    font,
                    "TIME SIGNATURE",
                    17f,
                    FontStyles.Bold,
                    SecondaryTextColor,
                    TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(28f, -274f),
                    new Vector2(424f, 22f));

                CreateDropdown("NumeratorDropdown", panel.transform, panelSprite, font, new Vector2(52f, -306f));
                CreateText(
                    "TimeSignatureSlash",
                    panel.transform,
                    font,
                    "/",
                    34f,
                    FontStyles.Normal,
                    PrimaryTextColor,
                    TextAlignmentOptions.Center,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(220f, -306f),
                    new Vector2(40f, 52f));
                CreateDropdown("DenominatorDropdown", panel.transform, panelSprite, font, new Vector2(268f, -306f));

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
                new Vector2(28f, -102f),
                new Vector2(424f, 74f));
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

            GameObject viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(background.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            SetRect(
                viewportRect,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(-42f, 0f),
                new Vector2(-108f, -12f));

            TextMeshProUGUI inputText = CreateText(
                "Text",
                viewport.transform,
                font,
                "240.0",
                42f,
                FontStyles.Normal,
                PrimaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(4f, 0f),
                new Vector2(-8f, 0f));
            inputText.textWrappingMode = TextWrappingModes.NoWrap;

            TextMeshProUGUI placeholder = CreateText(
                "Placeholder",
                viewport.transform,
                font,
                "240.0",
                42f,
                FontStyles.Normal,
                new Color32(128, 132, 143, 150),
                TextAlignmentOptions.MidlineLeft,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(4f, 0f),
                new Vector2(-8f, 0f));

            input.textViewport = viewportRect;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.text = "240.0";

            CreateText(
                "BpmSuffix",
                background.transform,
                font,
                "BPM",
                22f,
                FontStyles.Bold,
                AccentColor,
                TextAlignmentOptions.MidlineRight,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f),
                new Vector2(80f, 0f));
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
                new Vector2(100f, 50f));
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateControlColors();

            CreateText(
                "Label",
                image.transform,
                font,
                label,
                25f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
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
                new Vector2(160f, 52f));
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            TMP_Dropdown dropdown = background.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.colors = CreateControlColors();

            TextMeshProUGUI caption = CreateText(
                "Label",
                background.transform,
                font,
                "4",
                26f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                new Vector2(-12f, 0f),
                new Vector2(-44f, -8f));

            CreateText(
                "Arrow",
                background.transform,
                font,
                "▼",
                16f,
                FontStyles.Normal,
                AccentColor,
                TextAlignmentOptions.Center,
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f),
                new Vector2(28f, 0f));

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
                new Vector2(0f, 4f),
                new Vector2(0f, 236f));
            templateImage.type = Image.Type.Sliced;
            templateImage.raycastTarget = true;

            ScrollRect scrollRect = templateImage.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 34f;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(template, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            SetRect(viewportRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8f, -8f));

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportRect, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            SetRect(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 576f));

            Image itemBackground = CreateImage("Item", content, null, Color.clear);
            SetRect(
                itemBackground.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(0f, 36f));
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
                22f,
                FontStyles.Normal,
                PrimaryTextColor,
                TextAlignmentOptions.Center,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-12f, 0f));

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
    }
}
