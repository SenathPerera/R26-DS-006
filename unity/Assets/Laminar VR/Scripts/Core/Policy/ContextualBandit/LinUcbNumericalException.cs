using System;

namespace LaminarVR.AdaptiveMeditation.Policy.ContextualBandit
{
    public sealed class LinUcbNumericalException : InvalidOperationException
    {
        public LinUcbNumericalException(string message)
            : base(message)
        {
        }
    }
}
