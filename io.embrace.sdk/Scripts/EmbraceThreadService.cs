using System.Threading;
using UnityEngine;

namespace EmbraceSDK.Internal
{
    public class EmbraceThreadService
    {
        private Thread _mainThread;
        private EmbraceLogHandler _embraceLogHandler;

        public EmbraceThreadService(Thread mainThread, EmbraceLogHandler embraceLogHandler)
        {
            _mainThread = mainThread;
            _embraceLogHandler = embraceLogHandler;
        }
        
        internal Thread GetMainThread() => _mainThread;
        
        internal bool IsMainThread()
        {
            if (_mainThread == null) return false;
            return _mainThread.Equals(Thread.CurrentThread);
        }

#if EMBRACE_USE_THREADING
        internal void Embrace_Threaded_Log_Handler(string message, string stack, LogType type)
        {
            if (IsMainThread())
            {
                return;
            }

            _embraceLogHandler.HandleEmbraceLog(message, stack, type);
        }
#endif

    }
}