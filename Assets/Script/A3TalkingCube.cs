using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;

/// <summary>
/// Talking Cube — Speech → Text → LLM → Speech, through the UMN AI Gateway.
///
/// Each step is one web request to the gateway (it speaks the OpenAI API):
///   Speech → Text   POST /v1/audio/transcriptions
///   Text → LLM      POST /v1/chat/completions
///   Text → Speech   POST /v1/audio/speech
/// </summary>
public class A3TalkingCube : MonoBehaviour
{
    [Header("UMN AI Gateway")]
    [SerializeField] private string gatewayBaseUrl = "https://api.aigateway.umn.edu";
    [Tooltip("Your UMN AI Gateway key (starts with sk-). It is saved in the scene file, so don't push it to a public repo.")]
    [SerializeField] private string apiKey = "";

    [Header("Models — one per step")]
    [SerializeField] private string sttModel = "gemini-3.5-transcribe-preview";
    [SerializeField] private string chatModel = "gpt-6-luna";
    [SerializeField] private string ttsModel = "azure-tts";
    [SerializeField] private string ttsVoice = "alloy";
    [SerializeField] private string systemPrompt = "You are a friendly talking cube with eyes. You can see what the user sees through their headset camera. When the user asks about something in their view, describe what you see. Keep answers short. Your replies are spoken aloud, so don't use markdown or emoji.";

    [Header("Camera")]
    [SerializeField] private A4CameraFrameProvider cameraFrameProvider;

    [Header("Recording")]
    [SerializeField] private int maxRecordingSeconds = 15;
    [SerializeField] private int sampleRate = 48000;

    [Header("Visual Feedback")]
    [SerializeField] private Color idleColor = Color.white;
    [SerializeField] private Color recordingColor = Color.red;
    [SerializeField] private Color processingColor = Color.yellow;
    [SerializeField] private Color speakingColor = Color.green;

    // ── State ────────────────────────────────────────────────────────────────
    private enum State { Idle, Recording, Processing, Speaking }
    private State currentState = State.Idle;

    // ── One client for all three models ──────────────────────────────────────
    private AIGatewayClient gateway;

    // ── Components ───────────────────────────────────────────────────────────
    private AudioSource audioSource;
    private Renderer cubeRenderer;
    private MaterialPropertyBlock propBlock;

    // ── Recording ────────────────────────────────────────────────────────────
    private string micDevice;
    private AudioClip recordingClip;

    // ── Conversation memory: a list of chat messages ─────────────────────────
    private readonly JArray history = new JArray();
    private CancellationTokenSource cts;

    void Start()
    {
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
        // The gateway client setup
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Trim().StartsWith("<"))
        {
            Debug.LogError("[TalkingCube] Paste your gateway API key into the \"Api Key\" field in the Inspector.");
            enabled = false;
            return;
        }
        gateway = new AIGatewayClient(gatewayBaseUrl, apiKey.Trim());

        // Unity components
        audioSource = gameObject.AddComponent<AudioSource>();
        cubeRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();

        
        micDevice = GetValidMicrophone();
        

        history.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

