// Editor helper: enable Native leak detection with stack traces so allocation sources will be reported
#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using Unity.Collections;

[InitializeOnLoad]
public static class NativeLeakDetectionHelper
{
    static NativeLeakDetectionHelper()
    {
        try
        {
            // Enable the most verbose leak detection so leak messages include a stacktrace where possible.
            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
            Debug.Log("[NativeLeakDetectionHelper] NativeLeakDetection.Mode set to EnabledWithStackTrace");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[NativeLeakDetectionHelper] Could not change NativeLeakDetection mode: " + ex.Message);
        }
    }

    [MenuItem("Tools/Native Leak Detection/Enable (With StackTrace)")]
    private static void EnableWithStackTrace()
    {
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        Debug.Log("[NativeLeakDetectionHelper] Enabled NativeLeakDetectionMode.EnabledWithStackTrace");
    }

    [MenuItem("Tools/Native Leak Detection/Enable")]
    private static void Enable()
    {
        NativeLeakDetection.Mode = NativeLeakDetectionMode.Enabled;
        Debug.Log("[NativeLeakDetectionHelper] Enabled NativeLeakDetectionMode.Enabled");
    }

    [MenuItem("Tools/Native Leak Detection/Disable")]
    private static void Disable()
    {
        NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;
        Debug.Log("[NativeLeakDetectionHelper] Disabled NativeLeakDetection");
    }
}
#endif
