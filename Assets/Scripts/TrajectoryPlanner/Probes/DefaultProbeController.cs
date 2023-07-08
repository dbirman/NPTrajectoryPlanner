using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

public class DefaultProbeController : ProbeController
{
    #region Movement Constants
    private const float MOVE_INCREMENT_TAP = 0.010f; // move 1 um per tap
    private const float MOVE_INCREMENT_TAP_FAST = 0.100f;
    private const float MOVE_INCREMENT_TAP_SLOW = 0.001f;
    private const float MOVE_INCREMENT_HOLD = 0.100f; // move 50 um per second when holding
    private const float MOVE_INCREMENT_HOLD_FAST = 1.000f;
    private const float MOVE_INCREMENT_HOLD_SLOW = 0.010f;
    private const float ROT_INCREMENT_TAP = 1f;
    private const float ROT_INCREMENT_TAP_FAST = 10f;
    private const float ROT_INCREMENT_TAP_SLOW = 0.1f;
    private const float ROT_INCREMENT_HOLD = 5f;
    private const float ROT_INCREMENT_HOLD_FAST = 25;
    private const float ROT_INCREMENT_HOLD_SLOW = 2.5f;
    #endregion

    #region Key hold flags
    private bool keyFast = false;
    private bool keySlow = false;
    private bool keyHeld = false; // If a key is held, we will skip re-checking the key hold delay for any other keys that are added
    private float keyPressTime = 0f;
    private const float keyHoldDelay = 0.300f;
    #endregion

    #region Angle limits
    private const float minPitch = -90f;
    private const float maxPitch = 0f;
    #endregion

    #region Defaults
    // in ap/ml/dv
    private Vector3 defaultStart = Vector3.zero; // new Vector3(5.4f, 5.7f, 0.332f);
    private float defaultDepth = 0f;
    private Vector2 defaultAngles = new Vector2(-90f, 0f); // 0 yaw is forward, default pitch is 90 degrees down from horizontal, but internally this is a value of 0f
    #endregion

    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private float depth;

    // Offset vectors
    private GameObject probeTipOffset;
    private GameObject probeTipTop;

    // References
    [SerializeField] private Transform _probeTipT;
    [FormerlySerializedAs("rotateAround")] [SerializeField] private Transform _rotateAround;

    public override Transform ProbeTipT { get { return _probeTipT; } }

    private void Awake()
    {
        // Create two points offset from the tip that we'll use to interpolate where we are on the probe
        probeTipOffset = new GameObject(name + "TipOffset");
        probeTipOffset.transform.parent = _probeTipT;
        probeTipOffset.transform.position = _probeTipT.position + _probeTipT.up * 0.2f;

        probeTipTop = new GameObject(name + "TipTop");
        probeTipTop.transform.parent = _probeTipT;
        probeTipTop.transform.position = _probeTipT.position + _probeTipT.up * 10.2f;

        depth = defaultDepth;

        _initialPosition = transform.position;
        _initialRotation = transform.rotation;

        Insertion = new ProbeInsertion(defaultStart, defaultAngles, CoordinateSpaceManager.ActiveCoordinateSpace, CoordinateSpaceManager.ActiveCoordinateTransform);
    }

    private void Start()
    {
        SetProbePosition();
    }

    /// <summary>
    /// Put this probe back at Bregma
    /// </summary>
    public override void ResetInsertion()
    {
        ResetPosition();
        ResetAngles();
        SetProbePosition();
    }

    public override void ResetPosition()
    {
        Insertion.apmldv = defaultStart;
    }

    public override void ResetAngles()
    {
        Insertion.angles = defaultAngles;
    }

    #region Keyboard movement

    private void CheckForSpeedKeys()
    {
        keyFast = Input.GetKey(KeyCode.LeftShift);
        keySlow = Input.GetKey(KeyCode.LeftControl);
    }

