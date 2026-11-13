using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using LookDev.Avatar;
using MetaHuman.Dna;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// An on-device conversational Taro: text -> chat module (SLM) -> speech module (TTS) -> lipsync module (ARKit curves)
// -> DNA rig controls. The modules are interfaces (AvatarModules.cs); the models behind them are adapter components in
// Modules/*, each compiled only when its package is installed. The modules used are the adapter components on this
// GameObject (Add Component > Avatar Modules); -avatarChat / -avatarSpeech / -avatarLipSync <id|none> override a slot
// from the command line. Without an SLM Taro says what you type, without a voice the line is only shown, and without a
// lipsync module the mouth stays still. PhotoShotPlayer keeps the idle performance (blinks, gaze, head, hair) on an
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

    [Header("Dialogue")]
    [TextArea(2, 5)] public string systemPrompt = "You are Taro, a MetaHuman running on-device with Unity Sentis, and you always reply in one short sentence.";
    [Tooltip("Reply length limit. A reply cut off by it is spoken up to its last full sentence.")] [Range(16, 160)] public int maxNewTokens = 96;
    [Tooltip("Previous exchanges included in the prompt.")] [Range(0, 6)] public int historyTurns = 3;

    [Header("Face")]
    [Range(0, 2)] public float mouthGain = 1;
    [Tooltip("Brows, squint and cheeks.")] [Range(0, 2)] public float upperFaceGain = 1;
    [Tooltip("Seconds to ease from rest into a line and back.")] [Range(0, 1)] public float attackSeconds = .18f;
    [Range(0, 1.5f)] public float releaseSeconds = .45f;
    [Tooltip("The lipsync modules output 60 fps curves.")] public int targetFrameRate = 60;

    [Header("UI")]
    public InputField input;
    public Text replyText, statusText;

    // The modules in use: adapter components on this GameObject, found in Start.
    IChatModule chat;
    ISpeechModule speech;
    ILipSyncModule lipSync;
    ArkitToDna mapper;
    readonly List<ChatTurn> history = new List<ChatTurn>();
    bool ready, busy, answering, releasing, speaking;
    FaceCurves frames, lastCurves;
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
        bool Gone() { if (this) return false; DisposeModules(); return true; }
        var failed = new List<string>();
        // A module that fails to load is released and its slot left empty; the others still work.
        async Awaitable<T> Load<T>(T module, string label, string what) where T : class, IAvatarModule
        {
            if (module == null) return null;
            Status(label);
            await Awaitable.NextFrameAsync();
            if (!this) return module;
            try
            {
                await module.LoadAsync(destroyCancellationToken);
                return module;
            }
            catch (Exception e) when (this && !(e is OperationCanceledException))
            {
                Debug.LogException(e, this);
                module.Dispose();
                failed.Add(what);
                return null;
            }
        }
        try
        {
            if (rig)
            {
                if (rig.Rig == null) rig.Initialize();
                mapper = new ArkitToDna(rig.Rig.RawControlNames);
            }
            chat = Resolve<IChatModule>("-avatarChat");
            speech = Resolve<ISpeechModule>("-avatarSpeech");
            lipSync = Resolve<ILipSyncModule>("-avatarLipSync");
            Debug.Log($"[LocalAvatar] chat: {AvatarModules.Name(chat)}, speech: {AvatarModules.Name(speech)}, lipsync: {AvatarModules.Name(lipSync)}", this);
            chat = await Load(chat, "Loading…", "chat model");
            if (Gone()) return;
            speech = await Load(speech, "Loading voice…", "voice");
            if (Gone()) return;
            lipSync = await Load(lipSync, "Loading face…", "lipsync");
            if (Gone()) return;
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
        var missing = new List<string>();
        bool notInstalled = false;
        void Missing<T>(object module, string what) where T : IAvatarModule
        {
            if (module != null) return;
            if (failed.Contains(what)) { missing.Add($"{what} failed to load, see the Console"); return; }
            bool installed = AvatarModules.Of<T>().Any();
            notInstalled |= !installed;
            missing.Add(installed ? $"no {what} added" : $"no {what} installed");
        }
        Missing<IChatModule>(chat, "chat model");
        Missing<ISpeechModule>(speech, "voice");
        Missing<ILipSyncModule>(lipSync, "lipsync");
        string note = chat == null ? " Taro says what you type." : "";
        if (notInstalled) note += " Install the model packages, see the README.";
        Status(missing.Count == 0 ? "Ready" : "Ready (" + string.Join(", ", missing) + ")." + note);
        SetInteractable(true);
    }

    // The adapter component on this GameObject, unless the command line names one: an id adds that adapter if it is not
    // there yet, "none" leaves the slot empty. An id that is not installed falls back to the component.
    T Resolve<T>(string arg) where T : class, IAvatarModule
    {
        var found = GetComponents<T>();
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, arg);
        string id = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (id != null)
        {
            if (string.Equals(id, "none", StringComparison.OrdinalIgnoreCase)) return null;
            var type = AvatarModules.Find<T>(id);
            if (type != null)
            {
                var existing = GetComponent(type);
                return (T)(object)(existing ? existing : gameObject.AddComponent(type));
            }
        }
        if (found.Length > 1)
            Debug.LogWarning($"{found.Length} {typeof(T).Name} components on {name}: using {AvatarModules.Name(found[0])}. Remove the others.", this);
        return found.FirstOrDefault();
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
            if (chat != null) Status("Thinking…");
            float t0 = Time.realtimeSinceStartup;
            string reply = question;   // without a chat module Taro says what you type
            if (chat != null)
            {
                // The whole reply is shown and spoken: every sentence the model generates goes to the voice.
                var request = new ChatRequest { SystemPrompt = systemPrompt, History = history.ToArray(), Message = question, MaxNewTokens = maxNewTokens };
                reply = CompleteSentences(CleanForSpeech(await chat.ReplyAsync(request, t => { if (replyText) replyText.text = CleanForSpeech(t); }, cancel)));
                cancel.ThrowIfCancellationRequested();
            }
            if (string.IsNullOrWhiteSpace(reply)) reply = "Sorry, I have nothing to say to that.";
            if (replyText) replyText.text = reply;

            float t1 = Time.realtimeSinceStartup;
            var audio = speech != null ? await speech.SynthesizeAsync(reply, cancel) : default;
            cancel.ThrowIfCancellationRequested();
            float t2 = Time.realtimeSinceStartup;
            FaceCurves curves = null;
            if (lipSync != null && audio.Duration > 0)
            {
                curves = await lipSync.AnimateAsync(audio, cancel);
                cancel.ThrowIfCancellationRequested();
            }
            float t3 = Time.realtimeSinceStartup;
            // The status line names the stages, not the models: which module fills a slot is a code/scene choice and
            // is only logged.
            var timing = new List<string>();
            if (chat != null) timing.Add($"reply {t1 - t0:F2}s");
            if (speech != null) timing.Add($"voice {t2 - t1:F2}s");
            if (curves != null) timing.Add($"face {t3 - t2:F2}s");
            if (timing.Count > 0) Status(string.Join(" / ", timing));   // otherwise the Ready note stays
            Debug.Log($"[LocalAvatar] {AvatarModules.Name(chat)} {t1 - t0:F2}s / {AvatarModules.Name(speech)} {t2 - t1:F2}s / {AvatarModules.Name(lipSync)} {t3 - t2:F2}s", this);

            // Only a reply that is spoken joins the context, and the clip is replaced with its curves (Space replays both).
            if (chat != null)
            {
                history.Add(new ChatTurn(question, reply));
                while (history.Count > historyTurns) history.RemoveAt(0);
            }
            if (clip) Destroy(clip);
            clip = null;
            if (audio.Duration > 0)
            {
                clip = AudioClip.Create("reply", audio.Samples.Length, 1, audio.SampleRate, false);
                clip.SetData(audio.Samples, 0);
            }
            lastCurves = curves;
            answering = true;
            if (clip) Play();
            else Finish();   // nothing to voice: the text is the answer
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
        speaking = true;
        playClock = 0;
        releasing = false;
        releaseClock = 0;
        if (voice) { voice.clip = clip; voice.Play(); }
    }

    // The voice is over: ease the last pose back to rest and hand the field back.
    void Finish()
    {
        speaking = false;
        releasing = frames != null;
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
            else if (speaking) Finish();                              // or the one being spoken
        }
        if (busy || (input && input.isFocused)) return;   // while busy the field is disabled but the user may still type
        if (keys.spaceKey.wasPressedThisFrame && ready && !speaking && frames == null && clip) { busy = true; SetInteractable(false); Play(); }
        if (keys.cKey.wasPressedThisFrame && shot) shot.cameraMove = !shot.cameraMove;   // the sweep stops where it is
