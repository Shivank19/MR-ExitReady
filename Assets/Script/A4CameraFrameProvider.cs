using System.Threading;
using System.Threading.Tasks;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Assignment 4: reads frames from Meta's Passthrough Camera Access (PCA) and
///  - shows the live feed on a debug RawImage,
///  - hands out a snapshot as a Texture2D (CaptureFrameAsync) for the LLM.
/// Needs the PassthroughCameraAccess building block in the scene, "Passthrough Camera Access"
/// enabled on OVRManager, and the horizonos.permission.HEADSET_CAMERA permission.
/// </summary>
public class A4CameraFrameProvider : MonoBehaviour
{
    [Header("Passthrough Camera Access")]
    [SerializeField] private PassthroughCameraAccess passthroughCamera;

    [Header("Debug preview (optional)")]
    [SerializeField] private RawImage debugPreview;

    [Header("Capture")]
    [Tooltip("Seconds to wait for the camera to deliver its first frame.")]
    [SerializeField] private float startTimeoutSeconds = 5f;

    private void Update()
    {
        if (debugPreview == null || passthroughCamera == null || !passthroughCamera.IsPlaying)
        {
            return;
        }

        var tex = passthroughCamera.GetTexture();
        if (tex != null && debugPreview.texture != tex)
        {
            debugPreview.texture = tex;
        }
    }

    /// <summary>
    /// Copies the current camera frame into a new CPU-side Texture2D (RGBA32).
    /// Returns null if the camera isn't available. The caller owns (and should Destroy) the texture.
    /// </summary>
    public async Task<Texture2D> CaptureFrameAsync(CancellationToken token = default)
    {
        if (passthroughCamera == null)
        {
            Debug.LogWarning("[A4Camera] No PassthroughCameraAccess assigned.");
            return null;
        }

        // Wait until the camera is streaming (permission prompt / startup can take a moment).
        var deadline = Time.realtimeSinceStartup + startTimeoutSeconds;
        while (!passthroughCamera.IsPlaying || passthroughCamera.GetTexture() == null)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogWarning("[A4Camera] Camera is not playing (is the permission granted?).");
                return null;
            }

            token.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        var source = passthroughCamera.GetTexture();
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);

        // Read back from the GPU without stalling the frame.
        var request = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
        while (!request.done)
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        Texture2D result = null;
        if (!request.hasError)
        {
            result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            result.LoadRawTextureData(request.GetData<byte>());
            result.Apply();
        }
        else
        {
            Debug.LogWarning("[A4Camera] GPU readback failed.");
        }

        RenderTexture.ReleaseTemporary(rt);
        return result;
    }
}
