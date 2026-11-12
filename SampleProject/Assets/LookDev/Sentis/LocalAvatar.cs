using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MetaHuman.Dna;
using SentisModels;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// An on-device conversational Taro: text -> Gemma 3 270M -> Supertonic TTS -> NeuroSync curves -> DNA rig controls,
// all on Unity Inference Engine (Sentis). PhotoShotPlayer keeps the idle performance (blinks, gaze, head, hair) on an
// eyes-only copy of its talk track; this component runs after it and before the rig evaluates, and owns the face.
[DefaultExecutionOrder(-50)]
public sealed class LocalAvatar : MonoBehaviour
{
    [Header("Character")]
    public PhotoShotPlayer shot;
    public DnaRigDriver rig;
    public AudioSource voice;
    [Tooltip("Talk track whose blinks and gaze become the idle performance. The scene leaves the shot without a track, so it stays still when this component is not compiled.")]
    public TalkTrack idleSource;

    [Header("Backends")]
    public BackendType llmBackend = BackendType.GPUCompute;
    public BackendType ttsBackend = BackendType.GPUCompute;
    public BackendType lipsyncFeatureBackend = BackendType.GPUCompute;
    public BackendType lipsyncDecoderBackend = BackendType.GPUCompute;

    [Header("Dialogue")]
    [TextArea(2, 5)] public string systemPrompt = "You are Taro, a MetaHuman running on-device with Unity Sentis, and you always reply in one short sentence.";
    [Range(16, 160)] public int maxNewTokens = 48;
    [Tooltip("Previous exchanges included in the prompt.")] [Range(0, 6)] public int historyTurns = 3;
    [Tooltip("Speak only the first sentence that says something (a small model can run on).")] public bool firstSentenceOnly = true;
    [Tooltip("Supertonic voice style: F1-F5, M1-M5.")] public string voiceStyle = "M5";
    [Range(2, 16)] public int ttsSteps = 6;
    [Range(.7f, 1.5f)] public float ttsSpeed = 1.05f;

    [Header("Face")]
    [Range(0, 2)] public float mouthGain = 1;
    [Tooltip("Brows, squint and cheeks.")] [Range(0, 2)] public float upperFaceGain = 1;
    [Tooltip("Seconds to ease from rest into a line and back.")] [Range(0, 1)] public float attackSeconds = .18f;
    [Range(0, 1.5f)] public float releaseSeconds = .45f;
    [Tooltip("The curves are 60 fps.")] public int targetFrameRate = 60;

    [Header("UI")]
    public InputField input;
    public Text replyText, statusText;

    Gemma3Model llm;
    SupertonicTts tts;
    NeuroSync.NeuroSyncRunner lipsync;
    ArkitToDna mapper;
    readonly List<(string user, string reply)> history = new List<(string, string)>();
    bool ready, busy, answering, releasing;
    float[] frames, lastCurves;
    AudioClip clip;
    CancellationTokenSource generation;
    float playClock, releaseClock;
    int previousVSync, previousFrameRate;
    float previousMaxDeltaTime;

    void Awake()
    {
        if (!shot) shot = GetComponent<PhotoShotPlayer>();
        if (shot)
        {
            if (!voice) voice = shot.voice;
            shot.voice = null;
            shot.keyboardShortcuts = false;   // Space would restart the idle track: Update maps it to a replay
            shot.pauseSeconds = 0;
            var source = idleSource ? idleSource : shot.track;
            if (source) shot.track = IdleTrack(source);
        }
        if (voice) { voice.Stop(); voice.playOnAwake = false; voice.loop = false; }
        previousVSync = QualitySettings.vSyncCount; previousFrameRate = Application.targetFrameRate; previousMaxDeltaTime = Time.maximumDeltaTime;
        if (targetFrameRate > 0) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = targetFrameRate; }
        // A slow frame (model work) slows the idle and camera motion for a moment instead of jumping it ahead.
        Time.maximumDeltaTime = 1f / 30;

