using System;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

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

        [Header("Placement")]
        [SerializeField, Min(0.5f)]
        private float panelDistanceMeters = 1.75f;

        [Header("Controller Ray Input")]
        [SerializeField]
        private InputActionAsset inputActions = null;

        [SerializeField, Min(1f)]
        private float controllerRayDistanceMeters = 5f;

        [Header("Optional Accessibility Fallback")]
        [SerializeField]
        private bool enableGazeFallback = false;

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
        private InputAction leftAimPosition;
        private InputAction leftAimRotation;
        private InputAction rightAimPosition;
        private InputAction rightAimRotation;
        private bool leftPressWasActive;
        private bool rightPressWasActive;
        private GameObject controllerVisualRoot;
        private LineRenderer leftControllerRay;
        private LineRenderer rightControllerRay;
        private RectTransform leftControllerCursor;
        private RectTransform rightControllerCursor;
        private Material controllerRayMaterial;
        private bool controllerTrackingWasAvailable = true;

        public string EnteredCode => enteredCode;

        public string StatusText => statusText != null
            ? statusText.text
            : string.Empty;

        private void OnEnable()
        {
            lifetime = new CancellationTokenSource();
            controllerTrackingWasAvailable = true;
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
            ResolveControllerActions();
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            if (controllerVisualRoot != null)
            {
                controllerVisualRoot.SetActive(true);
            }

            SetStatus(
                pairingController == null
                    ? "Pairing is not configured."
                    : "Point at a key and press either trigger.");
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

            var controllerTracked = ProcessControllerRay(
                XRNode.LeftHand,
                leftAimPosition,
                leftAimRotation,
                leftControllerRay,
                leftControllerCursor,
                ref leftPressWasActive);
            controllerTracked |= ProcessControllerRay(
                XRNode.RightHand,
                rightAimPosition,
                rightAimRotation,
                rightControllerRay,
                rightControllerCursor,
                ref rightPressWasActive);

            if (controllerTracked != controllerTrackingWasAvailable)
            {
                controllerTrackingWasAvailable = controllerTracked;
                SetStatus(
                    controllerTracked
                        ? enteredCode.Length == RequiredCodeLength
                            ? "Code ready. Point at PAIR and press trigger."
                            : "Point at a key and press either trigger."
                        : "No tracked controller detected. Move or wake a "
                            + "controller.");
            }

            if (!controllerTracked && enableGazeFallback)
            {
                ProcessGaze();
            }
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

            if (controllerVisualRoot != null)
            {
                controllerVisualRoot.SetActive(false);
            }
        }

        private void OnValidate()
        {
            panelDistanceMeters = Mathf.Max(0.5f, panelDistanceMeters);
            controllerRayDistanceMeters = Mathf.Max(
                1f,
                controllerRayDistanceMeters);
            dwellSeconds = Mathf.Max(0.5f, dwellSeconds);
        }

        private void OnDestroy()
        {
            if (controllerRayMaterial != null)
            {
                Destroy(controllerRayMaterial);
                controllerRayMaterial = null;
            }
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
                    ? "Code ready. Point at PAIR and press trigger."
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
                if (controllerVisualRoot != null)
                {
                    controllerVisualRoot.SetActive(false);
                }
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

        private bool ProcessControllerRay(
            XRNode hand,
            InputAction aimPosition,
            InputAction aimRotation,
            LineRenderer rayRenderer,
            RectTransform cursor,
            ref bool pressWasActive)
        {
            var device = InputDevices.GetDeviceAtXRNode(hand);
            if (!device.isValid
                || viewingCamera == null
                || !device.TryGetFeatureValue(
                    XRCommonUsages.devicePosition,
                    out var localPosition)
                || !device.TryGetFeatureValue(
                    XRCommonUsages.deviceRotation,
                    out var localRotation))
            {
                if (rayRenderer != null)
                {
                    rayRenderer.enabled = false;
                }

                if (cursor != null)
                {
                    cursor.gameObject.SetActive(false);
                }

                pressWasActive = false;
                return false;
            }

            if (aimPosition != null
                && aimRotation != null
                && aimPosition.enabled
                && aimRotation.enabled)
            {
                var actionRotation = aimRotation.ReadValue<Quaternion>();
                if (Quaternion.Dot(actionRotation, actionRotation) > 0.5f)
                {
                    localPosition = aimPosition.ReadValue<Vector3>();
                    localRotation = actionRotation;
                }
            }

            var trackingSpace = viewingCamera.transform.parent;
            var rayOrigin = trackingSpace != null
                ? trackingSpace.TransformPoint(localPosition)
                : localPosition;
            var rayRotation = trackingSpace != null
                ? trackingSpace.rotation * localRotation
                : localRotation;
            var ray = new Ray(rayOrigin, rayRotation * Vector3.forward);
            var target = FindNearestTarget(
                ray,
                controllerRayDistanceMeters,
                out var hitDistance);
            UpdateControllerRay(
                rayRenderer,
                ray,
                target == null
                    ? controllerRayDistanceMeters
                    : hitDistance);
            UpdateControllerCursor(cursor, ray);

            var hasTriggerButton = device.TryGetFeatureValue(
                XRCommonUsages.triggerButton,
                out var triggerPressed);
            var hasTriggerValue = device.TryGetFeatureValue(
                XRCommonUsages.trigger,
                out var triggerValue);
            var pressIsActive = hasTriggerButton
                ? triggerPressed
                : hasTriggerValue && triggerValue >= 0.5f;
            if (pressIsActive && !pressWasActive && target != null)
            {
                Activate(target.Action);
            }
            else if (pressIsActive && !pressWasActive)
            {
                SetStatus(
                    "Trigger detected. Point the cursor directly at a key.");
            }

            pressWasActive = pressIsActive;
            return true;
        }

        private QuestPairingGazeTarget FindNearestTarget(
            Ray ray,
            float maximumDistance,
            out float hitDistance)
        {
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                gazeHits,
                maximumDistance);
            QuestPairingGazeTarget target = null;
            hitDistance = maximumDistance;
            for (var index = 0; index < hitCount; index++)
            {
                var candidate = gazeHits[index].collider.GetComponent<
                    QuestPairingGazeTarget>();
                if (candidate != null
                    && gazeHits[index].distance < hitDistance)
                {
                    target = candidate;
                    hitDistance = gazeHits[index].distance;
                }
            }

            return target;
        }

        private static void UpdateControllerRay(
            LineRenderer rayRenderer,
            Ray ray,
            float distance)
        {
            if (rayRenderer == null)
            {
                return;
            }

            rayRenderer.enabled = true;
            rayRenderer.SetPosition(0, ray.origin);
            rayRenderer.SetPosition(1, ray.GetPoint(distance));
        }

        private void UpdateControllerCursor(
            RectTransform cursor,
            Ray ray)
        {
            if (cursor == null || panelRoot == null)
            {
                return;
            }

            var panelPlane = new Plane(
                panelRoot.transform.forward,
                panelRoot.transform.position);
            if (!panelPlane.Raycast(ray, out var distance)
                || distance < 0f
                || distance > controllerRayDistanceMeters)
            {
                cursor.gameObject.SetActive(false);
                return;
            }

            var localPoint = panelRoot.transform.InverseTransformPoint(
                ray.GetPoint(distance));
            var canvasRect = panelRoot.GetComponent<RectTransform>();
            var halfWidth = canvasRect.rect.width * 0.5f;
            var halfHeight = canvasRect.rect.height * 0.5f;
            if (Mathf.Abs(localPoint.x) > halfWidth
                || Mathf.Abs(localPoint.y) > halfHeight)
            {
                cursor.gameObject.SetActive(false);
                return;
            }

            cursor.anchoredPosition = new Vector2(localPoint.x, localPoint.y);
            cursor.gameObject.SetActive(true);
            cursor.SetAsLastSibling();
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

            controllerVisualRoot = new GameObject(
                "Quest Pairing Controller Visuals");
            controllerVisualRoot.transform.SetParent(transform, false);

            leftControllerRay = CreateControllerRay(
                "Left Controller Ray",
                new Color(0.25f, 0.9f, 0.85f, 0.9f));
            rightControllerRay = CreateControllerRay(
                "Right Controller Ray",
                new Color(0.45f, 0.72f, 1f, 0.9f));

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

            leftControllerCursor = CreateControllerCursor(
                "Left Controller Cursor",
                new Color(0.25f, 0.9f, 0.85f, 1f));
            rightControllerCursor = CreateControllerCursor(
                "Right Controller Cursor",
                new Color(0.45f, 0.72f, 1f, 1f));
        }

        private void ResolveControllerActions()
        {
            if (inputActions == null)
            {
                return;
            }

            leftAimPosition = inputActions.FindAction(
                "XRI Left/Aim Position",
                false);
            leftAimRotation = inputActions.FindAction(
                "XRI Left/Aim Rotation",
                false);
            rightAimPosition = inputActions.FindAction(
                "XRI Right/Aim Position",
                false);
            rightAimRotation = inputActions.FindAction(
                "XRI Right/Aim Rotation",
                false);
        }

        private LineRenderer CreateControllerRay(
            string objectName,
            Color color)
        {
            if (controllerRayMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    return null;
                }

                controllerRayMaterial = new Material(shader)
                {
                    name = "Quest Pairing Controller Ray Material"
                };
            }

            var rayObject = new GameObject(objectName);
            rayObject.transform.SetParent(controllerVisualRoot.transform, false);
            var rayRenderer = rayObject.AddComponent<LineRenderer>();
            rayRenderer.useWorldSpace = true;
            rayRenderer.positionCount = 2;
            rayRenderer.startWidth = 0.006f;
            rayRenderer.endWidth = 0.003f;
            rayRenderer.sharedMaterial = controllerRayMaterial;
            rayRenderer.startColor = color;
            rayRenderer.endColor = new Color(
                color.r,
                color.g,
                color.b,
                0.35f);
            rayRenderer.enabled = false;
            return rayRenderer;
        }

        private RectTransform CreateControllerCursor(
            string objectName,
            Color color)
        {
            var cursorObject = new GameObject(objectName);
            cursorObject.transform.SetParent(panelRoot.transform, false);
            var cursor = cursorObject.AddComponent<RectTransform>();
            cursor.sizeDelta = new Vector2(28f, 28f);
            var image = cursorObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            cursorObject.SetActive(false);
            return cursor;
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