        // Ensure the cube starts in the correct color.
        ApplyColor();
    }

    private string GetValidMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[TalkingCube] No microphone devices found.");
            return null;
        }

        foreach (string device in Microphone.devices)
        {
            Microphone.GetDeviceCaps(device, out int minFreq, out int maxFreq);
            Debug.Log($"[TalkingCube] Device: '{device}' | Min: {minFreq}Hz, Max: {maxFreq}Hz");

            // Skip devices that report invalid or zero sample rates
            if (maxFreq > 0 || minFreq > 0 || device.Contains("Oculus") || device.Contains("Meta"))
            {
        Debug.Log("chosen");
                return device;
            }
        }
        Debug.Log("Found nothing ig");

        // Fallback to null (default mic) if no device reported explicit capabilities
        return Microphone.devices[0];
    }

    [ContextMenu("Begin Listening")]
    public void BeginListening()
    {
        if (currentState != State.Idle) return;

        // Grab active device and query hardware caps
        string device = GetValidMicrophone();
        Microphone.GetDeviceCaps(device, out int minFreq, out int maxFreq);

        // If device specifies a fixed rate (like 48000 Hz), use it!
        int targetSampleRate = (maxFreq > 0) ? maxFreq : sampleRate;

        Debug.Log($"[TalkingCube] Recording using '{device}' at {targetSampleRate} Hz");

        // Passing 'null' forces Unity to use its default FMOD audio bridge
        recordingClip = Microphone.Start(null, false, maxRecordingSeconds, 44100);
        //recordingClip = Microphone.Start(device, loop: false, maxRecordingSeconds, targetSampleRate);

        SetState(State.Recording);
    }

    /// <summary>Call this when the user releases the button.</summary>
    [ContextMenu("Stop Listening")]
    public void StopListening()
    {
        if (currentState != State.Recording) return;

        // If the button was held longer than maxRecordingSeconds, the mic has already stopped by itself.
        int capturedSamples = Microphone.IsRecording(micDevice)
            ? Microphone.GetPosition(micDevice)
            : recordingClip.samples;
        Microphone.End(micDevice);

        float[] sampleData = new float[capturedSamples * recordingClip.channels];
        recordingClip.GetData(sampleData, 0);

        //AudioSource audioSource = GetComponent<AudioSource>();
        //if (audioSource != null && recordingClip != null)
        //{
        //    audioSource.clip = recordingClip;
        //    audioSource.volume = 1.0f;
        //    audioSource.Play();
        //    Debug.Log("[TalkingCube Debug] Playing back recorded clip...");
        //}
        
        if (capturedSamples < recordingClip.frequency / 4)
        {
            Debug.Log("[TalkingCube] Recording too short, ignoring.");
            SetState(State.Idle);
            return;
        }

        // Processing state should show yellow
        SetState(State.Processing);

        cts = new CancellationTokenSource();
        _ = RunPipelineAsync(recordingClip, capturedSamples, cts.Token);
    }

    // ── Pipeline (Part B) ─────────────────────────────────────────────────────
    async Task RunPipelineAsync(AudioClip clip, int capturedSamples, CancellationToken token)
    {
        try
        {
            // Part B.1: Speech → Text
            byte[] wavBytes = WavUtility.FromAudioClip(clip, capturedSamples);
            float[] sampleData = new float[clip.samples * clip.channels];
            clip.GetData(sampleData, 0);

            float maxVolume = 0f;
            foreach (float sample in sampleData)
            {
                if (Mathf.Abs(sample) > maxVolume)
                    maxVolume = Mathf.Abs(sample);
            }

            Debug.Log($"[TalkingCube Debug] Peak Audio Volume: {maxVolume:F4}");

            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("model", sttModel),
                new MultipartFormDataSection("language", "en"),
                new MultipartFormFileSection("file", wavBytes, "recording.wav", "audio/wav")
            };

            JObject sttResponse = await gateway.PostFormAsync("/v1/audio/transcriptions", form, token);
            string userText = sttResponse["text"]?.ToString();

            if (string.IsNullOrWhiteSpace(userText))
            {
                Debug.Log("[TalkingCube] No speech detected.");
                Debug.Log(sttResponse);
                Debug.Log(wavBytes.Length);
                SetState(State.Idle);
                return;
            }

            Debug.Log($"[TalkingCube] Transcribed: {userText}");

            // Part B.2: Text → LLM
            // A4: grab what the user is looking at, to send along with their words.
            Texture2D snapshot = null;
            if (cameraFrameProvider != null)
            {
                snapshot = await cameraFrameProvider.CaptureFrameAsync(token);
            }

            // Always store text-only in history to avoid sending images repeatedly.
            history.Add(new JObject { ["role"] = "user", ["content"] = userText });

            // Throwaway copy of the history: only its last message carries the image.
            var requestMessages = (JArray)history.DeepClone();
            if (snapshot != null)
            {
                string snapshotBase64 = Convert.ToBase64String(snapshot.EncodeToJPG(75));
                Destroy(snapshot);
                requestMessages[requestMessages.Count - 1] = new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JObject { ["url"] = "data:image/jpeg;base64," + snapshotBase64 }
                        },
                        new JObject { ["type"] = "text", ["text"] = userText }
                    }
                };
            }

            JObject chatBody = new JObject
            {
                ["model"] = chatModel,
                ["messages"] = requestMessages
            };

            JObject chatResponse = await gateway.PostJsonAsync("/v1/chat/completions", chatBody, token);
            string reply = chatResponse["choices"]?[0]?["message"]?["content"]?.ToString();

            if (string.IsNullOrWhiteSpace(reply))
            {
                Debug.LogWarning("[TalkingCube] Received empty LLM reply.");
                SetState(State.Idle);
                return;
            }

            history.Add(new JObject { ["role"] = "assistant", ["content"] = reply });
            Debug.Log($"[TalkingCube] LLM Reply: {reply}");

            // Part B.3: Text → Speech
            JObject ttsBody = new JObject
            {
                ["model"] = ttsModel,
                ["input"] = reply,
                ["voice"] = ttsVoice,
                ["response_format"] = "wav"
            };

            byte[] speechBytes = await gateway.PostJsonForBytesAsync("/v1/audio/speech", ttsBody, token);
            AudioClip speechClip = WavUtility.ToAudioClip(speechBytes);

            // Part B.4: Play + state transitions
            SetState(State.Speaking);
            audioSource.PlayOneShot(speechClip);

            int durationMs = Mathf.CeilToInt(speechClip.length * 1000f) + 200; // include padding
            await Task.Delay(durationMs, token);

            SetState(State.Idle);
        }
        catch (OperationCanceledException)
        {
            SetState(State.Idle);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TalkingCube] Error: {ex.Message}");
            SetState(State.Idle);
        }
    }

    // ── State + Visual Feedback (Part A) ──────────────────────────────────────
    void SetState(State newState)
    {
        currentState = newState;
        ApplyColor();
    }

    void ApplyColor()
    {
        if (cubeRenderer == null) return;

        Color targetColor = currentState switch
        {
            State.Idle => idleColor,
            State.Recording => recordingColor,
            State.Processing => processingColor,
            State.Speaking => speakingColor,
            _ => idleColor
        };

        cubeRenderer.GetPropertyBlock(propBlock);
        // Supports URP Lit (_BaseColor) and Built-in Standard (_Color)
        propBlock.SetColor("_BaseColor", targetColor);
        propBlock.SetColor("_Color", targetColor);
        cubeRenderer.SetPropertyBlock(propBlock);
    }

    void OnDestroy()
    {
        cts?.Cancel();
        cts?.Dispose();
    }
}