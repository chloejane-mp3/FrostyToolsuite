using System;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk.Managers;

namespace MaterialMappingPlugin
{
    // === Batch export: Tools > Material Mapping > Export All Material Mappings ===
    public class MaterialMappingMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "Material Mapping";
        public override string MenuItemName => "Export All Material Mappings";
        public override ImageSource Icon => imageSource;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select output directory for material mappings";
                folderDialog.ShowNewFolderButton = true;
                
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string outputDir = folderDialog.SelectedPath;
                    MaterialMappingExporter exporter = new MaterialMappingExporter();
                    exporter.ExportAllMeshMaterialMappings(outputDir);
                }
            }
        });
    }

    // === Single selected mesh export: Tools > Material Mapping > Export Selected Mesh ===
    public class MaterialMappingSelectedMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource2 = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "Material Mapping";
        public override string MenuItemName => "Export Selected Mesh";
        public override ImageSource Icon => imageSource2;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            // Tools menu doesn't pass the selected asset via 'o'.
            // Ask the user to paste the asset path (they can copy it from Data Explorer).
            string assetPath = null;
            
            // Simple input dialog using a Form
            using (var inputForm = new System.Windows.Forms.Form())
            {
                inputForm.Text = "Export Selected Mesh Material Mapping";
                inputForm.Size = new System.Drawing.Size(520, 160);
                inputForm.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                inputForm.TopMost = true;

                var label = new System.Windows.Forms.Label
                {
                    Text = "Paste the asset path from Data Explorer:\n(Right-click the mesh > Copy Path, or just type it)",
                    Location = new System.Drawing.Point(12, 12),
                    Size = new System.Drawing.Size(494, 40)
                };

                var textBox = new System.Windows.Forms.TextBox
                {
                    Location = new System.Drawing.Point(12, 58),
                    Size = new System.Drawing.Size(494, 24),
                    Font = new System.Drawing.Font("Consolas", 10)
                };

                var okButton = new System.Windows.Forms.Button
                {
                    Text = "Export",
                    Location = new System.Drawing.Point(380, 96),
                    Size = new System.Drawing.Size(80, 28),
                    DialogResult = System.Windows.Forms.DialogResult.OK
                };

                var cancelButton = new System.Windows.Forms.Button
                {
                    Text = "Cancel",
                    Location = new System.Drawing.Point(472, 96),
                    Size = new System.Drawing.Size(34, 28),
                    DialogResult = System.Windows.Forms.DialogResult.Cancel
                };

                inputForm.Controls.AddRange(new System.Windows.Forms.Control[] { label, textBox, okButton, cancelButton });
                inputForm.AcceptButton = okButton;
                inputForm.CancelButton = cancelButton;

                if (inputForm.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    assetPath = textBox.Text.Trim().Trim('"');
                }
            }

            if (string.IsNullOrWhiteSpace(assetPath))
                return;

            // Strip file extension if pasted with one
            if (assetPath.EndsWith("_mesh", StringComparison.OrdinalIgnoreCase) == false)
            {
                // Try stripping common extensions
                string[] exts = { ".fbx", ".obj", ".mesh" };
                foreach (var ext in exts)
                {
                    if (assetPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                        assetPath = assetPath.Substring(0, assetPath.Length - ext.Length);
                }
            }

            // Look up the asset
            EbxAssetEntry selectedEntry = null;
            try
            {
                selectedEntry = App.AssetManager.GetEbxEntry(assetPath);
            }
            catch { }

            if (selectedEntry == null)
            {
                MessageBox.Show(
                    $"Could not find asset:\n\n{assetPath}\n\nMake sure you copied the full path from Data Explorer.",
                    "Material Mapping Exporter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (selectedEntry.Type != "MeshAsset" && selectedEntry.Type != "RigidMeshAsset" && selectedEntry.Type != "SkinnedMeshAsset" && selectedEntry.Type != "CompositeMeshAsset")
            {
                MessageBox.Show(
                    $"Selected asset is not a mesh type.\n\nType: {selectedEntry.Type}\nName: {selectedEntry.Name}\n\nPlease use a RigidMeshAsset, SkinnedMeshAsset, MeshAsset, or CompositeMeshAsset.",
                    "Material Mapping Exporter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = $"Select output directory for: {selectedEntry.Name}";
                folderDialog.ShowNewFolderButton = true;
                
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string outputDir = folderDialog.SelectedPath;
                    MaterialMappingExporter exporter = new MaterialMappingExporter();
                    exporter.ExportSelectedMeshMaterialMapping(selectedEntry, outputDir);
                }
            }
        });
    }

    // === Texture batch export: Tools > Material Mapping > Export All Textures ===
    public class TextureExportMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource3 = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "Material Mapping";
        public override string MenuItemName => "Export All Textures";
        public override ImageSource Icon => imageSource3;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select output directory for textures";
                folderDialog.ShowNewFolderButton = true;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string outputDir = folderDialog.SelectedPath;
                    MaterialMappingExporter exporter = new MaterialMappingExporter();
                    exporter.ExportAllTextures(outputDir, "tga");
                }
            }
        });
    }

    // === FBX batch export: Tools > Material Mapping > Export All Meshes (FBX) ===
    public class MeshFbxExportMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource4 = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "Material Mapping";
        public override string MenuItemName => "Export All Meshes (FBX)";
        public override ImageSource Icon => imageSource4;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            var result = MessageBox.Show(
                "Export all meshes as FBX for Unreal Engine?\n\n" +
                "Settings:\n" +
                "• Scale: 100x (Centimeters for Unreal)\n" +
                "• LOD: Highest only (LOD0)\n" +
                "• Parts: Kept separate for material mapping\n\n" +
                "This may take a VERY long time. Continue?",
                "Batch FBX Export",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                using (var folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = "Select output directory for FBX files";
                    folderDialog.ShowNewFolderButton = true;

                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        string outputDir = folderDialog.SelectedPath;
                        MeshBatchExporter fbxExporter = new MeshBatchExporter();
                        fbxExporter.ExportAllMeshes(outputDir, unrealScale: true);

                        MessageBox.Show(
                            "FBX export complete!\n\n" +
                            "Unreal Engine Import Settings:\n" +
                            "• Import mesh scale: 1.0 (already scaled 100x)\n" +
                            "• Materials: Use JSON mappings from earlier export\n" +
                            "• Each FBX part corresponds to a material slot",
                            "Export Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
        });
    }

    // === Level data export: Tools > Material Mapping > Export All Level Data ===
    public class LevelDataExportMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource5 = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "Material Mapping";
        public override string MenuItemName => "Export All Level Data";
        public override ImageSource Icon => imageSource5;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            var result = MessageBox.Show(
                "Export all level/world data to XML?\n\n" +
                "This will export:\n" +
                "• Entity placements and transforms\n" +
                "• Mesh references and blueprints\n" +
                "• World structure and bundles\n\n" +
                "TIP: Click 'No' to run diagnostic report first\n" +
                "Click 'Yes' to start export\n" +
                "Click 'Cancel' to abort",
                "Level Data Export",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes || result == DialogResult.No)
            {
                using (var folderDialog = new FolderBrowserDialog())
                {
                    folderDialog.Description = result == DialogResult.Yes ? 
                        "Select output directory for level XML files" :
                        "Select output directory for diagnostic report";
                    folderDialog.ShowNewFolderButton = true;

                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        string outputDir = folderDialog.SelectedPath;
                        LevelDataExporter exporter = new LevelDataExporter();
                        
                        if (result == DialogResult.No)
                        {
                            // Run diagnostic
                            exporter.DiagnoseLayerBundles(outputDir);
                            MessageBox.Show(
                                "Diagnostic report generated!\n\n" +
                                "Check 'layer_diagnostic_report.txt' in the output folder.\n\n" +
                                "This report shows:\n" +
                                "• All SubWorldData entries\n" +
                                "• Entries containing '_Art'\n" +
                                "• Level groupings detected\n" +
                                "• Sample mesh reference counts",
                                "Diagnostic Complete",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                        else
                        {
                            // Run export
                            exporter.ExportAllLevelData(outputDir);
                            MessageBox.Show(
                                "Level data export complete!\n\n" +
                                "XML files contain:\n" +
                                "• Entity positions and rotations\n" +
                                "• References to meshes and blueprints\n" +
                                "• Bundle information",
                                "Export Complete",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                }
            }
        });
    }

    // === Diagnostic tool: Tools > Material Mapping > Diagnose Layer Bundles ===
    public class DiagnoseLayerBundlesMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource6 = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "Material Mapping";
        public override string MenuItemName => "Diagnose Layer Bundles";
        public override ImageSource Icon => imageSource6;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select output directory for diagnostic report";
                folderDialog.ShowNewFolderButton = true;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string outputDir = folderDialog.SelectedPath;
                    LevelDataExporter exporter = new LevelDataExporter();
                    exporter.DiagnoseLayerBundles(outputDir);

                    MessageBox.Show(
                        "Diagnostic report generated!\n\n" +
                        "Check 'layer_diagnostic_report.txt' in the output folder.\n\n" +
                        "This report shows:\n" +
                        "• All SubWorldData entries\n" +
                        "• Entries containing '_Art'\n" +
                        "• Level groupings detected\n" +
                        "• Sample mesh reference counts",
                        "Diagnostic Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        });
    }
}
