using System;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk.Managers;

namespace MaterialMappingPlugin
{
    // === Batch export: Tools > Batch Export > Export Material Mappings ===
    public class MaterialMappingMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => "Batch Export";
        public override string MenuItemName => "Export Material Mappings";
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

    // === Single selected mesh export: Tools > Export Selected Mesh Material Mapping ===
    public class MaterialMappingSelectedMenuExtension : MenuExtension
    {
        internal static ImageSource imageSource2 = new ImageSourceConverter()
            .ConvertFromString("pack://application:,,,/FrostyEditor;component/Images/Export.png") as ImageSource;

        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => null;
        public override string MenuItemName => "Export Selected Mesh Material Mapping";
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

            if (selectedEntry.Type != "MeshAsset" && selectedEntry.Type != "RigidMeshAsset" && selectedEntry.Type != "SkinnedMeshAsset")
            {
                MessageBox.Show(
                    $"Selected asset is not a mesh type.\n\nType: {selectedEntry.Type}\nName: {selectedEntry.Name}\n\nPlease use a RigidMeshAsset, SkinnedMeshAsset, or MeshAsset.",
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
}
