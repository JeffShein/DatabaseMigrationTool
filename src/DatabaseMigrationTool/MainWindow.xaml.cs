using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using DatabaseMigrationTool.Services;
using DatabaseMigrationTool.Utilities;
using System.Threading;
using System.Threading.Tasks;
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
using System.Globalization;
using DatabaseMigrationTool.Controls;

namespace DatabaseMigrationTool
{
    public partial class MainWindow : Window
    {
        // Table cache to avoid repeated database calls
        private readonly Dictionary<string, List<TableSchema>> _tableCache = new Dictionary<string, List<TableSchema>>();
        private readonly Dictionary<string, string> _lastConnectionInfo = new Dictionary<string, string>();
        
        // Shared profile management
        private readonly ConnectionProfileManager _sharedProfileManager;
        private ConnectionProfile? _currentSharedProfile;
        
        // Enhanced error handling and recovery
        private readonly OperationRecoveryService _recoveryService;

        private string ExportConnectionString => GetExportConnectionString();

        private string ImportConnectionString => GetImportConnectionString();

        private string SchemaConnectionString => GetSchemaConnectionString();

        private int ExportProviderIndex => ExportConnectionControl.ProviderIndex;
        private int ImportProviderIndex => ImportConnectionControl.ProviderIndex;
        private int SchemaProviderIndex => SchemaConnectionControl.ProviderIndex;

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize error handling system
            ErrorHandler.Initialize();
            
            // Initialize shared profile manager
            _sharedProfileManager = new ConnectionProfileManager();
            
            // Initialize recovery service
            _recoveryService = new OperationRecoveryService();
            
            // Initialize provider ComboBoxes
            var providers = DatabaseProviderFactory.GetSupportedProviders();
            
            // Set up cache invalidation when connection settings change
            SetupCacheInvalidation();
            
            // Set up shared profile system
            SetupSharedProfileSystem();
        }

        private void SetupCacheInvalidation()
        {
            // We'll check for changes when browsing tables instead of real-time monitoring
            // This is simpler and more reliable than trying to monitor all connection changes
        }

