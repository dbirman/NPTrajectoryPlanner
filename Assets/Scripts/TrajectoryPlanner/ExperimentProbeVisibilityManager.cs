using System.Collections;
using System.Collections.Generic;
using TrajectoryPlanner;
using UnityEngine;

/// <summary>
/// This class handles communicating data between probes that are in the scene and the AccountsManager class
/// that handles saving/loading data from the Unisave database. 
/// </summary>
public class ExperimentProbeVisibilityManager : MonoBehaviour
{
    [SerializeField] private UnisaveAccountsManager _accountsManager;
    [SerializeField] private TrajectoryPlannerManager _tpmanager;

    // Track active probe gameobjects by UUID
    Dictionary<string, ProbeManager> _experimentProbeManagers;

    /// <summary>
    /// 
    /// </summary>
    public void SyncVisibleProbes()
    {
        // Collect the active data
        var activeProbeData = _accountsManager.GetActiveProbeInsertions();

        List<string> UUIDs = new List<string>();

        foreach (var probeData in activeProbeData)
            UUIDs.Add(probeData.UUID);

        // Go through the active probes
        // Remove ProbeManagers in the scene that aren't in the list
        foreach (var kvp in _experimentProbeManagers)
        {
            // Destroy insertions that are related to a different experiment
            if (!UUIDs.Contains(kvp.Key))
                _tpmanager.DestroyProbe(kvp.Value);
        }

        // Add ProbeManagers for all the insertions that are in this experiment

    }
}