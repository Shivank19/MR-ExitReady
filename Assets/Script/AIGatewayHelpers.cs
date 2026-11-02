// AIGatewayHelpers.cs — small helpers for calling the UMN AI Gateway (an OpenAI-compatible API)
// from Unity without an SDK. Students don't need to change this file.
//
// Tags match A3TalkingCube.cs: [A3] used from the first lab, [A4] camera lab, [A5] scene-tools lab.
//
// Requires Newtonsoft JSON: Package Manager → "+" → Add package by name → com.unity.nuget.newtonsoft-json

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>[A3] Sends requests to the gateway: adds the API key, waits for the reply, turns errors into readable exceptions.</summary>
public class AIGatewayClient
{
    private readonly string baseUrl;
    private readonly string apiKey;

    public AIGatewayClient(string baseUrl, string apiKey)
    {
        this.baseUrl = baseUrl.Trim().TrimEnd('/');
        this.apiKey = apiKey;
    }

    /// <summary>[A3] POST a JSON body, get a JSON reply. Used for /v1/chat/completions.</summary>
    public async Task<JObject> PostJsonAsync(string path, JObject body, CancellationToken token = default)
    {
        using var request = JsonRequest(path, body);
        await SendAsync(request, token);
        return JObject.Parse(request.downloadHandler.text);
    }

    /// <summary>[A3] POST a JSON body, get raw bytes back (e.g. an audio file). Used for /v1/audio/speech.</summary>
    public async Task<byte[]> PostJsonForBytesAsync(string path, JObject body, CancellationToken token = default)
    {
        using var request = JsonRequest(path, body);
        await SendAsync(request, token);
        return request.downloadHandler.data;
    }

    /// <summary>[A3] POST a multipart form (fields + files), get a JSON reply. Used for /v1/audio/transcriptions.</summary>
    public async Task<JObject> PostFormAsync(string path, List<IMultipartFormSection> form, CancellationToken token = default)
    {
        using var request = UnityWebRequest.Post(baseUrl + path, form);
        await SendAsync(request, token);
        return JObject.Parse(request.downloadHandler.text);
    }

    private UnityWebRequest JsonRequest(string path, JObject body)
    {
        var request = new UnityWebRequest(baseUrl + path, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None))),
            downloadHandler = new DownloadHandlerBuffer(),
        };
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    private async Task SendAsync(UnityWebRequest request, CancellationToken token)
    {
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        request.timeout = 60;

        // Works in every Unity version (newer ones can also just "await request.SendWebRequest()").
        var done = new TaskCompletionSource<bool>();
        request.SendWebRequest().completed += _ => done.TrySetResult(true);
        using (token.Register(() => request.Abort()))
        {
            await done.Task;
        }
        token.ThrowIfCancellationRequested();

        if (request.result != UnityWebRequest.Result.Success)
        {
            // Gateway errors look like {"error": {"message": "..."}}.
            string message = request.downloadHandler?.text;
            try { message = (string)JObject.Parse(message)["error"]?["message"] ?? message; }
            catch (Exception) { /* not JSON — keep the raw text */ }
            throw new Exception($"HTTP {request.responseCode} from {request.url}: {message ?? request.error}");
        }
    }

    // ── Small JSON helpers for chat requests ─────────────────────────────────

    /// <summary>[A5] Describes one function (tool) the model may call, in the chat-completions "tools" format.</summary>
    public static JObject FunctionTool(string name, string description, JObject parameters, params string[] required) =>
        new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = parameters,
                    ["required"] = new JArray(required),
                },
            },
        };

    /// <summary>[A4] A chat "content part" holding an image, sent inline as a base64 JPEG data URL.</summary>
    public static JObject ImagePart(Texture2D texture, int jpegQuality = 75) =>
        new JObject
        {
            ["type"] = "image_url",
            ["image_url"] = new JObject
            {
                ["url"] = "data:image/jpeg;base64," + Convert.ToBase64String(texture.EncodeToJPG(jpegQuality)),
            },
        };

    /// <summary>[A3] A chat "content part" holding text.</summary>
    public static JObject TextPart(string text) => new JObject { ["type"] = "text", ["text"] = text };

    /// <summary>[A3] Message content is usually a string, but some models return a list of text parts.</summary>
    public static string ContentAsText(JToken content)
    {
        if (content == null || content.Type == JTokenType.Null) return string.Empty;
        if (content.Type == JTokenType.String) return (string)content;

        var sb = new StringBuilder();
        foreach (JToken part in content)
        {
            if ((string)part["type"] == "text") sb.Append((string)part["text"]);
        }
        return sb.ToString();
    }

    /// <summary>[A5] Reads a tool argument as a string, even if the model sent an object (e.g. inline JSON).</summary>
    public static string ArgAsString(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        return token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None);
    }
}

/// <summary>[A3] Converts between Unity AudioClips and WAV files (a small header followed by 16-bit samples).</summary>
public static class WavUtility
{
    /// <summary>Encodes the first <paramref name="sampleCount"/> samples of a clip as a 16-bit WAV file. Call on the main thread.</summary>
    public static byte[] FromAudioClip(AudioClip clip, int sampleCount)
    {
        sampleCount = Mathf.Clamp(sampleCount, 0, clip.samples);
        var samples = new float[sampleCount * clip.channels];
        clip.GetData(samples, 0);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int dataBytes = samples.Length * 2;

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                                        // fmt chunk size
        writer.Write((short)1);                                  // PCM
        writer.Write((short)clip.channels);
        writer.Write(clip.frequency);                            // sample rate
        writer.Write(clip.frequency * clip.channels * 2);        // bytes per second
        writer.Write((short)(clip.channels * 2));                // bytes per sample frame
        writer.Write((short)16);                                 // bits per sample
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        foreach (float sample in samples)
        {
            writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Decodes a 16-bit PCM WAV file into an AudioClip. Call on the main thread.</summary>
    public static AudioClip ToAudioClip(byte[] wav, string clipName = "speech")
    {
        if (wav == null || wav.Length < 12 ||
            Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
        {
            throw new Exception("Expected a WAV file but got something else.");
        }

        int channels = 1, rate = 24000, bits = 16;
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string chunkId = Encoding.ASCII.GetString(wav, pos, 4);
            int chunkSize = BitConverter.ToInt32(wav, pos + 4);
            pos += 8;

            if (chunkId == "fmt ")
            {
                channels = BitConverter.ToInt16(wav, pos + 2);
                rate = BitConverter.ToInt32(wav, pos + 4);
                bits = BitConverter.ToInt16(wav, pos + 14);
            }
            else if (chunkId == "data")
            {
                if (bits != 16) throw new Exception($"Expected 16-bit WAV audio, got {bits}-bit.");

                // Streamed WAVs sometimes leave the size empty; then the data runs to the end of the file.
                if (chunkSize <= 0 || pos + chunkSize > wav.Length) chunkSize = wav.Length - pos;

                int frames = chunkSize / 2 / channels;
                var samples = new float[frames * channels];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(wav, pos + i * 2) / 32768f;
                }

                var clip = AudioClip.Create(clipName, frames, channels, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }

            if (chunkSize < 0) break;
            pos += chunkSize + (chunkSize & 1);   // chunks are padded to an even length
        }

        throw new Exception("WAV file has no audio data.");
    }
}