using UnityEditor;
using UnityEngine;
using System.IO;

public class ExtractKF21Materials
{
    public static void Execute()
    {
        string fbxPath = "Assets/Models/KF21/KF-21.fbx";
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer != null)
        {
            // Set material import mode
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.Local;
            
            // Create folders if they don't exist
            if (!AssetDatabase.IsValidFolder("Assets/Models/KF21/Textures"))
                AssetDatabase.CreateFolder("Assets/Models/KF21", "Textures");
            if (!AssetDatabase.IsValidFolder("Assets/Models/KF21/Materials"))
                AssetDatabase.CreateFolder("Assets/Models/KF21", "Materials");

            // Extract textures
            importer.ExtractTextures("Assets/Models/KF21/Textures");
            
            importer.SaveAndReimport();
            Debug.Log("Reimported KF-21.fbx to extract materials and textures.");
        }
        else
        {
            Debug.LogError("Could not find ModelImporter for " + fbxPath);
        }
    }
}
