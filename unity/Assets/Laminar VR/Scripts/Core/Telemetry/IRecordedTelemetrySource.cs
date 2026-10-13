namespace LaminarVR.AdaptiveMeditation.Telemetry
{
    public interface IRecordedTelemetrySource
    {
        int PendingEventCount { get; }

        bool TryDequeue(out TelemetryEvent telemetryEvent);
    }
}
