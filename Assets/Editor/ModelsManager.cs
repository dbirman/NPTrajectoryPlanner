using System.Collections;
using System.IO;
using System.Net;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Editor
{
    public static class ModelsManager
    {
        [MenuItem("Tools/Update Schemas")]
        public static void UpdateSchemas()
        {
            var webClient = new WebClient();
            
            webClient.DownloadFile(
                "https://raw.githubusercontent.com/VirtualBrainLab/vbl-aquarium/main/models/csharp/EphysLinkModels.cs",
                "Assets/Scripts/EphysLink/EphysLinkModels.cs");

            webClient.DownloadFile(
                "https://raw.githubusercontent.com/VirtualBrainLab/vbl-aquarium/main/models/csharp/PinpointModels.cs",
                "Assets/Scripts/Pinpoint/JSON/PinpointModels.cs");

            Debug.Log("Schemas updated successfully!");
        }

        private static void GetSchemas(string srcURL, string outFile)
        {
            if (!Directory.Exists(outFile)) Directory.CreateDirectory(outFile);

            var files = Directory.GetFiles(srcURL, "*.cs");

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destFilePath = Path.Combine(outFile, fileName);
                File.Copy(file, destFilePath, true);
            }

            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Update Pinpoint Schemas")]
        public static void UpdatePinpointSchemas()
        {
            string sourceFile = "C:\\proj\\VBL\\vbl-aquarium\\models\\csharp\\PinpointModels.cs";
            string destinationFile = "Assets/Scripts/Pinpoint/Models/PinpointModels.cs";

            File.Copy(sourceFile, destinationFile, true);

            AssetDatabase.Refresh();
        }
    }
}