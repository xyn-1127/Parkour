using UnityEngine;

public class LocomotionTechnique : MonoBehaviour
{
    [Header("Respawn")]
    public OVRCameraRig ovrRig;
    public Transform trackingSpace;
    public Transform centerEyeAnchor;
    public float respawnFreezeSeconds = 0.15f;
    private bool _respawning;

    [Header("OVR / Rig References")]
    public OVRInput.Controller leftController;
    public OVRInput.Controller rightController;
    public GameObject hmd;

    [Header("Global Gain")]
    [Range(0, 10)] public float translationGain = 1.0f;

    [SerializeField] private float leftTriggerValue;
    [SerializeField] private float rightTriggerValue;
    [SerializeField] private bool isIndexTriggerDown;

    [Header("Physics Movement")]
    public bool useRigidbodyMovement = true;
    public bool autoConfigureRigidbody = true;
    public bool enableGravity = true;
    public float maxSpeedMps = 10.0f;
    public float walkSpeedMps = 2.0f;
    public float accelMps2 = 14f;
    public float decelMps2 = 20f;
    public float fixedStepClampMps = 10.0f;

    [Header("Grounding / Collision Check")]
    public LayerMask groundMask = ~0;
    public float groundCheckDistance = 0.15f;
    public float groundCheckStartOffset = 0.05f;
    [Range(0.5f, 0.99f)] public float groundProbeRadiusScale = 0.9f;

    [Header("Hold-to-Engage Gate")]
    [Range(0f, 1f)] public float triggerHoldThreshold = 0.95f;

    [Header("Arm-Swing Locomotion")]
    public bool enableArmSwing = true;
    public float abdomenOffsetMeters = 0.50f;

    [Header("Baseline (EMA)")]
    [Range(0.2f, 5f)] public float baselineTau = 1.6f;
    [Range(0.01f, 0.30f)] public float baselineFreezeAbsSignal = 0.08f;

    [Header("Swing Power -> Speed")]
    [Range(0.05f, 0.6f)] public float powerTau = 0.18f;
    [Range(0.1f, 6f)] public float powerAtWalk = 0.3f;
    [Range(0.2f, 12f)] public float powerAtRun = 1.8f;
    [Range(0f, 0.5f)] public float powerDead = 0.08f;
    [Range(0.1f, 5f)] public float gaitDecayTau = 1.8f;

    [SerializeField] private float leftSignalMeters;
    [SerializeField] private float rightSignalMeters;
    [SerializeField] private float swingPower;
    [SerializeField] private float gaitAxis01;

    private float _baseL, _baseR;
    private float _prevSigL, _prevSigR;
    private float _powerLP;

    [Header("Jump (Button)")]
    public bool enableButtonJump = true;
    public float jumpSpeedMps = 2.2f;
    public float jumpCooldown = 0.25f;
    private float lastJumpTime = -999f;
    public bool vrJumpUseButtonOne = true;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private bool leftTriggerHeld;
    private bool rightTriggerHeld;
    private Vector3 desiredHorizontalVel;
    private float prevUpdateTime;

