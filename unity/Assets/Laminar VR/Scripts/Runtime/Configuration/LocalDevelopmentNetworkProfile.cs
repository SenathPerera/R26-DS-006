using System;
using UnityEngine;

namespace LaminarVR.AdaptiveMeditation.Runtime.Configuration
{
    [CreateAssetMenu(
        fileName = "LocalDevelopmentNetworkProfile",
        menuName = "Adaptive Meditation/Networking/Local Development Network Profile")]
    public sealed class LocalDevelopmentNetworkProfile : ScriptableObject
    {
        [Tooltip(
            "LAN host name or IP address of the development machine. Do not "
            + "include a scheme, port, path, or query string.")]
        [SerializeField]
        private string host = string.Empty;

        [Header("Service Ports")]
        [SerializeField, Min(1)]
        private int componentBPort = 8000;

        [SerializeField, Min(1)]
        private int lyriaBackendPort = 8002;

        [SerializeField, Min(1)]
        private int sessionRelayPort = 8080;

        public string Host => host?.Trim() ?? string.Empty;

        public bool TryGetComponentBStreamEndpoint(
            out string endpoint,
            out string validationError)
        {
            return TryBuildEndpoint(
                "ws",
                componentBPort,
                "/stream",
                out endpoint,
                out validationError);
        }

        public bool TryGetLyriaHttpBaseUrl(
            out string endpoint,
            out string validationError)
        {
            return TryBuildEndpoint(
                "http",
                lyriaBackendPort,
                string.Empty,
                out endpoint,
                out validationError);
        }

        public bool TryGetLyriaRealtimeWebsocketUrl(
            out string endpoint,
            out string validationError)
        {
            return TryBuildEndpoint(
                "ws",
                lyriaBackendPort,
                "/live-music",
                out endpoint,
                out validationError);
        }

        public bool TryGetSessionRelayEndpoint(
            out string endpoint,
            out string validationError)
        {
            return TryBuildEndpoint(
                "ws",
                sessionRelayPort,
                "/realtime?role=quest",
                out endpoint,
                out validationError);
        }

        private bool TryBuildEndpoint(
            string scheme,
            int port,
            string pathAndQuery,
            out string endpoint,
            out string validationError)
        {
            endpoint = string.Empty;
            string normalizedHost = Host;
            if (string.IsNullOrWhiteSpace(normalizedHost))
            {
                validationError =
                    "A local development host is required. Set "
                    + "MINDSYNC_DEVELOPMENT_HOST in services/lyria_backend/.env.";
                return false;
            }

            if (normalizedHost.Contains("://")
                || normalizedHost.Contains("/")
                || normalizedHost.Contains("?")
                || normalizedHost.Contains("#")
                || Uri.CheckHostName(normalizedHost) == UriHostNameType.Unknown)
            {
                validationError =
                    "The local development host must be a host name or IP "
                    + "address without a scheme, port, path, or query string.";
                return false;
            }

            if (port < 1 || port > 65535)
            {
                validationError = "Local development service ports must be between 1 and 65535.";
                return false;
            }

            endpoint = $"{scheme}://{normalizedHost}:{port}{pathAndQuery}";
            validationError = string.Empty;
            return true;
        }
    }
}
