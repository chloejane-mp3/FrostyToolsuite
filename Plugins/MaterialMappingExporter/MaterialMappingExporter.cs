using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Frosty.Core;
using Frosty.Core.Windows;
using Frosty.Core.Viewport;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Resources;
using Newtonsoft.Json;
using MeshSetPlugin.Resources;
using TexturePlugin;

namespace MaterialMappingPlugin
{
    public class MaterialMapping
    {
        public string MeshAssetPath { get; set; }
        public string MeshAssetName { get; set; }
        public List<MaterialInfo> Materials { get; set; } = new List<MaterialInfo>();
        public List<LodInfo> Lods { get; set; } = new List<LodInfo>();
    }

    public class MaterialInfo
    {
        public string MaterialName { get; set; }
        public string MaterialPath { get; set; }
        public Guid MaterialGuid { get; set; }
        public int MaterialIndex { get; set; }
        public Dictionary<string, TextureParameterInfo> Textures { get; set; } = new Dictionary<string, TextureParameterInfo>();
        public Dictionary<string, object> ScalarParameters { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, float[]> VectorParameters { get; set; } = new Dictionary<string, float[]>();
    }

    public class TextureParameterInfo
    {
        public string ParameterName { get; set; }
        public string TexturePath { get; set; }
        public string TextureName { get; set; }
        public Guid TextureGuid { get; set; }
    }

    public class LodInfo
    {
        public int LodLevel { get; set; }
        public List<SectionInfo> Sections { get; set; } = new List<SectionInfo>();
    }

    public class SectionInfo
    {
        public int SectionIndex { get; set; }
        public int MaterialIndex { get; set; }
        public string MaterialName { get; set; }
    }

