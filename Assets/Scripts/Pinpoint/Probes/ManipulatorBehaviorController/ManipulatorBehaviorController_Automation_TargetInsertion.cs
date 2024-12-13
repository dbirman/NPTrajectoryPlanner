using System;
using System.Globalization;
using BrainAtlas;
using EphysLink;
using UnityEngine;

namespace Pinpoint.Probes.ManipulatorBehaviorController
{
    public partial class ManipulatorBehaviorController
    {
        #region Constants

        // Axes colors.
        private static readonly Color AP_COLOR = new(1, 0.3215686f, 0.3215686f);
        private static readonly Color ML_COLOR = new(0.2039216f, 0.6745098f, 0.8784314f);
        private static readonly Color DV_COLOR = new(1, 0.854902f, 0.4745098f);

        // Trajectory line properties.
        private const float LINE_WIDTH = 0.1f;
        private const int NUM_SEGMENTS = 2;

        // Trajectory values.
        public static readonly Vector3 PRE_DEPTH_DRIVE_DV_OFFSET = new(0, 3.5f, 0);

        /// <summary>
        ///     Distance from the entry coordinate to the Dura. This is considered a safe distance to put the probe.
        /// </summary>
        private const float IDEAL_ENTRY_COORDINATE_TO_DURA_DISTANCE = 3.5f;

        #endregion

        #region Components

        private (GameObject ap, GameObject ml, GameObject dv) _trajectoryLineGameObjects;
        private (LineRenderer ap, LineRenderer ml, LineRenderer dv) _trajectoryLineLineRenderers;

        #endregion

        #region Properties

        /// <summary>
        ///     Trajectory broken into 3 stages (for 3 axes of movement).
        /// </summary>
        /// <remarks>Execution order: DV, AP, ML. Defaults to negative infinity when there is no trajectory.</remarks>
        private (Vector3 first, Vector3 second, Vector3 third) _trajectoryCoordinates = (
            Vector3.negativeInfinity,
            Vector3.negativeInfinity,
            Vector3.negativeInfinity
        );

        /// <summary>
        ///     Record the depth at the entry coordinate.
        /// </summary>
        /// <remarks>Used during insertion to calculate the actual distance needed to retract back to the entry coordinate.</remarks>
        private float _entryCoordinateDepth;

        #endregion

        #region Public Functions

        /// <summary>
        ///     Compute the entry coordinate and trajectory for the target insertion. Also, draw the trajectory lines.
        /// </summary>
        /// <param name="targetInsertionProbeManager">Probe manager of the target insertion</param>
        /// <returns>
        ///     The computed entry coordinate in AP, ML, DV coordinates. Negative infinity if target is unset or already
        ///     there.
        /// </returns>
        public Vector3 ComputeEntryCoordinateTrajectory(ProbeManager targetInsertionProbeManager)
        {
            // If set to null or already past the entry coordinate, cleanup and remove insertion trajectory.
            if (
                targetInsertionProbeManager == null
                || ProbeAutomationStateManager.HasReachedTargetEntryCoordinate()
            )
            {
                RemoveTrajectoryLines();
                return Vector3.negativeInfinity;
            }

            // Compute the trajectory.
            ComputeTargetEntryCoordinateTrajectory(targetInsertionProbeManager);

            // Create trajectory lines.
            CreateTrajectoryLines();

            // Set trajectory lines.
            UpdateTrajectoryLines();

            // Return final entry coordinate (coordinate of the ML movement, the last in the trajectory).
            return _trajectoryCoordinates.third;
        }

