using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using MeshSetPlugin;
using MeshSetPlugin.Resources;
using MeshSetPlugin.Fbx;

namespace MaterialMappingPlugin
{
    /// <summary>
    /// Handles batch export of meshes to FBX format for use in Unreal Engine
    /// </summary>
    public class MeshBatchExporter
    {
        /// <summary>
        /// Export all meshes in the game to FBX format
        /// </summary>
        /// <param name="outputDirectory">Root directory for FBX exports</param>
        /// <param name="unrealScale">If true, scale by 100x for Unreal Engine (centimeters to meters)</param>
        public void ExportAllMeshes(string outputDirectory, bool unrealScale = true)
        {
            int totalCount = 0;
            int successCount = 0;
            int failedCount = 0;
            int skippedCount = 0;

            // Count total meshes
            List<EbxAssetEntry> meshEntries = new List<EbxAssetEntry>();
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx())
            {
                if (IsMeshAsset(entry.Type))
                {
                    meshEntries.Add(entry);
                    totalCount++;
                }
            }

            App.Logger.Log($"Found {totalCount} mesh assets to export");

            FrostyTaskWindow.Show("Exporting Meshes", "", (task) =>
            {
                int processedCount = 0;

                foreach (EbxAssetEntry entry in meshEntries)
                {
                    task.Update($"Exporting: {entry.Name}", (processedCount / (double)totalCount) * 100.0);

                    try
                    {
                        // Create output path preserving folder structure
                        string relativePath = entry.Name;
                        string outputPath = Path.Combine(outputDirectory, relativePath + ".fbx");
                        string outputDir = Path.GetDirectoryName(outputPath);

                        // Create directory if needed
                        if (!Directory.Exists(outputDir))
                            Directory.CreateDirectory(outputDir);

                        // Check if already exported
                        if (File.Exists(outputPath))
                        {
                            skippedCount++;
                            processedCount++;
                            continue;
                        }

                        // Export the mesh
                        bool success = ExportSingleMesh(entry, outputPath, unrealScale, task);
                        
                        if (success)
                            successCount++;
                        else
                            failedCount++;
                    }
                    catch (Exception ex)
                    {
                        App.Logger.LogError($"Error exporting mesh {entry.Name}: {ex.Message}");
                        failedCount++;
                    }

                    processedCount++;
                }

                task.Update("Export complete", 100.0);
            });

            App.Logger.Log($"Mesh export complete: {successCount} succeeded, {failedCount} failed, {skippedCount} skipped");
        }

        /// <summary>
        /// Export a single mesh asset to FBX
        /// </summary>
        private bool ExportSingleMesh(EbxAssetEntry entry, string outputPath, bool unrealScale, FrostyTaskWindow task)
        {
            try
            {
                // Load the mesh asset
                EbxAsset asset = App.AssetManager.GetEbx(entry);
                dynamic meshAsset = asset.RootObject;

                // Get mesh resource
                ulong resRid = meshAsset.MeshSetResource;
                ResAssetEntry resEntry = App.AssetManager.GetResEntry(resRid);
                MeshSet meshSet = App.AssetManager.GetResAs<MeshSet>(resEntry);

                if (meshSet == null || meshSet.Lods.Count == 0)
                {
                    App.Logger.LogWarning($"Mesh {entry.Name} has no LODs");
                    return false;
                }

                // Determine skeleton for skinned meshes
                string skeletonPath = "";
                if (entry.Type == "SkinnedMeshAsset")
                {
                    skeletonPath = GetSkeletonPath(asset);
                }

                // Create FBX exporter - pass the task window
                FBXExporter exporter = new FBXExporter(task);

                // Export settings for Unreal Engine:
                // - FBX 2013 (good compatibility)
                // - Centimeters if unrealScale=true (Unreal uses cm), Meters otherwise
                // - Flatten hierarchy to keep materials separate
                // - Export single LOD (LOD0 only)
                string fbxVersion = "2013";
                string units = unrealScale ? "Centimeters" : "Meters";
                bool flattenHierarchy = true;  // CRITICAL: keeps mesh parts separate for material mapping
                bool exportSingleLod = true;   // Only export LOD0
                string fileType = "binary";

                // Export the mesh
                exporter.ExportFBX(
                    meshAsset,
                    outputPath,
                    fbxVersion,
                    units,
                    flattenHierarchy,
                    exportSingleLod,
                    skeletonPath,
                    fileType,
                    meshSet
                );

                return true;
            }
            catch (Exception ex)
            {
                App.Logger.LogError($"Failed to export {entry.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Determine if an asset type is a mesh
        /// </summary>
        private bool IsMeshAsset(string type)
        {
            return type == "RigidMeshAsset" ||
                   type == "SkinnedMeshAsset" ||
                   type == "MeshAsset" ||
                   type == "CompositeMeshAsset";
        }

        /// <summary>
        /// Get skeleton path for a skinned mesh
        /// </summary>
        private string GetSkeletonPath(EbxAsset meshAsset)
        {
            try
            {
                // Try to find skeleton reference in the mesh asset
                dynamic mesh = meshAsset.RootObject;
                
                // Different games store skeleton differently
                // This is a simplified approach - may need game-specific logic
                if (mesh.Skeleton != null)
                {
                    PointerRef skeletonRef = mesh.Skeleton;
                    if (skeletonRef.Type == PointerRefType.External)
                    {
                        EbxAssetEntry skeletonEntry = App.AssetManager.GetEbxEntry(skeletonRef.External.FileGuid);
                        if (skeletonEntry != null)
                            return skeletonEntry.Name;
                    }
                }

                // Fallback: try to find skeleton based on naming convention
                // e.g., character mesh might reference character skeleton
                string meshPath = meshAsset.FileGuid.ToString();
                // Add game-specific skeleton finding logic here if needed

                return "";
            }
            catch
            {
                return "";
            }
        }
    }
}
