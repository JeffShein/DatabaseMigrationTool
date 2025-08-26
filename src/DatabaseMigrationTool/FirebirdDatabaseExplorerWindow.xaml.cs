using DatabaseMigrationTool.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DatabaseMigrationTool
{
    /// <summary>
    /// Interaction logic for FirebirdDatabaseExplorerWindow.xaml
    /// </summary>
    public partial class FirebirdDatabaseExplorerWindow : Window
    {
        private Utilities.FirebirdDatabaseReader? _firebirdReader;
        private string _filePath = string.Empty;
        private List<string> _tables = new List<string>();
        
        public FirebirdDatabaseExplorerWindow()
        {
            InitializeComponent();
        }
        
        public FirebirdDatabaseExplorerWindow(string filePath) : this()
        {
            _filePath = filePath;
            FilePathTextBox.Text = filePath;
            
            // Auto-open the file
            OpenDatabaseFile();
        }
        
        #region Event Handlers
        
        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open Firebird Database File",
                Filter = "Firebird Database Files (*.fdb;*.gdb)|*.fdb;*.gdb|All Database Files (*.db;*.fdb;*.gdb)|*.db;*.fdb;*.gdb|All Files (*.*)|*.*",
                CheckFileExists = true
            };
            
            if (dialog.ShowDialog() == true)
            {
                _filePath = dialog.FileName;
                FilePathTextBox.Text = _filePath;
                OpenDatabaseFile();
            }
        }
        
        private async void AnalyzeHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            await AnalyzeHeaderAsync();
        }
        
        private async void ScanTablesButton_Click(object sender, RoutedEventArgs e)
        {
            await ScanForTablesAsync();
        }
        
        private async void ScanDataStructuresButton_Click(object sender, RoutedEventArgs e)
        {
            await ScanDataStructuresAsync();
        }
        
        private void TableFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterTables();
        }
        
        private void TablesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Will be implemented when we can extract table data
        }
        
        private void ExportMetadataButton_Click(object sender, RoutedEventArgs e)
        {
            ExportMetadata();
        }
        
        private void ExportLogButton_Click(object sender, RoutedEventArgs e)
        {
            ExportLog();
        }
        
        private async void ViewDumpButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewRawDumpAsync();
        }
        
        private void TablesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Handle double-click on a table item
            if (TablesListBox.SelectedItem != null)
            {
                ViewTableDetails(TablesListBox.SelectedItem.ToString() ?? string.Empty);
            }
        }
        
        #endregion
        
        #region Implementation Methods
        
        private void OpenDatabaseFile()
        {
            if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            {
                MessageBox.Show("Please select a valid file.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // Close existing reader if any
                if (_firebirdReader != null)
                {
                    _firebirdReader.Close();
                    _firebirdReader = null;
                }
                
                // Clear any existing data
                HeaderAnalysisTextBox.Clear();
                MetadataTextBox.Clear();
                DataStructuresTextBox.Clear();
                RawDumpTextBox.Clear();
                LogTextBox.Clear();
                TablesListBox.Items.Clear();
                
                // Update UI
                HeaderTextBlock.Text = $"Firebird Database Explorer - {Path.GetFileName(_filePath)}";
                
                // Open the file and determine format
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Check the first 100 bytes to see if it has Firebird signature
                        using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            byte[] header = new byte[100];
                            await fs.ReadAsync(header, 0, header.Length).ConfigureAwait(false);
                            
                            // Check for Firebird signature (bytes 2-3 = "39 30" for "90" in ASCII)
                            if (header.Length > 3 && header[2] == 0x39 && header[3] == 0x30) // "90" in ASCII
                            {
                                // This is a Firebird format file, use specialized reader
                                // Create the specialized reader
                                _firebirdReader = new Utilities.FirebirdDatabaseReader(_filePath);
                                await _firebirdReader.AnalyzeDatabaseStructureAsync();
                                
                                Dispatcher.Invoke(() =>
                                {
                                    LogTextBox.Text = _firebirdReader.GetLog();
                                    StatusTextBlock.Text = "Firebird format file opened successfully";
                                    
                                    // We'll analyze the header from the UI thread directly
                                });
                                
                                // Intentionally not awaited - UI thread will continue normally
                                _ = Dispatcher.BeginInvoke(new Action(async () => await AnalyzeHeaderAsync()));
                                return;
                            }
                            else
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    LogTextBox.Text = "File does not have Firebird format signature";
                                    StatusTextBlock.Text = "Not a Firebird format database";
                                    MessageBox.Show("This file does not appear to be a Firebird format database.", "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogTextBox.Text += $"Error opening file: {ex.Message}\r\n";
                            StatusTextBlock.Text = "Error opening file";
                            MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                LogTextBox.Text += $"Error: {ex.Message}\r\n";
                StatusTextBlock.Text = "Error";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async Task AnalyzeHeaderAsync()
        {
            if (_firebirdReader == null)
            {
                MessageBox.Show("Please open a database file first.", "No File Open", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                StatusTextBlock.Text = "Analyzing header...";
                Mouse.OverrideCursor = Cursors.Wait;
                
                // We'll just display the metadata that was already gathered during opening
                // No need for additional processing
                
                // Update metadata display - use the Firebird reader
                Dictionary<string, object> metadata;
                string log;
                
                if (_firebirdReader != null)
                {
                    metadata = _firebirdReader.GetMetadata();
                    log = _firebirdReader.GetLog();
                }
                else
                {
                    metadata = new Dictionary<string, object>();
                    log = string.Empty;
                }
                
                var sb = new StringBuilder();
                
                foreach (var kvp in metadata.OrderBy(k => k.Key))
                {
                    if (kvp.Value is IEnumerable<string> stringList)
                    {
                        sb.AppendLine($"{kvp.Key}:");
                        foreach (var item in stringList)
                        {
                            sb.AppendLine($"  - {item}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                    }
                }
                
                MetadataTextBox.Text = sb.ToString();
                
                // Also format a specialized header analysis
                var headerSb = new StringBuilder();
                headerSb.AppendLine($"File: {metadata.GetValueOrDefault("FilePath", "Unknown")}");
                headerSb.AppendLine($"Size: {metadata.GetValueOrDefault("FileSize", "Unknown")} bytes");
                headerSb.AppendLine($"Last Modified: {metadata.GetValueOrDefault("LastModified", "Unknown")}");
                
                if (_firebirdReader != null)
                {
                    headerSb.AppendLine($"Format: Firebird");
                    headerSb.AppendLine($"Page Size: {metadata.GetValueOrDefault("PageSize", "Unknown")} bytes");
                    headerSb.AppendLine($"Total Pages: {metadata.GetValueOrDefault("TotalPages", "Unknown")}");
                    headerSb.AppendLine($"Header Version: {metadata.GetValueOrDefault("HeaderVersion", "Unknown")}");
                }
                    
                // Wait for a moment to update the UI (just for appearance)
                await Task.Delay(500);
                
                HeaderAnalysisTextBox.Text = headerSb.ToString();
                LogTextBox.Text = log;
                
                StatusTextBlock.Text = "Header analysis complete";
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"Error analyzing header: {ex.Message}\r\n");
                StatusTextBlock.Text = "Error analyzing header";
                MessageBox.Show($"Error analyzing header: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        
        private async Task ScanForTablesAsync()
        {
            if (_firebirdReader == null)
            {
                MessageBox.Show("Please open a database file first.", "No File Open", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                StatusTextBlock.Text = "Scanning for tables...";
                Mouse.OverrideCursor = Cursors.Wait;
                
                // Use Task.Run to perform this work on a background thread
                await Task.Run(() =>
                {
                    // Extract tables - use the Firebird reader
                    if (_firebirdReader != null)
                    {
                        // Use our specialized page analysis to find table-like structures
                        var pages = _firebirdReader.GetPages();
                        var dataPages = pages.Where(p => p.PageType == FirebirdDatabaseReader.PageType.Data).ToList();
                        
                        _tables = new List<string>();
                        
                        // Create table entries based on data pages
                        for (int i = 0; i < dataPages.Count; i++)
                        {
                            _tables.Add($"DataTable_{i+1}_Page{dataPages[i].PageNumber}");
                        }
                    }
                });
                
                // Update log
                LogTextBox.Text = _firebirdReader?.GetLog() ?? string.Empty;
                
                // Update UI
                TablesListBox.Items.Clear();
                foreach (var table in _tables)
                {
                    TablesListBox.Items.Add(table);
                }
                
                StatusTextBlock.Text = $"Found {_tables.Count} potential tables";
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"Error scanning for tables: {ex.Message}\r\n");
                StatusTextBlock.Text = "Error scanning for tables";
                MessageBox.Show($"Error scanning for tables: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        
        private async Task ScanDataStructuresAsync()
        {
            if (_firebirdReader == null)
            {
                MessageBox.Show("Please open a database file first.", "No File Open", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                StatusTextBlock.Text = "Scanning data structures...";
                Mouse.OverrideCursor = Cursors.Wait;
                
                // Use Task.Run to perform CPU-intensive work on background thread
                var resultText = await Task.Run(() =>
                {
                    var sb = new StringBuilder();
                    
                    if (_firebirdReader != null)
                    {
                        // Get record information from Firebird reader
                        var records = _firebirdReader.GetRecords();
                        var recordsBySize = records.GroupBy(r => r.Size)
                                           .OrderByDescending(g => g.Count())
                                           .ToList();
                                           
                        sb.AppendLine($"Found {records.Count} potential records across {recordsBySize.Count} different sizes");
                        sb.AppendLine();
                        
                        foreach (var group in recordsBySize)
                        {
                            sb.AppendLine($"Record size: {group.Key} bytes (Count: {group.Count()})");
                            
                            // Find field information for this size if available
                            var metadata = _firebirdReader.GetMetadata();
                            var key = $"RecordStructure_{group.Key}";
                            
                            if (metadata.ContainsKey(key) && metadata[key] is List<FirebirdDatabaseReader.FieldInfo> fields)
                            {
                                sb.AppendLine($"  Fields detected: {fields.Count}");
                                
                                foreach (var field in fields)
                                {
                                    sb.AppendLine($"  - Offset: {field.Offset}, Size: {field.Size}, Type: {field.DataType}");
                                }
                                
                                sb.AppendLine();
                                
                                // Get a sample record
                                if (group.Any())
                                {
                                    var sample = group.First();
                                    sb.AppendLine("  Sample values:");
                                    
                                    foreach (var field in fields)
                                    {
                                        if (field.Offset + field.Size <= sample.Data.Length)
                                        {
                                            byte[] fieldData = new byte[field.Size];
                                            Array.Copy(sample.Data, field.Offset, fieldData, 0, field.Size);
                                            
                                            // Try to convert to appropriate type
                                            string value = "(binary data)";
                                            
                                            if (field.DataType == FirebirdDatabaseReader.FieldDataType.String)
                                            {
                                                // Find null terminator if any
                                                int length = 0;
                                                while (length < fieldData.Length && fieldData[length] != 0)
                                                    length++;
                                                    
                                                value = Encoding.ASCII.GetString(fieldData, 0, length);
                                            }
                                            else if (field.DataType == FirebirdDatabaseReader.FieldDataType.Integer)
                                            {
                                                if (field.Size >= 4)
                                                    value = BitConverter.ToInt32(fieldData, 0).ToString();
                                                else if (field.Size >= 2)
                                                    value = BitConverter.ToInt16(fieldData, 0).ToString();
                                                else
                                                    value = fieldData[0].ToString();
                                            }
                                            
                                            sb.AppendLine($"    Field at offset {field.Offset}: {value}");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                sb.AppendLine("  No detailed field structure available");
                            }
                            
                            sb.AppendLine();
                        }
                    }
                    else
                    {
                        // No reader available
                        sb.AppendLine("No database reader available.");
                    }
                    
                    return sb.ToString();
                });
                
                // Update UI with results
                DataStructuresScanTextBox.Text = resultText;
                
                // Update log
                LogTextBox.Text = _firebirdReader?.GetLog() ?? string.Empty;
                StatusTextBlock.Text = "Data structure scan complete";
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"Error scanning data structures: {ex.Message}\r\n");
                StatusTextBlock.Text = "Error scanning data structures";
                MessageBox.Show($"Error scanning data structures: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        
        private void FilterTables()
        {
            if (_tables.Count == 0)
                return;
                
            string filter = TableFilterTextBox.Text?.ToLower() ?? "";
            
            if (string.IsNullOrEmpty(filter))
            {
                // Show all tables
                TablesListBox.Items.Clear();
                foreach (var table in _tables)
                {
                    TablesListBox.Items.Add(table);
                }
            }
            else
            {
                // Apply filter
                TablesListBox.Items.Clear();
                foreach (var table in _tables.Where(t => t.ToLower().Contains(filter)))
                {
                    TablesListBox.Items.Add(table);
                }
            }
        }
        
        private void ExportMetadata()
        {
            if (_firebirdReader == null)
            {
                MessageBox.Show("Please open a database file first.", "No File Open", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var dialog = new SaveFileDialog
            {
                Title = "Save Metadata",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(_filePath) + "_metadata.txt",
                DefaultExt = "txt"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Combine all information into one file
                    var sb = new StringBuilder();
                    
                    sb.AppendLine("FIREBIRD DATABASE ANALYSIS REPORT");
                    sb.AppendLine("=================================");
                    sb.AppendLine();
                    
                    sb.AppendLine("HEADER ANALYSIS");
                    sb.AppendLine("--------------");
                    sb.AppendLine(HeaderAnalysisTextBox.Text);
                    sb.AppendLine();
                    
                    sb.AppendLine("METADATA");
                    sb.AppendLine("--------");
                    sb.AppendLine(MetadataTextBox.Text);
                    sb.AppendLine();
                    
                    if (!string.IsNullOrEmpty(DataStructuresTextBox.Text))
                    {
                        sb.AppendLine("DATA STRUCTURES");
                        sb.AppendLine("--------------");
                        sb.AppendLine(DataStructuresTextBox.Text);
                        sb.AppendLine();
                    }
                    
                    sb.AppendLine("POTENTIAL TABLES");
                    sb.AppendLine("---------------");
                    foreach (var table in _tables)
                    {
                        sb.AppendLine(table);
                    }
                    sb.AppendLine();
                    
                    sb.AppendLine("LOG");
                    sb.AppendLine("---");
                    sb.AppendLine(LogTextBox.Text);
                    
                    File.WriteAllText(dialog.FileName, sb.ToString());
                    
                    StatusTextBlock.Text = "Metadata exported successfully";
                    MessageBox.Show("Metadata exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error exporting metadata";
                    MessageBox.Show($"Error exporting metadata: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private void ExportLog()
        {
            if (_firebirdReader == null)
            {
                MessageBox.Show("Please open a database file first.", "No File Open", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            var dialog = new SaveFileDialog
            {
                Title = "Save Log",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(_filePath) + "_log.txt",
                DefaultExt = "txt"
            };
            
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, LogTextBox.Text);
                    
                    StatusTextBlock.Text = "Log exported successfully";
                    MessageBox.Show("Log exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error exporting log";
                    MessageBox.Show($"Error exporting log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private async Task ViewRawDumpAsync()
        {
            if (_firebirdReader == null)
            {
                MessageBox.Show("Please open a database file first.", "No File Open", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            try
            {
                // Get offset and length from text boxes
                if (!long.TryParse(OffsetTextBox.Text, out long offset))
                {
                    MessageBox.Show("Invalid offset value. Please enter a valid number.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (!int.TryParse(LengthTextBox.Text, out int length) || length <= 0 || length > 16384)
                {
                    MessageBox.Show("Invalid length value. Please enter a number between 1 and 16384.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                StatusTextBlock.Text = "Reading data...";
                Mouse.OverrideCursor = Cursors.Wait;
                
                // Read the specified portion of the file
                await Task.Run(async () =>
                {
                    try
                    {
                        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        
                        // Check if offset is valid
                        if (offset < 0 || offset >= fs.Length)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show($"Invalid offset. Must be between 0 and {fs.Length - 1}.", "Invalid Offset", MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                            return;
                        }
                        
                        // Adjust length if needed
                        if (offset + length > fs.Length)
                        {
                            length = (int)(fs.Length - offset);
                        }
                        
                        // Read the data
                        var buffer = new byte[length];
                        fs.Position = offset;
                        await fs.ReadAsync(buffer, 0, length).ConfigureAwait(false);
                        
                        // Generate hex dump
                        string hexDump = FirebirdDatabaseReader.HexDump(buffer, (int)offset, length);
                        
                        // Update UI
                        Dispatcher.Invoke(() =>
                        {
                            RawDumpTextBox.Text = hexDump;
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogTextBox.AppendText($"Error reading file: {ex.Message}\r\n");
                            MessageBox.Show($"Error reading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
                
                StatusTextBlock.Text = $"Read {length} bytes from offset {offset:X8}";
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"Error: {ex.Message}\r\n");
                StatusTextBlock.Text = "Error reading data";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        
        private void ViewTableDetails(string tableName)
        {
            try
            {
                // If using FirebirdReader and we have a table that looks like a page reference
                if (_firebirdReader != null && tableName.StartsWith("DataTable_") && tableName.Contains("_Page"))
                {
                    StatusTextBlock.Text = $"Analyzing data for {tableName}...";
                    Mouse.OverrideCursor = Cursors.Wait;
                    
                    try
                    {
                        // Extract page number from the table name
                        string pageNumberStr = tableName.Split('_').Last().Replace("Page", "");
                        if (int.TryParse(pageNumberStr, out int pageNumber))
                        {
                            // Find page data
                            var page = _firebirdReader.GetPages().FirstOrDefault(p => p.PageNumber == pageNumber);
                            
                            if (page != null && page.Data != null)
                            {
                                // Show hex dump of this page
                                string hexDump = FirebirdDatabaseReader.HexDump(page.Data, (int)page.Offset, page.Data.Length);
                                RawDumpTextBox.Text = $"Data Page {pageNumber} at offset 0x{page.Offset:X8}:\r\n\r\n" + hexDump;
                                
                                // Set offset for viewing in raw dump tab
                                OffsetTextBox.Text = page.Offset.ToString();
                                LengthTextBox.Text = page.Data.Length.ToString();
                                
                                // Show records from this page
                                var records = _firebirdReader.GetRecords().Where(r => r.PageOffset == page.Offset).ToList();
                                var structureInfo = new StringBuilder();
                                
                                structureInfo.AppendLine($"Data Page {pageNumber} at offset 0x{page.Offset:X8}");
                                structureInfo.AppendLine($"Page Type: {page.PageType}, Type Marker: 0x{page.TypeMarker:X2}");
                                structureInfo.AppendLine($"Contains {records.Count} records");
                                structureInfo.AppendLine();
                                
                                if (records.Count > 0)
                                {
                                    var recordsBySize = records.GroupBy(r => r.Size)
                                                              .OrderByDescending(g => g.Count())
                                                              .ToList();
                                                              
                                    foreach (var group in recordsBySize)
                                    {
                                        structureInfo.AppendLine($"Record size: {group.Key} bytes (Count: {group.Count()})");
                                        
                                        // Show first record in hex
                                        var firstRecord = group.First();
                                        structureInfo.AppendLine("First record:");
                                        structureInfo.AppendLine(FirebirdDatabaseReader.HexDump(firstRecord.Data, firstRecord.RecordOffset, firstRecord.Size));
                                        structureInfo.AppendLine();
                                    }
                                }
                                
                                DataStructuresTextBox.Text = structureInfo.ToString();
                            }
                            else
                            {
                                MessageBox.Show($"Could not find page {pageNumber}", "Page Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                    finally
                    {
                        StatusTextBlock.Text = $"Viewing data for {tableName}";
                        Mouse.OverrideCursor = null;
                    }
                }
                else
                {
                    // For now, just show a message
                    MessageBox.Show($"Table details for {tableName} are not available in this version.", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"Error viewing table details: {ex.Message}\r\n");
                StatusTextBlock.Text = "Error viewing table details";
                MessageBox.Show($"Error viewing table details: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        #endregion
    }
}