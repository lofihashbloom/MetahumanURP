using MetaHuman.Dna;
using MetaHuman.Groom;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Plays the Taro talking shot in Play mode: the lip-sync TalkTrack on the DNA rig, the speech, a slow procedural
// head and torso gesture with breathing, and the authored camera move (an eased orbit around the head with a push-in).
// A runtime port of the look-development render loop that produced the reference video; the numbers are that shot's.
// Runs before the rig (DnaRigDriver, LateUpdate order 0) and the facial grooms (GroomSurfaceBinding, order 1000).
// Space restarts the line; C toggles the camera move (off: the camera stays where it is).
[ExecuteAlways, DefaultExecutionOrder(-100)]
public sealed class PhotoShotPlayer : MonoBehaviour
{
    public DnaRigDriver rig;
    public TalkTrack track;
    public AudioSource voice;
    [Tooltip("Head joint of the face skeleton; its parent (the neck) takes 40% of every turn.")] public Transform head;
    [Tooltip("Chest joints that breathe and follow the head, one per skeleton (face, body, clothes).")] public Transform[] chests = new Transform[0];

    [Header("Camera")]
    public Camera view;
    [Tooltip("World point the camera orbits: the head at rest.")] public Vector3 anchor;
    public bool cameraMove = true;
    [Tooltip("Camera yaw around the head in degrees at the start and the end of the line (eased).")] public float orbitFrom = 24, orbitTo = -30;
    [Tooltip("Fraction of the camera distance covered by the push-in over the line.")] [Range(0, .9f)] public float push = .358f;
    [Tooltip("Seconds of one camera pass played back and forth on its own clock, which stops while the camera move is off; 0 times the move to the line.")] [Min(0)] public float cameraSweepSeconds;

    [Header("Gesture")]
    [Tooltip("Yaw, pitch and roll amplitudes of the idle head motion in degrees; w: breathing (chest pitch) in degrees.")]
    public Vector4 gesture = new Vector4(3, 2, 1, .8f);
    [Tooltip("Phase of the idle motion in radians.")] public float phase;
    [Tooltip("Share of the camera's orbit yaw the head turns toward the lens.")] public float headFollow = .4f;
    [Tooltip("Share of the head yaw the chest follows, and its lag in seconds.")] public float torsoFollow = .2f, torsoLagSeconds = .3f;

    [Header("Playback")]
    public bool loop = true;
    [Tooltip("Space restarts the line, C toggles the camera move.")] public bool keyboardShortcuts = true;
    [Min(0)] public float pauseSeconds = 1.5f;
    public bool bindFacialGrooms = true;

    [Header("Rendering")]
    [Tooltip("Pipeline used while this scene plays (restored afterwards).")] public RenderPipelineAsset pipeline;
    [Tooltip("Environment light on the hair strands (shader globals of the teaser hair shader).")] public float hairEnvironmentDiffuse = .8f, hairEnvironmentSpecular = .12f;

    [Header("Mouth")]
    [Tooltip("Teeth, gum and tongue materials darkened while the mouth is half open: the lips and cheeks keep direct light out, which the shadow maps miss at this scale.")]
    public Material[] mouthMaterials = new Material[0];
    [Tooltip("Largest darkening of the mouth interior (0 = off).")] [Range(0, .6f)] public float mouthShade;
    [Tooltip("jawOpen values: the darkening fades in between x and y, and out again between z and w (mouth wide open).")] public Vector4 mouthShadeJaw = new Vector4(.02f, .1f, .22f, .42f);

    public float Clock { get; private set; }
    public int BoundGrooms { get; private set; }

