using BrainAtlas;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CartesianProbeController : ProbeController
{
    #region Movement constants
    private const float MOVE_INCREMENT_TAP = 0.010f; // move 1 um per tap
    private const float MOVE_INCREMENT_TAP_ULTRA = 1.000f;
    private const float MOVE_INCREMENT_TAP_FAST = 0.100f;
    private const float MOVE_INCREMENT_TAP_SLOW = 0.001f;
    private const float MOVE_INCREMENT_HOLD = 0.100f; // move 50 um per second when holding
    private const float MOVE_INCREMENT_HOLD_ULTRA = 10.000f;
    private const float MOVE_INCREMENT_HOLD_FAST = 1.000f;
    private const float MOVE_INCREMENT_HOLD_SLOW = 0.010f;
    private const float ROT_INCREMENT_TAP = 1f;
    private const float ROT_INCREMENT_TAP_ULTRA = 15f;
    private const float ROT_INCREMENT_TAP_FAST = 5f;
    private const float ROT_INCREMENT_TAP_SLOW = 0.1f;
    private const float ROT_INCREMENT_HOLD = 1f;
    private const float ROT_INCREMENT_HOLD_ULTRA = 15f;
    private const float ROT_INCREMENT_HOLD_FAST = 5f;
    private const float ROT_INCREMENT_HOLD_SLOW = 0.1f;

    // [deprecated]
    //private readonly Vector4 _forwardDir = new(0f, 0f, -1f, 0f);
    //private readonly Vector4 _rightDir = new(-1f, 0f, 0f, 0f);
    //private readonly Vector4 _upDir = new(0f, 1f, 0f, 0f);
    private readonly Vector4 _depthDir = new(0f, 0f, 0f, 1f);

    private readonly Vector3 _yawDir = new(1f, 0f, 0f);
    private readonly Vector3 _pitchDir = new(0f, -1f, 0f);
    private readonly Vector3 _rollDir = new(0f, 0f, 1f);


    private Vector4 ForwardVecWorld { get => Settings.ConvertAPML2Probe ? ProbeTipT.up : Vector3.forward; }
    private Vector4 RightVecWorld { get => Settings.ConvertAPML2Probe ? ProbeTipT.right : Vector3.right; }
    private Vector4 UpVecWorld { get => Vector3.up; }
    private Vector4 DepthVecWorld { get => _depthDir; }

    private Vector4 _unlockedDir;
    public override Vector4 UnlockedDir {
        get => _unlockedDir;
        set
        {
            _unlockedDir = value;
            // If we are attached to the active probe manager, request a downstream UI update
            if (ProbeManager.ActiveProbeManager == ProbeManager)
                ProbeManager.ActiveProbeUIUpdateEvent.Invoke();
        }
    }

    private Vector3 _unlockedRot;
    public override Vector3 UnlockedRot
    {
        get => _unlockedRot;
        set
        {
            _unlockedRot = value;
            // If we are attached to the active probe mnager, request a downstream UI update
            if (ProbeManager.ActiveProbeManager == ProbeManager)
                ProbeManager.ActiveProbeUIUpdateEvent.Invoke();
        }
    }

    private bool _fullLock;
    public override bool Locked
    {
        get
        {
            return _fullLock;
        }
    }

    // angle limits
    private const float _minPitch = 0f;
    private const float _maxPitch = 90f;

    // defaults
    private readonly Vector3 _defaultStart = Vector3.zero;
    private const float _defaultDepth = 0f;
    private readonly Vector2 _defaultAngles = new Vector2(0f, 90f); // 0 yaw is forward, default pitch is 90f (downward)
    #endregion

    #region Key hold flags
    private int clickKeyHeld = 0;
    private int rotateKeyHeld = 0;
    private Vector4 clickHeldVector;
    private Vector3 rotateHeldVector;

    private float clickKeyPressTime;
    private float rotateKeyPressTime;
    private float keyHoldDelay = 0.35f;
    #endregion

    #region Private vars
    private Vector3 _initialPosition;
    private Quaternion _initialRotation;
    private float _depth;
    private Dictionary<int, bool> _clickDict;

    private bool _dirty;

    private ControlMode _controlMode;

    private enum ControlMode
    {
        APMLDV = 0,
        ForwardRightDown = 1
    }

    // Input system
    private ProbeControlInputActions inputActions;
    #endregion

    // References
    [SerializeField] private Transform _probeTipT;

    #region Public properties
    public override Transform ProbeTipT { get { return _probeTipT; } }
    public override string XAxisStr
    {
        get
        {
            return _controlMode == ControlMode.APMLDV ? "AP" : "Forward";
        }
    }
    public override string YAxisStr
    {
        get
        {
            return _controlMode == ControlMode.APMLDV ? "ML" : "Right";
        }
    }
    public override string ZAxisStr
    {
        get
        {
            return _controlMode == ControlMode.APMLDV ? "DV" : "Down";
        }
    }
    #endregion

    #region Unity
    private void Awake()
    {
        _depth = _defaultDepth;

        _initialPosition = transform.position;
        _initialRotation = transform.rotation;

        // Unlock axes
        UnlockedDir = Vector4.one;
        UnlockedRot = Vector3.one;
        _fullLock = false;

        // Input actions
        inputActions = new();
        var probeControlClick = inputActions.ProbeControl;
        probeControlClick.Enable();

        // There's something really broken about analog stick "pressing", where the performed event can be fired multiple times
        // by pulling the stick back and forward again without going all the way to zero (and triggering "canceled")
        // 
        // Until we can find a fix for this or figure out if this is a bug, we'll fix it here by ensuring that the "Click" call
        // for each action type can only ever get called once
        _clickDict = new();
        for (int i = 0; i <= 13; i++)
            _clickDict.Add(i, false);

        // Click actions
        probeControlClick.Forward.performed += x => ActionHandler(0, ForwardVecWorld, Click);
        probeControlClick.Forward.canceled += x => CancelHandler(0, ForwardVecWorld, CancelClick);
        probeControlClick.Right.performed += x => ActionHandler(1, RightVecWorld, Click);
        probeControlClick.Right.canceled += x => CancelHandler(1, RightVecWorld, CancelClick);
        probeControlClick.Back.performed += x => ActionHandler(2, -ForwardVecWorld, Click);
        probeControlClick.Back.canceled += x => CancelHandler(2, -ForwardVecWorld, CancelClick);
        probeControlClick.Left.performed += x => ActionHandler(3, -RightVecWorld, Click);
        probeControlClick.Left.canceled += x => CancelHandler(3, -RightVecWorld, CancelClick);

        probeControlClick.Up.performed += x => ActionHandler(4, UpVecWorld, Click);
        probeControlClick.Up.canceled += x => CancelHandler(4, UpVecWorld, CancelClick);
        probeControlClick.Down.performed += x => ActionHandler(5, -UpVecWorld, Click);
        probeControlClick.Down.canceled += x => CancelHandler(5, -UpVecWorld, CancelClick);

        probeControlClick.DepthDown.performed += x => ActionHandler(6, DepthVecWorld, Click);
        probeControlClick.DepthDown.canceled += x => CancelHandler(6, DepthVecWorld, CancelClick);
        probeControlClick.DepthUp.performed += x => ActionHandler(7, -DepthVecWorld, Click);
        probeControlClick.DepthUp.canceled += x => CancelHandler(7, -DepthVecWorld, CancelClick);

        // Rotate actions
        probeControlClick.YawClockwise.performed += x => ActionHandler(8, _yawDir, Rotate);
        probeControlClick.YawClockwise.canceled += x => CancelHandler(8, _yawDir, CancelRotate);
        probeControlClick.YawCounter.performed += x => ActionHandler(9, -_yawDir, Rotate);
        probeControlClick.YawCounter.canceled += x => CancelHandler(9, -_yawDir, CancelRotate);

        probeControlClick.PitchDown.performed += x => ActionHandler(10, _pitchDir, Rotate);
        probeControlClick.PitchDown.canceled += x => CancelHandler(10, _pitchDir, CancelRotate);
        probeControlClick.PitchUp.performed += x => ActionHandler(11, -_pitchDir, Rotate);
        probeControlClick.PitchUp.canceled += x => CancelHandler(11, -_pitchDir, CancelRotate);

        probeControlClick.RollClock.performed += x => ActionHandler(12, _rollDir, Rotate);
        probeControlClick.RollClock.canceled += x => CancelHandler(12, _rollDir, CancelRotate);
        probeControlClick.RollCounter.performed += x => ActionHandler(13, -_rollDir, Rotate);
        probeControlClick.RollCounter.canceled += x => CancelHandler(13, -_rollDir, CancelRotate);

        probeControlClick.InputControl.performed += x => ToggleControllerLock();

        Insertion = new ProbeInsertion(_defaultStart, _defaultAngles, BrainAtlasManager.ActiveReferenceAtlas.AtlasSpace.Name, BrainAtlasManager.ActiveAtlasTransform.Name);
    }

    #region Awake helpers
    private void ActionHandler(int idx, Vector4 dir, Action<Vector4> callback)
    {
        if (!_clickDict[idx])
        {
            _clickDict[idx] = true;
            callback(dir);
        }
    }
    private void ActionHandler(int idx, Vector3 dir, Action<Vector3> callback)
    {
        if (!_clickDict[idx])
        {
            _clickDict[idx] = true;
            callback(dir);
        }
    }

    private void CancelHandler(int idx, Vector4 dir, Action<Vector4> callback)
    {
        if (_clickDict[idx])
        {
            _clickDict[idx] = false;
            callback(dir);
        }
    }
    private void CancelHandler(int idx, Vector3 dir, Action<Vector3> callback)
    {
        if (_clickDict[idx])
        {
            _clickDict[idx] = false;
            callback(dir);
        }
    }
    #endregion

    private void Start()
    {
        SetProbePosition();
    }

    private void Update()
    {
        // If the user is holding one or more click keys and we are past the hold delay, increment the position
        if (clickKeyHeld > 0 && (Time.realtimeSinceStartup - clickKeyPressTime) > keyHoldDelay)
            // Set speed to Tap instead of Hold for manipulator keyboard control
            MoveProbe_XYZD(clickHeldVector,
                ManipulatorManualControl ? ComputeMoveSpeed_Tap() : ComputeMoveSpeed_Hold());

        // If the user is holding one or more rotate keys and we are past the hold delay, increment the angles
        if (rotateKeyHeld > 0 && (Time.realtimeSinceStartup - rotateKeyPressTime) > keyHoldDelay)
            MoveProbe_YPR(rotateHeldVector, ComputeRotSpeed_Hold());
    }

    private void LateUpdate()
    {
        if (_dirty)
        {
            _dirty = false;
            SetProbePosition();
        }
    }

    private void OnEnable()
    {
        inputActions.ProbeControl.Enable();
    }

    private void OnDisable()
    {
        inputActions.ProbeControl.Disable();
    }

    #endregion

    #region Overrides

    public override void ToggleControllerLock()
    {
        _fullLock = !_fullLock;

        if (_fullLock)
        {
            UnlockedDir = Vector4.zero;
            UnlockedRot = Vector3.zero;
        }
        else
        {
            UnlockedDir = Vector4.one;
            UnlockedRot = Vector3.one;
        }
    }

    public override void SetControllerLock(bool locked)
    {
        _fullLock = locked;
        
        if (_fullLock)
        {
            UnlockedDir = Vector4.zero;
            UnlockedRot = Vector3.zero;
        }
        else
        {
            UnlockedDir = Vector4.one;
            UnlockedRot = Vector3.one;
        }
        
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
        Insertion.APMLDV = _defaultStart;
    }

    public override void ResetAngles()
    {
        Insertion.Angles = _defaultAngles;
    }

    #endregion

    #region Input System

    private float ComputeMoveSpeed_Tap()
    {
        switch (Settings.ProbeSpeed)
        {
            case 0:
                return MOVE_INCREMENT_TAP_SLOW;
            case 1:
                return MOVE_INCREMENT_TAP;
            case 2:
                return MOVE_INCREMENT_TAP_FAST;
            case 3:
                return MOVE_INCREMENT_TAP_ULTRA;
            default:
                return 0f;
        }
    }

    private float ComputeMoveSpeed_Hold()
    {
        switch (Settings.ProbeSpeed)
        {
            case 0:
                return MOVE_INCREMENT_HOLD_SLOW * Time.deltaTime;
            case 1:
                return MOVE_INCREMENT_HOLD * Time.deltaTime;
            case 2:
                return MOVE_INCREMENT_HOLD_FAST * Time.deltaTime;
            case 3:
                return MOVE_INCREMENT_HOLD_ULTRA * Time.deltaTime;
            default:
                return 0f;
        }
    }

    private float ComputeRotSpeed_Tap()
    {
        switch (Settings.ProbeSpeed)
        {
            case 0:
                return ROT_INCREMENT_TAP_SLOW;
            case 1:
                return ROT_INCREMENT_TAP;
            case 2:
                return ROT_INCREMENT_TAP_FAST;
            case 3:
                return ROT_INCREMENT_TAP_ULTRA;
            default:
                return 0f;
        }
    }

    private float ComputeRotSpeed_Hold()
    {
        switch (Settings.ProbeSpeed)
        {
            case 0:
                return ROT_INCREMENT_HOLD_SLOW * Time.deltaTime;
            case 1:
                return ROT_INCREMENT_HOLD * Time.deltaTime;
            case 2:
                return ROT_INCREMENT_HOLD_FAST * Time.deltaTime;
            case 3:
                return ROT_INCREMENT_HOLD_ULTRA * Time.deltaTime;
            default:
                return 0f;
        }
    }

    /// <summary>
    /// Move the probe along a Unity world space 
    /// </summary>
    /// <param name="dir"></param>
    public void Click(Vector4 dir)
    {
        if (dragging || UIManager.InputsFocused) return;

        MoveProbe_XYZD(dir, ComputeMoveSpeed_Tap());
        clickHeldVector += dir;

        // If this is the first key being held, reset the hold timer
        if (clickKeyHeld == 0)
            clickKeyPressTime = Time.realtimeSinceStartup;

        clickKeyHeld++;
    }

    public void CancelClick(Vector4 dir)
    {
        if (dragging) return;

        clickKeyHeld--;
        clickHeldVector -= dir;
    }

    public void Rotate(Vector3 ang)
    {
        if (dragging || UIManager.InputsFocused) return;

        MoveProbe_YPR(ang, ComputeRotSpeed_Tap());

        rotateHeldVector += ang;

        // If this is the first key being held, reset the hold timer
        if (rotateKeyHeld == 0)
            rotateKeyPressTime = Time.realtimeSinceStartup;

        rotateKeyHeld++;
    }

    public void CancelRotate(Vector3 ang)
    {
        if (dragging) return;

        rotateKeyHeld--;
        rotateHeldVector -= ang;
    }

    private void ClearClickRotate()
    {
        clickKeyHeld = 0;
        clickHeldVector = Vector4.zero;

        rotateKeyHeld = 0;
        rotateHeldVector = Vector3.zero;
    }

    /// <summary>
    /// Shift the ProbeInsertion position by the Unity World vector in direction, multiplied by the speed
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="speed"></param>
    private async void MoveProbe_XYZD(Vector4 direction, float speed)
    {
        // Get the positional delta.
        var posDelta = Vector4.Scale(direction * speed,UnlockedDir);

        if (ManipulatorManualControl)
        {
            // Cancel if a movement is in progress.
            if (ManipulatorKeyboardMoveInProgress) return;
            
            // Disable/ignore more input until movement is done.
            ManipulatorKeyboardMoveInProgress = true;

            // Call movement and re-enable input when done.
            if (!await ProbeManager.ManipulatorBehaviorController.MoveByWorldSpaceDelta(posDelta))
                return;
            
            // Re-enable input.
            ManipulatorKeyboardMoveInProgress = false;
        }
        else
        {
            // Rotate the position delta (unity world space) into the insertion's transformed space
            // Note that we don't apply the transform beacuse we want 1um steps to = 1um steps in transformed space
            Insertion.APMLDV += Insertion.World2T_Vector(posDelta);
            _depth += posDelta.w;

            // Set probe position and update UI
            _dirty = true;
        }
    }

    /// <summary>
    /// Add the angles 
    /// </summary>
    /// <param name="angle">(yaw, pitch, roll)</param>
    /// <param name="speed"></param>
    private void MoveProbe_YPR(Vector3 angle, float speed)
    {
        var angleDelta = Vector3.Scale(angle * speed, UnlockedRot);

        Insertion.Yaw += angleDelta.x;
        Insertion.Pitch = Mathf.Clamp(Insertion.Pitch + angleDelta.y, _minPitch, _maxPitch);
        Insertion.Roll += angleDelta.z;

        // Set probe position and update UI
        _dirty = true;
    }

    #endregion

    #region Movement Controls

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
        if (EventSystem.current.IsPointerOverGameObject() || ProbeManager.IsEphysLinkControlled || UnlockedDir != Vector4.one)
            return;

        // Clear all keyboard movements
        ClearClickRotate();

        BrainCameraController.BlockBrainControl = true;

        axisLockZ = false;
        axisLockY = false;
        axisLockX = false;
        axisLockDepth = false;
        axisLockPitch = false;
        axisLockYaw = false;

        origAPMLDV = Insertion.APMLDV;
        origYaw = Insertion.Yaw;
        origPitch = Insertion.Pitch;
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
        if (ProbeManager.IsEphysLinkControlled || UnlockedDir != Vector4.one)
            return;

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
            Insertion.APMLDV = origAPMLDV + Insertion.World2T_Vector(newXYZ);
        }

        if (axisLockDepth)
        {
            worldOffset = curScreenPointWorld - lastClickPositionWorld;
            lastClickPositionWorld = curScreenPointWorld;
            _depth = -1.5f * worldOffset.y;
            moved = true;
        }

        if (axisLockPitch)
        {
            Insertion.Pitch = Mathf.Clamp(origPitch + 3f * worldOffset.y, _minPitch, _maxPitch);
            moved = true;
        }
        if (axisLockYaw)
        {
            Insertion.Yaw = origYaw - 3f * worldOffset.x;
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
        // Reset everything
        transform.position = _initialPosition;
        transform.rotation = _initialRotation;

        // Manually adjust the coordinates and rotation
        transform.position = Insertion.PositionWorldT();
        transform.RotateAround(_probeTipT.position, transform.up, Insertion.Yaw);
        transform.RotateAround(_probeTipT.position, transform.right, Insertion.Pitch);
        // Subtract roll to go clockwise
        transform.RotateAround(_probeTipT.position, _probeTipT.forward, -Insertion.Roll);

        // Compute depth transform, if needed
        if (_depth != 0f)
        {
            transform.position += transform.forward * _depth;
            Vector3 depthAdjustment = Insertion.World2T_Vector(transform.forward) * _depth;

            Insertion.APMLDV += depthAdjustment;
            _depth = 0f;
        }

        SetTipWorldU();

        // update surface position
        ProbeManager.UpdateSurfacePosition();

        // Tell the tpmanager we moved and update the UI elements
        MovedThisFrameEvent.Invoke();
        ProbeManager.UIUpdateEvent.Invoke();
    }

    public override void SetProbePosition(Vector3 position)
    {
        Insertion.APMLDV = position;
        SetProbePosition();
    }

    public override void SetProbePosition(Vector4 positionDepth)
    {
        Insertion.APMLDV = positionDepth;
        _depth = positionDepth.w;
        SetProbePosition();
    }

    public override void SetProbeAngles(Vector3 angles)
    {
        Insertion.Angles = angles;
        Insertion.Pitch = Mathf.Clamp(Insertion.Pitch, _minPitch, _maxPitch);
        SetProbePosition();
    }

    #endregion

    #region Getters

    private Vector3 _tipCoordWorldU;
    private Vector3 _tipRightWorldU;
    private Vector3 _tipUpWorldU;
    private Vector3 _tipForwardWorldU;

    private void SetTipWorldU()
    {        
        // Note: we need to use the reference coordinates here so that the world positions resolve to (0,0,0) at Bregma,
        // otherwise any rotations that get applied will be incorrect
        _tipCoordWorldU = BrainAtlasManager.WorldT2WorldU(_probeTipT.position, true);
        _tipRightWorldU = (BrainAtlasManager.WorldT2WorldU(_probeTipT.position + _probeTipT.right, true) - _tipCoordWorldU).normalized;
        _tipUpWorldU = (BrainAtlasManager.WorldT2WorldU(_probeTipT.position + _probeTipT.up, true) - _tipCoordWorldU).normalized;
        _tipForwardWorldU = (BrainAtlasManager.WorldT2WorldU(_probeTipT.position + _probeTipT.forward, true) - _tipCoordWorldU).normalized;
    }

    /// <summary>
    /// Return the tip coordinates and vectors in **un-transformed** world coordinates
    /// </summary>
    /// <returns></returns>
    public override (Vector3 tipCoordWorldU, Vector3 tipRightWorldU, Vector3 tipUpWorldU, Vector3 tipForwardWorldU) GetTipWorldU()
    {

        return (_tipCoordWorldU, _tipRightWorldU, _tipUpWorldU, _tipForwardWorldU);
    }
    #endregion

}
