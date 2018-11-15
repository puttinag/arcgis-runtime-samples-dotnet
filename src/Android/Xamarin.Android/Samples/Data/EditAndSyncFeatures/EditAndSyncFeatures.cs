// Copyright 2018 Esri.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific
// language governing permissions and limitations under the License.

using Android.App;
using Android.OS;
using Android.Widget;
using ArcGISRuntime.Samples.Managers;
using Esri.ArcGISRuntime.ArcGISServices;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Offline;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Esri.ArcGISRuntime;

namespace ArcGISRuntime.Samples.EditAndSyncFeatures
{
    [Activity]
    [ArcGISRuntime.Samples.Shared.Attributes.OfflineData("3f1bbf0ec70b409a975f5c91f363fe7d")]
    [ArcGISRuntime.Samples.Shared.Attributes.Sample(
        "Edit and sync features",
        "Data",
        "This sample demonstrates how to synchronize offline edits with a feature service.",
        "1. Pan and zoom to the area you would like to download point features for, ensuring that all point features are within the rectangle.\n2. Tap the 'generate' button. This will start the process of generating the offline geodatabase.\n3. Tap on a point feature within the area of the generated geodatabase. Then tap on the screen (anywhere within the range of the local geodatabase) to move the point to that location.\n4. Tap the 'Sync Geodatabase' button to synchronize the changes back to the feature service.\n\n Note that the basemap for this sample is downloaded from ArcGIS Online automatically.")]
    public class EditAndSyncFeatures : Activity
    {
        // Enumeration to track which phase of the workflow the sample is in.
        private enum EditState
        {
            NotReady, // Geodatabase has not yet been generated.
            Editing, // A feature is in the process of being moved.
            Ready // The geodatabase is ready for synchronization or further edits.
        }

        // URL for a feature service that supports geodatabase generation.
        private Uri _featureServiceUri = new Uri("https://sampleserver6.arcgisonline.com/arcgis/rest/services/Sync/WildfireSync/FeatureServer");

        // Path to the geodatabase file on disk.
        private string _gdbPath;

        // Task to be used for generating the geodatabase.
        private GeodatabaseSyncTask _gdbSyncTask;

        // Flag to indicate which stage of the edit process the sample is in.
        private EditState _readyForEdits = EditState.NotReady;

        // Hold a reference to the generated geodatabase.
        private Geodatabase _resultGdb;

        // Mapview.
        private MapView myMapView;

        // Generate Button.
        private Button myGenerateButton;

        // Sync Button.
        private Button mySyncButton;

        // Progress bar.
        private ProgressBar myProgressBar;

        // Help label.
        private TextView myHelpLabel;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            Title = "Edit and Sync Features";

            // Create the UI, setup the control references and execute initialization.
            CreateLayout();
            Initialize();
        }

        private void CreateLayout()
        {
            // Create the layout.
            LinearLayout layout = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical
            };

            // Add the help label.
            myHelpLabel = new TextView(this) { Text = "1. Click 'Generate'" };
            layout.AddView(myHelpLabel);

            // Add the progress bar.
            myProgressBar = new ProgressBar(this)
            {
                Visibility = Android.Views.ViewStates.Gone
            };
            layout.AddView(myProgressBar);

            // Add the generate button.
            myGenerateButton = new Button(this)
            {
                Text = "Generate",
                Enabled = false
            };
            myGenerateButton.Click += GenerateButton_Clicked;
            layout.AddView(myGenerateButton);

            // Add the sync button.
            mySyncButton = new Button(this)
            {
                Text = "Synchronize"
            };
            mySyncButton.Click += SyncButton_Click;
            mySyncButton.Enabled = false;
            layout.AddView(mySyncButton);

            // Add the mapview.
            myMapView = new MapView(this);
            layout.AddView(myMapView);

