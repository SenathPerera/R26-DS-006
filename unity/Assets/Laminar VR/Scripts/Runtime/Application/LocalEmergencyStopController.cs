using System;
using LaminarVR.AdaptiveMeditation.Session;
using UnityEngine;
using UnityEngine.XR;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    [AddComponentMenu(
        "Adaptive Meditation/Application/Local Emergency Stop Controller")]
    [DisallowMultipleComponent]
    public sealed class LocalEmergencyStopController : MonoBehaviour
    {
        private const string LocalCommandIdPrefix = "local-emergency-stop-";

        [Header("Session Command Target")]
        [SerializeField]
        private VisualSessionBoundary visualSessionBoundary = null;

        [Header("Quest Controller Input")]
        [Tooltip(
            "Controller whose application-menu button triggers the local "
            + "emergency stop. LeftHand maps to the Quest menu button; the "
            + "right controller system button remains owned by the headset.")]
        [SerializeField]
        private XRNode controllerNode = XRNode.LeftHand;

        private bool menuButtonWasPressed;

        public bool HasTriggered { get; private set; }

        public string LastCommandId { get; private set; } = string.Empty;

        private void Awake()
        {
            ResolveBoundary();
        }

        private void OnEnable()
        {
            menuButtonWasPressed = false;
        }

        private void Update()
        {
            if (HasTriggered)
            {
                return;
            }

            var device = InputDevices.GetDeviceAtXRNode(controllerNode);
            var isPressed = device.isValid
                && device.TryGetFeatureValue(
                    CommonUsages.menuButton,
                    out var menuButtonPressed)
                && menuButtonPressed;
            if (isPressed && !menuButtonWasPressed)
            {
                TryTriggerEmergencyStop();
            }

            menuButtonWasPressed = isPressed;
        }

        public void Configure(VisualSessionBoundary boundary)
        {
            visualSessionBoundary = boundary != null
                ? boundary
                : throw new ArgumentNullException(nameof(boundary));
        }

        public bool TryTriggerEmergencyStop()
        {
            if (HasTriggered)
            {
                return false;
            }

            ResolveBoundary();
            if (visualSessionBoundary == null)
            {
                Debug.LogError(
                    "[LocalEmergencyStopController] emergency_stop_rejected "
                    + "reason=visual-session-boundary-missing",
                    this);
                return false;
            }

            LastCommandId = LocalCommandIdPrefix
                + Guid.NewGuid().ToString("N");
            HasTriggered = true;
            visualSessionBoundary.ReceiveCommand(
                LastCommandId,
                SessionCommandType.EmergencyStop);
            Debug.LogWarning(
                "[LocalEmergencyStopController] emergency_stop_requested "
                + "source=quest-controller-menu-button",
                this);
            return true;
        }

        private void ResolveBoundary()
        {
            if (visualSessionBoundary == null)
            {
                visualSessionBoundary = GetComponent<VisualSessionBoundary>();
            }
        }
    }
}
