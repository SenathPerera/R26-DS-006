using System.Collections.Generic;
using AdaptiveAudioVR.Audio;
using AdaptiveAudioVR.Core;
using AdaptiveAudioVR.Integration;
using AdaptiveAudioVR.RL.Agent;
using AdaptiveAudioVR.Safety;
using AdaptiveAudioVR.Signals;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace AdaptiveAudioVR.UI
{
    public class AdaptiveDashboardInstaller : MonoBehaviour
    {
        [Header("Runtime References")]
        [SerializeField] private PrototypeBootstrap bootstrap;
        [SerializeField] private SignalSimulator signalSimulator;
        [SerializeField] private SafetyManager safetyManager;
        [SerializeField] private LyriaClipGenerationService lyriaClipGenerationService;
        [SerializeField] private LyriaRealtimeStreamingService lyriaRealtimeStreamingService;
        [SerializeField] private AudioMixerController audioMixerController;
        [SerializeField] private AudioSource meditationSource;
        [SerializeField] private AudioSource ambientSource;

        [Header("Sampling")]
        [SerializeField] private int historySamples = 180;
        [SerializeField] private int waveformSamples = 256;
        [SerializeField] private int spectrumBars = 32;
        [SerializeField] private float historySampleInterval = 0.05f;

        [Header("Look")]
        [SerializeField] private Color backgroundColor = new Color(0.05f, 0.07f, 0.10f, 0.90f);
        [SerializeField] private Color panelColor = new Color(0.10f, 0.14f, 0.18f, 0.96f);
        [SerializeField] private Color borderColor = new Color(0.20f, 0.27f, 0.34f, 1f);
        [SerializeField] private Color textColor = new Color(0.92f, 0.95f, 0.98f, 1f);
        [SerializeField] private Color accentColor = new Color(0.28f, 0.78f, 0.76f, 1f);
        [SerializeField] private Color stressColor = new Color(0.96f, 0.40f, 0.40f, 1f);
        [SerializeField] private Color confidenceColor = new Color(0.34f, 0.71f, 0.97f, 1f);
        [SerializeField] private Color waveformColor = new Color(0.30f, 1.00f, 0.66f, 1f);
        [SerializeField] private Color mixedColor = new Color(0.52f, 1.00f, 0.82f, 1f);

        private const int HeaderFontSize = 20;
        private const int BodyFontSize = 14;
        private const int SmallFontSize = 12;

        private Font dashboardFont;
        private float[] stressHistory;
        private float[] confidenceHistory;
        private float[] meditationWave;
        private float[] ambientWave;
        private float[] mixedWave;
        private float[] meditationLevelHistory;
        private float[] ambientLevelHistory;
        private float[] mixedLevelHistory;
        private float nextHistoryTime;

        private Text stressValueLabel;
        private Text confidenceValueLabel;
        private Text simulationModeLabel;

        private Text strategyValueLabel;
        private Text controllerModeValueLabel;
        private Text actionValueLabel;
        private Text policyValueLabel;
        private Text rewardValueLabel;
        private Text safetyValueLabel;
        private Text causeValueLabel;
        private Text lyriaStatusValueLabel;
        private Text lyriaClipValueLabel;
        private Text realtimeStatusValueLabel;
        private Text realtimeMetricsValueLabel;
        private Text meditationLevelValueLabel;
        private Text ambientLevelValueLabel;
        private Text mixedLevelValueLabel;
        private Text outputCauseValueLabel;
        private Text outputComparisonValueLabel;

        private Slider stressSlider;
        private Slider confidenceSlider;
        private Toggle emergencyMuteToggle;
        private Button generateClipButton;
        private Button restoreRawClipButton;
        private Button refreshBackendButton;
        private Button checkRealtimeButton;
        private Button startRealtimeButton;
        private Button pauseRealtimeButton;
        private Button stopRealtimeButton;
        private Button syncRealtimeButton;
        private readonly List<Button> modeButtons = new List<Button>();

        private readonly Dictionary<string, Image> parameterFills = new Dictionary<string, Image>();
        private readonly Dictionary<string, Text> parameterValueLabels = new Dictionary<string, Text>();

        private UIWaveformTexture signalStressGraph;
        private UIWaveformTexture signalConfidenceGraph;
        private UIWaveformTexture meditationWaveGraph;
        private UIWaveformTexture ambientWaveGraph;
        private UIWaveformTexture mixedWaveGraph;

        private bool uiBuilt;

        private void Awake()
        {
            ResolveReferences();
            AllocateBuffers();
            BuildDashboard();
        }

        private void Update()
        {
            if (!uiBuilt)
            {
                return;
            }

            if (Time.unscaledTime >= nextHistoryTime)
            {
                AppendHistorySample();
                nextHistoryTime = Time.unscaledTime + Mathf.Max(0.01f, historySampleInterval);
            }

            UpdateSignalUi();
            UpdateDecisionUi();
            UpdateParameterUi();
            UpdateAudioGraphs();
        }

        private void ResolveReferences()
        {
            bootstrap ??= FindAnyObjectByType<PrototypeBootstrap>();
            signalSimulator ??= FindAnyObjectByType<SignalSimulator>();
            safetyManager ??= FindAnyObjectByType<SafetyManager>();
            lyriaClipGenerationService ??= FindAnyObjectByType<LyriaClipGenerationService>();
            lyriaRealtimeStreamingService ??= FindAnyObjectByType<LyriaRealtimeStreamingService>();
            audioMixerController ??= FindAnyObjectByType<AudioMixerController>();

            if (lyriaClipGenerationService == null)
            {
                lyriaClipGenerationService = GetComponent<LyriaClipGenerationService>();
                if (lyriaClipGenerationService == null)
                {
                    lyriaClipGenerationService = gameObject.AddComponent<LyriaClipGenerationService>();
                }
            }

            if (lyriaRealtimeStreamingService == null)
            {
                lyriaRealtimeStreamingService = GetComponent<LyriaRealtimeStreamingService>();
                if (lyriaRealtimeStreamingService == null)
                {
                    lyriaRealtimeStreamingService = gameObject.AddComponent<LyriaRealtimeStreamingService>();
                }
            }

            if (meditationSource == null)
            {
                GameObject meditationObject = GameObject.Find("MeditationPlayer");
                if (meditationObject != null)
                {
                    meditationSource = meditationObject.GetComponent<AudioSource>();
                }
            }

            if (ambientSource == null)
            {
                GameObject ambientObject = GameObject.Find("AmbientPlayer");
                if (ambientObject == null)
                {
                    ambientObject = GameObject.Find("AmbientPlayer ");
                }

                if (ambientObject != null)
                {
                    ambientSource = ambientObject.GetComponent<AudioSource>();
                }
            }
        }

        private void AllocateBuffers()
        {
            historySamples = Mathf.Max(60, historySamples);
            waveformSamples = Mathf.Clamp(waveformSamples, 64, 1024);
            spectrumBars = Mathf.Clamp(spectrumBars, 8, 64);

            stressHistory = new float[historySamples];
            confidenceHistory = new float[historySamples];
            meditationWave = new float[waveformSamples];
            ambientWave = new float[waveformSamples];
            mixedWave = new float[waveformSamples];
            meditationLevelHistory = new float[historySamples];
            ambientLevelHistory = new float[historySamples];
            mixedLevelHistory = new float[historySamples];

            for (int i = 0; i < historySamples; i++)
            {
                stressHistory[i] = 0.5f;
                confidenceHistory[i] = 0.75f;
                meditationLevelHistory[i] = 0f;
                ambientLevelHistory[i] = 0f;
                mixedLevelHistory[i] = 0f;
            }
        }

        private void BuildDashboard()
        {
            dashboardFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();

            RectTransform root = CreateRootCanvas();
            if (root == null)
            {
                return;
            }

            CreateBackground(root);

            RectTransform contentRoot = CreateUiElement("DashboardRoot", root).GetComponent<RectTransform>();
            contentRoot.anchorMin = Vector2.zero;
            contentRoot.anchorMax = Vector2.one;
            contentRoot.offsetMin = new Vector2(16f, 16f);
            contentRoot.offsetMax = new Vector2(-16f, -16f);

            var rootLayout = contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.spacing = 12f;
            rootLayout.padding = new RectOffset(0, 0, 0, 0);
            rootLayout.childControlHeight = true;
            rootLayout.childControlWidth = true;
            rootLayout.childForceExpandHeight = true;
            rootLayout.childForceExpandWidth = true;

            CreateTitle(contentRoot, "Adaptive Audio Validation Dashboard");

            RectTransform topRow = CreateRow(contentRoot, 1f);
            RectTransform bottomRow = CreateRow(contentRoot, 1f);

            BuildInputPanel(CreatePanel(topRow, "Panel 1  Input Signals"));
            BuildDecisionPanel(CreatePanel(topRow, "Panel 2  RL Decision"));
            BuildParameterPanel(CreatePanel(bottomRow, "Panel 3  Audio Control Parameters"));
            BuildAudioPanel(CreatePanel(bottomRow, "Panel 4  Audio Output Visualization"));

            uiBuilt = true;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem != null)
            {
                EnsureCompatibleInputModule(eventSystem.gameObject);

                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            EnsureCompatibleInputModule(eventSystemObject);
            Object.DontDestroyOnLoad(eventSystemObject);
        }

        private static void EnsureCompatibleInputModule(GameObject eventSystemObject)
        {
#if ENABLE_INPUT_SYSTEM
            if (eventSystemObject.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystemObject.AddComponent<InputSystemUIInputModule>();
            }

            StandaloneInputModule legacyModule = eventSystemObject.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                legacyModule.enabled = false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
#else
            if (eventSystemObject.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }

        private RectTransform CreateRootCanvas()
        {
            Canvas existingCanvas = GetComponentInParent<Canvas>();
            if (existingCanvas != null)
            {
                return existingCanvas.transform as RectTransform;
            }

            GameObject canvasObject = new GameObject("AdaptiveDashboardCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvasObject.transform as RectTransform;
        }

        private void CreateBackground(RectTransform root)
        {
            RectTransform background = CreateUiElement("DashboardBackground", root).GetComponent<RectTransform>();
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = Vector2.zero;
            background.offsetMax = Vector2.zero;

            Image image = background.gameObject.AddComponent<Image>();
            image.color = backgroundColor;
            image.raycastTarget = false;

            background.SetAsFirstSibling();
        }

        private void CreateTitle(RectTransform parent, string title)
        {
            Text titleText = CreateText(parent, title, 26, FontStyle.Bold, TextAnchor.MiddleLeft);
            LayoutElement layout = titleText.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 40f;
        }

        private RectTransform CreateRow(RectTransform parent, float flexibleHeight)
        {
            RectTransform row = CreateUiElement("Row", parent).GetComponent<RectTransform>();
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;

            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.flexibleHeight = flexibleHeight;
            element.minHeight = 280f;
            return row;
        }

        private RectTransform CreatePanel(RectTransform parent, string title)
        {
            RectTransform panel = CreateUiElement(title, parent).GetComponent<RectTransform>();
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = panelColor;
            panelImage.raycastTarget = false;

            Outline outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(1f, -1f);

            LayoutElement element = panel.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;

            CreateText(panel, title, HeaderFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            return panel;
        }

        private void BuildInputPanel(RectTransform panel)
        {
            CreateText(panel, "Signals driving adaptation and the active simulation mode.", BodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft);

            RectTransform summaryRow = CreateColumn(panel, 56f);
            stressValueLabel = CreateText(summaryRow, "Stress: 0.50", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            confidenceValueLabel = CreateText(summaryRow, "Confidence: 0.75", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            simulationModeLabel = CreateText(summaryRow, "Mode: Oscillation", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);

            RectTransform graphContainer = CreateGraphContainer(panel, 140f);
            signalStressGraph = CreateLineGraph(graphContainer, stressColor);
            signalConfidenceGraph = CreateLineGraph(graphContainer, confidenceColor);

            stressSlider = CreateSliderRow(panel, "Stress", OnStressSliderChanged);
            confidenceSlider = CreateSliderRow(panel, "Confidence", OnConfidenceSliderChanged);

            RectTransform buttonRow = CreateButtonRow(panel);
            modeButtons.Add(CreateButton(buttonRow, "Manual", () => signalSimulator?.SetModeManual()));
            modeButtons.Add(CreateButton(buttonRow, "Oscillation", () => signalSimulator?.SetModeOscillation()));
            modeButtons.Add(CreateButton(buttonRow, "RandomWalk", () => signalSimulator?.SetModeRandomWalk()));

            emergencyMuteToggle = CreateToggleRow(panel, "Emergency Mute", OnEmergencyMuteChanged);
        }

        private void BuildDecisionPanel(RectTransform panel)
        {
            strategyValueLabel = CreateText(panel, "Strategy: Waiting", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            controllerModeValueLabel = CreateText(panel, "Controller Mode: Waiting", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            actionValueLabel = CreateText(panel, "Action: Waiting", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            policyValueLabel = CreateText(panel, "Policy: Waiting", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            rewardValueLabel = CreateText(panel, "Reward: 0.00", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            safetyValueLabel = CreateText(panel, "Safety: Normal", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);

            RectTransform policyModeRow = CreateButtonRow(panel);
            CreateButton(policyModeRow, "Rule Only", () => bootstrap?.SetRuleOnlyMode());
            CreateButton(policyModeRow, "PPO Residual", () => bootstrap?.SetPpoResidualMode());

            CreateText(panel, "Cause of change", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            causeValueLabel = CreateText(panel, "Waiting for runtime data.", BodyFontSize, FontStyle.Normal, TextAnchor.UpperLeft);
            LayoutElement causeLayout = causeValueLabel.gameObject.AddComponent<LayoutElement>();
            causeLayout.flexibleHeight = 1f;
            causeLayout.minHeight = 142f;

            CreateText(panel, "Lyria Clip Control", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            RectTransform buttonRow = CreateButtonRow(panel);
            generateClipButton = CreateButton(buttonRow, "Generate Lyria Clip", OnGenerateLyriaClipRequested);
            restoreRawClipButton = CreateButton(buttonRow, "Restore Raw Clip", OnRestoreRawClipRequested);
            refreshBackendButton = CreateButton(buttonRow, "Refresh Backend", OnRefreshBackendRequested);

            lyriaStatusValueLabel = CreateText(panel, "Lyria status: waiting for backend check.", SmallFontSize, FontStyle.Normal, TextAnchor.UpperLeft);
            LayoutElement lyriaStatusLayout = lyriaStatusValueLabel.gameObject.AddComponent<LayoutElement>();
            lyriaStatusLayout.minHeight = 52f;

            lyriaClipValueLabel = CreateText(panel, "Clip source: raw meditation clip.", SmallFontSize, FontStyle.Normal, TextAnchor.UpperLeft);
            LayoutElement lyriaClipLayout = lyriaClipValueLabel.gameObject.AddComponent<LayoutElement>();
            lyriaClipLayout.minHeight = 52f;

            CreateText(panel, "Lyria Realtime Control", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            RectTransform realtimeButtonRow = CreateButtonRow(panel);
            checkRealtimeButton = CreateButton(realtimeButtonRow, "Check Realtime", OnCheckRealtimeRequested);
            startRealtimeButton = CreateButton(realtimeButtonRow, "Start Realtime", OnStartRealtimeRequested);
            pauseRealtimeButton = CreateButton(realtimeButtonRow, "Pause Realtime", OnPauseRealtimeRequested);
            stopRealtimeButton = CreateButton(realtimeButtonRow, "Stop Realtime", OnStopRealtimeRequested);
            syncRealtimeButton = CreateButton(realtimeButtonRow, "Sync Realtime", OnSyncRealtimeRequested);

            realtimeStatusValueLabel = CreateText(panel, "Realtime status: idle.", SmallFontSize, FontStyle.Normal, TextAnchor.UpperLeft);
            LayoutElement realtimeStatusLayout = realtimeStatusValueLabel.gameObject.AddComponent<LayoutElement>();
            realtimeStatusLayout.minHeight = 72f;

            realtimeMetricsValueLabel = CreateText(panel, "Realtime metrics: no live session.", SmallFontSize, FontStyle.Normal, TextAnchor.UpperLeft);
            LayoutElement realtimeMetricsLayout = realtimeMetricsValueLabel.gameObject.AddComponent<LayoutElement>();
            realtimeMetricsLayout.minHeight = 52f;
        }

        private void BuildParameterPanel(RectTransform panel)
        {
            CreateText(panel, "These are the control outputs the RL stack applies to playback.", BodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft);

            CreateMeterRow(panel, "Intensity", "intensity");
            CreateMeterRow(panel, "Density", "density");
            CreateMeterRow(panel, "Brightness", "brightness");
            CreateMeterRow(panel, "Tempo", "tempo");
            CreateMeterRow(panel, "Fade", "fade");
            CreateMeterRow(panel, "Ambient Mix", "ambientMix");
            CreateMeterRow(panel, "Music Mix", "musicMix");
        }

        private void BuildAudioPanel(RectTransform panel)
        {
            CreateText(panel, "Smoothed level traces make source differences easier to validate than raw PCM oscillation.", BodyFontSize, FontStyle.Normal, TextAnchor.MiddleLeft);
            meditationWaveGraph = CreateTitledGraph(panel, "Meditation Source Level Trace", waveformColor, 68f);
            meditationLevelValueLabel = CreateText(panel, "Meditation Level: 0.000", SmallFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            ambientWaveGraph = CreateTitledGraph(panel, "Ambient Source Level Trace", accentColor, 68f);
            ambientLevelValueLabel = CreateText(panel, "Ambient Level: 0.000", SmallFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            mixedWaveGraph = CreateTitledGraph(panel, "Final Mixed Output Level Trace", mixedColor, 100f);
            mixedLevelValueLabel = CreateText(panel, "Final Mix Level: 0.000", SmallFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            outputCauseValueLabel = CreateText(panel, "Output cause: waiting for runtime data.", SmallFontSize, FontStyle.Normal, TextAnchor.UpperLeft);
            LayoutElement outputCauseLayout = outputCauseValueLabel.gameObject.AddComponent<LayoutElement>();
            outputCauseLayout.minHeight = 42f;
            outputComparisonValueLabel = CreateText(panel, "Dominant contribution: waiting for runtime data.", SmallFontSize, FontStyle.Normal, TextAnchor.UpperLeft);
            LayoutElement outputComparisonLayout = outputComparisonValueLabel.gameObject.AddComponent<LayoutElement>();
            outputComparisonLayout.minHeight = 22f;
        }

        private RectTransform CreateColumn(RectTransform parent, float preferredHeight = -1f)
        {
            RectTransform column = CreateUiElement("Column", parent).GetComponent<RectTransform>();
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            if (preferredHeight > 0f)
            {
                LayoutElement element = column.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = preferredHeight;
            }

            return column;
        }

        private RectTransform CreateGraphContainer(RectTransform parent, float preferredHeight)
        {
            RectTransform graph = CreateUiElement("Graph", parent).GetComponent<RectTransform>();
            LayoutElement element = graph.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;

            Image image = graph.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.10f, 0.14f, 1f);
            image.raycastTarget = false;
            return graph;
        }

        private UIWaveformTexture CreateLineGraph(RectTransform parent, Color color)
        {
            RectTransform lineTransform = CreateUiElement("Line", parent).GetComponent<RectTransform>();
            lineTransform.anchorMin = Vector2.zero;
            lineTransform.anchorMax = Vector2.one;
            lineTransform.offsetMin = new Vector2(6f, 6f);
            lineTransform.offsetMax = new Vector2(-6f, -6f);

            RawImage rawImage = lineTransform.gameObject.AddComponent<RawImage>();
            rawImage.raycastTarget = false;
            UIWaveformTexture lineGraph = lineTransform.gameObject.AddComponent<UIWaveformTexture>();
            lineGraph.SetColors(color, new Color(0f, 0f, 0f, 0f));
            return lineGraph;
        }

        private Slider CreateSliderRow(RectTransform parent, string label, UnityEngine.Events.UnityAction<float> callback)
        {
            RectTransform row = CreateUiElement(label + "Row", parent).GetComponent<RectTransform>();
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 32f;

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            CreateText(row, label, BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, 90f);

            GameObject sliderObject = CreateUiElement(label + "Slider", row);
            LayoutElement sliderLayout = sliderObject.AddComponent<LayoutElement>();
            sliderLayout.flexibleWidth = 1f;

            Slider slider = sliderObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;

            Image background = sliderObject.AddComponent<Image>();
            background.color = new Color(0.16f, 0.20f, 0.25f, 1f);
            background.raycastTarget = true;
            slider.targetGraphic = background;

            RectTransform fillArea = CreateUiElement("FillArea", sliderObject.transform).GetComponent<RectTransform>();
            fillArea.anchorMin = new Vector2(0f, 0.25f);
            fillArea.anchorMax = new Vector2(1f, 0.75f);
            fillArea.offsetMin = new Vector2(10f, 0f);
            fillArea.offsetMax = new Vector2(-10f, 0f);

            RectTransform fill = CreateUiElement("Fill", fillArea).GetComponent<RectTransform>();
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = accentColor;
            fillImage.raycastTarget = false;

            RectTransform handleArea = CreateUiElement("HandleArea", sliderObject.transform).GetComponent<RectTransform>();
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);

            RectTransform handle = CreateUiElement("Handle", handleArea).GetComponent<RectTransform>();
            handle.sizeDelta = new Vector2(16f, 28f);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Color.white;

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(callback);
            return slider;
        }

        private RectTransform CreateButtonRow(RectTransform parent)
        {
            RectTransform row = CreateUiElement("ButtonRow", parent).GetComponent<RectTransform>();
            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 34f;

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            return row;
        }

        private Button CreateButton(RectTransform parent, string label, UnityEngine.Events.UnityAction callback)
        {
            RectTransform buttonTransform = CreateUiElement(label + "Button", parent).GetComponent<RectTransform>();
            Image image = buttonTransform.gameObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.22f, 0.28f, 1f);

            Button button = buttonTransform.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(callback);

            ColorBlock colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = new Color(0.22f, 0.28f, 0.34f, 1f);
            colors.pressedColor = accentColor;
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            CreateText(buttonTransform, label, SmallFontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            return button;
        }

        private Toggle CreateToggleRow(RectTransform parent, string label, UnityEngine.Events.UnityAction<bool> callback)
        {
            RectTransform row = CreateUiElement(label + "ToggleRow", parent).GetComponent<RectTransform>();
            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 30f;

            Toggle toggle = row.gameObject.AddComponent<Toggle>();

            RectTransform background = CreateUiElement("Background", row).GetComponent<RectTransform>();
            background.anchorMin = new Vector2(0f, 0.5f);
            background.anchorMax = new Vector2(0f, 0.5f);
            background.pivot = new Vector2(0f, 0.5f);
            background.sizeDelta = new Vector2(20f, 20f);
            background.anchoredPosition = new Vector2(10f, 0f);
            Image backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.16f, 0.20f, 0.25f, 1f);

            RectTransform checkmark = CreateUiElement("Checkmark", background).GetComponent<RectTransform>();
            checkmark.anchorMin = new Vector2(0.2f, 0.2f);
            checkmark.anchorMax = new Vector2(0.8f, 0.8f);
            checkmark.offsetMin = Vector2.zero;
            checkmark.offsetMax = Vector2.zero;
            Image checkmarkImage = checkmark.gameObject.AddComponent<Image>();
            checkmarkImage.color = stressColor;

            RectTransform labelTransform = CreateUiElement("Label", row).GetComponent<RectTransform>();
            labelTransform.anchorMin = Vector2.zero;
            labelTransform.anchorMax = Vector2.one;
            labelTransform.offsetMin = new Vector2(40f, 0f);
            labelTransform.offsetMax = Vector2.zero;
            CreateText(labelTransform, label, BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);

            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;
            toggle.onValueChanged.AddListener(callback);
            return toggle;
        }

        private void CreateMeterRow(RectTransform parent, string displayName, string key)
        {
            RectTransform row = CreateUiElement(displayName + "Meter", parent).GetComponent<RectTransform>();
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 32f;

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            CreateText(row, displayName, BodyFontSize, FontStyle.Bold, TextAnchor.MiddleLeft, 110f);

            RectTransform barRoot = CreateUiElement(displayName + "Bar", row).GetComponent<RectTransform>();
            LayoutElement barLayout = barRoot.gameObject.AddComponent<LayoutElement>();
            barLayout.flexibleWidth = 1f;

            Image barBackground = barRoot.gameObject.AddComponent<Image>();
            barBackground.color = new Color(0.16f, 0.20f, 0.25f, 1f);
            barBackground.raycastTarget = false;

            RectTransform fill = CreateUiElement("Fill", barRoot).GetComponent<RectTransform>();
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = accentColor;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 0f;
            fillImage.raycastTarget = false;

            Text valueLabel = CreateText(row, "0.00", BodyFontSize, FontStyle.Bold, TextAnchor.MiddleRight, 48f);

            parameterFills[key] = fillImage;
            parameterValueLabels[key] = valueLabel;
        }

        private UIWaveformTexture CreateTitledGraph(RectTransform parent, string label, Color color, float preferredHeight)
        {
            CreateText(parent, label, SmallFontSize, FontStyle.Bold, TextAnchor.MiddleLeft);
            RectTransform graphRoot = CreateGraphContainer(parent, preferredHeight);
            return CreateLineGraph(graphRoot, color);
        }

        private Text CreateText(Transform parent, string content, int fontSize, FontStyle style, TextAnchor anchor, float preferredWidth = -1f)
        {
            GameObject textObject = CreateUiElement("Text", parent);
            Text text = textObject.AddComponent<Text>();
            text.font = dashboardFont;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = textColor;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;

            if (preferredWidth > 0f)
            {
                LayoutElement layout = textObject.AddComponent<LayoutElement>();
                layout.preferredWidth = preferredWidth;
            }

            return text;
        }

        private static GameObject CreateUiElement(string name, Transform parent)
        {
            GameObject element = new GameObject(name, typeof(RectTransform));
            element.transform.SetParent(parent, false);
            return element;
        }

        private void AppendHistorySample()
        {
            SignalPacket signal = bootstrap != null ? bootstrap.CurrentSignal : signalSimulator != null ? signalSimulator.CurrentSignal : SignalPacket.CreateDefault();

            ShiftAppend(stressHistory, signal.stress);
            ShiftAppend(confidenceHistory, signal.confidence);
            ShiftAppend(meditationLevelHistory, ComputeRmsLevel(meditationWave));
            ShiftAppend(ambientLevelHistory, ComputeRmsLevel(ambientWave));
            ShiftAppend(mixedLevelHistory, ComputeRmsLevel(mixedWave));
        }

        private void UpdateSignalUi()
        {
            SignalPacket signal = bootstrap != null ? bootstrap.CurrentSignal : signalSimulator != null ? signalSimulator.CurrentSignal : SignalPacket.CreateDefault();

            if (stressValueLabel != null)
            {
                stressValueLabel.text = $"Stress: {signal.stress:F2}";
            }

            if (confidenceValueLabel != null)
            {
                confidenceValueLabel.text = $"Confidence: {signal.confidence:F2}";
            }

            if (simulationModeLabel != null)
            {
                string mode = signal.hasPhysiologyWindow
                    ? $"Component B window {signal.sequenceId}"
                    : signalSimulator != null ? $"Simulation {signalSimulator.CurrentMode}" : "Unavailable";
                simulationModeLabel.text = $"Input: {mode} | Quality {signal.signalQuality:F2}";
            }

            if (stressSlider != null && !Mathf.Approximately(stressSlider.value, signal.stress))
            {
                stressSlider.SetValueWithoutNotify(signal.stress);
            }

            if (confidenceSlider != null && !Mathf.Approximately(confidenceSlider.value, signal.confidence))
            {
                confidenceSlider.SetValueWithoutNotify(signal.confidence);
            }

            if (emergencyMuteToggle != null && safetyManager != null)
            {
                emergencyMuteToggle.SetIsOnWithoutNotify(safetyManager.EmergencyMute);
            }

            signalStressGraph?.SetData(stressHistory, 0f, 1f);
            signalConfidenceGraph?.SetData(confidenceHistory, 0f, 1f);
        }

        private void UpdateDecisionUi()
        {
            if (bootstrap == null)
            {
                return;
            }

            SignalPacket signal = bootstrap.CurrentSignal;
            AudioParameters parameters = bootstrap.CurrentParameters;

            if (strategyValueLabel != null)
            {
                strategyValueLabel.text = $"Strategy: {bootstrap.CurrentStrategyName}";
            }

            if (controllerModeValueLabel != null)
            {
                controllerModeValueLabel.text = $"Controller Mode: {bootstrap.CurrentControllerMode}";
            }

            if (actionValueLabel != null)
            {
                actionValueLabel.text = $"Action: {bootstrap.CurrentActionName}";
            }

            if (policyValueLabel != null)
            {
                policyValueLabel.text = $"Policy: {bootstrap.CurrentRLMode} | {bootstrap.CurrentPolicyStatus}";
            }

            if (rewardValueLabel != null)
            {
                AudioRLRewardBreakdown reward = bootstrap.CurrentRewardBreakdown;
                rewardValueLabel.text = $"Reward: {bootstrap.CurrentReward:F2} | Stress {reward.stressImprovement:+0.00;-0.00;0.00} | Preference {reward.preferenceMatch:F2}";
            }

            if (safetyValueLabel != null)
            {
                safetyValueLabel.text = $"Safety: {bootstrap.CurrentSafetyMode} | Replay {bootstrap.CurrentReplayBufferCount}";
            }

            if (causeValueLabel != null)
            {
                causeValueLabel.text =
                    $"Cause chain:\n" +
                    $"{bootstrap.CurrentRLStateSummary}.\n" +
                    $"Rule action [{bootstrap.CurrentRuleAction}] + residual [{bootstrap.CurrentResidualAction}].\n" +
                    $"Final safe action [{bootstrap.CurrentFinalSafeAction}] -> '{bootstrap.CurrentActionName}'.\n" +
                    $"Targets: I {parameters.intensity:F2}, D {parameters.density:F2}, B {parameters.brightness:F2}, Tempo {parameters.tempo:F2}, Fade {parameters.fade:F2}, Ambient {parameters.ambientMix:F2}, Music {parameters.musicMix:F2}.\n" +
                    $"Safety: {bootstrap.CurrentSafetyReason}";
            }

            if (lyriaClipGenerationService != null)
            {
                if (lyriaStatusValueLabel != null)
                {
                    string cooldown = lyriaClipGenerationService.IsGenerating
                        ? "Generating now."
                        : lyriaClipGenerationService.RemainingCooldownSeconds > 0.01f
                            ? $"Cooldown {lyriaClipGenerationService.RemainingCooldownSeconds:F1}s."
                            : "Ready to request.";

                    lyriaStatusValueLabel.text =
                        $"Lyria status: {lyriaClipGenerationService.LastBackendHealthSummary}\n" +
                        $"Request state: {lyriaClipGenerationService.LastStatusMessage} {cooldown}\n" +
                        $"Reason: {lyriaClipGenerationService.LastGenerationReason} | Outcome: {lyriaClipGenerationService.LastGenerationOutcome}\n" +
                        $"Queue: {lyriaClipGenerationService.StandbyStatusLabel}";
                }

                if (lyriaClipValueLabel != null)
                {
                    string clipName = string.IsNullOrWhiteSpace(lyriaClipGenerationService.LastGeneratedClipPath)
                        ? "none yet"
                        : System.IO.Path.GetFileName(lyriaClipGenerationService.LastGeneratedClipPath);
                    string standbyName = lyriaClipGenerationService.HasStandbyClip
                        ? lyriaClipGenerationService.StandbyClipFileName
                        : "none";

                    lyriaClipValueLabel.text =
                        $"Clip source: {lyriaClipGenerationService.CurrentPlaybackSourceLabel}. " +
                        $"Last generated model: {(string.IsNullOrWhiteSpace(lyriaClipGenerationService.LastGeneratedModel) ? "none" : lyriaClipGenerationService.LastGeneratedModel)}. " +
                        $"Last clip file: {clipName}. " +
                        $"Standby clip: {standbyName}. " +
                        $"Cache: {lyriaClipGenerationService.LastCacheState}. " +
                        $"Count: {lyriaClipGenerationService.SuccessfulGenerationCount} ok / {lyriaClipGenerationService.CacheHitCount} cache / {lyriaClipGenerationService.FailedGenerationCount} failed.";
                }

                if (generateClipButton != null)
                {
                    generateClipButton.interactable =
                        !lyriaClipGenerationService.IsGenerating
                        && lyriaClipGenerationService.IsBackendReachable
                        && lyriaClipGenerationService.IsBackendConfigured;
                }

                if (restoreRawClipButton != null)
                {
                    restoreRawClipButton.interactable = true;
                }

                if (refreshBackendButton != null)
                {
                    refreshBackendButton.interactable = !lyriaClipGenerationService.IsRefreshingBackendHealth;
                }
            }

            if (lyriaRealtimeStreamingService != null)
            {
                if (realtimeStatusValueLabel != null)
                {
                    string capabilityState = lyriaRealtimeStreamingService.IsCapabilityCheckRunning
                        ? "Checking"
                        : !lyriaRealtimeStreamingService.HasCapabilityCheckResult
                            ? "Not checked"
                            : lyriaRealtimeStreamingService.LastCapabilityAvailable
                                ? "Ready"
                                : "Unavailable";

                    realtimeStatusValueLabel.text =
                        $"Realtime status: {lyriaRealtimeStreamingService.LastStatusMessage}\n" +
                        $"Capability: {capabilityState} | Model: {lyriaRealtimeStreamingService.LastCapabilityModel}\n" +
                        $"Capability detail: {lyriaRealtimeStreamingService.LastCapabilityMessage}\n" +
                        $"State: {lyriaRealtimeStreamingService.LastServerState} | " +
                        $"Connected: {lyriaRealtimeStreamingService.IsConnected} | " +
                        $"Streaming: {lyriaRealtimeStreamingService.IsStreaming} | " +
                        $"Paused: {lyriaRealtimeStreamingService.IsPaused}";
                }

                if (realtimeMetricsValueLabel != null)
                {
                    realtimeMetricsValueLabel.text =
                        $"Realtime metrics: Buffer {lyriaRealtimeStreamingService.BufferedSeconds:F2}s | " +
                        $"Underflow {lyriaRealtimeStreamingService.UnderflowSampleCount} | " +
                        $"Dropped {lyriaRealtimeStreamingService.DroppedSampleCount} | " +
                        $"Checked {lyriaRealtimeStreamingService.LastCapabilityCheckedAtUtc}\n" +
                        $"Prompt sync: {(string.IsNullOrWhiteSpace(lyriaRealtimeStreamingService.LastPromptSummary) ? "none yet" : lyriaRealtimeStreamingService.LastPromptSummary)}";
                }

                if (checkRealtimeButton != null)
                {
                    checkRealtimeButton.interactable = !lyriaRealtimeStreamingService.IsCapabilityCheckRunning;
                }

                if (startRealtimeButton != null)
                {
                    startRealtimeButton.interactable =
                        !lyriaRealtimeStreamingService.IsConnecting
                        && !lyriaRealtimeStreamingService.IsCapabilityCheckRunning
                        && (!lyriaRealtimeStreamingService.HasCapabilityCheckResult || lyriaRealtimeStreamingService.LastCapabilityAvailable);
                }

                if (pauseRealtimeButton != null)
                {
                    pauseRealtimeButton.interactable = lyriaRealtimeStreamingService.IsConnected;
                }

                if (stopRealtimeButton != null)
                {
                    stopRealtimeButton.interactable = lyriaRealtimeStreamingService.IsRealtimeActive;
                }

                if (syncRealtimeButton != null)
                {
                    syncRealtimeButton.interactable = lyriaRealtimeStreamingService.IsConnected;
                }
            }
        }

        private void UpdateParameterUi()
        {
            if (bootstrap == null)
            {
                return;
            }

            AudioParameters parameters = bootstrap.CurrentParameters;
            SetMeter("intensity", parameters.intensity);
            SetMeter("density", parameters.density);
            SetMeter("brightness", parameters.brightness);
            SetMeter("tempo", parameters.tempo);
            SetMeter("fade", parameters.fade);
            SetMeter("ambientMix", parameters.ambientMix);
            SetMeter("musicMix", parameters.musicMix);
        }

        private void UpdateAudioGraphs()
        {
            if (audioMixerController != null)
            {
                audioMixerController.GetMeditationOutputData(meditationWave);
            }
            else
            {
                SampleWaveform(meditationSource, meditationWave);
            }
            SampleWaveform(ambientSource, ambientWave);
            AudioListener.GetOutputData(mixedWave, 0);

            float meditationLevel = ComputeRmsLevel(meditationWave);
            float ambientLevel = ComputeRmsLevel(ambientWave);
            float mixedLevel = ComputeRmsLevel(mixedWave);

            SetLevelTraceData(meditationWaveGraph, meditationLevelHistory);
            SetLevelTraceData(ambientWaveGraph, ambientLevelHistory);
            SetLevelTraceData(mixedWaveGraph, mixedLevelHistory);

            if (meditationLevelValueLabel != null)
            {
                meditationLevelValueLabel.text = $"Meditation Level: {meditationLevel:F3}";
            }

            if (ambientLevelValueLabel != null)
            {
                ambientLevelValueLabel.text = $"Ambient Level: {ambientLevel:F3}";
            }

            if (mixedLevelValueLabel != null)
            {
                mixedLevelValueLabel.text = $"Final Mix Level: {mixedLevel:F3}";
            }

            if (outputCauseValueLabel != null && bootstrap != null)
            {
                SignalPacket signal = bootstrap.CurrentSignal;
                AudioParameters parameters = bootstrap.CurrentParameters;
                outputCauseValueLabel.text =
                    $"Output cause: Stress {signal.stress:F2} and Confidence {signal.confidence:F2} -> RL action '{bootstrap.CurrentActionName}' -> " +
                    $"Music {parameters.musicMix:F2}, Ambient {parameters.ambientMix:F2} -> Final Mix Level {mixedLevel:F3}. " +
                    $"Lyria outcome: {(lyriaClipGenerationService != null ? lyriaClipGenerationService.LastGenerationOutcome : "Unavailable")}";
            }

            if (outputComparisonValueLabel != null)
            {
                string dominant = meditationLevel > ambientLevel
                    ? "Meditation source currently dominates the output."
                    : ambientLevel > meditationLevel
                        ? "Ambient source currently dominates the output."
                        : "Meditation and ambient are currently balanced.";

                string sourceLabel = lyriaClipGenerationService != null
                    ? lyriaClipGenerationService.CurrentPlaybackSourceLabel
                    : "Unknown source";

                outputComparisonValueLabel.text =
                    $"Dominant contribution: {dominant} Playback source: {sourceLabel}. Meditation {meditationLevel:F3}, Ambient {ambientLevel:F3}, Final Mix {mixedLevel:F3}.";
            }
        }

        private static void SetLevelTraceData(UIWaveformTexture graph, float[] history)
        {
            if (graph == null || history == null || history.Length == 0)
            {
                return;
            }

            float maxValue = 0.05f;
            for (int i = 0; i < history.Length; i++)
            {
                maxValue = Mathf.Max(maxValue, history[i]);
            }

            graph.SetData(history, 0f, maxValue * 1.1f);
        }

        private void SetMeter(string key, float value)
        {
            if (parameterFills.TryGetValue(key, out Image fill))
            {
                fill.fillAmount = Mathf.Clamp01(value);
            }

            if (parameterValueLabels.TryGetValue(key, out Text label))
            {
                label.text = value.ToString("F2");
            }
        }

        private static void ShiftAppend(float[] buffer, float value)
        {
            for (int i = 0; i < buffer.Length - 1; i++)
            {
                buffer[i] = buffer[i + 1];
            }

            buffer[buffer.Length - 1] = value;
        }

        private static void SampleWaveform(AudioSource source, float[] buffer)
        {
            if (source == null)
            {
                ZeroBuffer(buffer);
                return;
            }

            source.GetOutputData(buffer, 0);
        }

        private static void ZeroBuffer(float[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0f;
            }
        }

        private static float ComputeRmsLevel(float[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                sum += buffer[i] * buffer[i];
            }

            return Mathf.Sqrt(sum / buffer.Length);
        }

        private void OnStressSliderChanged(float value)
        {
            signalSimulator?.SetModeManual();
            signalSimulator?.SetManualStress(value);
        }

        private void OnConfidenceSliderChanged(float value)
        {
            signalSimulator?.SetModeManual();
            signalSimulator?.SetManualConfidence(value);
        }

        private void OnEmergencyMuteChanged(bool muted)
        {
            if (safetyManager != null)
            {
                safetyManager.EmergencyMute = muted;
            }
        }

        private void OnGenerateLyriaClipRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Generate Lyria Clip button pressed.", this);
            lyriaClipGenerationService?.RequestGenerationFromCurrentPrompt();
        }

        private void OnRestoreRawClipRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Restore Raw Clip button pressed.", this);
            lyriaClipGenerationService?.RestoreOriginalMeditationClip();
        }

        private void OnRefreshBackendRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Refresh Backend button pressed.", this);
            lyriaClipGenerationService?.RequestBackendHealthRefresh();
        }

        private void OnStartRealtimeRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Start Realtime button pressed.", this);
            lyriaRealtimeStreamingService?.StartRealtimeStream();
        }

        private void OnCheckRealtimeRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Check Realtime button pressed.", this);
            lyriaRealtimeStreamingService?.RefreshRealtimeCapability();
        }

        private void OnPauseRealtimeRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Pause Realtime button pressed.", this);
            if (lyriaRealtimeStreamingService == null)
            {
                return;
            }

            if (lyriaRealtimeStreamingService.IsPaused)
            {
                lyriaRealtimeStreamingService.ResumeRealtimeStream();
            }
            else
            {
                lyriaRealtimeStreamingService.PauseRealtimeStream();
            }
        }

        private void OnStopRealtimeRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Stop Realtime button pressed.", this);
            lyriaRealtimeStreamingService?.StopRealtimeStream();
        }

        private void OnSyncRealtimeRequested()
        {
            Debug.Log("[AdaptiveDashboardInstaller] Sync Realtime button pressed.", this);
            lyriaRealtimeStreamingService?.PushCurrentFrameToRealtime();
        }
    }
}