        // The builtin font has no Hangul or kana.
        var font = Font.CreateDynamicFontFromOSFont(new[] { "Apple SD Gothic Neo", "AppleGothic", "Malgun Gothic", "Noto Sans CJK KR", "Arial Unicode MS" }, 22);
        if (font)
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                foreach (var t in canvas.GetComponentsInChildren<Text>(true)) t.font = font;
    }

    // Blinks and gaze of the sample track (only those channels), tiled with crossfaded seams to a length the shot player
    // never loops within a conversation (a restart would snap the head and eyes back to frame 0).
    static TalkTrack IdleTrack(TalkTrack source, float minutes = 30, int seam = 20)
    {
        var idle = Instantiate(source);
        idle.speech = null;
        idle.leadSeconds = 0;
        var eyes = new List<int>();
        for (int c = 0; c < source.controls.Length; c++)
            if (source.controls[c].Contains(".eyeBlink") || source.controls[c].Contains(".eyeLook")) eyes.Add(c);
        idle.controls = eyes.ConvertAll(c => source.controls[c]).ToArray();
        int length = source.frames, step = length - seam;
        int segments = Mathf.CeilToInt((minutes * 60 * source.framesPerSecond - seam) / step);
        idle.frames = segments * step + seam;
        idle.values = new float[eyes.Count * idle.frames];
        for (int e = 0; e < eyes.Count; e++)
        {
            for (int k = 0; k < segments; k++)
                for (int j = 0; j < length; j++)
                {
                    int i = e * idle.frames + k * step + j;
                    float v = source.values[eyes[e] * length + j];
                    idle.values[i] = k > 0 && j < seam ? Mathf.Lerp(idle.values[i], v, Mathf.SmoothStep(0, 1, (j + 1f) / (seam + 1))) : v;
                }
        }
        idle.headYaw = idle.headPitch = idle.headRoll = new float[0];
        return idle;
    }

    async void Start()
    {
        if (input) input.onEndEdit.AddListener(_ => { if (EnterPressed()) Submit(); });
        SetInteractable(false);
#if !ENABLE_LEGACY_INPUT_MANAGER
        Debug.LogWarning("The chat field is a uGUI InputField: set Active Input Handling to Both (Project Settings > Player).", this);
#endif
        Time.timeScale = 0;   // nothing moves while the models load (seconds of blocking work): the shot starts still
        // Leaving Play mode during a load: OnDestroy has already run, so whatever this load creates afterwards is disposed here.
        bool Gone() { if (this) return false; DisposeModels(); return true; }
        try
        {
            Status("Loading…");
            await Awaitable.NextFrameAsync();
            if (Gone()) return;
            llm = new Gemma3Model(Gpu(llmBackend)) { SystemPrompt = systemPrompt };
            llm.Load(ModelRoot(typeof(Gemma3Model), Gemma3Model.PackageName));
            await llm.WarmupAsync();
            if (Gone()) return;

            Status("Loading voice…");
            await Awaitable.NextFrameAsync();
            if (Gone()) return;
            tts = new SupertonicTts(Gpu(ttsBackend), ttsSteps, ttsSpeed);
            tts.Load(ModelRoot(typeof(SupertonicTts), "com.sky.sentis.supertonic"));
            await tts.Synthesize("Hi.", SupertonicLanguage.en, voiceStyle);
            if (Gone()) return;

            Status("Loading face…");
            await Awaitable.NextFrameAsync();
            if (Gone()) return;
            lipsync = await NeuroSync.NeuroSyncRunner.CreateAsync(Gpu(lipsyncFeatureBackend), Gpu(lipsyncDecoderBackend));
            if (Gone()) return;
            lipsync.Warmup();

            if (rig.Rig == null) rig.Initialize();
            mapper = new ArkitToDna(rig.Rig.RawControlNames, NeuroSync.NeuroSyncConstants.NumOutputs);
        }
        catch (Exception e)
        {
            if (Gone()) return;
            Debug.LogException(e);
            Status("Load failed: " + e.Message);
            Time.timeScale = 1;
            return;
        }
        Time.timeScale = 1;
        ready = true;
        Status("Ready");
        SetInteractable(true);
    }

    static BackendType Gpu(BackendType b) => b == BackendType.GPUCompute && !SystemInfo.supportsComputeShaders ? BackendType.CPU : b;

    // The package's Models~ folder in the editor; StreamingAssets/<package> in a player.
    static string ModelRoot(Type inPackage, string packageName)
    {
#if UNITY_EDITOR
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(inPackage.Assembly);
        if (package != null) return Path.Combine(package.resolvedPath, "Models~");
#endif
        return Path.Combine(Application.streamingAssetsPath, packageName);
    }

    public void Submit()
    {
        if (input && !string.IsNullOrWhiteSpace(input.text)) _ = Ask(input.text.Trim());
    }

    // One turn. The question stays in the disabled field until the answer has been spoken.
    public async Awaitable Ask(string question)
    {
        if (!ready || busy) return;
        busy = true;
        SetInteractable(false);
        if (replyText) replyText.text = "";
        generation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        var cancel = generation.Token;
        try
        {
            Status("Thinking…");
            float t0 = Time.realtimeSinceStartup;
            string reply = CleanForSpeech(await llm.GenerateFromPromptAsync(ChatPrompt(question), maxNewTokens, t => { if (replyText) replyText.text = t; }, cancel));
            cancel.ThrowIfCancellationRequested();
            if (firstSentenceOnly) reply = FirstSentence(reply);
            if (string.IsNullOrWhiteSpace(reply)) reply = "Sorry, I have nothing to say to that.";
            if (replyText) replyText.text = reply;

            float t1 = Time.realtimeSinceStartup;
            float[] pcm = await tts.Synthesize(reply, DetectLanguage(reply), voiceStyle);
            cancel.ThrowIfCancellationRequested();
            float t2 = Time.realtimeSinceStartup;
            int rate = tts.SampleRate;
            await Awaitable.BackgroundThreadAsync();
            var audio = NeuroSync.AudioUtils.Prepare(pcm, rate);
            await Awaitable.MainThreadAsync();
            cancel.ThrowIfCancellationRequested();
            var curves = await lipsync.GenerateAsync(audio);
            cancel.ThrowIfCancellationRequested();
            float t3 = Time.realtimeSinceStartup;
            Status($"LLM {t1 - t0:F2}s / TTS {t2 - t1:F2}s / Lipsync {t3 - t2:F2}s");

            // Only a reply that is spoken joins the context, and the clip is replaced with its curves (Space replays both).
            history.Add((question, reply));
            while (history.Count > historyTurns) history.RemoveAt(0);
            if (clip) Destroy(clip);
            clip = AudioClip.Create("reply", pcm.Length, 1, rate, false);
            clip.SetData(pcm, 0);
            lastCurves = curves;
            answering = true;
            Play();
        }
        catch (Exception e)
        {
            if (!this) return;   // left Play mode mid-turn
            if (e is OperationCanceledException) Status("Stopped");
            else { Debug.LogException(e); Status("Error: " + e.Message); }
            if (replyText && e is OperationCanceledException) replyText.text = "";
            busy = false;
            SetInteractable(true);
            if (input) input.ActivateInputField();
        }
        finally
        {
            generation?.Dispose();
            generation = null;
        }
    }

    void Play()
    {
        frames = lastCurves;
        playClock = 0;
        releasing = false;
        releaseClock = 0;
        if (voice) { voice.clip = clip; voice.Play(); }
    }

    // The voice is over: ease the last pose back to rest and hand the field back.
    void Finish()
    {
        releasing = true;
        releaseClock = 0;
        if (voice) voice.Stop();
        busy = false;
        if (input && answering) input.text = "";   // a replay leaves a draft in the field alone
        answering = false;
        SetInteractable(true);
        if (input) input.ActivateInputField();
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var keys = Keyboard.current;
        if (keys == null) return;
        if (keys.escapeKey.wasPressedThisFrame)
        {
            if (generation != null) generation.Cancel();             // stops the reply being prepared
            else if (frames != null && !releasing) Finish();         // or the one being spoken
        }
        if (busy || (input && input.isFocused)) return;   // while busy the field is disabled but the user may still type
        if (keys.spaceKey.wasPressedThisFrame && ready && frames == null && lastCurves != null) { busy = true; SetInteractable(false); Play(); }
        if (keys.cKey.wasPressedThisFrame && shot) shot.cameraMove = !shot.cameraMove;   // the sweep stops where it is
#endif
    }

    void LateUpdate()
    {
        if (mapper == null || !rig) return;
        if (frames == null) { Apply(null, 0, 0, 0); return; }
        float last = frames.Length / NeuroSync.NeuroSyncConstants.NumOutputs - 1;

        if (releasing)
        {
            releaseClock += Time.deltaTime;
            float k = releaseSeconds > 0 ? 1 - Mathf.SmoothStep(0, 1, releaseClock / releaseSeconds) : 0;
            if (k <= 0) { frames = null; releasing = false; Apply(null, 0, 0, 0); return; }
            Apply(frames, last, mouthGain * k, upperFaceGain * k);
            return;
        }

        // The audio is the master clock, so the lips never drift from the voice.
        if (voice && voice.isPlaying && voice.clip == clip) playClock = (float)voice.timeSamples / clip.frequency;
        else playClock += Time.deltaTime;

        float frame = playClock * NeuroSync.NeuroSyncConstants.FaceFps;
        if (frame >= last) { Apply(frames, last, mouthGain, upperFaceGain); Finish(); return; }
        float attack = attackSeconds > 0 ? Mathf.SmoothStep(0, 1, playClock / attackSeconds) : 1;
        Apply(frames, frame, mouthGain * attack, upperFaceGain * attack);
    }

    // The mouth interior darkens with the jaw, as in the sample shot (its idle track has no jaw).
    void Apply(float[] curves, float frame, float gain, float upperGain)
    {
        mapper.Apply(rig, curves, frame, gain, upperGain);
        if (shot) shot.ShadeMouth(mapper.JawOpen);
    }

    void OnDestroy()
    {
        Time.timeScale = 1;
        Time.maximumDeltaTime = previousMaxDeltaTime;
        QualitySettings.vSyncCount = previousVSync;
        Application.targetFrameRate = previousFrameRate;
        if (clip) Destroy(clip);
        DisposeModels();
    }

    void DisposeModels()
    {
        llm?.Dispose(); llm = null;
        tts?.Dispose(); tts = null;
        lipsync?.Dispose(); lipsync = null;
    }

    // Gemma 3 has no system role: the system prompt leads the first user turn.
    string ChatPrompt(string question)
    {
        var sb = new StringBuilder();
        void User(string text)
        {
            sb.Append("<start_of_turn>user\n");
            if (sb.Length == "<start_of_turn>user\n".Length && !string.IsNullOrEmpty(systemPrompt)) sb.Append(systemPrompt).Append("\n\n");
            sb.Append(text).Append("<end_of_turn>\n");
        }
        foreach (var (user, reply) in history) { User(user); sb.Append("<start_of_turn>model\n").Append(reply).Append("<end_of_turn>\n"); }
        User(question);
        return sb.Append("<start_of_turn>model\n").ToString();
    }

    static readonly Regex SentenceEnd = new Regex(@"(?<=[.!?。！？])\s", RegexOptions.Compiled);
    static readonly Regex Filler = new Regex(@"^(that'?s|what) (a|an) (really )?(great|good|tough|interesting|fun|wonderful) (question|one)|^(of course|sure|great question|good question)[.!]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Markup = new Regex(@"[*_#`~>|\[\]{}]|\p{Cs}|\p{So}", RegexOptions.Compiled);
    static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

    // Skips a leading stock phrase ("That's a great question!"), keeps a short sentence ("Yes!") with the next one and
    // a joke's setup question with its punchline.
    public static string FirstSentence(string s)
    {
        var parts = SentenceEnd.Split(s.Trim());
        int i = 0;
        while (i < parts.Length - 1 && Filler.IsMatch(parts[i].Trim())) i++;
        string reply = parts[i].Trim();
        while (i < parts.Length - 1 && reply.Split(' ').Length < 4) reply += " " + parts[++i].Trim();
        if (reply.EndsWith("?") && i < parts.Length - 1) reply += " " + parts[++i].Trim();
        return reply;
    }

    // Markdown and emoji are not voiced.
    public static string CleanForSpeech(string s) => string.IsNullOrEmpty(s) ? "" : Spaces.Replace(Markup.Replace(s, " "), " ").Trim();

    public static SupertonicLanguage DetectLanguage(string s)
    {
        int hangul = 0, kana = 0;
        foreach (char c in s)
        {
            if (c >= 0xAC00 && c <= 0xD7A3 || c >= 0x1100 && c <= 0x11FF || c >= 0x3130 && c <= 0x318F) hangul++;
            else if (c >= 0x3040 && c <= 0x30FF) kana++;
        }
        if (hangul > 0 && hangul >= kana) return SupertonicLanguage.ko;
        return kana > 0 ? SupertonicLanguage.ja : SupertonicLanguage.en;
    }

    static bool EnterPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
#else
        return true;
#endif
    }

    void SetInteractable(bool on)
    {
        if (input) input.interactable = on;
    }

    void Status(string s)
    {
        if (statusText) statusText.text = s;
    }
}
