using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Services;
using System.Threading;
using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;

namespace DatabaseMigrationTool
{
    public partial class MainWindow : Window
    {
        private string ExportConnectionString => GetExportConnectionString();

        private string ImportConnectionString => GetImportConnectionString();

        private string SchemaConnectionString => GetSchemaConnectionString();

        private int ExportProviderIndex => ExportConnectionControl.ProviderIndex;
        private int ImportProviderIndex => ImportConnectionControl.ProviderIndex;
        private int SchemaProviderIndex => SchemaConnectionControl.ProviderIndex;

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize provider ComboBoxes
            var providers = DatabaseProviderFactory.GetSupportedProviders();
        }

        private void BrowseExportOutputDirectory(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Export Output Directory";
            dialog.UseDescriptionForTitle = true;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ExportOutputDirectoryTextBox.Text = dialog.SelectedPath;
            }
        }
        
        private void BrowseImportInputDirectory(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Import Input Directory";
            dialog.UseDescriptionForTitle = true;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ImportInputDirectoryTextBox.Text = dialog.SelectedPath;
            }
        }
        
        private void BrowseSchemaOutputDirectory(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Schema Script Output Directory";
            dialog.UseDescriptionForTitle = true;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SchemaScriptPathTextBox.Text = dialog.SelectedPath;
            }
        }
        
        private void BrowseTableCriteriaFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*";
            dialog.Title = "Select Table Criteria File";
            
            if (dialog.ShowDialog() == true)
            {
                ExportTableCriteriaFileTextBox.Text = dialog.FileName;
            }
        }

        private async void StartExport(object sender, RoutedEventArgs e)
        {
            // Validate inputs before showing progress UI
            if (string.IsNullOrWhiteSpace(ExportConnectionString))
            {                
                System.Windows.MessageBox.Show("Please enter connection information.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (string.IsNullOrWhiteSpace(ExportOutputDirectoryTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please select an output directory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Validate table names if specified
            if (!string.IsNullOrWhiteSpace(ExportTablesTextBox.Text))
            {
                string[] tableNames = ExportTablesTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tableNames.Length == 0)
                {
                    System.Windows.MessageBox.Show("Please enter valid table names or leave the field empty to export all tables.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Check for empty table names after trimming
                if (tableNames.Any(t => string.IsNullOrWhiteSpace(t)))
                {
                    System.Windows.MessageBox.Show("One or more table names are empty. Please enter valid table names.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            
            // Show progress UI
            ExportProgressGroupBox.Visibility = Visibility.Visible;
            ExportButton.IsEnabled = false;
            ExportProgressBar.IsIndeterminate = true;
            ExportProgressText.Text = "Preparing to export...";
            
            try
            {
                // Parse batch size
                if (string.IsNullOrWhiteSpace(ExportBatchSizeTextBox.Text) || !int.TryParse(ExportBatchSizeTextBox.Text, out int batchSize) || batchSize <= 0)
                {
                    System.Windows.MessageBox.Show("Batch size must be a positive number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get provider
                string providerName = GetSelectedProviderName(ExportProviderIndex);
                var provider = DatabaseProviderFactory.Create(providerName);
                
                // Create connection - with additional logging to diagnose any issues
                StatusTextBlock.Text = "Creating connection to source database...";
                
                // Set logger to capture detailed connection information
                provider.SetLogger(message => {
                    Dispatcher.Invoke(() => {
                        StatusTextBlock.Text = message;
                    });
                });
                
                var connection = provider.CreateConnection(ExportConnectionString);
                StatusTextBlock.Text = "Connection created successfully, preparing to export...";
                
                // Create progress handler to update status
                Action<string> updateStatus = (message) => 
                {
                    Dispatcher.Invoke(() => StatusTextBlock.Text = message);
                };

                // Create progress reporter
                ProgressReportHandler progressReporter = (progress) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Update progress bar
                        ExportProgressBar.IsIndeterminate = progress.IsIndeterminate;
                        if (!progress.IsIndeterminate)
                        {
                            ExportProgressBar.Value = progress.Current;
                            ExportProgressBar.Maximum = progress.Total;
                        }
                        
                        // Update status text
                        ExportProgressText.Text = progress.Message;
                        StatusTextBlock.Text = progress.Message;
                    });
                };

                // Run export in background
                await Task.Run(async () =>
                {
                    try
                    {
                        // Get table criteria if specified
                        Dictionary<string, string>? tableCriteria = null;
                        string criteriaPath = Dispatcher.Invoke(() => ExportTableCriteriaFileTextBox.Text);
                        if (!string.IsNullOrWhiteSpace(criteriaPath) && File.Exists(criteriaPath))
                        {
                            string json = File.ReadAllText(criteriaPath);
                            tableCriteria = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        }
                        
                        // Create options using Dispatcher.Invoke for UI access
                        var exportTables = Dispatcher.Invoke(() => ExportTablesTextBox.Text);
                        var includeSchemaOnly = Dispatcher.Invoke(() => ExportSchemaOnlyCheckBox.IsChecked ?? false);
                        var outputDirectory = Dispatcher.Invoke(() => ExportOutputDirectoryTextBox.Text);
                        
                        var options = new ExportOptions
                        {
                            Tables = !string.IsNullOrWhiteSpace(exportTables) ?
                                new List<string>(exportTables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) : null,
                            BatchSize = batchSize,
                            IncludeSchemaOnly = includeSchemaOnly,
                            OutputDirectory = outputDirectory,
                            TableCriteria = tableCriteria
                        };
                        
                        var exporter = new DatabaseExporter(provider, connection, options);
                        exporter.SetProgressReporter(progressReporter);
                        await exporter.ExportAsync(options.OutputDirectory);
                        
                        updateStatus("Export completed successfully!");
                        
                        // Update progress one last time
                        Dispatcher.Invoke(() =>
                        {
                            ExportProgressBar.IsIndeterminate = false;
                            ExportProgressBar.Value = 100;
                            ExportProgressBar.Maximum = 100;
                            ExportProgressText.Text = "Export completed successfully!";
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string errorMessage;
                            string detailMessage = ex.Message;
                            
                            // Check for specific error types and provide more user-friendly messages
                            if (ex.Message.Contains("were not found in the database"))
                            {
                                errorMessage = "Invalid Table Name Error";
                                detailMessage = ex.Message;
                            }
                            else
                            {
                                errorMessage = "Export Error";
                            }
                            
                            System.Windows.MessageBox.Show($"Error during export: {detailMessage}", errorMessage, MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Export failed.";
                
                // Reset progress UI
                Dispatcher.Invoke(() =>
                {
                    ExportProgressBar.IsIndeterminate = false;
                    ExportProgressText.Text = $"Export failed: {ex.Message}";
                });
            }
            finally
            {
                // Re-enable export button
                ExportButton.IsEnabled = true;
            }
        }

        private async void StartImport(object sender, RoutedEventArgs e)
        {
            // Validate inputs before showing progress UI
            if (string.IsNullOrWhiteSpace(ImportConnectionString))
            {
                System.Windows.MessageBox.Show("Please enter connection information.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(ImportInputDirectoryTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please select an input directory.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Validate table names if specified
            if (!string.IsNullOrWhiteSpace(ImportTablesTextBox.Text))
            {
                string[] tableNames = ImportTablesTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tableNames.Length == 0)
                {
                    System.Windows.MessageBox.Show("Please enter valid table names or leave the field empty to import all tables.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Check for empty table names after trimming
                if (tableNames.Any(t => string.IsNullOrWhiteSpace(t)))
                {
                    System.Windows.MessageBox.Show("One or more table names are empty. Please enter valid table names.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            
            // Show progress UI
            ImportProgressGroupBox.Visibility = Visibility.Visible;
            ImportButton.IsEnabled = false;
            ImportProgressBar.IsIndeterminate = true;
            ImportProgressText.Text = "Preparing to import...";
            
            try
            {
                // Parse batch size
                if (string.IsNullOrWhiteSpace(ImportBatchSizeTextBox.Text) || !int.TryParse(ImportBatchSizeTextBox.Text, out int batchSize) || batchSize <= 0)
                {
                    System.Windows.MessageBox.Show("Batch size must be a positive number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Get provider
                string providerName = GetSelectedProviderName(ImportProviderIndex);
                var provider = DatabaseProviderFactory.Create(providerName);
                
                // Create connection - with additional logging to diagnose any issues
                StatusTextBlock.Text = "Creating connection to target database...";
                
                // Set logger to capture detailed connection information
                provider.SetLogger(message => {
                    Dispatcher.Invoke(() => {
                        StatusTextBlock.Text = message;
                    });
                });
                
                var connection = provider.CreateConnection(ImportConnectionString);
                StatusTextBlock.Text = "Connection created successfully, preparing to import...";
                
                // Create progress handler to update status
                Action<string> updateStatus = (message) => 
                {
                    Dispatcher.Invoke(() => StatusTextBlock.Text = message);
                };

                // Create progress reporter
                ProgressReportHandler progressReporter = (progress) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        // Check if this is a batch file progress update
                        if (progress.Message?.Contains("[Batch]") == true)
                        {
                            // Extract batch information from the message
                            string batchInfo = progress.Message.Replace("[Batch] ", "");
                            
                            // Update progress bar with batch progress
                            ImportProgressBar.IsIndeterminate = progress.IsIndeterminate;
                            if (!progress.IsIndeterminate)
                            {
                                ImportProgressBar.Value = progress.Current;
                                ImportProgressBar.Maximum = progress.Total;
                            }
                            
                            // Update status text with both table and batch information
                            // Get the current table info from previous status message
                            string tableInfo = ImportProgressText.Text;
                            
                            // Check for any previous batch info and remove it
                            if (tableInfo.Contains(" - Processing file"))
                            {
                                // Remove previous batch info
                                tableInfo = tableInfo.Substring(0, tableInfo.IndexOf(" - Processing file"));
                            }
                            
                            // Combine table info with new batch info
                            ImportProgressText.Text = $"{tableInfo} - {batchInfo}";
                            StatusTextBlock.Text = ImportProgressText.Text;
                        }
                        else
                        {
                            // Regular progress update for table import
                            ImportProgressBar.IsIndeterminate = progress.IsIndeterminate;
                            if (!progress.IsIndeterminate)
                            {
                                ImportProgressBar.Value = progress.Current;
                                ImportProgressBar.Maximum = progress.Total;
                            }
                            
                            // Update status text
                            ImportProgressText.Text = progress.Message;
                            StatusTextBlock.Text = progress.Message;
                        }
                    });
                };

                // Run import in background
                await Task.Run(async () =>
                {
                    try
                    {
                        // Create options using Dispatcher.Invoke for UI access
                        var importTables = Dispatcher.Invoke(() => ImportTablesTextBox.Text);
                        var createSchema = Dispatcher.Invoke(() => ImportCreateSchemaCheckBox.IsChecked ?? true);
                        var createForeignKeys = Dispatcher.Invoke(() => ImportCreateForeignKeysCheckBox.IsChecked ?? true);
                        var schemaOnly = Dispatcher.Invoke(() => ImportSchemaOnlyCheckBox.IsChecked ?? false);
                        var continueOnError = Dispatcher.Invoke(() => ImportContinueOnErrorCheckBox.IsChecked ?? false);
                        
                        var options = new ImportOptions
                        {
                            Tables = !string.IsNullOrWhiteSpace(importTables) ?
                                new List<string>(importTables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) : null,
                            BatchSize = batchSize,
                            CreateSchema = createSchema,
                            CreateForeignKeys = createForeignKeys,
                            SchemaOnly = schemaOnly,
                            ContinueOnError = continueOnError
                        };
                        
                        var importer = new DatabaseImporter(provider, connection, options);
                        importer.SetProgressReporter(progressReporter);
                        // Cache the text value on the background thread to avoid cross-thread access
                        string importPath = Dispatcher.Invoke(() => ImportInputDirectoryTextBox.Text);
                        await importer.ImportAsync(importPath);
                        
                        updateStatus("Import completed successfully!");
                        
                        // Update progress one last time
                        Dispatcher.Invoke(() =>
                        {
                            ImportProgressBar.IsIndeterminate = false;
                            ImportProgressBar.Value = 100;
                            ImportProgressBar.Maximum = 100;
                            ImportProgressText.Text = "Import completed successfully!";
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string errorMessage;
                            string detailMessage = ex.Message;
                            
                            // Check for specific error types and provide more user-friendly messages
                            if (ex.Message.Contains("were not found in the export data"))
                            {
                                errorMessage = "Invalid Table Name Error";
                                detailMessage = ex.Message;
                            }
                            else
                            {
                                errorMessage = "Import Error";
                            }
                            
                            System.Windows.MessageBox.Show($"Error during import: {detailMessage}", errorMessage, MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Import failed.";
                
                // Reset progress UI
                Dispatcher.Invoke(() =>
                {
                    ImportProgressBar.IsIndeterminate = false;
                    ImportProgressText.Text = $"Import failed: {ex.Message}";
                });
            }
            finally
            {
                // Re-enable import button
                ImportButton.IsEnabled = true;
            }
        }

        private void SchemaScriptOutputCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // This method is automatically called when the checkbox is checked/unchecked
            // The UI bindings will automatically enable/disable the related controls
        }
        
        private void BrowseSchemaScriptPath(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Schema Script Output Directory";
            dialog.UseDescriptionForTitle = true;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SchemaScriptPathTextBox.Text = dialog.SelectedPath;
            }
        }
        
        private async void ViewSchema(object sender, RoutedEventArgs e)
        {
            // Validate inputs before proceeding
            if (string.IsNullOrWhiteSpace(SchemaConnectionString))
            {
                StatusTextBlock.Text = "Error: Please enter connection information";
                MessageBox.Show("Please enter connection information.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update status
            StatusTextBlock.Text = "Connecting to database...";
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                // Get provider
                string providerName = GetSelectedProviderName(SchemaProviderIndex);
                var provider = DatabaseProviderFactory.Create(providerName);
                
                // Add diagnostic logging
                provider.SetLogger(message => {
                    Dispatcher.Invoke(() => {
                        StatusTextBlock.Text = message;
                    });
                });

                StatusTextBlock.Text = $"Retrieving schema from {providerName}...";

                // Parse table names (if any)
                List<string>? tableNames = null;
                if (!string.IsNullOrWhiteSpace(SchemaTablesTextBox.Text))
                {
                    tableNames = SchemaTablesTextBox.Text.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    StatusTextBlock.Text = $"Retrieving schema for {tableNames.Count} tables...";
                }
                else
                {
                    StatusTextBlock.Text = "Retrieving schema for all tables...";
                }

                // Fetch schema data
                List<TableSchema> tables;
                
                // Standard handling for database types
                using (var connection = provider.CreateConnection(SchemaConnectionString))
                {
                    try
                    {
                        await connection.OpenAsync();
                        StatusTextBlock.Text = "Connection opened successfully";
                        
                        // Log connection info for diagnostic purposes
                        string connectionInfo = $"Database: {connection.Database}, Provider: {providerName}, State: {connection.State}";
                        StatusTextBlock.Text = $"Reading schema... ({connectionInfo})";
                        
                        // Get tables
                        tables = await provider.GetTablesAsync(connection, tableNames);
                        StatusTextBlock.Text = $"Schema retrieved: {tables.Count} tables found";
                    }
                    catch (Exception ex)
                    {
                        StatusTextBlock.Text = $"Error retrieving schema: {ex.Message}";
                        MessageBox.Show($"Error retrieving schema: {ex.Message}\n\nDetails: {ex}", "Schema Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                if (tables == null || tables.Count == 0)
                {
                    StatusTextBlock.Text = "No tables found in database";
                    MessageBox.Show("No tables were found in the database. Check your connection string and table names if specified.", "No Tables Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                StatusTextBlock.Text = $"Displaying schema for {tables.Count} tables...";

                // Reset the cursor before showing the schema viewer
                Mouse.OverrideCursor = null;
                
                // Display schema viewer
                bool showDetailedInfo = SchemaVerboseCheckBox.IsChecked ?? false;
                var schemaViewer = new SchemaViewWindow(tables, showDetailedInfo, SchemaConnectionString, providerName);
                schemaViewer.Owner = this;
                schemaViewer.ShowDialog();

                // Generate scripts if requested
                if ((SchemaScriptOutputCheckBox.IsChecked ?? false) && !string.IsNullOrEmpty(SchemaScriptPathTextBox.Text))
                {
                    StatusTextBlock.Text = "Generating SQL scripts...";
                    await GenerateSqlScripts(tables, SchemaScriptPathTextBox.Text, providerName);
                    StatusTextBlock.Text = "SQL scripts generated successfully";
                }

                StatusTextBlock.Text = "Schema view completed";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error in schema view: {ex.Message}";
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        
        private async Task GenerateSqlScripts(List<TableSchema> tables, string outputPath, string providerName)
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            var provider = DatabaseProviderFactory.Create(providerName);
            foreach (var table in tables)
            {
                try
                {
                    string script = provider.GenerateTableCreationScript(table);
                    string fileName = Path.Combine(outputPath, $"{table.FullName}.sql");
                    await File.WriteAllTextAsync(fileName, script);
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = $"Error generating script for {table.Name}: {ex.Message}";
                }
            }
        }

        private string GetExportConnectionString()
        {
            return ExportConnectionControl?.ConnectionString ?? string.Empty;
        }

        private string GetImportConnectionString()
        {
            return ImportConnectionControl?.ConnectionString ?? string.Empty;
        }

        private string GetSchemaConnectionString()
        {
            return SchemaConnectionControl?.ConnectionString ?? string.Empty;
        }
        
        private string GetSelectedProviderName(int providerIndex)
        {
            switch (providerIndex)
            {
                case 0: return "SqlServer";
                case 1: return "MySQL";
                case 2: return "PostgreSQL";
                case 3: return "Firebird";
                default: return "SqlServer";
            }
        }
    }
}