    public ParkourCounter parkourCounter;
    public string stage;
    public SelectionTaskMeasure selectionTaskMeasure;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
    }

    void Start()
    {
        prevUpdateTime = Time.time;

        if (autoConfigureRigidbody && rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = enableGravity;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        ResetPowerState(true);
    }

    void Update()
    {
        float now = Time.time;
        float dt = Mathf.Max(1e-4f, now - prevUpdateTime);

        leftTriggerValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, leftController);
        rightTriggerValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, rightController);
        leftTriggerHeld = leftTriggerValue > triggerHoldThreshold;
        rightTriggerHeld = rightTriggerValue > triggerHoldThreshold;

        bool vrEngaged = leftTriggerHeld || rightTriggerHeld;
        isIndexTriggerDown = vrEngaged;

        Vector3 forwardWorld = (hmd != null) ? hmd.transform.forward : transform.forward;
        forwardWorld.y = 0f;
        forwardWorld = (forwardWorld.sqrMagnitude > 1e-6f) ? forwardWorld.normalized : Vector3.forward;

        if (enableArmSwing && vrEngaged)
        {
            UpdatePowerFromVrHands(dt);
        }
        else
        {
            DecayGait(dt);
            leftSignalMeters = 0f;
            rightSignalMeters = 0f;
            swingPower = 0f;
        }

        float forwardAxis = vrEngaged ? gaitAxis01 : 0f;

        bool vrJumpPressed = false;
        if (enableButtonJump && vrJumpUseButtonOne)
        {
            vrJumpPressed = OVRInput.GetDown(OVRInput.Button.One);
        }

        if (enableButtonJump && vrJumpPressed && (now - lastJumpTime >= jumpCooldown))
        {
            DoJump();
            lastJumpTime = now;
        }

        float targetSpeed;
        float clampedAxis = Mathf.Clamp(forwardAxis, -1f, 1f);
        float absAxis = Mathf.Abs(clampedAxis);
        targetSpeed = Mathf.Sign(clampedAxis) * absAxis * maxSpeedMps * translationGain;

        Vector3 currentHoriz = Vector3.zero;
        if (rb != null)
        {
            Vector3 v = GetRBVelocity(rb);
            currentHoriz = new Vector3(v.x, 0f, v.z);
        }

        Vector3 targetHoriz = forwardWorld * targetSpeed;
        float accel = (Mathf.Abs(targetSpeed) > 0.01f) ? accelMps2 : decelMps2;
        desiredHorizontalVel = Vector3.MoveTowards(currentHoriz, targetHoriz, accel * dt);

        bool vrRespawnPressed = OVRInput.GetDown(OVRInput.Button.Two) || OVRInput.GetDown(OVRInput.Button.Four);

        if (vrRespawnPressed)
        {
            if (parkourCounter != null && parkourCounter.parkourStart)
            {
                Vector3 respawnPos = parkourCounter.currentRespawnPos;

                if (rb != null)
                {
                    SetRBVelocity(rb, Vector3.zero);
                    rb.angularVelocity = Vector3.zero;
                    rb.position = respawnPos;
                }
                else
                {
                    transform.position = respawnPos;
                }
            }
        }

        prevUpdateTime = now;
    }

    void FixedUpdate()
    {
        if (!useRigidbodyMovement || rb == null) return;

        Vector3 v = GetRBVelocity(rb);
        Vector3 horiz = desiredHorizontalVel;

        if (horiz.magnitude > fixedStepClampMps)
            horiz = horiz.normalized * fixedStepClampMps;

        v.x = horiz.x;
        v.z = horiz.z;
        SetRBVelocity(rb, v);
    }

    private void ResetPowerState(bool hard)
    {
        if (hard)
        {
            _baseL = 0f;
            _baseR = 0f;
            _prevSigL = 0f;
            _prevSigR = 0f;
            _powerLP = 0f;
            gaitAxis01 = 0f;
        }
    }

    private void UpdatePowerFromVrHands(float dt)
    {
        if (hmd == null) { DecayGait(dt); return; }

        Vector3 hmdLocal = hmd.transform.localPosition;
        Vector3 abdomen = hmdLocal + Vector3.down * abdomenOffsetMeters;

        Vector3 localForward = GetLocalForwardOnPlane();
        if (localForward.sqrMagnitude < 1e-6f) localForward = Vector3.forward;

        Vector3 leftLocal = OVRInput.GetLocalControllerPosition(leftController);
        Vector3 rightLocal = OVRInput.GetLocalControllerPosition(rightController);

        Vector3 leftRel = leftLocal - abdomen;
        Vector3 rightRel = rightLocal - abdomen;

        float leftRawF = Vector3.Dot(leftRel, localForward);
        float rightRawF = Vector3.Dot(rightRel, localForward);

        UpdatePowerFromRaw(dt, leftRawF, rightRawF);
    }

    private void UpdatePowerFromRaw(float dt, float leftRawF, float rightRawF)
    {
        float sigL0 = leftRawF - _baseL;
        float sigR0 = rightRawF - _baseR;

        bool freezeL = Mathf.Abs(sigL0) > baselineFreezeAbsSignal;
        bool freezeR = Mathf.Abs(sigR0) > baselineFreezeAbsSignal;

        float aBase = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, baselineTau));
        if (!freezeL) _baseL = Mathf.Lerp(_baseL, leftRawF, aBase);
        if (!freezeR) _baseR = Mathf.Lerp(_baseR, rightRawF, aBase);

        float sigL = leftRawF - _baseL;
        float sigR = rightRawF - _baseR;

        leftSignalMeters = sigL;
        rightSignalMeters = sigR;

        float vL = (sigL - _prevSigL) / Mathf.Max(1e-4f, dt);
        float vR = (sigR - _prevSigR) / Mathf.Max(1e-4f, dt);
        _prevSigL = sigL;
        _prevSigR = sigR;

        float rawPower = Mathf.Abs(vL) + Mathf.Abs(vR);

        float aPow = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, powerTau));
        _powerLP = Mathf.Lerp(_powerLP, rawPower, aPow);
        swingPower = _powerLP;

        float t = 0f;
        if (_powerLP <= powerDead)
        {
            DecayGait(dt);
            return;
        }
        else
        {
            float lo = Mathf.Max(1e-3f, powerDead);
            float hi = Mathf.Max(lo + 1e-3f, powerAtRun);
            t = Mathf.InverseLerp(lo, hi, _powerLP);
            t = Mathf.Clamp01(t);
        }

        float aG = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, powerTau));
        gaitAxis01 = Mathf.Lerp(gaitAxis01, t, aG);
    }

    private void DecayGait(float dt)
    {
        float decayTau = 0.3f;
        float a = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, decayTau));
        gaitAxis01 = Mathf.Lerp(gaitAxis01, 0f, a);
        _powerLP = Mathf.Lerp(_powerLP, 0f, a);
    }

    private void DoJump()
    {
        if (rb == null) return;
        Vector3 v = GetRBVelocity(rb);
        v.y = jumpSpeedMps;
        SetRBVelocity(rb, v);
    }

    private Vector3 GetLocalForwardOnPlane()
    {
        if (hmd != null)
        {
            Vector3 f = hmd.transform.localRotation * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) return f.normalized;
        }
        return Vector3.forward;
    }

    private static Vector3 GetRBVelocity(Rigidbody r)
    {
#if UNITY_6000_0_OR_NEWER
        return r.linearVelocity;
#else
        return r.velocity;
#endif
    }

    private static void SetRBVelocity(Rigidbody r, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        r.linearVelocity = v;
#else
        r.velocity = v;
#endif
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("banner"))
        {
            stage = other.gameObject.name;
            if (parkourCounter != null) parkourCounter.isStageChange = true;
        }
        else if (other.CompareTag("objectInteractionTask"))
        {
            if (selectionTaskMeasure != null)
            {
                selectionTaskMeasure.isTaskStart = true;
                selectionTaskMeasure.scoreText.text = "";
                selectionTaskMeasure.partSumErr = 0f;
                selectionTaskMeasure.partSumTime = 0f;

                float tempValueY = other.transform.position.y > 0 ? 12 : 0;
                Vector3 tmpTarget = new Vector3(hmd.transform.position.x, tempValueY, hmd.transform.position.z);
                selectionTaskMeasure.taskUI.transform.LookAt(tmpTarget);
                selectionTaskMeasure.taskUI.transform.Rotate(new Vector3(0, 180f, 0));
                selectionTaskMeasure.taskStartPanel.SetActive(true);
            }
        }
        else if (other.CompareTag("coin"))
        {
            if (parkourCounter != null) parkourCounter.coinCount += 1;

            var audio = GetComponent<AudioSource>();
            if (audio != null) audio.Play();

            other.gameObject.SetActive(false);
        }
    }
}