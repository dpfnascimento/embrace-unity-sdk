using EmbraceSDK.Internal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EmbraceSDK
{
    public class EmbraceBehavior: MonoBehaviour
    {
        private EmbraceScenesToViewReporter _scenesToViewReporter;
        
        public static EmbraceBehavior Create()
        {
#if UNITY_2022_3_OR_NEWER
            var embraceBehavior = FindAnyObjectByType<EmbraceBehavior>();
#else
            var embraceBehavior = FindObjectOfType<EmbraceBehavior>();
#endif
            if (embraceBehavior != null)
            {
                DestroyImmediate(embraceBehavior.gameObject);
            }

            var go = new GameObject { name = "Embrace" };
            go.AddComponent<EmbraceBehavior>();

            return embraceBehavior;
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
            if (Embrace.Instance.EmbraceBehavior != this)
            {
                Destroy(gameObject);
            }
            else
            {
                //Embrace.Instance.Initialize(); TODO: review if this is necessary...
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

        public void TrackCurrentScene()
        {
            if (_scenesToViewReporter == null)
            {
                _scenesToViewReporter = new EmbraceScenesToViewReporter();
            }
            
            _scenesToViewReporter.StartViewFromScene(SceneManager.GetActiveScene());
        }
    }
}