using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using EmbraceSDK.Editor;
using EmbraceSDK.Internal;
using EmbraceSDK.Utilities;
using UnityEngine.SceneManagement;

#if EMBRACE_ENABLE_BUGSHAKE_FORM
using EmbraceSDK.Bugshake;
#endif

namespace EmbraceSDK
{
    public class Embrace : MonoBehaviour, IEmbraceUnityApi
    {
        public IEmbraceProvider Provider;
        
        internal EmbraceLogHandler EmbraceLogHandler;
        
        private static Embrace _instance;
        private static EmbraceSdkInfo sdkInfo;
        private bool _started;
        private Dictionary<string, string> _emptyDictionary = new Dictionary<string, string>();
        private EmbraceThreadService _threadService;
        private EmbraceScenesToViewReporter _scenesToViewReporter;

        /// <inheritdoc />
        public bool IsStarted => _started;

        public static Embrace Instance
        {
            get
            {
                // Only initialize in a built player or Play Mode in the Editor
                if (_instance != null || !Application.isPlaying)
                {
                    return _instance;
                }

                #if UNITY_2022_3_OR_NEWER
                Embrace embrace = FindAnyObjectByType<Embrace>();
                #else
                Embrace embrace = FindObjectOfType<Embrace>();
                #endif
                if (embrace == null)
                {
                    var go = new GameObject { name = "Embrace" };
                    embrace = go.AddComponent<Embrace>();
                    DontDestroyOnLoad(go);
                }
                
                embrace.Initialize();
                return embrace;
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus) {
                Provider.InstallUnityThreadSampler();
#if UNITY_ANDROID
                // The behaviors of the native Android SDK and the native iOS SDK have been
                // demonstrated to be different. Namely, the iOS SDK perpetuates the views
                // as expected when returning from a long-pause (long enough to create a new session)
                // However, the Android SDK does not do so and instead loses that information, instead
                // capturing the Unity activity and possibly a test view label "a_view" as well.
                // As a result, the StartView and EndView clauses here should forcibly capture
                // the view information we need for this feature.
                _scenesToViewReporter?.StartViewFromScene(SceneManager.GetActiveScene());
                    
#if EMBRACE_ENABLE_BUGSHAKE_FORM
                // We should attempt to register the shake listener whenever the app is resumed
                // Because the internal behavior of the Android SDK is such that it contains a ShakeListener singleton
                // we will not cause issues by registering it multiple times.
                BugshakeService.Instance.RegisterShakeListener();
#endif
#endif
            } else
            {
#if UNITY_ANDROID
                _scenesToViewReporter?.EndViewFromScene(SceneManager.GetActiveScene());
#endif
            }
        }

        /// <summary>
        /// Alternative way of creating the Embrace singleton, primarily used for unit testing.
        /// Use Embrace.Instance in all other cases.
        /// </summary>
        /// <returns></returns>
        public static Embrace Create()
        {
            #if UNITY_2022_3_OR_NEWER
            var embraceInstance = FindObjectOfType<Embrace>();
            #else
            var embraceInstance = FindObjectOfType<Embrace>();
            #endif
            if (embraceInstance != null)
            {
                DestroyImmediate(embraceInstance.gameObject);
            }

            var go = new GameObject { name = "Embrace" };
            embraceInstance = go.AddComponent<Embrace>();

            TextAsset targetFile = Resources.Load<TextAsset>("Info/EmbraceSdkInfo");
            sdkInfo = JsonUtility.FromJson<EmbraceSdkInfo>(targetFile.text);
            embraceInstance.Provider = new Embrace_Stub();
            _instance = embraceInstance;
            
            InternalEmbrace.SetInternalInstance(_instance);

            return embraceInstance;
        }

        /// <summary>
        /// Initializes core SDK parameters and instantiates a platform specific provider.
        /// </summary>
        private void Initialize()
        {
            _instance = this;

            // Set the main thread for the Embrace SDK
            _threadService = new EmbraceThreadService(Thread.CurrentThread, EmbraceLogHandler);
            
            // Init the Embrace log handler
            EmbraceLogHandler = new EmbraceLogHandler();
            
            TextAsset targetFile = Resources.Load<TextAsset>("Info/EmbraceSdkInfo");
            sdkInfo = JsonUtility.FromJson<EmbraceSdkInfo>(targetFile.text);

#if UNITY_ANDROID && !UNITY_EDITOR
            provider = new Embrace_Android();
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
            provider = new Embrace_iOS();
#else
            Provider = new Embrace_Stub();
#endif
                
#if UNITY_ANDROID && EMBRACE_ENABLE_BUGSHAKE_FORM
#if EMBRACE_USE_BUGSHAKE_SCENE_MANAGER_OVERRIDE
            if (SceneManagerAPI.overrideAPI == null)
            {
                SceneManagerAPI.overrideAPI = new EmbraceSceneManagerOverride(
                    BugshakeService.Instance.MarkBugReportFormSwapUnsafe, BugshakeService.Instance.MarkBugReportFormSwapSafe);
            }
            else
            {
                // The only ways to handle this are to either invoke reflection at runtime or use a weaver to capture the user's SceneManagerAPI override and weave into that.
                EmbraceLogger.LogWarning("User requested to use the EmbraceSceneManagerOverride, but the override API is already set. EmbraceSceneManagerOverride assignment skipped.");
            }
#endif
            
            // We should allow the user to configure if this is enabled or not by default.
            // For now we don't have a good way to allow the user to configure this setting.
            // We could use an instance variable since this is a Monobehaviour, but we don't force
            // the user to setup the prefab in the scene at edit time.
            // As a result if the prefab is instantiated dynamically we have no good behavioral assumption.
            // For now we will enable this by default.
            BugshakeService.Instance.RegisterShakeListener();
#endif
            
            Provider.InitializeSDK();
        }
        
