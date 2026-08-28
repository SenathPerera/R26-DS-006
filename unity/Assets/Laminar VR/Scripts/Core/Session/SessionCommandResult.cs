namespace LaminarVR.AdaptiveMeditation.Session
{
    public enum SessionCommandResultCode
    {
        Accepted,
        DuplicateIgnored,
        InvalidCommandId,
        UnsupportedCommand,
        InvalidForCurrentPhase,
        FreshPhysiologyRequired,
        SessionAlreadyTerminal
    }

    public readonly struct SessionCommandResult
    {
        public SessionCommandResult(
            string commandId,
            SessionCommandType commandType,
            SessionCommandResultCode resultCode,
            VrSessionPhase previousPhase,
            VrSessionPhase currentPhase)
        {
            CommandId = commandId;
            CommandType = commandType;
            ResultCode = resultCode;
            PreviousPhase = previousPhase;
            CurrentPhase = currentPhase;
        }

        public string CommandId { get; }

        public SessionCommandType CommandType { get; }

        public SessionCommandResultCode ResultCode { get; }

        public VrSessionPhase PreviousPhase { get; }

        public VrSessionPhase CurrentPhase { get; }

        public bool Applied => ResultCode == SessionCommandResultCode.Accepted;
    }
}