    Transform neck;
    Quaternion headRest, neckRest, cameraRest;
    Quaternion[] chestRest = new Quaternion[0];
    Vector3 cameraRestPosition;
    RenderPipelineAsset previousPipeline;
    bool pipelineSwapped, started;
    int warmup, jawControl = -1;
    float sweepClock;
    GroomSway[] sways = new GroomSway[0];
    readonly System.Collections.Generic.List<(Renderer renderer, int slot, Color colour)> mouthSlots = new System.Collections.Generic.List<(Renderer, int, Color)>();
    MaterialPropertyBlock block;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    void OnEnable()
    {
        Shader.SetGlobalFloat("_TeaserHairEnvDiffuse", hairEnvironmentDiffuse);
        Shader.SetGlobalFloat("_TeaserHairEnvSpecular", hairEnvironmentSpecular);
        if (!Application.isPlaying || !pipeline || QualitySettings.renderPipeline == pipeline) return;
        previousPipeline = QualitySettings.renderPipeline; QualitySettings.renderPipeline = pipeline; pipelineSwapped = true;
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat("_TeaserHairEnvDiffuse", 0);
        Shader.SetGlobalFloat("_TeaserHairEnvSpecular", 0);
        if (!pipelineSwapped) return;
        QualitySettings.renderPipeline = previousPipeline; pipelineSwapped = false;
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        if (bindFacialGrooms)
            foreach (var binding in GetComponentsInChildren<GroomSurfaceBinding>(true))
            {
                try { binding.Bind(); BoundGrooms++; }
                catch (System.Exception e) { Debug.LogWarning("Facial groom not bound: " + binding.name + ": " + e.Message, binding); binding.enabled = false; }
            }
        // Joints move after the animation step: skinned meshes must take the pose of the frame they render in.
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
        CaptureRest();
        sways = FindObjectsByType<GroomSway>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        warmup = 3;   // the line starts after the first frames (shader and buffer set-up hitches), not during them
    }

    /// <summary>Records the rest pose the shot is built on. Call once, before the first Pose.</summary>
    public void CaptureRest()
    {
        if (head)
        {
            neck = head.parent; headRest = head.localRotation;
            if (neck) neckRest = neck.localRotation;
        }
        chestRest = new Quaternion[chests.Length];
        for (int i = 0; i < chests.Length; i++) if (chests[i]) chestRest[i] = chests[i].localRotation;
        if (view) { cameraRestPosition = view.transform.position; cameraRest = view.transform.rotation; }
        mouthSlots.Clear(); jawControl = track ? System.Array.IndexOf(track.controls, "CTRL_expressions.jawOpen") : -1;
        if (mouthShade > 0)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                    if (shared[i] && System.Array.IndexOf(mouthMaterials, shared[i]) >= 0 && shared[i].HasProperty(BaseColor)) mouthSlots.Add((r, i, shared[i].GetColor(BaseColor)));
            }
        started = true;
    }

    public void Restart()
    {
        Clock = 0;
        // The pose jumps back to the first frame: without this the hair sway would take that jump as a violent head move.
        foreach (var sway in sways) if (sway) sway.ResetState();
        if (!voice || !track || !track.speech) return;
        voice.Stop(); voice.clip = track.speech; voice.PlayDelayed(track.leadSeconds);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null || !keyboardShortcuts) return;
        if (Keyboard.current.spaceKey.wasPressedThisFrame) Restart();
        if (Keyboard.current.cKey.wasPressedThisFrame) cameraMove = !cameraMove;
