using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;

namespace MaterialMappingPlugin
{
    /// <summary>
    /// Exports level data (world, entity placements, transforms) to XML
    /// </summary>
    public class LevelDataExporter
    {
        /// <summary>
        /// Diagnostic: List all SubWorldData entries to help find layer bundles
        /// </summary>
        public void DiagnoseLayerBundles(string outputDirectory)
        {
            List<string> subWorldEntries = new List<string>();
            List<string> artLayers = new List<string>();
            Dictionary<string, List<string>> levelToLayers = new Dictionary<string, List<string>>();
            
            App.Logger.Log("=== DIAGNOSTIC: Searching for SubWorldData entries ===");
            
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx())
            {
                if (entry.Type == "SubWorldData")
                {
                    subWorldEntries.Add(entry.Name);
                    
                    if (entry.Name.Contains("_Art") || entry.Name.Contains("_art"))
                    {
                        artLayers.Add(entry.Name);
                    }
                    
                    // Try to find base level name
                    string[] parts = entry.Name.Split('_');
                    if (parts.Length > 1)
                    {
                        string lastPart = parts[parts.Length - 1];
                        if (lastPart.Equals("Art", StringComparison.OrdinalIgnoreCase) ||
                            lastPart.Equals("Logic", StringComparison.OrdinalIgnoreCase) ||
                            lastPart.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
                            lastPart.Equals("Lighting", StringComparison.OrdinalIgnoreCase))
                        {
                            string baseName = string.Join("_", parts, 0, parts.Length - 1);
                            if (!levelToLayers.ContainsKey(baseName))
                                levelToLayers[baseName] = new List<string>();
                            levelToLayers[baseName].Add(entry.Name);
                        }
                    }
                }
            }
            
            // Write diagnostic report
            string reportPath = Path.Combine(outputDirectory, "layer_diagnostic_report.txt");
            using (StreamWriter writer = new StreamWriter(reportPath))
            {
                writer.WriteLine("=== LAYER BUNDLE DIAGNOSTIC REPORT ===");
                writer.WriteLine($"Generated: {DateTime.Now}");
                writer.WriteLine();
                
                writer.WriteLine($"Total SubWorldData entries found: {subWorldEntries.Count}");
                writer.WriteLine($"Entries containing '_Art': {artLayers.Count}");
                writer.WriteLine();
                
                writer.WriteLine("=== ALL SubWorldData ENTRIES ===");
                foreach (string name in subWorldEntries.OrderBy(x => x))
                {
                    writer.WriteLine(name);
                }
                writer.WriteLine();
                
                writer.WriteLine("=== ENTRIES CONTAINING '_Art' ===");
                foreach (string name in artLayers.OrderBy(x => x))
                {
                    writer.WriteLine(name);
                }
                writer.WriteLine();
                
                writer.WriteLine("=== DETECTED LEVEL GROUPINGS ===");
                foreach (var kvp in levelToLayers.OrderBy(x => x.Key))
                {
                    writer.WriteLine($"\nBase: {kvp.Key}");
                    foreach (string layer in kvp.Value.OrderBy(x => x))
                    {
                        writer.WriteLine($"  -> {layer}");
                    }
                }
                writer.WriteLine();
                
                // Also check for entities with mesh references
                writer.WriteLine("=== SEARCHING FOR MESH REFERENCES IN LEVELS ===");
                int checkedCount = 0;
                foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx())
                {
                    if (IsLevelAsset(entry.Type) && checkedCount < 20) // Check first 20 levels
                    {
                        try
                        {
                            EbxAsset asset = App.AssetManager.GetEbx(entry);
                            int meshCount = 0;
                            
                            if (asset.Objects != null)
                            {
                                foreach (var obj in asset.Objects)
                                {
                                    Type objType = obj.GetType();
                                    
                                    // Check for mesh properties
                                    var meshProp = objType.GetProperty("Mesh");
                                    if (meshProp != null && meshProp.GetValue(obj) != null)
                                        meshCount++;
                                }
                            }
                            
                            if (meshCount > 0)
                            {
                                writer.WriteLine($"{entry.Name} ({entry.Type}): {meshCount} mesh references");
                            }
                            
                            checkedCount++;
                        }
                        catch { }
                    }
                }
            }
            
