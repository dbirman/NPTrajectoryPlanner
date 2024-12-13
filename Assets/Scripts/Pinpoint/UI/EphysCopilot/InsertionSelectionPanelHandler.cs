using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrainAtlas;
using EphysLink;
using Pinpoint.Probes;
using Pinpoint.Probes.ManipulatorBehaviorController;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Pinpoint.UI.EphysCopilot
{
    public class InsertionSelectionPanelHandler : MonoBehaviour
    {
        #region Constants

        private const float LINE_WIDTH = 0.1f;
        private const int NUM_SEGMENTS = 2;
        public static readonly Vector3 PRE_DEPTH_DRIVE_DV_OFFSET = new(0, 3.5f, 0);
        private const string MOVE_TO_TARGET_INSERTION_STR = "Move to Target Insertion";
        private const string STOP_MOVEMENT_STR = "Stop Movement";

        #endregion

        #region Components

        [SerializeField]
        private Color _apColor;

        [SerializeField]
        private Color _mlColor;

        [SerializeField]
        private Color _dvColor;

        [SerializeField]
        private TMP_Text _manipulatorIDText;

        [SerializeField]
        private TMP_Dropdown _targetInsertionDropdown;

        [SerializeField]
        private Button _moveButton;

        [SerializeField]
        private TMP_Text _moveButtonText;

        public ProbeManager ProbeManager { private get; set; }

        private (GameObject ap, GameObject ml, GameObject dv) _lineGameObjects;
        private (LineRenderer ap, LineRenderer ml, LineRenderer dv) _lineRenderers;

        #endregion

        #region Properties

        private bool _isMoving;

        private bool _acknowledgedOutOfBounds;

        /// <summary>
        ///     Filter for probe managers this manipulator can target.
        ///     1. Not already selected
        ///     2. Angles are coterminal
        /// </summary>
        private IEnumerable<ProbeManager> _targetProbeManagerOptions =>
            TargetableProbeManagers.Where(manager =>
                !ManipulatorIDToSelectedTargetProbeManager
                    .Where(pair =>
                        pair.Key != ProbeManager.ManipulatorBehaviorController.ManipulatorID
                    )
                    .Select(pair => pair.Value)
                    .Contains(manager)
                && IsCoterminal(
                    manager.ProbeController.Insertion.Angles,
                    ProbeManager.ProbeController.Insertion.Angles
                )
            );

        private (ProbeInsertion ap, ProbeInsertion ml, ProbeInsertion dv) _movementAxesInsertions;

        #region Shared

        /// <summary>
        ///     Filter for probe managers that are targetable.
        ///     1. Are not ephys link controlled
        ///     2. Are inside the brain (not NaN)
        /// </summary>
        public static IEnumerable<ProbeManager> TargetableProbeManagers =>
            ProbeManager
                .Instances.Where(manager => !manager.IsEphysLinkControlled)
                .Where(manager =>
                    !float.IsNaN(
                        manager
                            .FindEntryIdxCoordinate(
                                BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(
                                    manager.ProbeController.Insertion.PositionWorldU()
                                ),
                                BrainAtlasManager.ActiveReferenceAtlas.World2Atlas_Vector(
                                    manager.ProbeController.GetTipWorldU().tipUpWorldU
                                )
                            )
                            .x
                    )
                );

        public static readonly Dictionary<
            string,
            ProbeManager
        > ManipulatorIDToSelectedTargetProbeManager = new();

        private static readonly UnityEvent _shouldUpdateTargetInsertionOptionsEvent = new();

        #endregion

        #endregion

        #region Unity

        private void Start()
        {
            // Update manipulator ID text
            _manipulatorIDText.text =
                "Manipulator " + ProbeManager.ManipulatorBehaviorController.ManipulatorID;
            _manipulatorIDText.color = ProbeManager.Color;

            // Attach to dropdown events
            _shouldUpdateTargetInsertionOptionsEvent.AddListener(UpdateTargetInsertionOptions);
            UpdateTargetInsertionOptions();

            // Create line renderer
            InitializeLineRenderers();

            // Add listener to probe movements and update trajectory
            ProbeManager.ProbeController.MovedThisFrameEvent.AddListener(ComputeMovementInsertions);
        }

        /// <summary>
        ///     Cleanup line renderers on destroy
        /// </summary>
        private void OnDestroy()
        {
            Destroy(_lineGameObjects.ap);
            Destroy(_lineGameObjects.ml);
            Destroy(_lineGameObjects.dv);
        }

        #endregion

        #region UI Functions

        /// <summary>
        ///     Update record of selected target insertion for this panel.
        ///     Triggers all other panels to update their target insertion options.
        /// </summary>
        /// <param name="value">Selected index</param>
        public void OnTargetInsertionDropdownValueChanged(int value)
        {
            // Update selection record and text fields
            if (value == 0)
            {
                // Remove record if no insertion selected
                ManipulatorIDToSelectedTargetProbeManager.Remove(
                    ProbeManager.ManipulatorBehaviorController.ManipulatorID
                );

                // Hide line
                _lineGameObjects.ap.SetActive(false);
                _lineGameObjects.ml.SetActive(false);
                _lineGameObjects.dv.SetActive(false);

                // Reset dropdown color.
                var colorBlockCopy = _targetInsertionDropdown.colors;
                colorBlockCopy.normalColor = new Color(1, 1, 1);
                colorBlockCopy.selectedColor = new Color(0.8f, 0.8f, 0.8f);
                colorBlockCopy.highlightedColor = colorBlockCopy.selectedColor;
                _targetInsertionDropdown.colors = colorBlockCopy;
            }
            else
            {
                // Extract insertion name from dropdown text (looks for 'A' in ": AP" and bumps index back to ':')
                var probeNameString = _targetInsertionDropdown.options[value].text[
                    .._targetInsertionDropdown
                        .options[value]
                        .text.LastIndexOf(": A", StringComparison.Ordinal)
                ];

                // Get selection as probe manager
                var probeManager = TargetableProbeManagers.First(manager =>
                    manager.name.Equals(probeNameString)
                    || (manager.OverrideName?.Equals(probeNameString) ?? false)
                );

                // Update record if insertion selected
                ManipulatorIDToSelectedTargetProbeManager[
                    ProbeManager.ManipulatorBehaviorController.ManipulatorID
                ] = probeManager;

                // Show lines
                _lineGameObjects.ap.SetActive(true);
                _lineGameObjects.ml.SetActive(true);
                _lineGameObjects.dv.SetActive(true);

                // Compute movement insertions
                ComputeMovementInsertions();
            }

            // Update dropdown options
            _shouldUpdateTargetInsertionOptionsEvent.Invoke();
            UpdateMoveButtonInteractable();
        }

        public void MoveOrStopProbeToInsertionTarget()
        {
            if (_isMoving)
            // Movement in progress -> should stop movement
            {
                // Log that movement is stopping.
                OutputLog.Log(
                    new[]
                    {
                        "Copilot",
                        DateTime.Now.ToString(CultureInfo.InvariantCulture),
                        "MoveToTargetInsertion",
                        ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                        "Stop"
                    }
                );

                // Stop movement.
                CommunicationManager.Instance.Stop(
                    ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                    () =>
                    {
                        _isMoving = false;
                        _moveButtonText.text = MOVE_TO_TARGET_INSERTION_STR;
                    },
                    Debug.LogError
                );
            }
            else
            {
                // Log that movement is starting.
                OutputLog.Log(
                    new[]
                    {
                        "Copilot",
                        DateTime.Now.ToString(CultureInfo.InvariantCulture),
                        "MoveToTargetInsertion",
                        ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                        "Start"
                    }
                );

                // Make movement.
                MoveToTargetInsertion();
                _moveButtonText.text = STOP_MOVEMENT_STR;
            }
        }

        #endregion

        #region Internal Functions

        private void InitializeLineRenderers()
        {
            // Create hosting game objects
            _lineGameObjects = (
                new GameObject("APLine") { layer = 5 },
                new GameObject("MLLine") { layer = 5 },
                new GameObject("DVLine") { layer = 5 }
            );

            // Default them to hidden
            _lineGameObjects.ap.SetActive(false);
            _lineGameObjects.ml.SetActive(false);
            _lineGameObjects.dv.SetActive(false);

            // Create line renderer components
            _lineRenderers = (
                _lineGameObjects.ap.AddComponent<LineRenderer>(),
                _lineGameObjects.ml.AddComponent<LineRenderer>(),
                _lineGameObjects.dv.AddComponent<LineRenderer>()
            );

            // Set materials
            var defaultSpriteShader = Shader.Find("Sprites/Default");
            _lineRenderers.ap.material = new Material(defaultSpriteShader) { color = _apColor };
            _lineRenderers.ml.material = new Material(defaultSpriteShader) { color = _mlColor };
            _lineRenderers.dv.material = new Material(defaultSpriteShader) { color = _dvColor };

            // Set line width
            _lineRenderers.ap.startWidth = _lineRenderers.ap.endWidth = LINE_WIDTH;
            _lineRenderers.ml.startWidth = _lineRenderers.ml.endWidth = LINE_WIDTH;
            _lineRenderers.dv.startWidth = _lineRenderers.dv.endWidth = LINE_WIDTH;

            // Set Segment count
            _lineRenderers.ap.positionCount =
                _lineRenderers.ml.positionCount =
                _lineRenderers.dv.positionCount =
                    NUM_SEGMENTS;
        }

        /// <summary>
        ///     Update the target insertion dropdown options.
        ///     Try to maintain/restore previous selection
        /// </summary>
        public void UpdateTargetInsertionOptions()
        {
            // Clear options
            _targetInsertionDropdown.ClearOptions();

            // Add default option
            _targetInsertionDropdown.options.Add(
                new TMP_Dropdown.OptionData("Select a target insertion...")
            );

            // Add other options
            _targetInsertionDropdown.AddOptions(
                _targetProbeManagerOptions
                    .Select(probeManager =>
                        (probeManager.OverrideName ?? probeManager.name)
                        + ": "
                        + SurfaceCoordinateToString(probeManager.GetSurfaceCoordinateT())
                    )
                    .ToList()
            );

            // Return early if no manipulator ID
            if (ProbeManager.ManipulatorBehaviorController.ManipulatorID == null)
                return;

            // Restore selection (if possible)
            var selectedProbeManager = ManipulatorIDToSelectedTargetProbeManager.GetValueOrDefault(
                ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                null
            );
            if (selectedProbeManager == null)
                // Select none if no previous selection.
                _targetInsertionDropdown.SetValueWithoutNotify(0);
            else
                _targetInsertionDropdown.SetValueWithoutNotify(
                    _targetProbeManagerOptions.ToList().IndexOf(selectedProbeManager) + 1
                );

            // Color dropdown to match probe color
            if (!selectedProbeManager)
                return;
            var colorBlockCopy = _targetInsertionDropdown.colors;
            colorBlockCopy.normalColor = selectedProbeManager.Color;
            colorBlockCopy.selectedColor = new Color(
                colorBlockCopy.normalColor.r * 0.9f,
                colorBlockCopy.normalColor.g * 0.9f,
                colorBlockCopy.normalColor.b * 0.9f
            );
            colorBlockCopy.highlightedColor = colorBlockCopy.selectedColor;
            _targetInsertionDropdown.colors = colorBlockCopy;
        }

        private static string SurfaceCoordinateToString(
            (Vector3 surfaceCoordinateT, float depthT) surfaceCoordinate
        )
        {
            var apMicrometers = Math.Truncate(surfaceCoordinate.surfaceCoordinateT.x * 1000);
            var mlMicrometers = Math.Truncate(surfaceCoordinate.surfaceCoordinateT.y * 1000);
            var dvMicrometers = Math.Truncate(surfaceCoordinate.surfaceCoordinateT.z * 1000);
            var depthMicrometers = Math.Truncate(surfaceCoordinate.depthT * 1000);
            return "AP: "
                + (Settings.DisplayUM ? apMicrometers : apMicrometers / 1000f)
                + " ML: "
                + (Settings.DisplayUM ? mlMicrometers : mlMicrometers / 1000f)
                + " DV: "
                + (Settings.DisplayUM ? dvMicrometers : dvMicrometers / 1000f)
                + " Depth: "
                + (Settings.DisplayUM ? depthMicrometers : depthMicrometers / 1000f);
        }

        private void ComputeMovementInsertions()
        {
            // Shortcut exit if lines are not drawn (and therefore no path is being planned)
            if (!_lineGameObjects.ap.activeSelf)
                return;

            // Update insertion selection listings
            UpdateTargetInsertionOptions();

            // Abort insertion if it is invalid
            if (
                !TargetableProbeManagers.Contains(
                    ManipulatorIDToSelectedTargetProbeManager[
                        ProbeManager.ManipulatorBehaviorController.ManipulatorID
                    ]
                )
            )
            {
                // Remove record (deselected)
                ManipulatorIDToSelectedTargetProbeManager.Remove(
                    ProbeManager.ManipulatorBehaviorController.ManipulatorID
                );

                // Hide line
                _lineGameObjects.ap.SetActive(false);
                _lineGameObjects.ml.SetActive(false);
                _lineGameObjects.dv.SetActive(false);

                // Disable movement button
                _moveButton.interactable = false;

                // Exit
                return;
            }

            // DV axis
            _movementAxesInsertions.dv = new ProbeInsertion(ProbeManager.ProbeController.Insertion)
            {
                DV = ProbeManager
                    .ProbeController.Insertion.World2T_Vector(PRE_DEPTH_DRIVE_DV_OFFSET)
                    .z
            };

            // Recalculate AP and ML based on pre-depth-drive DV
            var brainSurfaceTransformed = ManipulatorIDToSelectedTargetProbeManager[
                ProbeManager.ManipulatorBehaviorController.ManipulatorID
            ]
                .GetSurfaceCoordinateT()
                .surfaceCoordinateT;

            // AP Axis
            _movementAxesInsertions.ap = new ProbeInsertion(_movementAxesInsertions.dv)
            {
                AP = brainSurfaceTransformed.x
            };

            // ML Axis
            _movementAxesInsertions.ml = new ProbeInsertion(_movementAxesInsertions.ap)
            {
                ML = brainSurfaceTransformed.y
            };

            // Check if within bounds
            var manipulatorPosition =
                ProbeManager.ManipulatorBehaviorController.ConvertInsertionAPMLDVToManipulatorPosition(
                    _movementAxesInsertions.ml.APMLDV
                );
            if (
                !_acknowledgedOutOfBounds
                && (
                    manipulatorPosition.x < 0
                    || manipulatorPosition.x
                        > ProbeManager.ManipulatorBehaviorController.CoordinateSpace.Dimensions.x
                    || manipulatorPosition.y < 0
                    || manipulatorPosition.y
                        > ProbeManager.ManipulatorBehaviorController.CoordinateSpace.Dimensions.y
                    || manipulatorPosition.z < 0
                    || manipulatorPosition.z
                        > ProbeManager.ManipulatorBehaviorController.CoordinateSpace.Dimensions.z
                )
            )
            {
                QuestionDialogue.Instance.NewQuestion(
                    "This insertion is outside the bounds of the manipulator. Are you sure you want to continue?"
                );
                QuestionDialogue.Instance.YesCallback = () => _acknowledgedOutOfBounds = true;
                QuestionDialogue.Instance.NoCallback = () => _targetInsertionDropdown.value = 0;
            }

            // Update line renderer
            _lineRenderers.dv.SetPosition(0, ProbeManager.ProbeController.ProbeTipT.position);
            _lineRenderers.dv.SetPosition(1, _movementAxesInsertions.dv.PositionWorldT());

            _lineRenderers.ap.SetPosition(0, _movementAxesInsertions.dv.PositionWorldT());
            _lineRenderers.ap.SetPosition(1, _movementAxesInsertions.ap.PositionWorldT());

            _lineRenderers.ml.SetPosition(0, _movementAxesInsertions.ap.PositionWorldT());
            _lineRenderers.ml.SetPosition(1, _movementAxesInsertions.ml.PositionWorldT());
        }

        /// <summary>
        ///     Move to target insertion and handle callback when all movements are done
        /// </summary>
        private void MoveToTargetInsertion()
        {
            // Check if a target insertion is selected
            if (
                !ManipulatorIDToSelectedTargetProbeManager.ContainsKey(
                    ProbeManager.ManipulatorBehaviorController.ManipulatorID
                )
            )
                return;

            // Setup and compute movement
            _isMoving = true;
            var apPosition =
                ProbeManager.ManipulatorBehaviorController.ConvertInsertionAPMLDVToManipulatorPosition(
                    _movementAxesInsertions.ap.APMLDV
                );
            var mlPosition =
                ProbeManager.ManipulatorBehaviorController.ConvertInsertionAPMLDVToManipulatorPosition(
                    _movementAxesInsertions.ml.APMLDV
                );
            var dvPosition =
                ProbeManager.ManipulatorBehaviorController.ConvertInsertionAPMLDVToManipulatorPosition(
                    _movementAxesInsertions.dv.APMLDV
                );

            // Move
            CommunicationManager.Instance.SetPosition(
                new SetPositionRequest(
                    ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                    dvPosition,
                    ManipulatorBehaviorController.AUTOMATIC_MOVEMENT_SPEED
                ),
                _ =>
                {
                    CommunicationManager.Instance.SetPosition(
                        new SetPositionRequest(
                            ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                            apPosition,
                            ManipulatorBehaviorController.AUTOMATIC_MOVEMENT_SPEED
                        ),
                        _ =>
                        {
                            CommunicationManager.Instance.SetPosition(
                                new SetPositionRequest(
                                    ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                                    mlPosition,
                                    ManipulatorBehaviorController.AUTOMATIC_MOVEMENT_SPEED
                                ),
                                _ =>
                                {
                                    // Hide lines
                                    _lineGameObjects.ap.SetActive(false);
                                    _lineGameObjects.ml.SetActive(false);
                                    _lineGameObjects.dv.SetActive(false);

                                    // Complete movement
                                    EndMovement();
                                    _moveButton.interactable = false;
                                },
                                error =>
                                {
                                    Debug.LogError(error);
                                    EndMovement();
                                }
                            );
                        },
                        error =>
                        {
                            Debug.LogError(error);
                            EndMovement();
                        }
                    );
                },
                error =>
                {
                    Debug.LogError(error);
                    EndMovement();
                }
            );
            return;

            void EndMovement()
            {
                _isMoving = false;
                _moveButtonText.text = MOVE_TO_TARGET_INSERTION_STR;

                // Log movement finish.
                OutputLog.Log(
                    new[]
                    {
                        "Copilot",
                        DateTime.Now.ToString(CultureInfo.InvariantCulture),
                        "MoveToTargetInsertion",
                        ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                        "Finish"
                    }
                );
            }
        }

        private void UpdateMoveButtonInteractable()
        {
            _moveButton.interactable = ManipulatorIDToSelectedTargetProbeManager.ContainsKey(
                ProbeManager.ManipulatorBehaviorController.ManipulatorID
            );
        }

        private static bool IsCoterminal(Vector3 first, Vector3 second)
        {
            return Mathf.Abs(first.x - second.x) % 360 < 0.01f
                && Mathf.Abs(first.y - second.y) % 360 < 0.01f
                && Mathf.Abs(first.z - second.z) % 360 < 0.01f;
        }

        #endregion
    }
}