#endif
    }

    void LateUpdate()
    {
        if (mapper == null || !rig) return;
        if (releasing)
        {
            releaseClock += Time.deltaTime;
            float k = releaseSeconds > 0 ? 1 - Mathf.SmoothStep(0, 1, releaseClock / releaseSeconds) : 0;
            if (k <= 0) { frames = null; releasing = false; Apply(null, 0, 0, 0); return; }
            Apply(frames, frames.FrameCount - 1, mouthGain * k, upperFaceGain * k);
            return;
        }
        if (!speaking) { Apply(null, 0, 0, 0); return; }

        // The audio is the master clock, so the lips never drift from the voice.
        bool voicePlaying = voice && voice.isPlaying && voice.clip == clip;
        if (voicePlaying) playClock = (float)voice.timeSamples / clip.frequency;
        else playClock += Time.deltaTime;
        // Without a face the voice alone sets the end.
        if (frames == null) { Apply(null, 0, 0, 0); if (!voicePlaying) Finish(); return; }

        float frame = playClock * frames.Fps, last = frames.FrameCount - 1;
        // The face can end a little before the voice: hold its last pose until the voice is done, so its tail is not cut.
        if (frame >= last)
        {
            Apply(frames, last, mouthGain, upperFaceGain);
            if (!voicePlaying) Finish();
            return;
        }
        float attack = attackSeconds > 0 ? Mathf.SmoothStep(0, 1, playClock / attackSeconds) : 1;
        Apply(frames, frame, mouthGain * attack, upperFaceGain * attack);
    }

    // The mouth interior darkens with the jaw, as in the sample shot (its idle track has no jaw).
    void Apply(FaceCurves curves, float frame, float gain, float upperGain)
    {
        if (curves == null || curves.FrameCount == 0) mapper.Apply(rig, null, 0, 0, 0);
        else mapper.Apply(rig, curves.Values, curves.Stride, frame, gain, upperGain);
        if (shot) shot.ShadeMouth(mapper.JawOpen);
    }

    void OnDestroy()
    {
        Time.timeScale = 1;
        Time.maximumDeltaTime = previousMaxDeltaTime;
        QualitySettings.vSyncCount = previousVSync;
        Application.targetFrameRate = previousFrameRate;
        if (clip) Destroy(clip);
        DisposeModules();
    }

    // The slots keep their (serialized) modules: only the loaded models are released.
    void DisposeModules()
    {
        chat?.Dispose();
        speech?.Dispose();
        lipSync?.Dispose();
    }

    static readonly Regex Markup = new Regex(@"[*_#`~>|\[\]{}]|\p{Cs}|\p{So}", RegexOptions.Compiled);
    static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

    // A sentence end (closing quotes and brackets included) followed by a space or the end of the text.
    static readonly Regex SentenceEnd = new Regex(@"[.!?。！？…~]['""”’)\]]*(?=\s|$)", RegexOptions.Compiled | RegexOptions.RightToLeft);

    // A reply cut off by maxNewTokens ends mid-sentence: it is kept up to its last full sentence, when it has one.
    public static string CompleteSentences(string s)
    {
        s = s.TrimEnd();
        var last = SentenceEnd.Match(s);   // right to left: the last sentence end
        return last.Success ? s.Substring(0, last.Index + last.Length) : s;
    }

    // Markdown and emoji are not voiced.
    public static string CleanForSpeech(string s) => string.IsNullOrEmpty(s) ? "" : Spaces.Replace(Markup.Replace(s, " "), " ").Trim();

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