        // Called by Unity runtime
        private void Start()
        {
            // If some other Game Object gets added to the scene that has an Embrace
            // component that doesn't match our singleton then get rid of it...
            if (_instance && _instance != this)
            {
                Destroy(gameObject);
            }
            else if (_instance == null)
            {
                // ...otherwise if the singleton instance is null, invoke Initialize() to create it.
                // This scenario is likely to occur if a user adds the Embrace Monobehaviour to a
                // game object in a startup scene, but doesn't invoke the StartSDK() method through
                // the singleton instance until later in the application's startup process.
                Initialize();
                DontDestroyOnLoad(gameObject);
            }
        }

        // Called by Unity runtime
        private void OnDestroy()
        {
            _scenesToViewReporter?.Dispose();
        }

        /// <inheritdoc />
        public void StartSDK(bool enableIntegrationTesting = false)
        {
            if (_started)
            {
                return;
            }

            if (_instance == null)
            {
                Initialize();
            }

            Provider.StartSDK(enableIntegrationTesting);
            Provider.SetMetaData(Application.unityVersion, Application.buildGUID, sdkInfo.version);

            TimeUtil.Clean();
            TimeUtil.InitStopWatch();

            Application.logMessageReceived += EmbraceLogHandler.HandleEmbraceLog;

            // Scene change registration here
#if EMBRACE_AUTO_CAPTURE_ACTIVE_SCENE_AS_VIEW
            scenesToViewReporter = new EmbraceScenesToViewReporter();
            scenesToViewReporter.StartViewFromScene(SceneManager.GetActiveScene());
#endif

#if EMBRACE_USE_THREADING
            // If this directive is defined, the Embrace SDK will capture messages regardless of whether they
            // originate from the main thread or not.  For more details please see Unity documentation:
            // https://docs.unity3d.com/ScriptReference/Application-logMessageReceivedThreaded.html
            Application.logMessageReceivedThreaded += _threadService.Embrace_Threaded_Log_Handler;
            Debug.LogWarning("THREADED LOGGING ENABLED");
#endif

            _started = true;
            
            InternalEmbrace.SetInternalInstance(_instance);

            EmbraceLogger.Log("Embrace SDK enabled. Version: " + sdkInfo.version);
        }
        
        public void Embrace_Log_Handler(string message, string stack, LogType type)
        {
            EmbraceLogger.LogWarning("Warning: Embrace_Log_Handler is a deprecated API and " +
                                     "will be removed in a future release.");
            
            EmbraceLogHandler.HandleEmbraceLog(message, stack, type);
        }

        /// <inheritdoc />
        public void EndAppStartup(Dictionary<string, string> properties = null)
        {
            if (properties == null)
            {
                properties = _emptyDictionary;
            }

            Provider.EndAppStartup(properties);
        }

        /// <inheritdoc />
        public LastRunEndState GetLastRunEndState()
        {
            return IsStarted ? Provider.GetLastRunEndState() : LastRunEndState.Invalid;
        }

        /// <inheritdoc />
        public void SetUserIdentifier(string identifier)
        {
            if (identifier == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("user identifier"));
                return;
            }

            Provider.SetUserIdentifier(identifier);
        }

        /// <inheritdoc />
        public void ClearUserIdentifier()
        {
            Provider.ClearUserIdentifier();
        }

        /// <inheritdoc />
        public void SetUsername(string username)
        {
            if (username == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("username"));
                return;
            }

