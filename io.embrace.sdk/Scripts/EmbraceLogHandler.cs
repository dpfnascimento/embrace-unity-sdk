using UnityEngine;

namespace EmbraceSDK.Internal
{
    public class EmbraceLogHandler
    {
        private UnhandledExceptionRateLimiting rateLimiter = new UnhandledExceptionRateLimiting();
        
        /// <summary>
        /// Handles log messages of LogType.Exception and LogType.Assert. For internal use and testing only.
        /// </summary>
        /// <param name="message">Custom message that will be attached to this log.</param>
        /// <param name="stack">Stack trace of the message origin</param>
        /// <param name="type">Log type (see UnityEngine.LogType for more info)</param>
        internal void HandleEmbraceLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Assert)
            {
                UnhandledException ue = new UnhandledException(message, stack);
                if (!rateLimiter.IsAllowed(ue))
                {
                    return;
                }

                (string splitName, string splitMessage) = UnhandledExceptionUtility.SplitConcatenatedExceptionNameAndMessage(message);
                Embrace.Instance.LogUnhandledUnityException(splitName, splitMessage, stack);
            }
        }
    }
}