        /// <summary>
        ///     Move the probe along the planned trajectory to the target entry coordinate.<br />
        /// </summary>
        /// <exception cref="InvalidOperationException">No trajectory planned for probe</exception>
        /// <remarks>Will log that movement has started and completed.</remarks>
        /// <returns>True if movement was successful, false otherwise.</returns>
        public async Awaitable<bool> DriveToTargetEntryCoordinate()
        {
            // Throw exception if invariant is violated.
            if (float.IsNegativeInfinity(_trajectoryCoordinates.first.x))
                throw new InvalidOperationException(
                    "No trajectory planned for probe " + _probeManager.name
                );

            // Convert insertions to manipulator positions.
            var dvPosition = ConvertInsertionAPMLDVToManipulatorPosition(
                _trajectoryCoordinates.first
            );
            var apPosition = ConvertInsertionAPMLDVToManipulatorPosition(
                _trajectoryCoordinates.second
            );
            var mlPosition = ConvertInsertionAPMLDVToManipulatorPosition(
                _trajectoryCoordinates.third
            );

            // Log that movement is starting.
            OutputLog.Log(
                new[]
                {
                    "Automation",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "DriveToTargetEntryCoordinate",
                    ManipulatorID,
                    "Start"
                }
            );

            // First move.
            var firstMoveResponse = await CommunicationManager.Instance.SetPosition(
                new SetPositionRequest(ManipulatorID, dvPosition, AUTOMATIC_MOVEMENT_SPEED)
            );

            // Shortcut exit if error.
            if (CommunicationManager.HasError(firstMoveResponse.Error))
            {
                LogDriveToTargetEntryCoordinateProgress("Failed to move to DV position.");
                return false;
            }

            // Second move.
            var secondMoveResponse = await CommunicationManager.Instance.SetPosition(
                new SetPositionRequest(ManipulatorID, apPosition, AUTOMATIC_MOVEMENT_SPEED)
            );

            // Shortcut exit if error.
            if (CommunicationManager.HasError(secondMoveResponse.Error))
            {
                LogDriveToTargetEntryCoordinateProgress("Failed to move to AP position.");
                return false;
            }

            // Third move.
            var thirdMoveResponse = await CommunicationManager.Instance.SetPosition(
                new SetPositionRequest(ManipulatorID, mlPosition, AUTOMATIC_MOVEMENT_SPEED)
            );

            // Shortcut exit if error.
            if (CommunicationManager.HasError(thirdMoveResponse.Error))
            {
                LogDriveToTargetEntryCoordinateProgress("Failed to move to ML position.");
                return false;
            }

            // Complete drive.

            // Record depth at entry coordinate.
            var entryCoordinatePositionResponse = await CommunicationManager.Instance.GetPosition(
                ManipulatorID
            );
            if (CommunicationManager.HasError(entryCoordinatePositionResponse.Error))
            {
                LogDriveToTargetEntryCoordinateProgress(
                    "Failed to get final position at entry coordinate."
                );
                return false;
            }

            _entryCoordinateDepth = entryCoordinatePositionResponse.Position.w;

            // Remove trajectory lines.
            RemoveTrajectoryLines();

            // Log drive finished.
            LogDriveToTargetEntryCoordinateProgress("Finish");

            // Return success.
            return true;
        }

        /// <summary>
        ///     Stop the probe from moving to the target entry coordinate.
        /// </summary>
        /// <remarks>Will log that movement has stopped.</remarks>
        /// <returns>True if movement was stopped successfully, false otherwise.</returns>
        public async Awaitable<bool> StopDriveToTargetEntryCoordinate()
        {
            // Log that movement is stopping.
            LogDriveToTargetEntryCoordinateProgress("Stopped");

            // Stop movement.
            var stopResponse = await CommunicationManager.Instance.Stop(ManipulatorID);

            // Shortcut exit if no errors.
            if (string.IsNullOrEmpty(stopResponse))
                return true;

            // Log errors.
            Debug.LogError(stopResponse);
            return false;
        }

        #endregion

        #region Internal Functions

        /// <summary>
        ///     Compute the trajectory to the target insertion entry coordinate.
        /// </summary>
        /// <param name="targetInsertionProbeManager">Probe manager of the target insertion to compute the entry coordinate for.</param>
        private void ComputeTargetEntryCoordinateTrajectory(
            ProbeManager targetInsertionProbeManager
        )
        {
            // Compute entry coordinate in world space.
            var entryCoordinateWorld =
                targetInsertionProbeManager.GetSurfaceCoordinateWorldT()
                - targetInsertionProbeManager.ProbeController.GetTipWorldU().tipForwardWorldU
                    * IDEAL_ENTRY_COORDINATE_TO_DURA_DISTANCE;

            // Convert world space to transformed space.
            var entryCoordinateAPMLDV = BrainAtlasManager.ActiveAtlasTransform.U2T(
                BrainAtlasManager.ActiveReferenceAtlas.World2Atlas(entryCoordinateWorld)
            );

            // Get current probe coordinate.
            var currentCoordinate = _probeManager.ProbeController.Insertion.APMLDV;

            // Set first movement (DV).
            _trajectoryCoordinates.first = new Vector3(
                currentCoordinate.x,
                currentCoordinate.y,
                entryCoordinateAPMLDV.z
            );

            // Set second movement (AP).
            _trajectoryCoordinates.second = new Vector3(
                entryCoordinateAPMLDV.x,
                currentCoordinate.y,
                entryCoordinateAPMLDV.z
            );

            // Set third movement (ML).
            _trajectoryCoordinates.third = entryCoordinateAPMLDV;
        }