    public void MoveProbe_Keyboard()
    {
        // Exit when using drag movement or when Locked and not under keyboard control
        if (dragging || (Locked && !ManipulatorKeyboardControl))
        {
            keyHeld = false;
            return;
        }

        bool moved = false;
        bool keyHoldDelayPassed = (Time.realtimeSinceStartup - keyPressTime) > keyHoldDelay;

        CheckForSpeedKeys();
        // Handle click inputs

        // A note about key presses. In Unity on most computers with high frame rates pressing a key *once* will trigger:
        // Frame 0: KeyDown and Key
        // Frame 1: Key
        // Frame 2...N-1 : Key
        // Frame N: Key and KeyUp
        // On *really* fast computers you might get multiple frames with Key before you see the KeyUp event. This is... a pain, if we want to be able to do both smooth motion and single key taps.
        // We handle this by having a minimum "hold" time of say 50 ms before we start paying attention to the Key events

        // [TODO] There's probably a smart refactor to be done here so that key press/hold is functionally separate from calling the Move() functions
        // probably need to store the held KeyCodes in a list or something? 

        // APML movements
        if (Input.GetKeyDown(KeyCode.W))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            MoveProbeXYZ(0f, 0f, -1f, true);
        }
        else if (Input.GetKey(KeyCode.W) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeXYZ(0f, 0f, -1f, false);
        }
        if (Input.GetKeyUp(KeyCode.W))
            keyHeld = false;

