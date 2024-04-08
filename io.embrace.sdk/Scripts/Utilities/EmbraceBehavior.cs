using EmbraceSDK.Internal;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmbraceSDK
{
    public class EmbraceBehavior: MonoBehaviour
    {
        private static EmbraceBehavior _instance;
        private EmbraceScenesToViewReporter _scenesToViewReporter;
        
        public static EmbraceBehavior Instance
        {
            get
            {
                // Only initialize in a built player or Play Mode in the Editor
                if (_instance != null || !Application.isPlaying)
                {
                    return _instance;
                }

#if UNITY_2022_3_OR_NEWER
                EmbraceBehavior embrace = FindAnyObjectByType<EmbraceBehavior>();
#else
                EmbraceBehavior embraceBehavior = FindObjectOfType<EmbraceBehavior>();
#endif
                if (embraceBehavior == null)
                {
                    var go = new GameObject { name = "Embrace" };
                    embraceBehavior = go.AddComponent<EmbraceBehavior>();
                    DontDestroyOnLoad(go);
                }
                
                return embraceBehavior;
            }
        }
        
        void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus) {
                Embrace.Instance.Provider.InstallUnityThreadSampler();
#if UNITY_ANDROID
#if EMBRACE_AUTO_CAPTURE_ACTIVE_SCENE_AS_VIEW
                // The behaviors of the native Android SDK and the native iOS SDK have been
                // demonstrated to be different. Namely, the iOS SDK perpetuates the views
                // as expected when returning from a long-pause (long enough to create a new session)
                // However, the Android SDK does not do so and instead loses that information, instead
                // capturing the Unity activity and possibly a test view label "a_view" as well.
                // As a result, the StartView and EndView clauses here should forcibly capture
                // the view information we need for this feature.
                _scenesToViewReporter?.StartViewFromScene(SceneManager.GetActiveScene());
#endif
                    
#if EMBRACE_ENABLE_BUGSHAKE_FORM
                // We should attempt to register the shake listener whenever the app is resumed
                // Because the internal behavior of the Android SDK is such that it contains a ShakeListener singleton
                // we will not cause issues by registering it multiple times.
                BugshakeService.Instance.RegisterShakeListener();
#endif
#endif
            } else
            {
#if UNITY_ANDROID && EMBRACE_AUTO_CAPTURE_ACTIVE_SCENE_AS_VIEW
                _scenesToViewReporter?.EndViewFromScene(SceneManager.GetActiveScene());
#endif
            }
        }
        
        // Called by Unity runtime
        private void Start()
        {
            // If some other Game Object gets added to the scene that has an Embrace
            // component that doesn't match our singleton then get rid of it...
            if (Embrace.Instance._embraceBehavior != this)
            {
                Destroy(gameObject);
            }
            else
            {
                // ...otherwise if the singleton instance is null, invoke Initialize() to create it.
                // This scenario is likely to occur if a user adds the Embrace Monobehaviour to a
                // game object in a startup scene, but doesn't invoke the StartSDK() method through
                // the singleton instance until later in the application's startup process.
                Embrace.Instance._embraceBehavior = this;
                Embrace.Instance.Initialize();
                DontDestroyOnLoad(gameObject);
            }
        }
        
        // Called by Unity runtime
        private void OnDestroy()
        {
#if EMBRACE_AUTO_CAPTURE_ACTIVE_SCENE_AS_VIEW
            _scenesToViewReporter?.Dispose();
#endif
        }
        
        public static void Create()
        {
#if UNITY_2022_3_OR_NEWER
            var embraceInstance = FindObjectOfType<Embrace>();
#else
            var embraceInstance = FindObjectOfType<EmbraceBehavior>();
#endif
            if (embraceInstance != null)
            {
                DestroyImmediate(embraceInstance.gameObject);
            }

            var go = new GameObject { name = "Embrace" };
            go.AddComponent<EmbraceBehavior>();
        }
    }
}