        /// <summary>
        ///     Create the trajectory line game objects and line renderers (if needed).
        /// </summary>
        private void CreateTrajectoryLines()
        {
            // Shortcut exit if they already exist.
            if (_trajectoryLineGameObjects.ap != null)
                return;

            // Create the trajectory line game objects.
            _trajectoryLineGameObjects = (
                new GameObject("APTrajectoryLine") { layer = 5 },
                new GameObject("MLTrajectoryLine") { layer = 5 },
                new GameObject("DVTrajectoryLine") { layer = 5 }
            );

            // Create the line renderers.
            _trajectoryLineLineRenderers = (
                _trajectoryLineGameObjects.ap.AddComponent<LineRenderer>(),
                _trajectoryLineGameObjects.ml.AddComponent<LineRenderer>(),
                _trajectoryLineGameObjects.dv.AddComponent<LineRenderer>()
            );

            // Apply materials.
            var defaultSpriteShader = Shader.Find("Sprites/Default");
            _trajectoryLineLineRenderers.ap.material = new Material(defaultSpriteShader)
            {
                color = AP_COLOR
            };
            _trajectoryLineLineRenderers.ml.material = new Material(defaultSpriteShader)
            {
                color = ML_COLOR
            };
            _trajectoryLineLineRenderers.dv.material = new Material(defaultSpriteShader)
            {
                color = DV_COLOR
            };

            // Set line widths.
            _trajectoryLineLineRenderers.ap.startWidth = _trajectoryLineLineRenderers.ap.endWidth =
                LINE_WIDTH;
            _trajectoryLineLineRenderers.ml.startWidth = _trajectoryLineLineRenderers.ml.endWidth =
                LINE_WIDTH;
            _trajectoryLineLineRenderers.dv.startWidth = _trajectoryLineLineRenderers.dv.endWidth =
                LINE_WIDTH;

            // Set segment counts.
            _trajectoryLineLineRenderers.ap.positionCount = NUM_SEGMENTS;
            _trajectoryLineLineRenderers.ml.positionCount = NUM_SEGMENTS;
            _trajectoryLineLineRenderers.dv.positionCount = NUM_SEGMENTS;
        }

        /// <summary>
        ///     Update the trajectory line positions.
        /// </summary>
        private void UpdateTrajectoryLines()
        {
            _trajectoryLineLineRenderers.dv.SetPosition(0, _probeController.ProbeTipT.position);
            _trajectoryLineLineRenderers.dv.SetPosition(
                1,
                BrainAtlasManager.ActiveReferenceAtlas.Atlas2World(
                    BrainAtlasManager.ActiveAtlasTransform.T2U_Vector(_trajectoryCoordinates.first)
                )
            );

            _trajectoryLineLineRenderers.ap.SetPosition(
                0,
                BrainAtlasManager.ActiveReferenceAtlas.Atlas2World(
                    BrainAtlasManager.ActiveAtlasTransform.T2U_Vector(_trajectoryCoordinates.first)
                )
            );
            _trajectoryLineLineRenderers.ap.SetPosition(
                1,
                BrainAtlasManager.ActiveReferenceAtlas.Atlas2World(
                    BrainAtlasManager.ActiveAtlasTransform.T2U_Vector(_trajectoryCoordinates.second)
                )
            );

            _trajectoryLineLineRenderers.ml.SetPosition(
                0,
                BrainAtlasManager.ActiveReferenceAtlas.Atlas2World(
                    BrainAtlasManager.ActiveAtlasTransform.T2U_Vector(_trajectoryCoordinates.second)
                )
            );
            _trajectoryLineLineRenderers.ml.SetPosition(
                1,
                BrainAtlasManager.ActiveReferenceAtlas.Atlas2World(
                    BrainAtlasManager.ActiveAtlasTransform.T2U_Vector(_trajectoryCoordinates.third)
                )
            );
        }

        /// <summary>
        ///     Destroy the trajectory line game objects and line renderers. Also reset the references.
        /// </summary>
        private void RemoveTrajectoryLines()
        {
            // Destroy the objects.
            Destroy(_trajectoryLineGameObjects.ap);
            Destroy(_trajectoryLineGameObjects.ml);
            Destroy(_trajectoryLineGameObjects.dv);

            // Reset the references.
            _trajectoryLineGameObjects = (null, null, null);
            _trajectoryLineLineRenderers = (null, null, null);
        }

        /// <summary>
        ///     Log the progress of the drive to the target entry coordinate.
        /// </summary>
        /// <param name="progressMessage">Message to log</param>
        private void LogDriveToTargetEntryCoordinateProgress(string progressMessage)
        {
            OutputLog.Log(
                new[]
                {
                    "Automation",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "DriveToTargetEntryCoordinate",
                    ManipulatorID,
                    progressMessage
                }
            );
        }

        #endregion
    }
}
