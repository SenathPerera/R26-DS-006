using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Quest Pairing Runtime Panel")]
    [DisallowMultipleComponent]
    public sealed class QuestPairingRuntimePanel : MonoBehaviour
    {
        private const int RequiredCodeLength = 6;

        [Header("Pairing")]
        [SerializeField]
        private SessionRelayPairingController pairingController = null;

        [SerializeField]
        private Camera viewingCamera = null;

        [Header("Comfortable Gaze Input")]
        [SerializeField, Min(0.5f)]
        private float panelDistanceMeters = 1.75f;

        [SerializeField, Min(0.5f)]
        private float dwellSeconds = 1.1f;

        private CancellationTokenSource lifetime;
        private readonly RaycastHit[] gazeHits = new RaycastHit[16];
        private QuestClientIdentityProvider identityProvider;
        private GameObject panelRoot;
        private Text codeText;
        private Text statusText;
        private string enteredCode = string.Empty;
        private QuestPairingGazeTarget currentTarget;
        private QuestPairingGazeTarget consumedTarget;
        private float currentDwellSeconds;

        public string EnteredCode => enteredCode;

        public string StatusText => statusText != null
            ? statusText.text
            : string.Empty;

        private void OnEnable()
        {
            lifetime = new CancellationTokenSource();
            identityProvider = new QuestClientIdentityProvider(
                new PlayerPrefsQuestClientIdentityStore());
            if (pairingController == null)
            {
                pairingController = GetComponent<
                    SessionRelayPairingController>();
            }

            if (viewingCamera == null)
            {
                viewingCamera = Camera.main;
            }

            BuildPanelIfRequired();
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            SetStatus(
                pairingController == null
                    ? "Pairing is not configured."
                    : "Look at a key until it activates.");
        }

        private void Update()
        {
            if (viewingCamera == null)
            {
                viewingCamera = Camera.main;
                BuildPanelIfRequired();
            }

            if (panelRoot == null
                || !panelRoot.activeSelf
                || pairingController == null
                || viewingCamera == null
                || pairingController.IsPairing)
            {
                return;
            }

            ProcessGaze();
        }

        private void OnDisable()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void OnValidate()
        {
            panelDistanceMeters = Mathf.Max(0.5f, panelDistanceMeters);
            dwellSeconds = Mathf.Max(0.5f, dwellSeconds);
        }

        public void AppendDigit(int digit)
        {
            if (digit < 0 || digit > 9 || enteredCode.Length >= RequiredCodeLength)
            {
                return;
            }

            enteredCode += digit.ToString();
            RefreshCode();
            SetStatus(
                enteredCode.Length == RequiredCodeLength
                    ? "Code ready. Look at PAIR."
                    : "Enter all six digits.");
        }

        public void Backspace()
        {
            if (enteredCode.Length == 0)
            {
                return;
            }

            enteredCode = enteredCode.Substring(0, enteredCode.Length - 1);
            RefreshCode();
            SetStatus("Enter all six digits.");
        }

        public void ClearCode()
        {
            enteredCode = string.Empty;
            RefreshCode();
            SetStatus("Enter the code shown on your phone.");
        }

        public void Submit()
        {
            if (pairingController == null)
            {
                SetStatus("Pairing is not configured.");
                return;
            }

            if (enteredCode.Length != RequiredCodeLength)
            {
                SetStatus("Enter all six digits first.");
                return;
            }

            BeginPairing();
        }

        private async void BeginPairing()
        {
            try
            {
                SetStatus("Connecting to your session...");
                await pairingController.PairAsync(
                    enteredCode,
                    identityProvider.GetOrCreate(),
                    lifetime?.Token ?? CancellationToken.None);
                SetStatus("Connected. Loading your session...");
                panelRoot.SetActive(false);
            }
            catch (OperationCanceledException)
            {
                if (isActiveAndEnabled)
                {
                    SetStatus("Pairing cancelled.");
                }
            }
            catch (Exception)
            {
                enteredCode = string.Empty;
                RefreshCode();
                SetStatus(
                    string.IsNullOrWhiteSpace(
                        pairingController.LastPairingError)
                        ? "Could not pair. Request a new code on your phone."
                        : "Could not pair: "
                            + pairingController.LastPairingError);
            }
        }

        private void ProcessGaze()
        {
            var ray = new Ray(
                viewingCamera.transform.position,
                viewingCamera.transform.forward);
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                gazeHits,
                panelDistanceMeters + 1f);
            QuestPairingGazeTarget target = null;
            var nearestDistance = float.MaxValue;
            for (var index = 0; index < hitCount; index++)
            {
                var candidate = gazeHits[index].collider.GetComponent<
                    QuestPairingGazeTarget>();
                if (candidate != null
                    && gazeHits[index].distance < nearestDistance)
                {
                    target = candidate;
                    nearestDistance = gazeHits[index].distance;
                }
            }

            if (target == null)
            {
                currentTarget = null;
                consumedTarget = null;
                currentDwellSeconds = 0f;
                return;
            }

            if (!ReferenceEquals(target, currentTarget))
            {
                currentTarget = target;
                consumedTarget = null;
                currentDwellSeconds = 0f;
            }

            if (ReferenceEquals(target, consumedTarget))
            {
                return;
            }

            currentDwellSeconds += Time.unscaledDeltaTime;
            if (currentDwellSeconds < dwellSeconds)
            {
                return;
            }

            consumedTarget = target;
            Activate(target.Action);
        }

        private void Activate(string action)
        {
            if (int.TryParse(action, out var digit))
            {
                AppendDigit(digit);
            }
            else if (string.Equals(action, "back", StringComparison.Ordinal))
            {
                Backspace();
            }
            else if (string.Equals(action, "clear", StringComparison.Ordinal))
            {
                ClearCode();
            }
            else if (string.Equals(action, "pair", StringComparison.Ordinal))
            {
                Submit();
            }
        }

        private void BuildPanelIfRequired()
        {
            if (panelRoot != null || viewingCamera == null)
            {
                return;
            }

            panelRoot = new GameObject("Quest Pairing Panel");
            panelRoot.transform.SetParent(transform, true);
            panelRoot.transform.position = viewingCamera.transform.position
                + viewingCamera.transform.forward * panelDistanceMeters;
            panelRoot.transform.rotation = Quaternion.LookRotation(
                panelRoot.transform.position
                - viewingCamera.transform.position,
                viewingCamera.transform.up);

            var canvas = panelRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            var scaler = panelRoot.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 20f;
            var canvasRect = panelRoot.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(900f, 650f);
            canvasRect.localScale = Vector3.one * 0.0015f;

            var background = panelRoot.AddComponent<Image>();
            background.color = new Color(0.025f, 0.07f, 0.14f, 0.97f);

            CreateText(
                "Title",
                "Enter your session code",
                new Vector2(0f, 265f),
                new Vector2(780f, 70f),
                42,
                Color.white);
            codeText = CreateText(
                "Code",
                "- - - - - -",
                new Vector2(0f, 185f),
                new Vector2(780f, 70f),
                48,
                new Color(0.25f, 0.9f, 0.85f));
            statusText = CreateText(
                "Status",
                string.Empty,
                new Vector2(0f, -275f),
                new Vector2(780f, 55f),
                24,
                new Color(0.8f, 0.86f, 0.92f));

            var labels = new[]
            {
                "1", "2", "3", "4", "5", "6", "7", "8", "9",
                "CLEAR", "0", "BACK", "PAIR"
            };
            var actions = new[]
            {
                "1", "2", "3", "4", "5", "6", "7", "8", "9",
                "clear", "0", "back", "pair"
            };
            for (var index = 0; index < labels.Length; index++)
            {
                Vector2 position;
                Vector2 size;
                if (index < 12)
                {
                    var row = index / 3;
                    var column = index % 3;
                    position = new Vector2(
                        (column - 1) * 205f,
                        95f - row * 92f);
                    size = new Vector2(175f, 72f);
                }
                else
                {
                    position = new Vector2(0f, -235f);
                    size = new Vector2(585f, 62f);
                }

                CreateGazeKey(labels[index], actions[index], position, size);
            }
        }

        private Text CreateText(
            string objectName,
            string value,
            Vector2 position,
            Vector2 size,
            int fontSize,
            Color color)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(panelRoot.transform, false);
            var rect = child.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = child.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private void CreateGazeKey(
            string label,
            string action,
            Vector2 position,
            Vector2 size)
        {
            var key = new GameObject("Pairing Key " + label);
            key.transform.SetParent(panelRoot.transform, false);
            var rect = key.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = key.AddComponent<Image>();
            image.color = action == "pair"
                ? new Color(0.2f, 0.72f, 0.68f, 1f)
                : new Color(0.08f, 0.16f, 0.28f, 1f);
            var collider = key.AddComponent<BoxCollider>();
            collider.size = new Vector3(size.x, size.y, 10f);
            key.AddComponent<QuestPairingGazeTarget>().Configure(action);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(key.transform, false);
            var labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.sizeDelta = size;
            var text = labelObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            text.text = label;
            text.fontSize = action == "pair" ? 30 : 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
        }

        private void RefreshCode()
        {
            if (codeText == null)
            {
                return;
            }

            var characters = new string[RequiredCodeLength];
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = index < enteredCode.Length
                    ? enteredCode[index].ToString()
                    : "-";
            }

            codeText.text = string.Join(" ", characters);
        }

        private void SetStatus(string value)
        {
            if (statusText != null)
            {
                statusText.text = value;
            }
        }
    }
}