            // Add the layout to the view.
            SetContentView(layout);
        }

        private async void Initialize()
        {
            // Create a tile cache and load it with the SanFrancisco streets tpk.
            TileCache tileCache = new TileCache(DataManager.GetDataFolder("3f1bbf0ec70b409a975f5c91f363fe7d", "SanFrancisco.tpk"));

            // Create the corresponding layer based on the tile cache.
            ArcGISTiledLayer tileLayer = new ArcGISTiledLayer(tileCache);

            // Create the basemap based on the tile cache.
            Basemap sfBasemap = new Basemap(tileLayer);

            // Create the map with the tile-based basemap.
            Map myMap = new Map(sfBasemap);

            // Assign the map to the MapView.
            myMapView.Map = myMap;

            // Create a new symbol for the extent graphic.
            SimpleLineSymbol lineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.Red, 2);

            // Create graphics overlay for the extent graphic and apply a renderer.
            GraphicsOverlay extentOverlay = new GraphicsOverlay
            {
                Renderer = new SimpleRenderer(lineSymbol)
            };

            // Add graphics overlay to the map view.
            myMapView.GraphicsOverlays.Add(extentOverlay);

            // Set up an event handler for when the viewpoint (extent) changes.
            myMapView.ViewpointChanged += MapViewExtentChanged;

            // Set up event handler for mapview taps.
            myMapView.GeoViewTapped += GeoViewTapped;

            try
            {
                // Create a task for generating a geodatabase (GeodatabaseSyncTask).
                _gdbSyncTask = await GeodatabaseSyncTask.CreateAsync(_featureServiceUri);

                // Add all graphics from the service to the map.
                foreach (IdInfo layer in _gdbSyncTask.ServiceInfo.LayerInfos)
                {
                    // Get the Uri for this particular layer.
                    Uri onlineTableUri = new Uri(_featureServiceUri + "/" + layer.Id);

                    // Create the ServiceFeatureTable.
                    ServiceFeatureTable onlineTable = new ServiceFeatureTable(onlineTableUri);

                    // Wait for the table to load.
                    await onlineTable.LoadAsync();

                    // Add the layer to the map's operational layers if load succeeds.
                    if (onlineTable.LoadStatus == LoadStatus.Loaded)
                    {
                        myMap.OperationalLayers.Add(new FeatureLayer(onlineTable));
                    }
                }

                // Update the graphic - in case user doesn't interact with the map.
                UpdateMapExtent();

                // Enable the generate button now that the sample is ready.
                myGenerateButton.Enabled = true;
            }
            catch (Exception e)
            {
                ShowStatusMessage(e.ToString());
            }
        }

        private async void GeoViewTapped(object sender, GeoViewInputEventArgs e)
        {
            // Disregard if not ready for edits.
            try
            {
                if (_readyForEdits == EditState.NotReady) { return; }

                // If an edit is in process, finish it.
                if (_readyForEdits == EditState.Editing)
                {
                    // Hold a list of any selected features.
                    List<Feature> selectedFeatures = new List<Feature>();

                    // Get all selected features then clear selection.
                    foreach (FeatureLayer layer in myMapView.Map.OperationalLayers)
                    {
                        // Get the selected features.
                        FeatureQueryResult layerFeatures = await layer.GetSelectedFeaturesAsync();

                        // FeatureQueryResult implements IEnumerable, so it can be treated as a collection of features.
                        selectedFeatures.AddRange(layerFeatures);

                        // Clear the selection.
                        layer.ClearSelection();
                    }

                    // Update all selected features' geometry.
                    foreach (Feature feature in selectedFeatures)
                    {
                        // Get a reference to the correct feature table for the feature.
                        GeodatabaseFeatureTable table = (GeodatabaseFeatureTable)feature.FeatureTable;

                        // Ensure the geometry type of the table is point.
                        if (table.GeometryType != GeometryType.Point)
                        {
                            continue;
                        }

                        // Set the new geometry.
                        feature.Geometry = e.Location;

                        try
                        {
                            // Update the feature in the table.
                            await table.UpdateFeatureAsync(feature);
                        }
                        catch (Esri.ArcGISRuntime.ArcGISException)
                        {
                            ShowStatusMessage("Feature must be within extent of geodatabase.");
                        }
                    }

                    // Update the edit state.
                    _readyForEdits = EditState.Ready;

                    // Enable the sync button.
                    mySyncButton.Enabled = true;

                    // Update the help label.
                    myHelpLabel.Text = "4. Click 'Synchronize' or edit more features";
                }
                // Otherwise, start an edit.
                else
                {
                    // Define a tolerance for use with identifying the feature.
                    double tolerance = 15 * myMapView.UnitsPerPixel;

                    // Define the selection envelope.
                    Envelope selectionEnvelope = new Envelope(e.Location.X - tolerance, e.Location.Y - tolerance, e.Location.X + tolerance, e.Location.Y + tolerance);

                    // Define query parameters for feature selection.
                    QueryParameters query = new QueryParameters()
                    {
                        Geometry = selectionEnvelope
                    };

                    // Select the feature in all applicable tables.
                    foreach (FeatureLayer layer in myMapView.Map.OperationalLayers)
                    {
                        await layer.SelectFeaturesAsync(query, SelectionMode.New);
                    }

                    // Set the edit state.
                    _readyForEdits = EditState.Editing;

                    // Update the help label.
                    myHelpLabel.Text = "3. Tap on the map to move the point";
                }
            }
            catch (Exception ex)
            {
                ShowStatusMessage(ex.ToString());
            }
        }

        private void UpdateMapExtent()
        {
            // Return if mapview is null.
            if (myMapView == null) { return; }

            // Get the new viewpoint.
            Viewpoint myViewPoint = myMapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry);

            // Return if viewpoint is null.
            if (myViewPoint == null) { return; }

            // Get the updated extent for the new viewpoint.
            Envelope extent = myViewPoint.TargetGeometry as Envelope;

            // Return if extent is null.
            if (extent == null) { return; }

            // Create an envelope that is a bit smaller than the extent.
            EnvelopeBuilder envelopeBldr = new EnvelopeBuilder(extent);
            envelopeBldr.Expand(0.80);

            // Get the (only) graphics overlay in the map view.
            GraphicsOverlay extentOverlay = myMapView.GraphicsOverlays.FirstOrDefault();

            // Return if the extent overlay is null.
            if (extentOverlay == null) { return; }

            // Get the extent graphic.
            Graphic extentGraphic = extentOverlay.Graphics.FirstOrDefault();

            // Create the extent graphic and add it to the overlay if it doesn't exist.
            if (extentGraphic == null)
            {
                extentGraphic = new Graphic(envelopeBldr.ToGeometry());
                extentOverlay.Graphics.Add(extentGraphic);
            }
            else
            {
                // Otherwise, update the graphic's geometry.
                extentGraphic.Geometry = envelopeBldr.ToGeometry();
            }
        }

        private async Task StartGeodatabaseGeneration()
        {
            // Update geodatabase path.
            _gdbPath = $"{Path.GetTempFileName()}.geodatabase";

            // Create a task for generating a geodatabase (GeodatabaseSyncTask).
            _gdbSyncTask = await GeodatabaseSyncTask.CreateAsync(_featureServiceUri);

            // Get the (only) graphic in the map view.
            Graphic redPreviewBox = myMapView.GraphicsOverlays.First().Graphics.First();

            // Get the current extent of the red preview box.
            Envelope extent = redPreviewBox.Geometry as Envelope;

            // Get the default parameters for the generate geodatabase task.
            GenerateGeodatabaseParameters generateParams = await _gdbSyncTask.CreateDefaultGenerateGeodatabaseParametersAsync(extent);

            // Create a generate geodatabase job.
            GenerateGeodatabaseJob generateGdbJob = _gdbSyncTask.GenerateGeodatabase(generateParams, _gdbPath);

            // Handle the progress changed event with an inline (lambda) function to show the progress bar.
            generateGdbJob.ProgressChanged += (sender, e) =>
            {
                // Get the job.
                GenerateGeodatabaseJob job = (GenerateGeodatabaseJob)sender;

                // Update the progress bar.
                UpdateProgressBar(job.Progress);
            };

            // Show the progress bar.
            myProgressBar.Visibility = Android.Views.ViewStates.Visible;

            // Start the job.
            generateGdbJob.Start();

            // Wait for the result.
            _resultGdb = await generateGdbJob.GetResultAsync();

            // Hide the progress bar.
            myProgressBar.Visibility = Android.Views.ViewStates.Gone;

            // Do the rest of the work.
            HandleGenerationCompleted(generateGdbJob);
        }

        private void HandleGenerationCompleted(GenerateGeodatabaseJob job)
        {
            JobStatus status = job.Status;

            // If the job completed successfully, add the geodatabase data to the map.
            if (status == JobStatus.Succeeded)
            {
                // Clear out the existing layers.
                myMapView.Map.OperationalLayers.Clear();

                // Loop through all feature tables in the geodatabase and add a new layer to the map.
                foreach (GeodatabaseFeatureTable table in _resultGdb.GeodatabaseFeatureTables)
                {
                    // Create a new feature layer for the table.
                    FeatureLayer layer = new FeatureLayer(table);

                    // Add the new layer to the map.
                    myMapView.Map.OperationalLayers.Add(layer);
                }

                // Enable editing features.
                _readyForEdits = EditState.Ready;

                // Update the help label.
                myHelpLabel.Text = "2. Tap a point feature to select";
            }

            // See if the job failed.
            if (status == JobStatus.Failed)
            {
                // Create a message to show the user.
                string message = "Generate geodatabase job failed";

                // Show an error message (if there is one).
                if (job.Error != null)
                {
                    message += ": " + job.Error.Message;
                }
                else
                {
                    // If no error, show messages from the job.
                    foreach (JobMessage m in job.Messages)
                    {
                        // Get the text from the JobMessage and add it to the output string.
                        message += "\n" + m.Message;
                    }
                }

                // Show the message.
                ShowStatusMessage(message);
            }
        }

        private void HandleSyncCompleted(SyncGeodatabaseJob job)
        {
            JobStatus status = job.Status;

            // Tell the user about job completion.
            if (status == JobStatus.Succeeded)
            {
                ShowStatusMessage("Sync task completed");
            }

            // See if the job failed.
            if (status == JobStatus.Failed)
            {
                // Create a message to show the user.
                string message = "Sync geodatabase job failed";

                // Show an error message (if there is one).
                if (job.Error != null)
                {
                    message += ": " + job.Error.Message;
                }
                else
                {
                    // If no error, show messages from the job.
                    foreach (JobMessage m in job.Messages)
                    {
                        // Get the text from the JobMessage and add it to the output string.
                        message += "\n" + m.Message;
                    }
                }

                // Show the message.
                ShowStatusMessage(message);
            }
        }

        private async Task SyncGeodatabase()
        {
            // Return if not ready.
            if (_readyForEdits != EditState.Ready) { return; }

            // Disable the sync button.
            mySyncButton.Enabled = false;

            // Create parameters for the sync task.
            SyncGeodatabaseParameters parameters = new SyncGeodatabaseParameters()
            {
                GeodatabaseSyncDirection = SyncDirection.Bidirectional,
                RollbackOnFailure = false
            };

            // Get the layer Id for each feature table in the geodatabase, then add to the sync job.
            foreach (GeodatabaseFeatureTable table in _resultGdb.GeodatabaseFeatureTables)
            {
                // Get the ID for the layer.
                long id = table.ServiceLayerId;

                // Create the SyncLayerOption.
                SyncLayerOption option = new SyncLayerOption(id);

                // Add the option.
                parameters.LayerOptions.Add(option);
            }

            // Create job.
            SyncGeodatabaseJob job = _gdbSyncTask.SyncGeodatabase(parameters, _resultGdb);

            // Subscribe to progress updates.
            job.ProgressChanged += (o, e) =>
            {
                // Update the progress bar.
                UpdateProgressBar(job.Progress);
            };

            // Show the progress bar.
            myProgressBar.Visibility = Android.Views.ViewStates.Visible;

            // Start the sync.
            job.Start();

            // Wait for the job to finish.
            await job.GetResultAsync();

            // Hide the progress bar.
            myProgressBar.Visibility = Android.Views.ViewStates.Gone;

            // Do the rest of the work.
            HandleSyncCompleted(job);

            // Re-enable the sync button.
            mySyncButton.Enabled = true;
        }

        private void ShowStatusMessage(string message)
        {
            // Display the message to the user.
            AlertDialog.Builder builder = new AlertDialog.Builder(this);
            builder.SetMessage(message).SetTitle("Alert").Show();
        }

        private async void GenerateButton_Clicked(object sender, EventArgs e)
        {
            // Disable the generate button.
            try
            {
                myGenerateButton.Enabled = false;

                // Call the geodatabase generation method.
                await StartGeodatabaseGeneration();
            }
            catch (Exception ex)
            {
                ShowStatusMessage(ex.ToString());
            }
        }

        private void MapViewExtentChanged(object sender, EventArgs e)
        {
            // Call the map extent update method.
            UpdateMapExtent();
        }

        private void UpdateProgressBar(int progress)
        {
            // Due to the nature of the threading implementation,
            //     the dispatcher needs to be used to interact with the UI.
            // The dispatcher takes an Action, provided here as a lambda function.
            RunOnUiThread(() =>
            {
                // Update the progress bar value.
                myProgressBar.Progress = progress;
            });
        }

        private async void SyncButton_Click(object sender, EventArgs e)
        {
            try
            {
                await SyncGeodatabase();
            }
            catch (Exception ex)
            {
                ShowStatusMessage(ex.ToString());
            }
        }
    }
}