        if (Input.GetKeyDown(KeyCode.S))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            MoveProbeXYZ(0f, 0f, 1f, true);
        }
        else if (Input.GetKey(KeyCode.S) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeXYZ(0f, 0f, 1f, false);
        }
        if (Input.GetKeyUp(KeyCode.S))
            keyHeld = false;

        if (Input.GetKeyDown(KeyCode.D))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            MoveProbeXYZ(-1f, 0f, 0f, true);
        }
        else if (Input.GetKey(KeyCode.D) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeXYZ(-1f, 0f, 0f, false);
        }
        if (Input.GetKeyUp(KeyCode.D))
            keyHeld = false;

        if (Input.GetKeyDown(KeyCode.A))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            MoveProbeXYZ(1f, 0f, 0f, true);
        }
        else if (Input.GetKey(KeyCode.A) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeXYZ(1f, 0f, 0f, false);
        }
        if (Input.GetKeyUp(KeyCode.A))
            keyHeld = false;

        // DV movement

        if (Input.GetKeyDown(KeyCode.Q))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            ProbeManager.ManipulatorBehaviorController.IsSetToDropToSurfaceWithDepth = false;
            MoveProbeXYZ(0f, -1f, 0f, true);
        }
        else if (Input.GetKey(KeyCode.Q) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeXYZ(0f, -1f, 0f, false);
        }
        if (Input.GetKeyUp(KeyCode.Q))
            keyHeld = false;

        if (Input.GetKeyDown(KeyCode.E))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            ProbeManager.ManipulatorBehaviorController.IsSetToDropToSurfaceWithDepth = false;
            MoveProbeXYZ(0f, 1f, 0f, true);
        }
        else if (Input.GetKey(KeyCode.E) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeXYZ(0f, 1f, 0f, false);
        }
        if (Input.GetKeyUp(KeyCode.E))
            keyHeld = false;

        // Depth movement

        if (Input.GetKeyDown(KeyCode.Z))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            ProbeManager.ManipulatorBehaviorController.IsSetToDropToSurfaceWithDepth = true;
            MoveProbeDepth(1f, true);
        }
        else if (Input.GetKey(KeyCode.Z) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeDepth(1f, false);
        }
        if (Input.GetKeyUp(KeyCode.Z))
            keyHeld = false;

        if (Input.GetKeyDown(KeyCode.X))
        {
            moved = true;
            keyPressTime = Time.realtimeSinceStartup;
            ProbeManager.ManipulatorBehaviorController.IsSetToDropToSurfaceWithDepth = true;
            MoveProbeDepth(-1f, true);
        }
        else if (Input.GetKey(KeyCode.X) && (keyHeld || keyHoldDelayPassed))
        {
            keyHeld = true;
            moved = true;
            MoveProbeDepth(-1f, false);
        }
        if (Input.GetKeyUp(KeyCode.X))
            keyHeld = false;

        // Rotations (not allowed with manipulator keyboard controls)

        if (!ManipulatorKeyboardControl)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                moved = true;
                keyPressTime = Time.realtimeSinceStartup;
                YawPitchProbe(-1f, 0f, true);
            }
            else if (Input.GetKey(KeyCode.Alpha1) && (keyHeld || keyHoldDelayPassed))
            {
                keyHeld = true;
                moved = true;
                YawPitchProbe(-1f, 0f, false);
            }

            if (Input.GetKeyUp(KeyCode.Alpha1))
                keyHeld = false;

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                moved = true;
                keyPressTime = Time.realtimeSinceStartup;
                YawPitchProbe(1f, 0f, true);
            }
            else if (Input.GetKey(KeyCode.Alpha3) && (keyHeld || keyHoldDelayPassed))
            {
                keyHeld = true;
                moved = true;
                YawPitchProbe(1f, 0f, false);
            }

            if (Input.GetKeyUp(KeyCode.Alpha3))
                keyHeld = false;

            if (Input.GetKeyDown(KeyCode.R))
            {
                moved = true;
                keyPressTime = Time.realtimeSinceStartup;
                YawPitchProbe(0f, 1f, true);
            }
            else if (Input.GetKey(KeyCode.R) && (keyHeld || keyHoldDelayPassed))
            {
                keyHeld = true;
                moved = true;
                YawPitchProbe(0f, 1f, false);
            }

            if (Input.GetKeyUp(KeyCode.R))
                keyHeld = false;

            if (Input.GetKeyDown(KeyCode.F))
            {
                moved = true;
                keyPressTime = Time.realtimeSinceStartup;
                YawPitchProbe(0f, -1f, true);
            }
            else if (Input.GetKey(KeyCode.F) && (keyHeld || keyHoldDelayPassed))
            {
                keyHeld = true;
                moved = true;
                YawPitchProbe(0f, -1f, false);
            }

            if (Input.GetKeyUp(KeyCode.F))
                keyHeld = false;

            // Spin controls
            if (Input.GetKeyDown(KeyCode.Comma))
            {
                moved = true;
                keyPressTime = Time.realtimeSinceStartup;
                RollProbe(-1f, true);
            }
            else if (Input.GetKey(KeyCode.Comma) && (keyHeld || keyHoldDelayPassed))
            {
                keyHeld = true;
                moved = true;
                RollProbe(-1f, false);
            }

            if (Input.GetKeyUp(KeyCode.Comma))
                keyHeld = false;

            if (Input.GetKeyDown(KeyCode.Period))
            {
                moved = true;
                keyPressTime = Time.realtimeSinceStartup;
                RollProbe(1f, true);
            }
            else if (Input.GetKey(KeyCode.Period) && (keyHeld || keyHoldDelayPassed))
            {
                keyHeld = true;
                moved = true;
                RollProbe(1f, false);
            }

            if (Input.GetKeyUp(KeyCode.Period))
                keyHeld = false;
        }

        // Apply movements
        if (!moved) return;
        // If the probe was moved, set the new position
        SetProbePosition();

        // Update all the UI panels
        FinishedMovingEvent.Invoke();
    }

    #endregion


    #region Movement Controls

    public void MoveProbeXYZ(float x, float y, float z, bool pressed)
    {
        var speed = pressed || ManipulatorKeyboardControl
            ? keyFast ? MOVE_INCREMENT_TAP_FAST : keySlow ? MOVE_INCREMENT_TAP_SLOW : MOVE_INCREMENT_TAP
            : keyFast
                ? MOVE_INCREMENT_HOLD_FAST * Time.deltaTime
                : keySlow
                    ? MOVE_INCREMENT_HOLD_SLOW * Time.deltaTime
                    : MOVE_INCREMENT_HOLD * Time.deltaTime;

        // Get the positional delta
        var posDelta = new Vector3(x, y, z) * speed;

        // Compute target APMLDV
        var targetAPMLDV = Insertion.apmldv + Insertion.World2TransformedAxisChange(posDelta);

        if (ManipulatorKeyboardControl)
        {
            // Disable/ignore more input until movement is done
            ManipulatorKeyboardControl = false;

            // Call movement and reset keyboard control when done
            ProbeManager.ManipulatorBehaviorController.MoveXYZByWorldSpaceDelta(posDelta,
                _ => { ManipulatorKeyboardControl = true; }, Debug.LogError);
        }
        else
        {
            Insertion.apmldv = targetAPMLDV;
        }
    }

    public void MoveProbeDepth(float unitDepth, bool pressed)
    {
        var speed = pressed || ManipulatorKeyboardControl
            ? keyFast ? MOVE_INCREMENT_TAP_FAST : keySlow ? MOVE_INCREMENT_TAP_SLOW : MOVE_INCREMENT_TAP
            : keyFast
                ? MOVE_INCREMENT_HOLD_FAST * Time.deltaTime
                : keySlow
                    ? MOVE_INCREMENT_HOLD_SLOW * Time.deltaTime
                    : MOVE_INCREMENT_HOLD * Time.deltaTime;

        // Compute the depth delta
        var depthDelta = unitDepth * speed;

        if (ManipulatorKeyboardControl)
        {
            // Disable/ignore more input until movement is done
            ManipulatorKeyboardControl = false;

            ProbeManager.ManipulatorBehaviorController.MoveDepthByWorldSpaceDelta(depthDelta,
                _ => { ManipulatorKeyboardControl = true; }, Debug.LogError);
        }
        else
            depth += depthDelta;
    }

    public void YawPitchProbe(float yaw, float pitch, bool pressed)
    {
        float speed = pressed ?
            keyFast ? ROT_INCREMENT_TAP_FAST : keySlow ? ROT_INCREMENT_TAP_SLOW : ROT_INCREMENT_TAP :
            keyFast ? ROT_INCREMENT_HOLD_FAST * Time.deltaTime : keySlow ? ROT_INCREMENT_HOLD_SLOW * Time.deltaTime : ROT_INCREMENT_HOLD * Time.deltaTime;

        Insertion.yaw += yaw * speed;
        Insertion.pitch = Mathf.Clamp(Insertion.pitch + pitch * speed, minPitch, maxPitch);
    }

    public void RollProbe(float roll, bool pressed)
    {
        float speed = pressed ?
            keyFast ? ROT_INCREMENT_TAP_FAST : keySlow ? ROT_INCREMENT_TAP_SLOW : ROT_INCREMENT_TAP :
            keyFast ? ROT_INCREMENT_HOLD_FAST * Time.deltaTime : keySlow ? ROT_INCREMENT_HOLD_SLOW * Time.deltaTime : ROT_INCREMENT_HOLD * Time.deltaTime;

        Insertion.roll += roll * speed;
    }

    // Drag movement variables
    private bool axisLockZ;
    private bool axisLockX;
    private bool axisLockY;
    private bool axisLockDepth;
    private bool axisLockPitch;
    private bool axisLockYaw;
    private bool dragging;

    private Vector3 origAPMLDV;
    private float origYaw;
    private float origPitch;

    // Camera variables
    private Vector3 originalClickPositionWorld;
    private Vector3 lastClickPositionWorld;
    private float cameraDistance;

    /// <summary>
    /// Handle setting up drag movement after a user clicks on the probe
    /// </summary>
    public void DragMovementClick()
    {
        // ignore mouse clicks if we're over a UI element
        // Cancel movement if being controlled by EphysLink
        if (EventSystem.current.IsPointerOverGameObject() || ProbeManager.IsEphysLinkControlled || Locked)
            return;

        BrainCameraController.BlockBrainControl = true;

        axisLockZ = false;
        axisLockY = false;
        axisLockX = false;
        axisLockDepth = false;
        axisLockPitch = false;
        axisLockYaw = false;

        origAPMLDV = Insertion.apmldv;
        origYaw = Insertion.yaw;
        origPitch = Insertion.pitch;
        // Note: depth is special since it gets absorbed into the probe position on each frame

        // Track the screenPoint that was initially clicked
        cameraDistance = Vector3.Distance(Camera.main.transform.position, gameObject.transform.position);
        originalClickPositionWorld = Camera.main.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, cameraDistance));
        lastClickPositionWorld = originalClickPositionWorld;

        dragging = true;
    }

    /// <summary>
    /// Helper function: if the user was already moving on some other axis and then we *switch* axis, or
    /// if they repeatedly tap the same axis key we shouldn't jump back to the original position the
    /// probe was in.
    /// </summary>
    private void CheckForPreviousDragClick()
    {
        if (axisLockZ || axisLockY || axisLockX || axisLockDepth || axisLockYaw || axisLockPitch)
            DragMovementClick();
    }

    /// <summary>
    /// Handle probe movements when a user is dragging while keeping the mouse pressed
    /// </summary>
    public void DragMovementDrag()
    {
        // Cancel movement if being controlled by EphysLink
        if (ProbeManager.IsEphysLinkControlled || Locked)
            return;

        CheckForSpeedKeys();
        Vector3 curScreenPointWorld = Camera.main.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, cameraDistance));
        Vector3 worldOffset = curScreenPointWorld - originalClickPositionWorld;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.S))
        {
            // If the user was previously moving on a different axis we shouldn't accidentally reset their previous motion data
            CheckForPreviousDragClick();
            axisLockZ = true;
            axisLockX = false;
            axisLockY = false;
            axisLockDepth = false;
            axisLockYaw = false;
            axisLockPitch = false;
            ProbeManager.SetAxisVisibility(false, false, true, false);
        }
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D))
        {
            CheckForPreviousDragClick();
            axisLockZ = false;
            axisLockX = true;
            axisLockY = false;
            axisLockDepth = false;
            axisLockYaw = false;
            axisLockPitch = false;
            ProbeManager.SetAxisVisibility(true, false, false, false);
        }
        if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.X))
        {
            CheckForPreviousDragClick();
            axisLockZ = false;
            axisLockX = false;
            axisLockY = false;
            axisLockDepth = true;
            axisLockYaw = false;
            axisLockPitch = false;
            ProbeManager.SetAxisVisibility(false, false, false, true);
        }
        if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.F))
        {
            CheckForPreviousDragClick();
            axisLockZ = false;
            axisLockX = false;
            axisLockY = false;
            axisLockDepth = false;
            axisLockYaw = false;
            axisLockPitch = true;
        }
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E))
        {
            CheckForPreviousDragClick();
            axisLockZ = false;
            axisLockX = false;
            axisLockY = true;
            axisLockDepth = false;
            axisLockYaw = false;
            axisLockPitch = false;
            ProbeManager.SetAxisVisibility(false, true, false, false);
        }
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha3))
        {
            CheckForPreviousDragClick();
            axisLockZ = false;
            axisLockX = false;
            axisLockY = false;
            axisLockDepth = false;
            axisLockYaw = true;
            axisLockPitch = false;
        }


        bool moved = false;

        Vector3 newXYZ = Vector3.zero;

        if (axisLockX)
        {
            newXYZ.x = worldOffset.x;
            moved = true;
        }
        if (axisLockY)
        {
            newXYZ.y = worldOffset.y;
            moved = true;
        }
        if (axisLockZ)
        {
            newXYZ.z = worldOffset.z;
            moved = true;
        }

        if (moved)
        {
            Insertion.apmldv = origAPMLDV + Insertion.World2TransformedAxisChange(newXYZ);
        }

        if (axisLockDepth)
        {
            worldOffset = curScreenPointWorld - lastClickPositionWorld;
            lastClickPositionWorld = curScreenPointWorld;
            depth = -1.5f * worldOffset.y;
            moved = true;
        }

        if (axisLockPitch)
        {
            Insertion.pitch = Mathf.Clamp(origPitch + 3f * worldOffset.y, minPitch, maxPitch);
            moved = true;
        }
        if (axisLockYaw)
        {
            Insertion.yaw = origYaw - 3f * worldOffset.x;
            moved = true;
        }


        if (moved)
        {
            SetProbePosition();

            ProbeManager.SetAxisTransform(ProbeTipT);

            ProbeManager.UIUpdateEvent.Invoke();

            MovedThisFrameEvent.Invoke();
        }

    }

    /// <summary>
    /// Release control of mouse movements after the user releases the mouse button from a probe
    /// </summary>
    public void DragMovementRelease()
    {
        // release probe control
        dragging = false;
        ProbeManager.SetAxisVisibility(false, false, false, false);
        BrainCameraController.BlockBrainControl = false;
        FinishedMovingEvent.Invoke();
    }

    #endregion

    #region Set Probe pos/angles
    
    /// <summary>
    /// Set the probe position to the current apml/depth/angles values
    /// </summary>
    public override void SetProbePosition()
    {
        SetProbePositionHelper();
    }

    public void SetProbePosition(float depthOverride)
    {
        depth = depthOverride;
        SetProbePosition();
    }

    public override void SetProbePosition(Vector3 position)
    {
        Insertion.apmldv = position;
        SetProbePosition();
    }

    public override void SetProbePosition(Vector4 positionDepth)
    {
        Insertion.apmldv = positionDepth;
        depth = positionDepth.w;
        SetProbePosition();
    }

    public override void SetProbeAngles(Vector3 angles)
    {
        Insertion.angles = angles;
        SetProbePosition();
    }

    /// <summary>
    /// Set the position of the probe to match a ProbeInsertion object in CCF coordinates
    /// </summary>
    private void SetProbePositionHelper()
    {
        // Reset everything
        transform.position = _initialPosition;
        transform.rotation = _initialRotation;

        // Manually adjust the coordinates and rotation
        transform.position += Insertion.PositionWorldT();
        transform.RotateAround(_rotateAround.position, transform.up, Insertion.yaw);
        transform.RotateAround(_rotateAround.position, transform.forward, Insertion.pitch);
        transform.RotateAround(_rotateAround.position, _rotateAround.up, Insertion.roll);

        // Compute depth transform, if needed
        if (depth != 0f)
        {
            transform.position += -transform.up * depth;
            Vector3 depthAdjustment = Insertion.World2TransformedAxisChange(-transform.up) * depth;

            Insertion.apmldv += depthAdjustment;
            depth = 0f;
        }

        // update surface position
        ProbeManager.UpdateSurfacePosition();

        // Tell the tpmanager we moved and update the UI elements
        MovedThisFrameEvent.Invoke();
        ProbeManager.UIUpdateEvent.Invoke();
    }

    //public override void SetProbePosition(ProbeInsertion localInsertion)
    //{
    //    // localInsertion gets copied to Insertion
    //    Insertion.apmldv = localInsertion.apmldv;
    //    Insertion.angles = localInsertion.angles;
    //}

    #endregion

    #region Getters

    /// <summary>
    /// Return the tip coordinates in **un-transformed** world coordinates
    /// </summary>
    /// <returns></returns>
    public override (Vector3 tipCoordWorldU, Vector3 tipUpWorldU, Vector3 tipForwardWorldU) GetTipWorldU()
    {
        Vector3 tipCoordWorldU = WorldT2WorldU(_probeTipT.position);
        Vector3 tipUpWorldU = (WorldT2WorldU(_probeTipT.position + _probeTipT.up) - tipCoordWorldU).normalized;
        Vector3 tipForwardWorldU = (WorldT2WorldU(_probeTipT.position + _probeTipT.forward) - tipCoordWorldU).normalized;

        return (tipCoordWorldU, tipUpWorldU, tipForwardWorldU);
    }

    /// <summary>
    /// Convert a transformed world coordinate into an un-transformed coordinate
    /// </summary>
    /// <param name="coordWorldT"></param>
    /// <returns></returns>
    private Vector3 WorldT2WorldU(Vector3 coordWorldT)
    {
        return Insertion.CoordinateSpace.Space2World(Insertion.CoordinateTransform.Transform2Space(Insertion.CoordinateTransform.Space2TransformAxisChange(Insertion.CoordinateSpace.World2Space(coordWorldT))));
    }


    #endregion

}