            Provider.SetUsername(username);
        }

        /// <inheritdoc />
        public void ClearUsername()
        {
            Provider.ClearUsername();
        }

        /// <inheritdoc />
        public void SetUserEmail(string email)
        {
            if (email == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("user email"));
                return;
            }

            Provider.SetUserEmail(email);
        }

        /// <inheritdoc />
        public void ClearUserEmail()
        {
            Provider.ClearUserEmail();
        }

        /// <inheritdoc />
        public void SetUserAsPayer()
        {
            Provider.SetUserAsPayer();
        }

        /// <inheritdoc />
        public void ClearUserAsPayer()
        {
            Provider.ClearUserAsPayer();
        }

        [System.Obsolete("Please use AddUserPersona() instead. This method will be removed in a future release.")]
        public void SetUserPersona(string persona)
        {
            AddUserPersona(persona);
        }
        
        /// <inheritdoc />
        public void AddUserPersona(string persona)
        {
            if (persona == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("user persona"));
                return;
            }

            Provider.AddUserPersona(persona);
        }

        /// <inheritdoc />
        public void ClearUserPersona(string persona)
        {
            if (persona == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("user persona"));
                return;
            }

            Provider.ClearUserPersona(persona);
        }

        /// <inheritdoc />
        public void ClearAllUserPersonas()
        {
            Provider.ClearAllUserPersonas();
        }

        /// <inheritdoc />
        public void AddSessionProperty(string key, string value, bool permanent)
        {
            if (key == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("session property key"));
                return;
            }

            if (value == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("session property value"));
                return;
            }

            Provider.AddSessionProperty(key, value, permanent);
        }

        /// <inheritdoc />
        public void RemoveSessionProperty(string key)
        {
            if (key == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("session property key"));
                return;
            }

            Provider.RemoveSessionProperty(key);
        }

        /// <inheritdoc />
        public Dictionary<string, string> GetSessionProperties()
        {
            var properties = Provider.GetSessionProperties();
            if (properties == null)
            {
                properties = _emptyDictionary;
            }

            return properties;
        }

        /// <inheritdoc />
        public void StartMoment(string name, string identifier = null, bool allowScreenshot = false, Dictionary<string, string> properties = null)
        {
            if (name == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("moment name"));
                return;
            }

            if (properties == null)
            {
                properties = _emptyDictionary;
            }

            Provider.StartMoment(name, identifier, allowScreenshot, properties);
        }

        /// <inheritdoc />
        public void EndMoment(string name, string identifier = null, Dictionary<string, string> properties = null)
        {
            if (name == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("moment name"));
                return;
            }

            if (properties == null)
            {
                properties = _emptyDictionary;
            }

            Provider.EndMoment(name, identifier, properties);
        }

        /// <inheritdoc />
        [System.Obsolete("Please use LogMessage() without the screenshot argument instead. This method will be removed in a future release.")]
        public void LogMessage(string message, EMBSeverity severity, Dictionary<string, string> properties = null, bool allowScreenshot = false)
        {
            LogMessage(message, severity, properties);
        }
        
        public void LogMessage(string message, EMBSeverity severity)
        {
            LogMessage(message, severity, null);
        }
        
        /// <inheritdoc />
        public void LogMessage(string message, EMBSeverity severity, Dictionary<string, string> properties)
        {
            if (message == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("log message"));
                return;
            }

            if (properties == null)
            {
                properties = _emptyDictionary;
            }

            Provider.LogMessage(message, severity, properties);
        }

        /// <inheritdoc />
        public void LogInfo(string message)
        {
            LogMessage(message, EMBSeverity.Info);
        }

        /// <inheritdoc />
        public void LogWarning(string message)
        {
            LogMessage(message, EMBSeverity.Warning);
        }

        /// <inheritdoc />
        public void LogError(string message)
        {
            LogMessage(message, EMBSeverity.Error);
        }

        [System.Obsolete("Please use AddBreadcrumb() instead. This method will be removed in a future release.")]
        public void LogBreadcrumb(string message)
        {
            AddBreadcrumb(message);
        }

        /// <inheritdoc />
        public void AddBreadcrumb(string message)
        {
            if (message == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("breadcrumb message"));
                return;
            }

            Provider.AddBreadcrumb(message);
        }

        /// <inheritdoc />
        public void EndSession(bool clearUserInfo = false)
        {
            Provider.EndSession(clearUserInfo);
        }

        /// <inheritdoc />
        public string GetDeviceId()
        {
            return Provider.GetDeviceId();
        }

        /// <inheritdoc />
        public bool StartView(string name)
        {
            if (name == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("view name"));
                return false;
            }

            return Provider.StartView(name);
        }

        /// <inheritdoc />
        public bool EndView(string name)
        {
            if (name == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("view name"));
                return false;
            }

            return Provider.EndView(name);
        }

        /// <summary>
        /// Causes a crash. Use this for test purposes only.
        /// </summary>
        [System.Obsolete("This method will be removed in a future release.")]
        public void Crash()
        {
            Provider.Crash();
        }

        /// <inheritdoc />
        [System.Obsolete("Please use RecordNetworkRequest() instead. This method will be removed in a future release.")]
        public void LogNetworkRequest(string url, HTTPMethod method, long startms, long endms, int bytesin, int bytesout, int code, string error)
        {
            RecordNetworkRequest(url, method, startms, endms, bytesin, bytesout, code, error);
        }
        
        /// <inheritdoc />
        [System.Obsolete("Please use RecordCompletedNetworkRequest() or RecordIncompleteNetworkRequest() instead. This method will be removed in a future release.")]

        public void RecordNetworkRequest(string url, HTTPMethod method, long startms, long endms, int bytesin, int bytesout, int code, string error = "")
        {
            if (!string.IsNullOrEmpty(error))
            {
                RecordIncompleteNetworkRequest(url, method, startms, endms, error);
                return;
            }

            Provider.RecordCompletedNetworkRequest(url, method, startms, endms, bytesin, bytesout, code);
        }
        
        /// <inheritdoc />
        public void RecordCompleteNetworkRequest(string url, HTTPMethod method, long startms, long endms, long bytesin, long bytesout, int code)
        {
            if (url == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("network url"));
                return;
            }
            
            Provider.RecordCompletedNetworkRequest(url, method, startms, endms, bytesin, bytesout, code);
        }
        
        /// <inheritdoc />
        public void RecordIncompleteNetworkRequest(string url, HTTPMethod method, long startms, long endms, string error)
        {
            if (url == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("network url"));
                return;
            }
            
            if (error == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("network error"));
                return;
            }
            
            Provider.RecordIncompleteNetworkRequest(url, method, startms, endms, error);
        }

        /// <inheritdoc />
        [Obsolete("Please use LogUnhandledUnityException instead. This method will be removed in a future release.")]
        public void logUnhandledUnityException(string exceptionMessage, string stack)
        {
            if (exceptionMessage == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("exception message"));
                return;
            }

            if (stack == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("exception stack"));
                return;
            }

            (string splitName, string splitMessage) = UnhandledExceptionUtility.SplitConcatenatedExceptionNameAndMessage(exceptionMessage);
            Provider.LogUnhandledUnityException(splitName, splitMessage, stack);
        }

        /// <inheritdoc />
        public void LogUnhandledUnityException(string exceptionName, string exceptionMessage, string stack)
        {
            if (string.IsNullOrEmpty(exceptionName))
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("exception name"));
                return;
            }

            Provider.LogUnhandledUnityException(exceptionName, exceptionMessage ?? "", stack ?? "");
        }

        /// <inheritdoc />
        public void LogUnhandledUnityException(Exception exception, string stack = null)
        {
            if (exception == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("exception"));
                return;
            }

            Provider.LogUnhandledUnityException(
                exceptionName: exception.GetType().Name,
                exceptionMessage: exception.Message ?? "",
                stack: stack ?? exception.StackTrace ?? "");
        }

        /// <inheritdoc />
        public void LogHandledUnityException(string exceptionName, string exceptionMessage, string stack)
        {
            if (string.IsNullOrEmpty(exceptionName))
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("exception name"));
                return;
            }

            Provider.LogHandledUnityException(exceptionName, exceptionMessage ?? "", stack ?? "");
        }

        /// <inheritdoc />
        public void LogHandledUnityException(Exception exception, string stack = null)
        {
            if (exception == null)
            {
                EmbraceLogger.LogError(EmbraceLogger.GetNullErrorMessage("exception"));
                return;
            }

            Provider.LogHandledUnityException(
                exceptionName: exception.GetType().Name,
                exceptionMessage: exception.Message ?? "",
                stack: stack ?? exception.StackTrace ?? "");
        }
        
        /// <inheritdoc />
        public string GetCurrentSessionId()
        {
            return Provider.GetCurrentSessionId();
        }

        /// <inheritdoc />
        public void RecordPushNotification(iOSPushNotificationArgs iosArgs)
        {
            #if UNITY_IOS
            provider.RecordPushNotification(iosArgs);
            #else
            EmbraceLogger.LogError("Attempting to record iOS push notification on non-iOS platform");
            #endif
        }

        /// <inheritdoc />
        public void RecordPushNotification(AndroidPushNotificationArgs androidArgs)
        {
            #if UNITY_ANDROID
            Provider.RecordPushNotification(androidArgs);
            #else
            EmbraceLogger.LogError("Attempting to record Android push notification on non-Android platform");
            #endif
        }

        /// <summary>
        /// Converts an HTTPMethod to an int value.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public static int __BridgedHTTPMethod(HTTPMethod method)
        {
            switch (method)
            {
                case HTTPMethod.GET: return 1;
                case HTTPMethod.POST: return 2;
                case HTTPMethod.PUT: return 3;
                case HTTPMethod.DELETE: return 4;
                case HTTPMethod.PATCH: return 5;
                default: return 0;
            }
        }
    }
}
