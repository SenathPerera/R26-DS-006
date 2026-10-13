using System;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Application
{
    public enum HeadsetLifecycleInterruptionReason
    {
        ApplicationFocusLost,
        ApplicationPaused
    }

    [AddComponentMenu(
        "Adaptive Meditation/Application/Headset Lifecycle Safety Controller")]
    [DisallowMultipleComponent]
    public sealed class HeadsetLifecycleSafetyController : MonoBehaviour
    {
        [Header("Session Safety Target")]
        [SerializeField]
        private ProductionSessionCoordinator productionCoordinator = null;

        private bool applicationPaused;
        private bool applicationFocused = true;
        private bool lifecycleUnavailable;

        public bool IsLifecycleUnavailable => lifecycleUnavailable;

        private void Awake()
        {
            applicationFocused = UnityEngine.Application.isFocused;
            ResolveCoordinator();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            SetApplicationPaused(pauseStatus);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            SetApplicationFocus(hasFocus);
        }

        public void Configure(ProductionSessionCoordinator coordinator)
        {
            productionCoordinator = coordinator != null
                ? coordinator
                : throw new ArgumentNullException(nameof(coordinator));
        }

        public void SetApplicationPaused(bool isPaused)
        {
            applicationPaused = isPaused;
            ReconcileLifecycleAvailability(
                HeadsetLifecycleInterruptionReason.ApplicationPaused);
        }

        public void SetApplicationFocus(bool hasFocus)
        {
            applicationFocused = hasFocus;
            ReconcileLifecycleAvailability(
                HeadsetLifecycleInterruptionReason.ApplicationFocusLost);
        }

        private void ReconcileLifecycleAvailability(
            HeadsetLifecycleInterruptionReason interruptionReason)
        {
            var unavailable = applicationPaused || !applicationFocused;
            if (unavailable == lifecycleUnavailable)
            {
                return;
            }

            ResolveCoordinator();
            if (productionCoordinator == null)
            {
                Debug.LogError(
                    "[HeadsetLifecycleSafetyController] lifecycle_event_rejected "
                    + "reason=production-coordinator-missing",
                    this);
                return;
            }

            lifecycleUnavailable = unavailable;
            var monotonicTimeSeconds = Time.realtimeSinceStartupAsDouble;
            var utcTimestampUnixSeconds =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            if (unavailable)
            {
                productionCoordinator.SuspendForHeadsetLifecycle(
                    interruptionReason,
                    monotonicTimeSeconds,
                    utcTimestampUnixSeconds);
            }
            else
            {
                productionCoordinator.RestoreHeadsetLifecycle(
                    monotonicTimeSeconds,
                    utcTimestampUnixSeconds);
            }
        }

        private void ResolveCoordinator()
        {
            if (productionCoordinator == null)
            {
                productionCoordinator = GetComponent<
                    ProductionSessionCoordinator>();
            }
        }
    }
}
