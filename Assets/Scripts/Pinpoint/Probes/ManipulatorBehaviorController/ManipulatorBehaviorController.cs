using System;
using System.Globalization;
using System.Linq;
using BrainAtlas;
using BrainAtlas.CoordinateSystems;
using EphysLink;
using Pinpoint.CoordinateSystems;
using UnityEngine;
using UnityEngine.Events;

namespace Pinpoint.Probes.ManipulatorBehaviorController
{
    public partial class ManipulatorBehaviorController : MonoBehaviour
    {
        #region Constants

        // Default movement speed: 0.5 mm/s
        public const float AUTOMATIC_MOVEMENT_SPEED = 0.5f;

        #endregion

        #region Components

        [SerializeField]
        private ProbeManager _probeManager;

        [SerializeField]
        private ProbeController _probeController;

        #endregion

        #region Properties

        public string ManipulatorID { get; private set; }

        public int NumAxes { get; set; }

        public Vector3 Dimensions { get; private set; }

        /// <summary>
        ///     Getter and setter or the zero coordinate offset of the manipulator.
        ///     If passed a NaN value, the previous value is kept.
        /// </summary>
        public Vector4 ReferenceCoordinateOffset
        {
            get => _referenceCoordinateOffset;
            set
            {
                _referenceCoordinateOffset = new Vector4(
                    float.IsNaN(value.x) ? _referenceCoordinateOffset.x : value.x,
                    float.IsNaN(value.y) ? _referenceCoordinateOffset.y : value.y,
                    float.IsNaN(value.z) ? _referenceCoordinateOffset.z : value.z,
                    float.IsNaN(value.w) ? _referenceCoordinateOffset.w : value.w
                );

                ZeroCoordinateOffsetChangedEvent.Invoke(_referenceCoordinateOffset);
            }
        }

        public float BrainSurfaceOffset
        {
            get => _brainSurfaceOffset;
            set
            {
                _brainSurfaceOffset = value;
                BrainSurfaceOffsetChangedEvent.Invoke(_brainSurfaceOffset);
            }
        }

        public CoordinateSpace CoordinateSpace { get; private set; }
        private CoordinateTransform CoordinateTransform { get; set; }

        public bool IsRightHanded
        {
            get => _isRightHanded;
            set
            {
                _isRightHanded = value;
                UpdateSpaceAndTransform();
            }
        }

        public readonly ProbeAutomationStateManager ProbeAutomationStateManager = new();

        // Helper functions to create and destroy a probe
        public Action<ProbeProperties.ProbeType> CreatePathfinderProbe { private get; set; }
        public Action DestroyThisProbe { private get; set; }

        #region Private internal fields

        private Vector4 _lastLoggedManipulatorPosition = Vector4.zero;
        private Vector4 _referenceCoordinateOffset = Vector4.zero;
        private float _brainSurfaceOffset;
        private bool _isRightHanded;
        private float _lastLoggedTime;
        private bool _isSetToInsideBrain;

        #endregion

        #endregion

        #region Events

        public UnityEvent<Vector4> ZeroCoordinateOffsetChangedEvent;
        public UnityEvent<float> BrainSurfaceOffsetChangedEvent;
        public UnityEvent<bool> IsSetToDropToSurfaceWithDepthChangedEvent;

        #endregion

        #region Unity

        /// <summary>
        ///     Setup this instance.
        /// </summary>
        private void Awake()
        {
            // Start off as disabled
            enabled = false;
        }

        /// <summary>
        ///     Cleanup this instance.
        /// </summary>
        private void OnDisable()
        {
            ManipulatorID = null;
            _referenceCoordinateOffset = Vector4.zero;
            _brainSurfaceOffset = 0;
        }

        #endregion


        #region Public Methods

        /// <summary>
        ///     Initialize the manipulator behavior controller with the given manipulator ID and calibration status.<br />
        ///     Starts to echo the manipulator position and locks the manipulator from manual control.
        /// </summary>
        /// <param name="manipulatorID">ID of the manipulator to represent.</param>
        /// <param name="calibrated">Whether this manipulator has been calibrated.</param>
        public async void Initialize(string manipulatorID, bool calibrated)
        {
            // Get manipulator information
            var manipulatorResponse = await CommunicationManager.Instance.GetManipulators();
            if (CommunicationManager.HasError(manipulatorResponse.Error))
                return;

            // Shortcut exit if we have an invalid manipulator ID
            if (!manipulatorResponse.Manipulators.Contains(manipulatorID))
                return;

            // Set manipulator ID, number of axes, and dimensions
            ManipulatorID = manipulatorID;
            NumAxes = manipulatorResponse.NumAxes;
            Dimensions = manipulatorResponse.Dimensions;

            // Update transform and space
            UpdateSpaceAndTransform();

            // Lock the manipulator from manual control
            _probeController.SetControllerLock(true);

            // Start echoing the manipulator position.
            EchoPosition();
        }