    public class MaterialMappingExporter
    {
        public void ExportSelectedMeshMaterialMapping(EbxAssetEntry selectedEntry, string outputDirectory)
        {
            if (selectedEntry == null)
            {
                App.Logger.LogError("[Material Mapping Exporter] No asset selected.");
                return;
            }

            if (selectedEntry.Type != "MeshAsset" && selectedEntry.Type != "RigidMeshAsset" && selectedEntry.Type != "SkinnedMeshAsset" && selectedEntry.Type != "CompositeMeshAsset")
            {
                App.Logger.LogError($"[Material Mapping Exporter] Selected asset '{selectedEntry.Name}' is not a mesh (Type: {selectedEntry.Type})");
                return;
            }

            FrostyTaskWindow.Show("Exporting Material Mapping", "", (task) =>
            {
                task.Update($"Processing {selectedEntry.Name}...", 10.0);

                try
                {
                    MaterialMapping mapping = ExtractMaterialMapping(selectedEntry);
                    if (mapping != null && mapping.Materials.Count > 0)
                    {
                        if (!Directory.Exists(outputDirectory))
                            Directory.CreateDirectory(outputDirectory);

                        // Export individual JSON preserving folder structure
                        string relativePath = mapping.MeshAssetPath.Replace('/', Path.DirectorySeparatorChar);
                        string fullPath = Path.Combine(outputDirectory, relativePath);
                        string directory = Path.GetDirectoryName(fullPath);
                        if (!Directory.Exists(directory))
                            Directory.CreateDirectory(directory);

                        string jsonPath = Path.ChangeExtension(fullPath, ".material.json");
                        File.WriteAllText(jsonPath, JsonConvert.SerializeObject(mapping, Formatting.Indented));

                        bool hasTextures = mapping.Materials.Any(m => m.Textures.Count > 0);
                        App.Logger.Log($"[Material Mapping Exporter] Exported {selectedEntry.Name} -> {jsonPath} (textures found: {hasTextures})");
                    }
                    else
                    {
                        App.Logger.LogWarning($"[Material Mapping Exporter] No material data extracted from {selectedEntry.Name}");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.LogError($"[Material Mapping Exporter] Failed to export {selectedEntry.Name}: {ex.Message}");
                }

                task.Update("Done!", 100.0);
            });
        }

        public void ExportAllMeshMaterialMappings(string outputDirectory)
        {
            List<MaterialMapping> allMappings = new List<MaterialMapping>();
            int processedCount = 0;
            int totalCount = 0;
            int skippedNoMaterials = 0;
            int skippedNoTextures = 0;

            // Count total meshes
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx())
            {
                if (entry.Type == "MeshAsset" || entry.Type == "RigidMeshAsset" || entry.Type == "SkinnedMeshAsset" || entry.Type == "CompositeMeshAsset")
                    totalCount++;
            }

            FrostyTaskWindow.Show("Exporting Material Mappings", "", (task) =>
            {
                // Load MeshVariationDb first (required for ShaderGraph materials)
                if (!MeshVariationDb.IsLoaded)
                {
                    task.Update("Loading Mesh Variation Database...", 0);
                    MeshVariationDb.LoadVariations(task);
                }

                foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx())
                {
                    if (entry.Type == "MeshAsset" || entry.Type == "RigidMeshAsset" || entry.Type == "SkinnedMeshAsset" || entry.Type == "CompositeMeshAsset")
                    {
                        try
                        {
                            MaterialMapping mapping = ExtractMaterialMapping(entry);
                            if (mapping != null)
                            {
                                if (mapping.Materials.Count > 0)
                                {
                                    bool hasTextures = mapping.Materials.Any(m => m.Textures.Count > 0);
                                    if (hasTextures)
                                    {
                                        allMappings.Add(mapping);
                                    }
                                    else
                                    {
                                        skippedNoTextures++;
                                        App.Logger.Log($"Skipped (no textures): {entry.Name}");
                                    }
                                }
                                else
                                {
                                    skippedNoMaterials++;
                                }
                            }
                            else
                            {
                                skippedNoMaterials++;
                            }

                            processedCount++;
                            task.Update($"Processing {entry.Name}", (processedCount / (double)totalCount) * 100.0);
                        }
                        catch (Exception ex)
                        {
                            App.Logger.LogError($"Failed to process {entry.Name}: {ex.Message}");
                        }
                    }
                }

                // Export to JSON
                task.Update("Writing JSON files...", 95.0);
                
                // Create output directory if it doesn't exist
                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                // Export individual JSON files (preserving folder structure)
                foreach (var mapping in allMappings)
                {
                    string relativePath = mapping.MeshAssetPath.Replace('/', Path.DirectorySeparatorChar);
                    string fullPath = Path.Combine(outputDirectory, relativePath);
                    string directory = Path.GetDirectoryName(fullPath);
                    
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    string jsonPath = Path.ChangeExtension(fullPath, ".material.json");
                    File.WriteAllText(jsonPath, JsonConvert.SerializeObject(mapping, Formatting.Indented));
                }

                // Also export a master JSON with all mappings
                string masterJsonPath = Path.Combine(outputDirectory, "all_material_mappings.json");
                File.WriteAllText(masterJsonPath, JsonConvert.SerializeObject(allMappings, Formatting.Indented));

                // Export statistics
                var stats = new
                {
                    TotalMeshes = totalCount,
                    SuccessfulExports = allMappings.Count,
                    SkippedNoMaterials = skippedNoMaterials,
                    SkippedNoTextures = skippedNoTextures,
                    ExportDate = DateTime.Now
                };
                
                string statsPath = Path.Combine(outputDirectory, "export_stats.json");
                File.WriteAllText(statsPath, JsonConvert.SerializeObject(stats, Formatting.Indented));

                task.Update("Export complete!", 100.0);
            });

            App.Logger.Log($"Exported {allMappings.Count} material mappings to {outputDirectory}");
            App.Logger.Log($"Skipped: {skippedNoMaterials} (no materials), {skippedNoTextures} (no textures)");
        }


