using UnityEditor;
using UnityEngine;

public class FixKF21Materials
{
    public static void Execute()
    {
        string fbxPath = "Assets/Models/KF21/KF-21.fbx";
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer != null)
        {
            // Use modern material extraction
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            
            // Extract materials to folder
            importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Local);
            
            importer.SaveAndReimport();
            Debug.Log("Reimported KF-21.fbx with SearchAndRemapMaterials.");
        }
    }
}
