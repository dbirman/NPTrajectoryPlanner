using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using EphysLink;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TrajectoryPlanner.UI.EphysCopilot
{
    #region Structures

    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal struct DemoDataJson
    {
        public List<ManipulatorDataJson> data;
    }

    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal struct ManipulatorDataJson
    {
        public List<float> angle;
        public List<float> idle;
        public List<float> insertion;
    }

    /// <summary>
    ///     Angle: Yaw, Pitch, Roll
    ///     Idle: AP, ML, DV
    ///     Insertion: AP, ML, DV, Depth
    /// </summary>
    internal struct ManipulatorData
    {
        public Vector3 Angle;
        public Vector4 IdlePos;
        public Vector4 EntryCoordinatePos;
        public Vector4 DuraPos;
        public float Depth;
    }

    internal enum ManipulatorState
    {
        Idle,
        Calibrated,
        AtEntryCoordinate,
        AtDura,
        Inserted,
        Retracted,
        Traveling
    }

    #endregion


    public class CopilotDemoHandler : MonoBehaviour
    {
        #region Constants

        // Manipulator movement speed when outside in mm/s
        private const float OUTSIDE_MOVEMENT_SPEED = 1f;

        // DV ceiling in um
        private const float DV_CEILING = 3500f;

        // Pause time in milliseconds
        private const long PAUSE_TIME = 1000;

        #endregion

        #region Components

        [SerializeField] private GameObject _startButton;
        [SerializeField] private GameObject _stopButton;

        #endregion

        #region Properties

        private readonly Dictionary<ProbeManager, ManipulatorData> _demoManipulatorToData = new();
        private readonly Dictionary<ProbeManager, ManipulatorState> _manipulatorToStates = new();

        #endregion

        #region Unity

        private void OnEnable()
        {
            // Parse JSON
            var jsonString = File.ReadAllText(Application.streamingAssetsPath + "/copilot_demo.json");
            var data = JsonUtility.FromJson<DemoDataJson>(jsonString);

            // Convert to ManipulatorData and match with manipulator
            foreach (var manipulatorData in data.data)
            {
                var convertedAngle = new Vector3(manipulatorData.angle[0], manipulatorData.angle[1],
                    manipulatorData.angle[2]);

                // Match to manipulator
                var matchingManipulator = ProbeManager.Instances.FirstOrDefault(
                    manager => manager.IsEphysLinkControlled &&
                               IsCoterminal(manager.ProbeController.Insertion.angles, convertedAngle));

                // Skip if there are no matching manipulators
                if (matchingManipulator == null) continue;

                // Convert data
                var convertedData = new ManipulatorData
                {
                    Angle = convertedAngle,
                    IdlePos =
                        matchingManipulator.ManipulatorBehaviorController.ConvertInsertionAPMLDVToManipulatorPosition(
                            new Vector3(manipulatorData.idle[0], manipulatorData.idle[1], manipulatorData.idle[2]) /
                            1000f),
                    EntryCoordinatePos =
                        matchingManipulator.ManipulatorBehaviorController.ConvertInsertionAPMLDVToManipulatorPosition(
                            new Vector3(manipulatorData.insertion[0], manipulatorData.insertion[1], DV_CEILING) /
                            1000f),
                    DuraPos =
                        matchingManipulator.ManipulatorBehaviorController.ConvertInsertionAPMLDVToManipulatorPosition(
                            new Vector3(manipulatorData.insertion[0], manipulatorData.insertion[1],
                                manipulatorData.insertion[2]) / 1000f),
                    Depth = manipulatorData.insertion[3] / 1000f
                };

                _demoManipulatorToData.Add(matchingManipulator, convertedData);

                // Default to traveling state on setup (will be moving to Idle soon)
                _manipulatorToStates.Add(matchingManipulator, ManipulatorState.Traveling);
            }

            // Show start button if there are manipulators to control
            _startButton.SetActive(_demoManipulatorToData.Count > 0);
        }

        private void Update()
        {
            if (_manipulatorToStates.Values.All(state => state == ManipulatorState.Idle))
            {
                print("All manipulators are at idle");

                // Chill for a bit
                SpinTimer();

                // Run Calibration
                Calibrate();
            }
            else if (_manipulatorToStates.Values.All(state => state == ManipulatorState.Calibrated))
            {
                print("All manipulators are calibrated");

                // Chill for a bit
                SpinTimer();

                // Drive to entry coordinate
                GoToEntryCoordinate();
            }
        }

        #endregion

        #region UI Functions

        public void OnStartPressed()
        {
            // Set all manipulators to can write
            var manipulatorIndex = 0;
            SetCanWrite(_demoManipulatorToData.Keys.ToList()[manipulatorIndex]);
            return;

            void SetCanWrite(ProbeManager manipulator)
            {
                CommunicationManager.Instance.SetCanWrite(manipulator.ManipulatorBehaviorController.ManipulatorID, true,
                    100, _ =>
                    {
                        if (++manipulatorIndex < _demoManipulatorToData.Count)
                            SetCanWrite(_demoManipulatorToData.Keys.ToList()[manipulatorIndex]);
                        else
                            ActuallyStart();
                    });
            }

            void ActuallyStart()
            {
                // Swap start and stop buttons
                _startButton.SetActive(false);
                _stopButton.SetActive(true);

                // Move to idle position
                foreach (var manipulatorToData in _demoManipulatorToData)
                {
                    _manipulatorToStates[manipulatorToData.Key] = ManipulatorState.Traveling;
                    CommunicationManager.Instance.GotoPos(
                        manipulatorToData.Key.ManipulatorBehaviorController.ManipulatorID,
                        manipulatorToData.Value.IdlePos, OUTSIDE_MOVEMENT_SPEED,
                        _ => _manipulatorToStates[manipulatorToData.Key] = ManipulatorState.Idle, Debug.LogError);
                }
            }
        }

        public void OnStopPressed()
        {
            CommunicationManager.Instance.Stop(_ => print("Stopped"));

            // Swap start and stop buttons
            _startButton.SetActive(true);
            _stopButton.SetActive(false);
        }

        #endregion

        #region Movement Functions

        private void Calibrate()
        {
            SetAllToTraveling();

            var manipulatorIndex = 0;
            CalibrateManipulator(_demoManipulatorToData.Keys.ToList()[manipulatorIndex]);
            return;

            void CalibrateManipulator(ProbeManager manipulator)
            {
                var manipulatorBehaviorController = manipulator.ManipulatorBehaviorController;

                // Goto bregma
                CommunicationManager.Instance.GotoPos(manipulatorBehaviorController.ManipulatorID,
                    manipulatorBehaviorController.ZeroCoordinateOffset,
                    OUTSIDE_MOVEMENT_SPEED,
                    _ =>
                    {
                        SpinTimer();

                        // Come back to idle
                        CommunicationManager.Instance.GotoPos(manipulatorBehaviorController.ManipulatorID,
                            _demoManipulatorToData[manipulator].IdlePos,
                            OUTSIDE_MOVEMENT_SPEED,
                            _ =>
                            {
                                SpinTimer();

                                // Complete and start next manipulator
                                _manipulatorToStates[manipulator] = ManipulatorState.Calibrated;
                                if (++manipulatorIndex < _demoManipulatorToData.Count)
                                    CalibrateManipulator(_demoManipulatorToData.Keys.ToList()[manipulatorIndex]);
                            }, Debug.LogError);
                    }, Debug.LogError);
            }
        }

        private void GoToEntryCoordinate()
        {
            SetAllToTraveling();

            foreach (var manipulatorData in _demoManipulatorToData)
            {
                _manipulatorToStates[manipulatorData.Key] = ManipulatorState.Traveling;
                CommunicationManager.Instance.GotoPos(manipulatorData.Key.ManipulatorBehaviorController.ManipulatorID,
                    manipulatorData.Value.EntryCoordinatePos, OUTSIDE_MOVEMENT_SPEED,
                    _ => _manipulatorToStates[manipulatorData.Key] = ManipulatorState.AtEntryCoordinate,
                    Debug.LogError);
            }
        }

        #endregion

        #region Helper functions

        /// <summary>
        ///     Determine if two Vector3 angles are coterminal
        /// </summary>
        /// <param name="first">one Vector3 angle</param>
        /// <param name="second">another Vector3 angle</param>
        /// <returns></returns>
        private static bool IsCoterminal(Vector3 first, Vector3 second)
        {
            return Mathf.Abs(first.x - second.x) % 360 < 0.01f && Mathf.Abs(first.y - second.y) % 360 < 0.01f &&
                   Mathf.Abs(first.z - second.z) % 360 < 0.01f;
        }

        /// <summary>
        ///     Basic spin timer
        /// </summary>
        /// <param name="durationMilliseconds">Timer length in milliseconds</param>
        private static void SpinTimer(long durationMilliseconds = PAUSE_TIME)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            while (stopwatch.ElapsedMilliseconds < durationMilliseconds)
            {
                // Spin
            }
        }

        /// <summary>
        ///     Set all manipulator states to Traveling
        /// </summary>
        private void SetAllToTraveling()
        {
            foreach (var manipulatorData in _demoManipulatorToData)
                _manipulatorToStates[manipulatorData.Key] = ManipulatorState.Traveling;
        }

        #endregion
    }
}