        /// <summary>
        ///     Configure this manipulator's coordinate space and transform based on its handedness, number of axes, and angles.
        /// </summary>
        private void UpdateSpaceAndTransform()
        {
            CoordinateSpace = new ManipulatorSpace(Dimensions);
            CoordinateTransform = NumAxes switch
            {
                4
                    => IsRightHanded
                        ? new FourAxisRightHandedManipulatorTransform(
                            _probeController.Insertion.Yaw
                        )
                        : new FourAxisLeftHandedManipulatorTransform(
                            _probeController.Insertion.Yaw
                        ),
                3
                    => new ThreeAxisLeftHandedTransform(
                        _probeController.Insertion.Yaw,
                        _probeController.Insertion.Pitch
                    ),
                _ => CoordinateTransform
            };
        }

        /// <summary>
        ///     Convert insertion AP, ML, DV coordinates to manipulator translation stage position.
        /// </summary>
        /// <param name="insertionAPMLDV">AP, ML, DV coordinates from an insertion.</param>
        /// <returns>Computed manipulator translation stage positions to match this coordinate.</returns>
        public Vector4 ConvertInsertionAPMLDVToManipulatorPosition(Vector3 insertionAPMLDV)
        {
            // Convert apmldv to world coordinate
            var convertToWorld = BrainAtlasManager.ActiveReferenceAtlas.Atlas2World_Vector(
                BrainAtlasManager.ActiveAtlasTransform.T2U_Vector(insertionAPMLDV)
            );

            // Convert to Manipulator space
            var posInManipulatorSpace = CoordinateSpace.World2Space(convertToWorld);
            Vector4 posInManipulatorTransform = CoordinateTransform.U2T(posInManipulatorSpace);

            // Apply brain surface offset
            posInManipulatorTransform.w -= float.IsNaN(BrainSurfaceOffset) ? 0 : BrainSurfaceOffset;

            // Apply coordinate offsets and return result
            return posInManipulatorTransform + ReferenceCoordinateOffset;
        }

        /// <summary>
        ///     Compute if a given AP, ML, DV coordinate is within the manipulator's reach.
        /// </summary>
        /// <param name="apmldv">Coordinate to check.</param>
        /// <returns>True if the coordinates are within the bounds, false otherwise.</returns>
        public bool IsAPMLDVWithinManipulatorBounds(Vector3 apmldv)
        {
            var manipulatorPosition = ConvertInsertionAPMLDVToManipulatorPosition(apmldv);
            return !(manipulatorPosition.x < 0)
                && !(manipulatorPosition.x > Dimensions.x)
                && !(manipulatorPosition.y < 0)
                && !(manipulatorPosition.y > Dimensions.y)
                && !(manipulatorPosition.z < 0)
                && !(manipulatorPosition.z > Dimensions.z);
        }

        /// <summary>
        ///     Set manipulator space offset from brain surface as Depth from manipulator or probe coordinates.
        /// </summary>
        public void ComputeBrainSurfaceOffset()
        {
            if (_probeManager.IsProbeInBrain())
            {
                // Just calculate the distance from the probe tip position to the brain surface
                BrainSurfaceOffset -= _probeManager.GetSurfaceCoordinateT().depthT;
            }
            else
            {
                // We need to calculate the surface coordinate ourselves
                var (brainSurfaceCoordinateIdx, _) = _probeManager.CalculateEntryCoordinate();

                if (float.IsNaN(brainSurfaceCoordinateIdx.x))
                {
                    Debug.LogError("Could not find brain surface! Canceling set brain offset.");
                    return;
                }

                var brainSurfaceToTransformed = BrainAtlasManager.ActiveAtlasTransform.U2T(
                    BrainAtlasManager.ActiveReferenceAtlas.World2Atlas(
                        BrainAtlasManager.ActiveReferenceAtlas.AtlasIdx2World(
                            brainSurfaceCoordinateIdx
                        )
                    )
                );

                BrainSurfaceOffset += Vector3.Distance(
                    brainSurfaceToTransformed,
                    _probeController.Insertion.APMLDV
                );
            }
        }

        /// <summary>
        ///     Manual adjustment of brain surface offset.
        /// </summary>
        /// <param name="increment">Amount to change the brain surface offset by</param>
        public void IncrementBrainSurfaceOffset(float increment)
        {
            BrainSurfaceOffset += increment;
        }

        /// <summary>
        ///     Move manipulator by a given delta in world space
        /// </summary>
        /// <param name="worldSpaceDelta">Delta (X, Y, Z, D) to move by in world space coordinates</param>
        /// <returns>True on successful movement, false otherwise.</returns>
        public async Awaitable<bool> MoveByWorldSpaceDelta(Vector4 worldSpaceDelta)
        {
            // Convert to manipulator axes (world -> space -> transform).
            var manipulatorSpaceDelta = CoordinateSpace.World2Space_Vector(worldSpaceDelta);
            var manipulatorTransformDelta = CoordinateTransform.U2T(manipulatorSpaceDelta);
            var manipulatorSpaceDepth = worldSpaceDelta.w;

            // Get manipulator position.
            var positionResponse = await CommunicationManager.Instance.GetPosition(ManipulatorID);
            if (CommunicationManager.HasError(positionResponse.Error))
                return false;

            // Apply delta.
            var targetPosition =
                positionResponse.Position
                + new Vector4(
                    manipulatorTransformDelta.x,
                    manipulatorTransformDelta.y,
                    manipulatorTransformDelta.z
                );

            // Move manipulator.
            var setPositionResponse = await CommunicationManager.Instance.SetPosition(
                new SetPositionRequest(ManipulatorID, targetPosition, AUTOMATIC_MOVEMENT_SPEED)
            );
            if (CommunicationManager.HasError(setPositionResponse.Error))
                return false;

            // Process depth movement.
            var targetDepth = positionResponse.Position.w + manipulatorSpaceDepth;

            // Move manipulator.
            var setDepthResponse = await CommunicationManager.Instance.SetDepth(
                new SetDepthRequest(ManipulatorID, targetDepth, AUTOMATIC_MOVEMENT_SPEED)
            );

            return !CommunicationManager.HasError(setDepthResponse.Error);
        }