        private void InvalidateTableCache(string tabName)
        {
            if (_tableCache.ContainsKey(tabName))
            {
                _tableCache.Remove(tabName);
                _lastConnectionInfo.Remove(tabName);
            }
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

        private async void OpenCriteriaHelper(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the selected provider and connection
                string providerName = GetSelectedProviderName(ExportProviderIndex);
                var connectionString = ExportConnectionString;
                
                if (string.IsNullOrEmpty(providerName))
                {
                    MessageBox.Show("Please select a database provider first.", "Provider Required", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (string.IsNullOrEmpty(connectionString))
                {
                    MessageBox.Show("Please configure the database connection first.", "Connection Required", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Get the selected tables
                var selectedTablesText = ExportTablesTextBox.Text?.Trim();
                var criteriaFilePath = ExportTableCriteriaFileTextBox.Text?.Trim();
                List<string>? selectedTableNames = null;
                
                if (!string.IsNullOrEmpty(selectedTablesText))
                {
                    selectedTableNames = selectedTablesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                }

                // Validate that tables are selected
                if (selectedTableNames?.Any() != true)
                {
                    MessageBox.Show(
                        "Please select tables first using the 'Browse...' button.\n\n" +
                        "The helper needs to know which tables you want to create criteria for.",
                        "No Tables Selected", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Information);
                    return;
                }

                // Show loading
                var loadingSpinner = new LoadingSpinner();
                var loadingWindow = new Window
                {
                    Content = loadingSpinner,
                    Title = "Loading Table Information...",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize
                };

                loadingSpinner.StartSpinning();
                loadingWindow.Show();

                try
                {
                    // Get table schemas in background
                    var tables = new List<TableSchema>();
                    var tableColumns = new Dictionary<string, List<Models.ColumnDefinition>>();

                    await Task.Run(async () =>
                    {
                        var provider = DatabaseProviderFactory.Create(providerName);
                        using var connection = provider.CreateConnection(connectionString);
                        await connection.OpenAsync();

                        // Get all tables or just selected ones
                        var allTables = await provider.GetTablesAsync(connection);
                        
                        if (selectedTableNames?.Any() == true)
                        {
                            // Filter to only selected tables
                            tables = allTables.Where(t => selectedTableNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase) ||
                                                         selectedTableNames.Contains(t.FullName, StringComparer.OrdinalIgnoreCase))
                                           .ToList();
                        }
                        else
                        {
                            tables = allTables;
                        }

                        // Get columns for each table
                        foreach (var table in tables)
                        {
                            try
                            {
                                var columns = await provider.GetColumnsAsync(connection, table.Name, table.Schema);
                                tableColumns[table.Name] = columns;
                            }
                            catch (Exception ex)
                            {
                                // Log error but continue with other tables
                                System.Diagnostics.Debug.WriteLine($"Failed to get columns for table {table.Name}: {ex.Message}");
                                tableColumns[table.Name] = new List<Models.ColumnDefinition>();
                            }
                        }
                    });

                    loadingWindow.Close();

                    if (!tables.Any())
                    {
                        MessageBox.Show("No tables found. Please select tables first or check your connection.", 
                            "No Tables", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Get existing criteria file path
                    var existingCriteriaPath = ExportTableCriteriaFileTextBox.Text?.Trim();
                    
                    // Open the criteria helper window
                    var helperWindow = new CriteriaHelperWindow(tables, tableColumns, existingCriteriaPath)
                    {
                        Owner = this
                    };

                    if (helperWindow.ShowDialog() == true && !string.IsNullOrEmpty(helperWindow.SavedFilePath))
                    {
                        // Update the criteria file path
                        ExportTableCriteriaFileTextBox.Text = helperWindow.SavedFilePath;
                        MessageBox.Show($"Criteria file saved successfully!\n\nPath: {helperWindow.SavedFilePath}", 
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                finally
                {
                    if (loadingWindow.IsVisible)
                    {
                        loadingWindow.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load table information: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                            var errorInfo = ErrorHandler.HandleError(ex, "Database Export", showUserMessage: false);
                            
                            // Show enhanced error message with recovery options
                            var message = errorInfo.Message;
                            if (!string.IsNullOrEmpty(errorInfo.SuggestedAction))
                            {
                                message += $"\n\nSuggested action: {errorInfo.SuggestedAction}";
                            }
                            
                            System.Windows.MessageBox.Show(message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                var errorInfo = ErrorHandler.HandleError(ex, "Export Operation", showUserMessage: false);
                StatusTextBlock.Text = "Export failed.";
                
                // Reset progress UI
                Dispatcher.Invoke(() =>
                {
                    ExportProgressBar.IsIndeterminate = false;
                    ExportProgressText.Text = $"Export failed: {errorInfo.Message}";
                });
                
                // Offer recovery options for recoverable errors
                if (errorInfo.IsRecoverable)
                {
                    var result = MessageBox.Show(
                        $"Export failed but may be recoverable.\n\n{errorInfo.Message}\n\nWould you like to try again?",
                        "Export Failed - Recovery Available", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Retry after a delay
                        _ = Task.Delay(2000).ContinueWith(_ => 
                        {
                            Dispatcher.Invoke(() => StartExport(this, new RoutedEventArgs()));
                        });
                    }
                }
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
                            var errorInfo = ErrorHandler.HandleError(ex, "Database Import", showUserMessage: false);
                            
                            // Show enhanced error message with recovery options
                            var message = errorInfo.Message;
                            if (!string.IsNullOrEmpty(errorInfo.SuggestedAction))
                            {
                                message += $"\n\nSuggested action: {errorInfo.SuggestedAction}";
                            }
                            
                            System.Windows.MessageBox.Show(message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                var errorInfo = ErrorHandler.HandleError(ex, "Import Operation", showUserMessage: false);
                StatusTextBlock.Text = "Import failed.";
                
                // Reset progress UI
                Dispatcher.Invoke(() =>
                {
                    ImportProgressBar.IsIndeterminate = false;
                    ImportProgressText.Text = $"Import failed: {errorInfo.Message}";
                });
                
                // Offer recovery options for recoverable errors
                if (errorInfo.IsRecoverable)
                {
                    var result = MessageBox.Show(
                        $"Import failed but may be recoverable.\n\n{errorInfo.Message}\n\nWould you like to try again?",
                        "Import Failed - Recovery Available", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Retry after a delay
                        _ = Task.Delay(2000).ContinueWith(_ => 
                        {
                            Dispatcher.Invoke(() => StartImport(this, new RoutedEventArgs()));
                        });
                    }
                }
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

        #region Table Selection Event Handlers

        private void BrowseExportTables_Click(object sender, RoutedEventArgs e)
        {
            BrowseTables(ExportConnectionControl, ExportTablesTextBox, "export");
        }

        private void ClearExportTables_Click(object sender, RoutedEventArgs e)
        {
            ExportTablesTextBox.Text = string.Empty;
        }

        private void BrowseImportTables_Click(object sender, RoutedEventArgs e)
        {
            BrowseTables(ImportConnectionControl, ImportTablesTextBox, "import");
        }

        private void ClearImportTables_Click(object sender, RoutedEventArgs e)
        {
            ImportTablesTextBox.Text = string.Empty;
        }

        private void BrowseSchemaTables_Click(object sender, RoutedEventArgs e)
        {
            BrowseTables(SchemaConnectionControl, SchemaTablesTextBox, "schema");
        }

        private void ClearSchemaTables_Click(object sender, RoutedEventArgs e)
        {
            SchemaTablesTextBox.Text = string.Empty;
        }

        private void BrowseTables(Controls.ConnectionStringControl connectionControl, TextBox tablesTextBox, string operation)
        {
            try
            {
                // Get connection string and provider
                string connectionString = connectionControl?.ConnectionString ?? string.Empty;
                int providerIndex = connectionControl?.ProviderIndex ?? 0;

                // Validate connection information
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    MessageBox.Show("Please configure the database connection first.", "Connection Required", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Get provider
                string providerName = GetSelectedProviderName(providerIndex);
                var provider = DatabaseProviderFactory.Create(providerName);

                // Create cache key based on connection details
                string cacheKey = $"{providerName}|{connectionString}";
                string tabName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(operation);

                // Check if we need to refresh the cache
                List<TableSchema>? cachedTables = null;
                bool useCache = _lastConnectionInfo.ContainsKey(tabName) && 
                               _lastConnectionInfo[tabName] == cacheKey &&
                               _tableCache.ContainsKey(tabName);

                if (useCache)
                {
                    cachedTables = _tableCache[tabName];
                }

                // Parse existing selected tables
                List<string>? preselectedTables = null;
                if (!string.IsNullOrWhiteSpace(tablesTextBox.Text))
                {
                    preselectedTables = tablesTextBox.Text
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                }

                // Show table selection window
                var tableSelectionWindow = new TableSelectionWindow(provider, connectionString, preselectedTables, cachedTables)
                {
                    Owner = this,
                    Title = $"Select Tables for {tabName}"
                };

                if (tableSelectionWindow.ShowDialog() == true)
                {
                    // Update the text box with selected tables
                    tablesTextBox.Text = string.Join(", ", tableSelectionWindow.SelectedTableNames);
                    
                    // Cache the loaded tables for future use
                    if (tableSelectionWindow.LoadedTables != null)
                    {
                        _tableCache[tabName] = tableSelectionWindow.LoadedTables;
                        _lastConnectionInfo[tabName] = cacheKey;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing tables: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Shared Profile System

        private void SetupSharedProfileSystem()
        {
            // Configure all connection controls to use the shared profile manager
            Loaded += (s, e) =>
            {
                if (ExportConnectionControl != null)
                    ConfigureConnectionControlForSharedProfiles(ExportConnectionControl, "Export");
                if (ImportConnectionControl != null)
                    ConfigureConnectionControlForSharedProfiles(ImportConnectionControl, "Import");
                if (SchemaConnectionControl != null)
                    ConfigureConnectionControlForSharedProfiles(SchemaConnectionControl, "Schema");
            };
        }

        private void ConfigureConnectionControlForSharedProfiles(ConnectionStringControl control, string tabName)
        {
            // Replace the control's profile manager with the shared one
            control.SetSharedProfileManager(_sharedProfileManager, OnSharedProfileChanged);
        }

        private void OnSharedProfileChanged(ConnectionProfile? profile, string initiatingTab)
        {
            _currentSharedProfile = profile;
            
            if (profile == null) 
            {
                // Hide global profile indicator
                if (GlobalProfileIndicator != null)
                    GlobalProfileIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            // Apply the selected profile to all other connection controls
            if (ExportConnectionControl != null && initiatingTab != "Export")
                ExportConnectionControl.LoadSharedProfile(profile);
            if (ImportConnectionControl != null && initiatingTab != "Import")
                ImportConnectionControl.LoadSharedProfile(profile);
            if (SchemaConnectionControl != null && initiatingTab != "Schema")
                SchemaConnectionControl.LoadSharedProfile(profile);

            // Show global profile indicator
            if (GlobalProfileIndicator != null && ActiveProfileText != null)
            {
                GlobalProfileIndicator.Visibility = Visibility.Visible;
                ActiveProfileText.Text = $"{profile.Name} ({profile.Provider} - {profile.Server})";
            }

            // Invalidate table cache since connection changed
            _tableCache.Clear();
            _lastConnectionInfo.Clear();
            
            // Update status
            StatusTextBlock.Text = $"Profile '{profile.Name}' loaded across all tabs";
        }
        
        private void ClearGlobalProfile_Click(object sender, RoutedEventArgs e)
        {
            // Clear the active profile from all connection controls
            _currentSharedProfile = null;
            
            if (ExportConnectionControl != null)
                ExportConnectionControl.ClearProfile();
            if (ImportConnectionControl != null)
                ImportConnectionControl.ClearProfile();
            if (SchemaConnectionControl != null)
                SchemaConnectionControl.ClearProfile();
            
            // Hide global profile indicator
            if (GlobalProfileIndicator != null)
                GlobalProfileIndicator.Visibility = Visibility.Collapsed;
            
            // Clear table cache
            _tableCache.Clear();
            _lastConnectionInfo.Clear();
            
            StatusTextBlock.Text = "Profile cleared from all tabs";
        }

        #endregion
        
        #region Recovery Operations

        private void ResumeOperations_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var recoveryWindow = new RecoveryWindow
                {
                    Owner = this
                };
                
                if (recoveryWindow.ShowDialog() == true && recoveryWindow.SelectedOperation != null)
                {
                    var operation = recoveryWindow.SelectedOperation;
                    
                    // Switch to the appropriate tab
                    if (operation.OperationType == "Export")
                    {
                        // Switch to export tab and resume export
                        // Implementation depends on your tab control structure
                        ResumeExportOperation(operation);
                    }
                    else if (operation.OperationType == "Import")
                    {
                        // Switch to import tab and resume import
                        ResumeImportOperation(operation);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open recovery window: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void ResumeExportOperation(OperationState operation)
        {
            try
            {
                StatusTextBlock.Text = $"Resuming export operation: {operation.OperationId}";

                // Validate operation before resume
                if (!_recoveryService.ValidateOperationForResume(operation))
                {
                    MessageBox.Show("Operation cannot be resumed. It may be corrupted or completed.",
                        "Resume Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Show progress UI
                ExportProgressGroupBox.Visibility = Visibility.Visible;
                ExportButton.IsEnabled = false;
                ExportProgressBar.IsIndeterminate = true;
                ExportProgressText.Text = "Resuming export...";

                // Get provider
                string providerName = GetSelectedProviderName(ExportProviderIndex);
                var provider = DatabaseProviderFactory.Create(providerName);

                // Create progress reporter
                ProgressReportHandler progressReporter = (progress) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ExportProgressBar.IsIndeterminate = progress.IsIndeterminate;
                        if (!progress.IsIndeterminate)
                        {
                            ExportProgressBar.Value = progress.Current;
                            ExportProgressBar.Maximum = progress.Total;
                        }
                        ExportProgressText.Text = progress.Message;
                        StatusTextBlock.Text = progress.Message;
                    });
                };

                // Resume export operation
                bool success = await _recoveryService.ResumeExportOperationAsync(
                    operation, provider, operation.ConnectionString, progressReporter);

                if (success)
                {
                    StatusTextBlock.Text = "Export operation resumed and completed successfully!";
                    ExportProgressText.Text = "Export completed successfully!";
                    MessageBox.Show("Export operation completed successfully!", "Resume Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "Failed to resume export operation.";
                    ExportProgressText.Text = "Export resume failed.";
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.HandleError(ex, "Resume Export Operation");
                StatusTextBlock.Text = "Export resume failed.";
                ExportProgressText.Text = $"Export resume failed: {ex.Message}";
            }
            finally
            {
                ExportButton.IsEnabled = true;
            }
        }
        
        private async void ResumeImportOperation(OperationState operation)
        {
            try
            {
                StatusTextBlock.Text = $"Resuming import operation: {operation.OperationId}";

                // Validate operation before resume
                if (!_recoveryService.ValidateOperationForResume(operation))
                {
                    MessageBox.Show("Operation cannot be resumed. It may be corrupted or completed.",
                        "Resume Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Show progress UI
                ImportProgressGroupBox.Visibility = Visibility.Visible;
                ImportButton.IsEnabled = false;
                ImportProgressBar.IsIndeterminate = true;
                ImportProgressText.Text = "Resuming import...";

                // Get provider
                string providerName = GetSelectedProviderName(ImportProviderIndex);
                var provider = DatabaseProviderFactory.Create(providerName);

                // Create progress reporter
                ProgressReportHandler progressReporter = (progress) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ImportProgressBar.IsIndeterminate = progress.IsIndeterminate;
                        if (!progress.IsIndeterminate)
                        {
                            ImportProgressBar.Value = progress.Current;
                            ImportProgressBar.Maximum = progress.Total;
                        }
                        ImportProgressText.Text = progress.Message;
                        StatusTextBlock.Text = progress.Message;
                    });
                };

                // Resume import operation
                bool success = await _recoveryService.ResumeImportOperationAsync(
                    operation, provider, operation.ConnectionString, progressReporter);

                if (success)
                {
                    StatusTextBlock.Text = "Import operation resumed and completed successfully!";
                    ImportProgressText.Text = "Import completed successfully!";
                    MessageBox.Show("Import operation completed successfully!", "Resume Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "Failed to resume import operation.";
                    ImportProgressText.Text = "Import resume failed.";
                }
            }
            catch (Exception ex)
            {
                ErrorHandler.HandleError(ex, "Resume Import Operation");
                StatusTextBlock.Text = "Import resume failed.";
                ImportProgressText.Text = $"Import resume failed: {ex.Message}";
            }
            finally
            {
                ImportButton.IsEnabled = true;
            }
        }

        #endregion
    }
}