#endif
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || !track || !rig || !started) return;
        if (warmup > 0) { if (--warmup == 0) Restart(); Pose(0); return; }
        Clock += Time.deltaTime;
        if (cameraMove && cameraSweepSeconds > 0) sweepClock += Time.deltaTime;
        // The speech is the master clock: a slow frame stalls Time.deltaTime (it is clamped) but not the audio, and the
        // lips would then trail the voice for the rest of the line.
        // timeSamples stays 0 during the lead-in (PlayDelayed reports isPlaying before any sound): only follow real audio.
        if (voice && voice.isPlaying && voice.clip == track.speech && voice.timeSamples > 0)
        {
            float error = (float)voice.timeSamples / voice.clip.frequency + track.leadSeconds - Clock;
            if (Mathf.Abs(error) > .25f) { Clock += error; foreach (var sway in sways) if (sway) sway.ResetState(); }   // a long stall: jump, and start the sway afresh
            else Clock += error * Mathf.Min(1, 6 * Time.deltaTime);                                                       // otherwise ease onto the audio clock: no visible step
        }
        if (Clock >= track.Seconds + pauseSeconds && loop) Restart();
        Pose(Clock * track.framesPerSecond);
    }

    /// <summary>Poses the camera, the gesture and the rig controls for a fractional track frame. The rig evaluates afterwards.</summary>
    public void Pose(float frame)
    {
        float seconds = frame / Mathf.Max(track.framesPerSecond, 1e-3f);
        bool sweep = cameraSweepSeconds > 0;
        float t = sweep ? Mathf.PingPong(sweepClock / cameraSweepSeconds, 1) : Mathf.Clamp01(frame / Mathf.Max(track.frames - 1, 1)), ease = t * t * (3 - 2 * t);
        float cameraYaw = Mathf.Lerp(orbitFrom, orbitTo, ease);
        Vector3 right = Vector3.right, forward = Vector3.forward;
        if (view)
        {
            if (cameraMove || sweep)
            {
                var yaw = Quaternion.Euler(0, cameraYaw, 0);
                view.transform.SetPositionAndRotation(anchor + yaw * (cameraRestPosition - anchor) * Mathf.Lerp(1, 1 - push, ease), yaw * cameraRest);
            }
            right = view.transform.right; forward = view.transform.forward;
        }

        for (int c = 0; c < track.controls.Length; c++) rig.SetRawControl(track.controls[c], track.Sample(c, frame));
        if (jawControl >= 0) ShadeMouth(track.Sample(jawControl, frame));
        if (!head) return;

        // Idle motion: slow layered sines. The turn is shared 40/60 between neck and head.
        const float Tau = 6.283f;
        float Yaw(float s) => gesture.x * (.7f * Mathf.Sin(Tau * .21f * s + phase) + .3f * Mathf.Sin(Tau * .53f * s + 2 * phase));
        float follow = cameraMove || sweep ? headFollow * cameraYaw : 0;
        float headYaw = Yaw(seconds) + follow;
        float pitch = gesture.y * (.6f * Mathf.Sin(Tau * .29f * seconds + 1.7f * phase) + .4f * Mathf.Sin(Tau * .71f * seconds + phase));
        float roll = gesture.z * Mathf.Sin(Tau * .17f * seconds + 2.3f * phase);
        float breath = gesture.w * Mathf.Sin(Tau * .25f * seconds + phase);
        // Speech-timed nods and phrase posture authored in the track.
        if (track.headYaw.Length > 0)
        {
            headYaw += track.Sample(track.headYaw, 0, frame); pitch += track.Sample(track.headPitch, 0, frame); roll += track.Sample(track.headRoll, 0, frame);
        }
        float torsoYaw = torsoFollow * (Yaw(seconds - torsoLagSeconds) + follow);
        for (int i = 0; i < chests.Length; i++)
        {
            if (!chests[i]) continue;
            chests[i].localRotation = chestRest[i];
            chests[i].rotation = Quaternion.AngleAxis(torsoYaw, Vector3.up) * Quaternion.AngleAxis(breath, right) * chests[i].rotation;
        }
        Quaternion Turn(float share) => Quaternion.AngleAxis(headYaw * share, Vector3.up) * Quaternion.AngleAxis(pitch * share, right) * Quaternion.AngleAxis(roll * share, forward);
        if (neck) { neck.localRotation = neckRest; neck.rotation = Turn(.4f) * neck.rotation; }
        head.localRotation = headRest; head.rotation = Turn(neck ? .6f : 1f) * head.rotation;
    }

    /// <summary>Darkens the mouth interior for a jawOpen value. Pose calls it with the track's jaw; a component that drives the jaw itself calls it after the track.</summary>
    public void ShadeMouth(float jaw)
    {
        if (mouthSlots.Count == 0) return;
        // Per-renderer override: the material assets are not modified.
        float w = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(mouthShadeJaw.x, mouthShadeJaw.y, jaw)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(mouthShadeJaw.z, mouthShadeJaw.w, jaw)));
        float k = 1 - mouthShade * w; block ??= new MaterialPropertyBlock();
        foreach (var (r, slot, colour) in mouthSlots)
        {
            r.GetPropertyBlock(block, slot); block.SetColor(BaseColor, new Color(colour.r * k, colour.g * k, colour.b * k, colour.a)); r.SetPropertyBlock(block, slot);
        }
    }
}