        #endregion

        #region Private Methods

        /// <summary>
        ///     Echo the manipulator position to the probe controller.
        /// </summary>
        private async void EchoPosition()
        {
            // Continue echoing position while enabled and there exists a probe controller.
            while (enabled && _probeController)
                await UpdateProbePositionFromManipulator();
        }

        /// <summary>
        ///     Update the probe's position based on the manipulator's position.
        /// </summary>
        private async Awaitable UpdateProbePositionFromManipulator()
        {
            // Get manipulator position.
            var positionResponse = await CommunicationManager.Instance.GetPosition(ManipulatorID);

            // Shortcut exit if there was an error.
            if (CommunicationManager.HasError(positionResponse.Error))
                return;

            // Apply zero coordinate offset.
            var zeroCoordinateAdjustedManipulatorPosition =
                positionResponse.Position - ReferenceCoordinateOffset;

            // Convert to coordinate space.
            var manipulatorSpacePosition = CoordinateTransform.T2U(
                zeroCoordinateAdjustedManipulatorPosition
            );

            // Brain surface adjustment.
            var brainSurfaceAdjustment = float.IsNaN(BrainSurfaceOffset) ? 0 : BrainSurfaceOffset;
            // Apply depth adjustment to manipulator position for non-3 axis manipulators.
            if (CoordinateTransform.Prefix != "3lhm")
                zeroCoordinateAdjustedManipulatorPosition.w += brainSurfaceAdjustment;

            // Convert to world space.
            var zeroCoordinateAdjustedWorldPosition = CoordinateSpace.Space2World(
                manipulatorSpacePosition
            );

            // Set probe position (change axes to match probe).
            var transformedApmldv = BrainAtlasManager.World2T_Vector(
                zeroCoordinateAdjustedWorldPosition
            );

            // Set probe position.
            // For 3-axis manipulators, use depth to adjust brain offset if applying offset on depth.
            if (CoordinateTransform.Prefix == "3lhm")
                _probeController.SetProbePosition(
                    new Vector4(
                        transformedApmldv.x,
                        transformedApmldv.y,
                        transformedApmldv.z,
                        brainSurfaceAdjustment
                    )
                );
            else
                _probeController.SetProbePosition(
                    new Vector4(
                        transformedApmldv.x,
                        transformedApmldv.y,
                        transformedApmldv.z,
                        zeroCoordinateAdjustedManipulatorPosition.w
                    )
                );

            // Don't log if the last position is the same.
            var positionDifference = _lastLoggedManipulatorPosition - positionResponse.Position;
            if (
                !(Mathf.Abs(positionDifference.x) > 0.0001)
                && !(Mathf.Abs(positionDifference.y) > 0.0001)
                && !(Mathf.Abs(positionDifference.z) > 0.0001)
                && !(Mathf.Abs(positionDifference.w) > 0.0001)
            )
                return;

            // Log every 4 hz
            if (!(Time.time - _lastLoggedTime >= 0.25))
                return;

            _lastLoggedTime = Time.time;
            var tipPos = _probeController.ProbeTipT.position;

            // ["ephys_link", Real time stamp, Manipulator ID, X, Y, Z, W, Phi, Theta, Spin, TipX, TipY, TipZ]
            OutputLog.Log(
                new[]
                {
                    "ephys_link",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    ManipulatorID,
                    positionResponse.Position.x.ToString(CultureInfo.InvariantCulture),
                    positionResponse.Position.y.ToString(CultureInfo.InvariantCulture),
                    positionResponse.Position.z.ToString(CultureInfo.InvariantCulture),
                    positionResponse.Position.w.ToString(CultureInfo.InvariantCulture),
                    _probeController.Insertion.Yaw.ToString(CultureInfo.InvariantCulture),
                    _probeController.Insertion.Pitch.ToString(CultureInfo.InvariantCulture),
                    _probeController.Insertion.Roll.ToString(CultureInfo.InvariantCulture),
                    tipPos.x.ToString(CultureInfo.InvariantCulture),
                    tipPos.y.ToString(CultureInfo.InvariantCulture),
                    tipPos.z.ToString(CultureInfo.InvariantCulture)
                }
            );

            // Update last logged position
            _lastLoggedManipulatorPosition = positionResponse.Position;
        }

        #endregion
    }
}