        private MaterialMapping ExtractMaterialMapping(EbxAssetEntry meshEntry)
        {
            try
            {
                EbxAsset meshAsset = App.AssetManager.GetEbx(meshEntry);
                dynamic meshObj = meshAsset.RootObject;

                MaterialMapping mapping = new MaterialMapping
                {
                    MeshAssetPath = meshEntry.Name,
                    MeshAssetName = meshEntry.Filename
                };

                // Get mesh resource
                MeshSet meshSet = null;
                try
                {
                    ResAssetEntry resEntry = App.AssetManager.GetResEntry(meshObj.MeshSetResource);
                    if (resEntry != null)
                    {
                        meshSet = App.AssetManager.GetResAs<MeshSet>(resEntry);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.LogWarning($"Could not load mesh resource for {meshEntry.Name}: {ex.Message}");
                }

                if (meshObj.Materials == null || meshObj.Materials.Count == 0)
                {
                    return null;
                }

                Dictionary<int, MaterialInfo> materialDict = new Dictionary<int, MaterialInfo>();

                for (int i = 0; i < meshObj.Materials.Count; i++)
                {
                    try
                    {
                        dynamic materialContainer = meshObj.Materials[i];
                        
                        MaterialInfo matInfo = new MaterialInfo
                        {
                            MaterialIndex = i,
                            MaterialName = $"Material_{i}",
                            MaterialGuid = Guid.Empty
                        };

                        // Get the actual material - it's in the Internal property (or External if not internal)
                        dynamic material = null;
                        
                        // Try to get internal material first
                        if (materialContainer.Internal != null)
                        {
                            material = materialContainer.Internal;
                        }
                        // If Internal is null, try External
                        else if (materialContainer.External != null && materialContainer.External.FileGuid != Guid.Empty)
                        {
                            try
                            {
                                EbxAssetEntry matEntry = App.AssetManager.GetEbxEntry(materialContainer.External.FileGuid);
                                if (matEntry != null)
                                {
                                    EbxAsset matAsset = App.AssetManager.GetEbx(matEntry);
                                    material = (dynamic)matAsset.RootObject;
                                    App.Logger.Log($"  Material {i}: Loaded external material from {matEntry.Name}");
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger.LogWarning($"  Failed to load external material: {ex.Message}");
                            }
                        }
                        
                        if (material == null)
                        {
                            App.Logger.LogWarning($"  Material {i}: Could not resolve material (both Internal and External are null)");
                            materialDict[i] = matInfo;
                            mapping.Materials.Add(matInfo);
                            continue;
                        }

                        // Get material GUID if available (not all material types have this)
                        try
                        {
                            if (material.Id != null && material.Id.Guid != null)
                            {
                                matInfo.MaterialGuid = material.Id.Guid;
                            }
                        }
                        catch
                        {
                            // Some material types don't have Id property, that's okay
                        }

                        bool foundTextures = false;

                        // **NEW: TRY METHOD 1 - ShaderInstance (inline textures)**
                        try
                        {
                            if (material.ShaderInstance != null)
                            {
                                App.Logger.Log($"  Material {i}: Found ShaderInstance");
                                dynamic shaderInstance = material.ShaderInstance;
                                
                                // Check if ShaderInstance is a PointerRef that needs dereferencing
                                if (shaderInstance is PointerRef)
                                {
                                    PointerRef shaderInstRef = shaderInstance;
                                    if (shaderInstRef.Type == PointerRefType.Internal && shaderInstRef.Internal != null)
                                    {
                                        shaderInstance = shaderInstRef.Internal;
                                        App.Logger.Log($"    Dereferenced ShaderInstance from Internal PointerRef");
                                    }
                                    else if (shaderInstRef.Type == PointerRefType.External)
                                    {
                                        EbxAssetEntry siEntry = App.AssetManager.GetEbxEntry(shaderInstRef.External.FileGuid);
                                        if (siEntry != null)
                                        {
                                            EbxAsset siAsset = App.AssetManager.GetEbx(siEntry);
                                            shaderInstance = (dynamic)siAsset.RootObject;
                                            App.Logger.Log($"    Dereferenced ShaderInstance from External PointerRef: {siEntry.Name}");
                                        }
                                    }
                                    else
                                    {
                                        App.Logger.LogWarning($"    ShaderInstance is PointerRef but type is {shaderInstRef.Type}");
                                        shaderInstance = null;
                                    }
                                }
                                
                                if (shaderInstance != null && shaderInstance.Shader != null)
                                {
                                    dynamic shaderData = shaderInstance.Shader;
                                    App.Logger.Log($"    Shader data type: {shaderData.GetType().Name}");
                                    
                                    // Get shader name
                                    if (shaderData.SurfaceShaderName != null)
                                    {
                                        matInfo.MaterialName = shaderData.SurfaceShaderName.ToString();
                                    }

                                    // Extract inline texture parameters
                                    if (shaderData.TextureParameters != null && shaderData.TextureParameters.Count > 0)
                                    {
                                        App.Logger.Log($"    Found {shaderData.TextureParameters.Count} inline texture parameters");
                                        foundTextures = ExtractTextureParameters(shaderData.TextureParameters, matInfo) || foundTextures;
                                    }
                                    
                                    ExtractVectorParameters(shaderData, matInfo);
                                    ExtractBoolParameters(shaderData, matInfo);
                                    ExtractConditionalParameters(shaderData, matInfo);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger.LogWarning($"  Error accessing ShaderInstance for material {i}: {ex.Message}");
                        }

                        // **EXISTING: METHOD 2 - Shader with external PointerRef**
                        // Only try this if we didn't find textures in ShaderInstance
                        if (!foundTextures)
                        {
                            try
                            {
                                if (material.Shader != null)
                                {
                                    App.Logger.Log($"  Material {i}: Checking Shader for external preset");
                                    dynamic shader = material.Shader;

                                    // Get material name from shader if not already set
                                    if (matInfo.MaterialName.StartsWith("Material_") && shader.SurfaceShaderName != null)
                                    {
                                        matInfo.MaterialName = shader.SurfaceShaderName.ToString();
                                    }

                                    // Check for external shader preset
                                    if (shader.Shader != null)
                                    {
                                        PointerRef shaderRef = shader.Shader;
                                        
                                        if (shaderRef.Type == PointerRefType.External)
                                        {
                                            EbxAssetEntry shaderEntry = App.AssetManager.GetEbxEntry(shaderRef.External.FileGuid);
                                            
                                            if (shaderEntry != null)
                                            {
                                                App.Logger.Log($"    Loading external shader: {shaderEntry.Name} (Type: {shaderEntry.Type})");
                                                
                                                if (TypeLibrary.IsSubClassOf(shaderEntry.Type, "SurfaceShaderPreset"))
                                                {
                                                    EbxAsset shaderAsset = App.AssetManager.GetEbx(shaderEntry);
                                                    dynamic shaderRoot = shaderAsset.RootObject;
                                                    dynamic shaderPreset = shaderRoot.ShaderPreset;
                                                    
                                                    if (shaderPreset != null && shaderPreset.TextureParameters != null)
                                                    {
                                                        App.Logger.Log($"    Found {shaderPreset.TextureParameters.Count} external texture parameters");
                                                        foundTextures = ExtractTextureParameters(shaderPreset.TextureParameters, matInfo);
                                                        
                                                        ExtractVectorParameters(shaderPreset, matInfo);
                                                        ExtractBoolParameters(shaderPreset, matInfo);
                                                        ExtractConditionalParameters(shaderPreset, matInfo);
                                                    }
                                                }
                                                else if (TypeLibrary.IsSubClassOf(shaderEntry.Type, "ShaderGraph"))
                                                {
                                                    App.Logger.Log($"    ShaderGraph detected - textures from MeshVariationDb");
                                                    
                                                    // ** ShaderGraph textures come from MeshVariationDb, not the EBX **
                                                    try
                                                    {
                                                        MeshVariationDbEntry mvEntry = MeshVariationDb.GetVariations(meshEntry.Guid);
                                                        if (mvEntry != null)
                                                        {
                                                            MeshVariation mvRoot = mvEntry.GetVariation(MeshVariationDbEntry.ROOT_VARIATION);
                                                            if (mvRoot != null && mvRoot.Materials.Count > i)
                                                            {
                                                                dynamic texParams = mvRoot.Materials[i].TextureParameters;
                                                                if (texParams != null && texParams.Count > 0)
                                                                {
                                                                    App.Logger.Log($"    Found {texParams.Count} texture parameters in MeshVariationDb");
                                                                    foundTextures = ExtractTextureParameters(texParams, matInfo) || foundTextures;
                                                                }
                                                                else
                                                                {
                                                                    App.Logger.Log($"    MeshVariationDb entry found but has no texture parameters for material {i}");
                                                                }
                                                            }
                                                            else
                                                            {
                                                                App.Logger.Log($"    MeshVariationDb entry found but has no root variation or material index {i}");
                                                            }
                                                        }
                                                        else
                                                        {
                                                            App.Logger.LogWarning($"    No MeshVariationDb entry found for mesh {meshEntry.Name} (Guid: {meshEntry.Guid})");
                                                        }
                                                    }
                                                    catch (Exception mvEx)
                                                    {
                                                        App.Logger.LogError($"    Error loading MeshVariationDb for ShaderGraph: {mvEx.Message}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    
                                    // Also check for direct texture parameters on the shader
                                    App.Logger.Log($"    Checking shader.TextureParameters...");
                                    try
                                    {
                                        if (shader.TextureParameters != null)
                                        {
                                            int count = shader.TextureParameters.Count;
                                            App.Logger.Log($"    shader.TextureParameters exists with {count} items");
                                            
                                            if (count > 0)
                                            {
                                                App.Logger.Log($"    Found {count} direct shader texture parameters");
                                                foundTextures = ExtractTextureParameters(shader.TextureParameters, matInfo) || foundTextures;
                                            }
                                        }
                                        else
                                        {
                                            App.Logger.LogWarning($"    shader.TextureParameters is null");
                                        }
                                    }
                                    catch (Exception texEx)
                                    {
                                        App.Logger.LogError($"    Error checking shader.TextureParameters: {texEx.Message}");
                                    }
                                    
                                    ExtractVectorParameters(shader, matInfo);
                                    ExtractBoolParameters(shader, matInfo);
                                    ExtractConditionalParameters(shader, matInfo);
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger.LogWarning($"  Error accessing Shader for material {i}: {ex.Message}");
                            }
                        }

                        if (!foundTextures)
                        {
                            App.Logger.LogWarning($"  No texture parameters found for material {i} (tried both ShaderInstance and Shader)");
                        }

                        // **METHOD 3 - Dump all mesh properties to find variation DB**
                        // Only do this once (material 0) to avoid log spam
                        if (!foundTextures && i == 0)
                        {
                            try
                            {
                                var meshType = meshObj.GetType();
                                App.Logger.Log($"    === ALL {meshType.GetProperties().Length} properties on {meshType.Name} ===");
                                foreach (var prop in meshType.GetProperties())
                                {
                                    try
                                    {
                                        var value = prop.GetValue(meshObj);
                                        string valueStr = value?.ToString() ?? "null";
                                        if (value is System.Collections.ICollection col)
                                        {
                                            valueStr = $"[Collection: {col.Count} items]";
                                        }
                                        App.Logger.Log($"      {prop.Name} = {valueStr}");
                                    }
                                    catch { App.Logger.Log($"      {prop.Name} = <error>"); }
                                }
                                App.Logger.Log($"    === END PROPERTIES ===");
                            }
                            catch (Exception ex)
                            {
                                App.Logger.LogError($"    Error dumping properties: {ex.Message}");
                            }
                        }

                        if (!foundTextures)
                        {
                            App.Logger.LogWarning($"  Still no textures after trying all methods including MeshVariationDbs");
                        }

                        materialDict[i] = matInfo;
                        mapping.Materials.Add(matInfo);
                    }
                    catch (Exception ex)
                    {
                        App.Logger.LogError($"Error processing material {i} for {meshEntry.Name}: {ex.Message}");
                    }
                }

                // Extract LOD and section information if we have the mesh resource
                if (meshSet != null)
                {
                    try
                    {
                        for (int lodIdx = 0; lodIdx < meshSet.Lods.Count; lodIdx++)
                        {
                            LodInfo lodInfo = new LodInfo { LodLevel = lodIdx };

                            for (int secIdx = 0; secIdx < meshSet.Lods[lodIdx].Sections.Count; secIdx++)
                            {
                                var section = meshSet.Lods[lodIdx].Sections[secIdx];
                                
                                SectionInfo secInfo = new SectionInfo
                                {
                                    SectionIndex = secIdx,
                                    MaterialIndex = section.MaterialId
                                };

                                if (materialDict.ContainsKey(section.MaterialId))
                                {
                                    secInfo.MaterialName = materialDict[section.MaterialId].MaterialName;
                                }

                                lodInfo.Sections.Add(secInfo);
                            }

                            mapping.Lods.Add(lodInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.LogError($"Error extracting LOD info for {meshEntry.Name}: {ex.Message}");
                    }
                }

                // Log if we didn't get any textures
                if (mapping.Materials.All(m => m.Textures.Count == 0))
                {
                    App.Logger.LogWarning($"No textures found for {meshEntry.Name}");
                }

                return mapping;
            }
            catch (Exception ex)
            {
                App.Logger.LogError($"Error extracting material mapping for {meshEntry.Name}: {ex.Message}");
                return null;
            }
        }

        private bool ExtractTextureParameters(dynamic textureParameters, MaterialInfo matInfo)
        {
            bool foundAny = false;
            foreach (dynamic texParam in textureParameters)
            {
                try
                {
                    // Skip null parameters
                    if (texParam == null || texParam.Value == null)
                        continue;

                    string paramName = texParam.ParameterName?.ToString() ?? "Unknown";
                    PointerRef texRef = texParam.Value;

                    if (texRef.Type == PointerRefType.External)
                    {
                        TextureParameterInfo texInfo = new TextureParameterInfo
                        {
                            ParameterName = paramName,
                            TextureGuid = texRef.External.FileGuid  // FIXED: was ClassGuid
                        };

                        try
                        {
                            EbxAssetEntry texEntry = App.AssetManager.GetEbxEntry(texRef.External.FileGuid);
                            if (texEntry != null)
                            {
                                texInfo.TexturePath = texEntry.Name;
                                texInfo.TextureName = texEntry.Filename;
                            }
                        }
                        catch { }

                        matInfo.Textures[paramName] = texInfo;
                        foundAny = true;
                    }
                    else if (texRef.Type == PointerRefType.Internal)
                    {
                        // Internal references - log but don't crash
                        App.Logger.Log($"      Texture {paramName} is Internal reference");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger.LogError($"Error extracting texture param: {ex.Message}");
                }
            }
            return foundAny;
        }

        private void ExtractVectorParameters(dynamic shader, MaterialInfo matInfo)
        {
            if (shader.VectorParameters != null)
            {
                foreach (dynamic vecParam in shader.VectorParameters)
                {
                    try
                    {
                        string paramName = vecParam.ParameterName;
                        dynamic vec = vecParam.Value;
                        matInfo.VectorParameters[paramName] = new float[] { vec.x, vec.y, vec.z, vec.w };
                    }
                    catch { }
                }
            }
        }

        private void ExtractBoolParameters(dynamic shader, MaterialInfo matInfo)
        {
            if (shader.BoolParameters != null)
            {
                foreach (dynamic boolParam in shader.BoolParameters)
                {
                    try
                    {
                        string paramName = boolParam.ParameterName;
                        bool value = boolParam.Value;
                        matInfo.ScalarParameters[paramName] = value;
                    }
                    catch { }
                }
            }
        }

        private void ExtractConditionalParameters(dynamic shader, MaterialInfo matInfo)
        {
            if (shader.ConditionalParameters != null)
            {
                foreach (dynamic condParam in shader.ConditionalParameters)
                {
                    try
                    {
                        string paramName = condParam.ParameterName != null ? condParam.ParameterName : "Conditional";
                        string value = condParam.Value;
                        matInfo.ScalarParameters[paramName] = value;
                    }
                    catch { }
                }
            }
        }

        public void ExportAllTextures(string outputDirectory, string format = "tga")
        {
            int processedCount = 0;
            int totalCount = 0;
            int successCount = 0;
            int failedCount = 0;

            // Count total textures
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx("TextureAsset"))
            {
                totalCount++;
            }

            FrostyTaskWindow.Show("Exporting Textures", "", (task) =>
            {
                foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx("TextureAsset"))
                {
                    try
                    {
                        task.Update($"Exporting {entry.Name}", (processedCount / (double)totalCount) * 100.0);

                        // Get the texture EBX
                        EbxAsset textureAsset = App.AssetManager.GetEbx(entry);
                        dynamic textureObj = textureAsset.RootObject;

                        // Get the texture resource
                        ulong resRid = textureObj.Resource;
                        ResAssetEntry resEntry = App.AssetManager.GetResEntry(resRid);
                        
                        if (resEntry == null)
                        {
                            App.Logger.LogWarning($"No resource found for texture: {entry.Name}");
                            failedCount++;
                            processedCount++;
                            continue;
                        }

                        // Create output path preserving folder structure
                        string relativePath = entry.Name.Replace('/', Path.DirectorySeparatorChar);
                        string fullPath = Path.Combine(outputDirectory, relativePath);
                        string directory = Path.GetDirectoryName(fullPath);

                        if (!Directory.Exists(directory))
                            Directory.CreateDirectory(directory);

                        string outputPath = fullPath + "." + format.ToLower();

                        // Load texture
                        Texture texture = App.AssetManager.GetResAs<Texture>(resEntry);
                        if (texture == null)
                        {
                            App.Logger.LogWarning($"Could not load texture resource: {entry.Name}");
                            failedCount++;
                            processedCount++;
                            continue;
                        }

                        try
                        {
                            // Use Frosty's built-in TextureExporter
                            TextureExporter exporter = new TextureExporter();
                            string filterType = "*." + format.ToLower();
                            exporter.Export(texture, outputPath, filterType);
                            successCount++;
                        }
                        catch (Exception texEx)
                        {
                            App.Logger.LogError($"Error exporting texture {entry.Name}: {texEx.Message}");
                            failedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.LogError($"Failed to export texture {entry.Name}: {ex.Message}");
                        failedCount++;
                    }

                    processedCount++;
                }

                // Export statistics
                var stats = new
                {
                    TotalTextures = totalCount,
                    SuccessfulExports = successCount,
                    FailedExports = failedCount,
                    ExportDate = DateTime.Now,
                    Format = format
                };

                string statsPath = Path.Combine(outputDirectory, "texture_export_stats.json");
                File.WriteAllText(statsPath, JsonConvert.SerializeObject(stats, Formatting.Indented));

                task.Update("Export complete!", 100.0);
            });

            App.Logger.Log($"Exported {successCount} textures to {outputDirectory}");
            App.Logger.Log($"Failed: {failedCount}");
        }
    }
}