            App.Logger.Log($"Diagnostic report written to: {reportPath}");
            App.Logger.Log($"Found {subWorldEntries.Count} SubWorldData entries total");
            App.Logger.Log($"Found {artLayers.Count} entries containing '_Art'");
        }

        /// <summary>
        /// Export all level data to XML files
        /// </summary>
        public void ExportAllLevelData(string outputDirectory)
        {
            int totalCount = 0;
            int successCount = 0;
            int failedCount = 0;

            // In Battlefront 2, geometry is spread across many SubWorldData entries
            // (rooms like Venator_Hangar, sections, etc.) - export them ALL
            List<EbxAssetEntry> allLevelEntries = new List<EbxAssetEntry>();
            
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx())
            {
                // Export all level/world types AND all SubWorldData
                if (IsLevelAsset(entry.Type) || entry.Type == "SubWorldData")
                {
                    allLevelEntries.Add(entry);
                    totalCount++;
                }
            }

            App.Logger.Log($"Found {totalCount} level/SubWorldData entries to export");

            FrostyTaskWindow.Show("Exporting Level Data", "", (task) =>
            {
                int processedCount = 0;

                foreach (EbxAssetEntry entry in allLevelEntries)
                {
                    task.Update($"Exporting: {entry.Name}", (processedCount / (double)totalCount) * 100.0);

                    try
                    {
                        // Create output path preserving folder structure
                        string relativePath = entry.Name;
                        string outputPath = Path.Combine(outputDirectory, relativePath + ".xml");
                        string outputDir = Path.GetDirectoryName(outputPath);

                        // Create directory if needed
                        if (!Directory.Exists(outputDir))
                            Directory.CreateDirectory(outputDir);

                        // Export the level/layer data
                        bool success = ExportSingleLevel(entry, outputPath);
                        
                        if (success)
                            successCount++;
                        else
                            failedCount++;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.LogError($"Error exporting level {entry.Name}: {ex.Message}");
                        failedCount++;
                    }

                    processedCount++;
                }

                task.Update("Export complete", 100.0);
            });

            App.Logger.Log($"Level data export complete: {successCount} succeeded, {failedCount} failed");
        }

        /// <summary>
        /// Export a single level to XML
        /// </summary>
        private bool ExportSingleLevel(EbxAssetEntry entry, string outputPath)
        {
            try
            {
                EbxAsset asset = App.AssetManager.GetEbx(entry);
                dynamic levelData = asset.RootObject;

                XDocument xml = new XDocument();
                XElement root = new XElement("Level",
                    new XAttribute("Name", entry.Filename),
                    new XAttribute("Type", entry.Type),
                    new XAttribute("Path", entry.Name)
                );

                // Extract basic level info
                try
                {
                    if (levelData.Name != null)
                        root.Add(new XAttribute("DisplayName", levelData.Name.ToString()));
                }
                catch { }

                // Extract entities/objects in the level
                XElement entitiesElement = new XElement("Entities");
                
                try
                {
                    // Try to extract objects - property names vary by game/asset type
                    ExtractEntities(levelData, entitiesElement, asset);
                }
                catch (Exception ex)
                {
                    App.Logger.LogWarning($"Could not extract entities from {entry.Name}: {ex.Message}");
                }

                root.Add(entitiesElement);

                // Extract bundles/references
                XElement bundlesElement = new XElement("Bundles");
                foreach (int bundleId in entry.EnumerateBundles())
                {
                    var bundle = App.AssetManager.GetBundleEntry(bundleId);
                    if (bundle != null)
                    {
                        bundlesElement.Add(new XElement("Bundle",
                            new XAttribute("Id", bundleId),
                            new XAttribute("Name", bundle.Name)
                        ));
                    }
                }
                root.Add(bundlesElement);

                xml.Add(root);
                xml.Save(outputPath);

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.LogError($"Failed to export level {entry.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extract entities/objects from level data
        /// </summary>
        private void ExtractEntities(dynamic levelData, XElement entitiesElement, EbxAsset asset)
        {
            // Try different common property names for entity containers
            string[] entityContainerNames = 
            { 
                "Objects", "Entities", "Components", "Children", 
                "BlueprintTransform", "ObjectVariations", "Instances",
                "MemberDatas" // StaticModelGroupEntityData uses this
            };

            foreach (var propertyName in entityContainerNames)
            {
                try
                {
                    var levelType = levelData.GetType();
                    var property = levelType.GetProperty(propertyName);
                    
                    if (property != null)
                    {
                        var collection = property.GetValue(levelData);
                        if (collection != null)
                        {
                            ExtractEntityCollection(collection, entitiesElement, propertyName);
                        }
                    }
                }
                catch { }
            }

            // Also check asset Objects collection (EBX internal structure)
            if (asset.Objects != null)
            {
                int objectIndex = 0;
                foreach (var obj in asset.Objects)
                {
                    try
                    {
                        // Skip the root object (already processed)
                        if (objectIndex == 0)
                        {
                            objectIndex++;
                            continue;
                        }

                        var objType = obj.GetType();
                        string typeName = objType.Name;

                        // Handle StaticModelGroupEntityData specially - it contains MemberDatas collection
                        if (typeName == "StaticModelGroupEntityData")
                        {
                            var memberDatasProp = objType.GetProperty("MemberDatas");
                            if (memberDatasProp != null)
                            {
                                var memberDatas = memberDatasProp.GetValue(obj);
                                if (memberDatas != null)
                                {
                                    ExtractEntityCollection(memberDatas, entitiesElement, "MemberDatas");
                                }
                            }
                        }
                        else
                        {
                            XElement entityElement = ExtractSingleEntity(obj, objectIndex);
                            if (entityElement != null)
                                entitiesElement.Add(entityElement);
                        }

                        objectIndex++;
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Extract a collection of entities
        /// </summary>
        private void ExtractEntityCollection(dynamic collection, XElement parentElement, string collectionName)
        {
            try
            {
                int index = 0;
                foreach (var item in collection)
                {
                    try
                    {
                        XElement entityElement = ExtractSingleEntity(item, index);
                        if (entityElement != null)
                        {
                            entityElement.Add(new XAttribute("Collection", collectionName));
                            parentElement.Add(entityElement);
                        }
                    }
                    catch { }
                    index++;
                }
            }
            catch { }
        }

        /// <summary>
        /// Extract a single entity with transform and mesh references
        /// </summary>
        private XElement ExtractSingleEntity(dynamic entity, int index)
        {
            try
            {
                var entityType = entity.GetType();
                string typeName = entityType.Name;

                XElement element = new XElement("Entity",
                    new XAttribute("Index", index),
                    new XAttribute("Type", typeName)
                );

                // Try to extract name
                try
                {
                    var nameProperty = entityType.GetProperty("Name");
                    if (nameProperty != null)
                    {
                        var name = nameProperty.GetValue(entity);
                        if (name != null)
                            element.Add(new XAttribute("Name", name.ToString()));
                    }
                }
                catch { }

                // Try to extract transform
                ExtractTransform(entity, entityType, element);

                // Try to extract mesh reference
                ExtractMeshReference(entity, entityType, element);

                // Try to extract blueprint reference
                ExtractBlueprintReference(entity, entityType, element);

                // CRITICAL: Extract InstanceTransforms for StaticModelGroupMemberData
                if (typeName == "StaticModelGroupMemberData")
                {
                    ExtractInstanceTransforms(entity, entityType, element);
                }

                return element;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extract InstanceTransforms array from StaticModelGroupMemberData
        /// This contains the actual world positions for each placed instance of the mesh
        /// </summary>
        private void ExtractInstanceTransforms(dynamic entity, Type entityType, XElement element)
        {
            try
            {
                // DEBUG: Log all properties to see what's available
                var allProps = entityType.GetProperties();
                var propNames = string.Join(", ", allProps.Select(p => p.Name));
                App.Logger.Log($"StaticModelGroupMemberData properties: {propNames}");
                
                var instanceTransformsProp = entityType.GetProperty("InstanceTransforms");
                if (instanceTransformsProp == null)
                {
                    App.Logger.LogWarning("InstanceTransforms property not found, trying alternatives...");
                    
                    // Try alternative names
                    string[] alternatives = { "Transforms", "Instances", "InstanceObjectTransforms", "MemberTransforms" };
                    foreach (var altName in alternatives)
                    {
                        instanceTransformsProp = entityType.GetProperty(altName);
                        if (instanceTransformsProp != null)
                        {
                            App.Logger.Log($"Found property: {altName}");
                            break;
                        }
                    }
                }
                
                if (instanceTransformsProp != null)
                {
                    App.Logger.Log($"Found InstanceTransforms property, type: {instanceTransformsProp.PropertyType.Name}");
                    var instanceTransforms = instanceTransformsProp.GetValue(entity);
                    
                    if (instanceTransforms == null)
                    {
                        App.Logger.LogWarning("InstanceTransforms property returned null");
                        return;
                    }
                    
                    App.Logger.Log($"InstanceTransforms has value, type: {instanceTransforms.GetType().Name}");
                    
                    XElement instanceTransformsElement = new XElement("InstanceTransforms");
                    
                    int instanceIndex = 0;
                    foreach (var transform in instanceTransforms)
                    {
                        try
                        {
                            var transformType = transform.GetType();
                            XElement instanceElement = new XElement("Instance");
                            
                            // Extract LinearTransform (contains right, up, forward, trans)
                            var linearTransformProp = transformType.GetProperty("Transform");
                            if (linearTransformProp == null)
                                linearTransformProp = transformType.GetProperty("LinearTransform");
                            
                            if (linearTransformProp != null)
                            {
                                var linearTransform = linearTransformProp.GetValue(transform);
                                if (linearTransform != null)
                                {
                                    var ltType = linearTransform.GetType();
                                    
                                    // Extract position (trans)
                                    var transProp = ltType.GetProperty("trans");
                                    if (transProp != null)
                                    {
                                        var trans = transProp.GetValue(linearTransform);
                                        if (trans != null)
                                        {
                                            var transType = trans.GetType();
                                            var xProp = transType.GetProperty("x");
                                            var yProp = transType.GetProperty("y");
                                            var zProp = transType.GetProperty("z");
                                            
                                            if (xProp != null && yProp != null && zProp != null)
                                            {
                                                instanceElement.Add(new XElement("Position",
                                                    new XAttribute("X", xProp.GetValue(trans)),
                                                    new XAttribute("Y", yProp.GetValue(trans)),
                                                    new XAttribute("Z", zProp.GetValue(trans))
                                                ));
                                            }
                                        }
                                    }
                                    
                                    // Extract rotation matrix (right, up, forward)
                                    XElement rotationElement = new XElement("Rotation");
                                    
                                    string[] vectorNames = { "right", "up", "forward" };
                                    foreach (var vectorName in vectorNames)
                                    {
                                        var vectorProp = ltType.GetProperty(vectorName);
                                        if (vectorProp != null)
                                        {
                                            var vector = vectorProp.GetValue(linearTransform);
                                            if (vector != null)
                                            {
                                                var vecType = vector.GetType();
                                                var xProp = vecType.GetProperty("x");
                                                var yProp = vecType.GetProperty("y");
                                                var zProp = vecType.GetProperty("z");
                                                
                                                if (xProp != null && yProp != null && zProp != null)
                                                {
                                                    string elementName = char.ToUpper(vectorName[0]) + vectorName.Substring(1);
                                                    rotationElement.Add(new XElement(elementName,
                                                        new XAttribute("X", xProp.GetValue(vector)),
                                                        new XAttribute("Y", yProp.GetValue(vector)),
                                                        new XAttribute("Z", zProp.GetValue(vector))
                                                    ));
                                                }
                                            }
                                        }
                                    }
                                    
                                    if (rotationElement.HasElements)
                                        instanceElement.Add(rotationElement);
                                }
                            }
                            
                            if (instanceElement.HasElements)
                            {
                                instanceTransformsElement.Add(instanceElement);
                                instanceIndex++;
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger.LogError($"Error extracting instance {instanceIndex}: {ex.Message}");
                        }
                    }
                    
                    App.Logger.Log($"Extracted {instanceIndex} instance transforms");
                    
                    if (instanceTransformsElement.HasElements)
                        element.Add(instanceTransformsElement);
                }
                else
                {
                    App.Logger.LogWarning("Could not find InstanceTransforms or any alternative property");
                }
            }
            catch (Exception ex)
            {
                App.Logger.LogError($"ExtractInstanceTransforms error: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract transform data (position, rotation, scale)
        /// </summary>
        private void ExtractTransform(dynamic entity, Type entityType, XElement element)
        {
            try
            {
                // Try common transform property names
                string[] transformNames = { "Transform", "LinearTransform", "Position", "Location" };
                
                foreach (var transformName in transformNames)
                {
                    var property = entityType.GetProperty(transformName);
                    if (property != null)
                    {
                        var transform = property.GetValue(entity);
                        if (transform != null)
                        {
                            XElement transformElement = new XElement("Transform");
                            
                            try
                            {
                                // LinearTransform has: right, up, forward, trans
                                var transformType = transform.GetType();
                                
                                var transProperty = transformType.GetProperty("trans");
                                if (transProperty != null)
                                {
                                    var trans = transProperty.GetValue(transform);
                                    transformElement.Add(new XElement("Position",
                                        new XAttribute("X", GetProperty(trans, "x", 0f)),
                                        new XAttribute("Y", GetProperty(trans, "y", 0f)),
                                        new XAttribute("Z", GetProperty(trans, "z", 0f))
                                    ));
                                }

                                var rightProperty = transformType.GetProperty("right");
                                var upProperty = transformType.GetProperty("up");
                                var forwardProperty = transformType.GetProperty("forward");

                                if (rightProperty != null && upProperty != null && forwardProperty != null)
                                {
                                    var right = rightProperty.GetValue(transform);
                                    var up = upProperty.GetValue(transform);
                                    var forward = forwardProperty.GetValue(transform);

                                    transformElement.Add(new XElement("Rotation",
                                        new XElement("Right",
                                            new XAttribute("X", GetProperty(right, "x", 0f)),
                                            new XAttribute("Y", GetProperty(right, "y", 0f)),
                                            new XAttribute("Z", GetProperty(right, "z", 0f))
                                        ),
                                        new XElement("Up",
                                            new XAttribute("X", GetProperty(up, "x", 0f)),
                                            new XAttribute("Y", GetProperty(up, "y", 0f)),
                                            new XAttribute("Z", GetProperty(up, "z", 0f))
                                        ),
                                        new XElement("Forward",
                                            new XAttribute("X", GetProperty(forward, "x", 0f)),
                                            new XAttribute("Y", GetProperty(forward, "y", 0f)),
                                            new XAttribute("Z", GetProperty(forward, "z", 0f))
                                        )
                                    ));
                                }
                            }
                            catch { }

                            if (transformElement.HasElements)
                                element.Add(transformElement);

                            break;
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Extract mesh reference
        /// </summary>
        private void ExtractMeshReference(dynamic entity, Type entityType, XElement element)
        {
            try
            {
                string[] meshPropertyNames = { "Mesh", "MeshAsset", "MeshVariation" };
                
                foreach (var propName in meshPropertyNames)
                {
                    var property = entityType.GetProperty(propName);
                    if (property != null)
                    {
                        var meshRef = property.GetValue(entity);
                        if (meshRef != null && meshRef is PointerRef)
                        {
                            PointerRef pointerRef = (PointerRef)meshRef;
                            if (pointerRef.Type == PointerRefType.External)
                            {
                                var meshEntry = App.AssetManager.GetEbxEntry(pointerRef.External.FileGuid);
                                if (meshEntry != null)
                                {
                                    element.Add(new XElement("MeshReference",
                                        new XAttribute("Path", meshEntry.Name),
                                        new XAttribute("Type", meshEntry.Type),
                                        new XAttribute("Guid", meshEntry.Guid)
                                    ));
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Extract blueprint/prefab reference
        /// </summary>
        private void ExtractBlueprintReference(dynamic entity, Type entityType, XElement element)
        {
            try
            {
                string[] blueprintPropertyNames = { "Blueprint", "Prefab", "ObjectBlueprint" };
                
                foreach (var propName in blueprintPropertyNames)
                {
                    var property = entityType.GetProperty(propName);
                    if (property != null)
                    {
                        var blueprintRef = property.GetValue(entity);
                        if (blueprintRef != null && blueprintRef is PointerRef)
                        {
                            PointerRef pointerRef = (PointerRef)blueprintRef;
                            if (pointerRef.Type == PointerRefType.External)
                            {
                                var blueprintEntry = App.AssetManager.GetEbxEntry(pointerRef.External.FileGuid);
                                if (blueprintEntry != null)
                                {
                                    element.Add(new XElement("BlueprintReference",
                                        new XAttribute("Path", blueprintEntry.Name),
                                        new XAttribute("Type", blueprintEntry.Type),
                                        new XAttribute("Guid", blueprintEntry.Guid)
                                    ));
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Helper to safely get property value
        /// </summary>
        private float GetProperty(dynamic obj, string propertyName, float defaultValue)
        {
            try
            {
                var type = obj.GetType();
                var property = type.GetProperty(propertyName);
                if (property != null)
                {
                    var value = property.GetValue(obj);
                    if (value != null)
                        return Convert.ToSingle(value);
                }
            }
            catch { }
            
            return defaultValue;
        }

        /// <summary>
        /// Determine if an asset type is a level/world
        /// </summary>
        /// <summary>
        /// Find all layer bundles related to a main level (like _Lighting, _Logic, _VFX, etc.)
        /// Battlefront 2 uses: _Lighting, _Logic, _VFX, _Occluders, _Enlighten, NOT _Art
        /// </summary>
        private List<EbxAssetEntry> FindRelatedLayerBundles(EbxAssetEntry mainLevelEntry)
        {
            List<EbxAssetEntry> layers = new List<EbxAssetEntry>();
            
            // Battlefront 2's actual layer suffixes
            string[] layerSuffixes = { "_Lighting", "_Logic", "_VFX", "_Occluders", "_Enlighten", "_Vfx" };
            
            string baseName = mainLevelEntry.Name;
            
            foreach (string suffix in layerSuffixes)
            {
                // Look for entries with the same base name + suffix
                string layerName = baseName + suffix;
                
                foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx())
                {
                    if (entry.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase) && 
                        entry.Type == "SubWorldData")
                    {
                        layers.Add(entry);
                        break;
                    }
                }
            }
            
            return layers;
        }

        private bool IsLevelAsset(string type)
        {
            // Common level/world asset types across Frostbite games
            return type == "WorldData" ||
                   type == "LevelData" ||
                   type == "SubWorldData" ||
                   type == "WorldPartData" ||
                   type == "PersistentLevelData" ||
                   type == "LevelDescription" ||
                   type == "GameModeConfiguration" ||
                   type.Contains("Level") && type.Contains("Data");
        }
    }
}
