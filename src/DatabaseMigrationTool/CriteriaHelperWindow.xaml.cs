using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace DatabaseMigrationTool
{
    public partial class CriteriaHelperWindow : Window
    {
        private readonly List<TableSchema> _tables;
        private readonly Dictionary<string, List<Models.ColumnDefinition>> _tableColumns;
        private readonly Dictionary<string, TextBox> _criteriaTextBoxes;
        private readonly Dictionary<string, string>? _existingCriteria;

        public string? SavedFilePath { get; private set; }

        public CriteriaHelperWindow(List<TableSchema> tables, Dictionary<string, List<Models.ColumnDefinition>> tableColumns, string? existingCriteriaFilePath = null)
        {
            InitializeComponent();
            _tables = tables ?? new List<TableSchema>();
            _tableColumns = tableColumns ?? new Dictionary<string, List<Models.ColumnDefinition>>();
            _criteriaTextBoxes = new Dictionary<string, TextBox>();
            
            // Load existing criteria if file path provided
            _existingCriteria = LoadExistingCriteria(existingCriteriaFilePath);
            
            BuildCriteriaInterface();
            UpdateJsonPreview();
        }

        private Dictionary<string, string>? LoadExistingCriteria(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            catch (Exception ex)
            {
                // Log error but don't fail - just return null
                System.Diagnostics.Debug.WriteLine($"Failed to load existing criteria: {ex.Message}");
                return null;
            }
        }

        private void BuildCriteriaInterface()
        {
            CriteriaPanel.Children.Clear();
            _criteriaTextBoxes.Clear();

            foreach (var table in _tables)
            {
                var groupBox = new GroupBox
                {
                    Header = table.FullName,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var stackPanel = new StackPanel();

                // Show available columns as reference
                if (_tableColumns.TryGetValue(table.Name, out var columns) && columns.Any())
                {
                    var columnsText = new TextBlock
                    {
                        Text = $"Available columns: {string.Join(", ", columns.Select(c => c.Name))}",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    stackPanel.Children.Add(columnsText);
                }

                // Criteria input
                var label = new TextBlock
                {
                    Text = "WHERE criteria:",
                    Margin = new Thickness(0, 0, 0, 2)
                };
                stackPanel.Children.Add(label);

                var textBox = new TextBox
                {
                    Height = 60,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                // Add some example placeholder text based on column types
                if (_tableColumns.TryGetValue(table.Name, out var cols) && cols.Any())
                {
                    var exampleCriteria = GenerateExampleCriteria(cols);
                    if (!string.IsNullOrEmpty(exampleCriteria))
                    {
                        var exampleText = new TextBlock
                        {
                            Text = $"Example: {exampleCriteria}",
                            FontStyle = FontStyles.Italic,
                            FontSize = 10,
                            Foreground = System.Windows.Media.Brushes.DarkGray,
                            Margin = new Thickness(0, 0, 0, 5)
                        };
                        stackPanel.Children.Add(exampleText);
                    }
                }

                // Pre-populate from existing criteria if available
                if (_existingCriteria?.TryGetValue(table.Name, out var existingCriteria) == true)
                {
                    textBox.Text = existingCriteria;
                }

                textBox.TextChanged += (s, e) => {
                    ValidateCriteria(textBox, table.Name);
                    UpdateJsonPreview();
                };
                
                _criteriaTextBoxes[table.Name] = textBox;
                stackPanel.Children.Add(textBox);

                groupBox.Content = stackPanel;
                CriteriaPanel.Children.Add(groupBox);
            }
        }

        private string GenerateExampleCriteria(List<Models.ColumnDefinition> columns)
        {
            // Generate simple examples based on common column patterns
            var examples = new List<string>();

            var idColumn = columns.FirstOrDefault(c => c.Name.ToLower().Contains("id"));
            if (idColumn != null)
            {
                examples.Add($"{idColumn.Name} > 0");
            }

            var dateColumn = columns.FirstOrDefault(c => 
                c.Name.ToLower().Contains("date") || 
                c.Name.ToLower().Contains("created") ||
                c.Name.ToLower().Contains("modified") ||
                c.DataType.ToLower().Contains("date") ||
                c.DataType.ToLower().Contains("time"));
            
            if (dateColumn != null)
            {
                examples.Add($"{dateColumn.Name} >= '2023-01-01'");
            }

            var nameColumn = columns.FirstOrDefault(c => 
                c.Name.ToLower().Contains("name") ||
                c.Name.ToLower().Contains("title") ||
                c.DataType.ToLower().Contains("varchar") ||
                c.DataType.ToLower().Contains("text"));
            
            if (nameColumn != null)
            {
                examples.Add($"{nameColumn.Name} LIKE '%search%'");
            }

            return string.Join(" AND ", examples.Take(2));
        }

        private void ValidateCriteria(TextBox textBox, string tableName)
        {
            var criteriaText = textBox.Text?.Trim();
            if (string.IsNullOrEmpty(criteriaText))
            {
                // Clear any validation styling
                textBox.ClearValue(TextBox.BorderBrushProperty);
                textBox.ClearValue(TextBox.ToolTipProperty);
                return;
            }

            // Get available columns for this table
            if (!_tableColumns.TryGetValue(tableName, out var columns) || !columns.Any())
            {
                return; // Can't validate without column information
            }

            var availableColumns = columns.Select(c => c.Name.ToLower()).ToHashSet();
            var validationIssues = new List<string>();

            // Simple validation - look for column-like patterns in the criteria
            // This is a basic implementation that looks for common SQL patterns
            var potentialColumnReferences = ExtractPotentialColumnNames(criteriaText);
            
            foreach (var columnRef in potentialColumnReferences)
            {
                if (!availableColumns.Contains(columnRef.ToLower()))
                {
                    validationIssues.Add($"Column '{columnRef}' not found in table '{tableName}'");
                }
            }

            // Check for common SQL syntax issues
            ValidateSqlSyntax(criteriaText, validationIssues);

            if (validationIssues.Any())
            {
                // Show validation error
                textBox.BorderBrush = System.Windows.Media.Brushes.Red;
                textBox.BorderThickness = new Thickness(2);
                textBox.ToolTip = string.Join("\n", validationIssues);
            }
            else
            {
                // Clear validation styling
                textBox.ClearValue(TextBox.BorderBrushProperty);
                textBox.ClearValue(TextBox.ToolTipProperty);
            }
        }

        private HashSet<string> ExtractPotentialColumnNames(string criteriaText)
        {
            var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Basic regex patterns to identify potential column references
            // This looks for identifiers that appear to be column references in common SQL patterns
            
            // Pattern 1: ColumnName = 'value' or ColumnName > 123
            var basicPattern = @"\b([a-zA-Z_][a-zA-Z0-9_]*)\s*[=<>!]+";
            var basicMatches = Regex.Matches(criteriaText, basicPattern);
            foreach (Match match in basicMatches)
            {
                var columnName = match.Groups[1].Value.Trim();
                if (!IsReservedWord(columnName))
                {
                    columnNames.Add(columnName);
                }
            }

            // Pattern 2: ColumnName LIKE 'pattern' or ColumnName IN (values)
            var likeInPattern = @"\b([a-zA-Z_][a-zA-Z0-9_]*)\s+(LIKE|IN|NOT\s+IN|IS\s+NULL|IS\s+NOT\s+NULL)\b";
            var likeInMatches = Regex.Matches(criteriaText, likeInPattern, RegexOptions.IgnoreCase);
            foreach (Match match in likeInMatches)
            {
                var columnName = match.Groups[1].Value.Trim();
                if (!IsReservedWord(columnName))
                {
                    columnNames.Add(columnName);
                }
            }

            // Pattern 3: ColumnName BETWEEN value1 AND value2
            var betweenPattern = @"\b([a-zA-Z_][a-zA-Z0-9_]*)\s+BETWEEN\b";
            var betweenMatches = Regex.Matches(criteriaText, betweenPattern, RegexOptions.IgnoreCase);
            foreach (Match match in betweenMatches)
            {
                var columnName = match.Groups[1].Value.Trim();
                if (!IsReservedWord(columnName))
                {
                    columnNames.Add(columnName);
                }
            }

            return columnNames;
        }

        private bool IsReservedWord(string word)
        {
            // Common SQL reserved words that should not be treated as column names
            var reservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AND", "OR", "NOT", "NULL", "TRUE", "FALSE", "LIKE", "IN", "BETWEEN", 
                "IS", "AS", "ON", "FROM", "WHERE", "SELECT", "INSERT", "UPDATE", "DELETE",
                "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "UNION", "EXISTS", "CASE", "WHEN", "THEN", "ELSE", "END"
            };
            return reservedWords.Contains(word);
        }

        private void ValidateSqlSyntax(string criteriaText, List<string> validationIssues)
        {
            // Check for problematic date formats
            ValidateDateFormats(criteriaText, validationIssues);
            
            // Check for quote issues
            ValidateQuotes(criteriaText, validationIssues);
            
            // Check for other common syntax issues
            ValidateGeneralSyntax(criteriaText, validationIssues);
        }

        private void ValidateDateFormats(string criteriaText, List<string> validationIssues)
        {
            // Check for problematic date patterns
            
            // Pattern 1: Any date-like pattern with mixed separators (like 2025/04-05)
            var mixedSeparatorPattern = @"'\d{4}[/-]\d{1,2}[/-]\d{1,2}'";
            var mixedMatches = Regex.Matches(criteriaText, mixedSeparatorPattern);
            foreach (Match match in mixedMatches)
            {
                var dateValue = match.Value;
                if ((dateValue.Contains("/") && dateValue.Contains("-")) || 
                    dateValue.Contains("/"))
                {
                    validationIssues.Add($"Invalid date format '{dateValue}': Use consistent 'YYYY-MM-DD' format (e.g., '2025-04-05')");
                }
            }

            // Pattern 2: Quoted dates with any non-hyphen separators
            var nonHyphenDatePattern = @"'\d{4}[^\d\-']\d{1,2}[^\d\-']\d{1,2}'";
            var nonHyphenMatches = Regex.Matches(criteriaText, nonHyphenDatePattern);
            foreach (Match match in nonHyphenMatches)
            {
                validationIssues.Add($"Date format '{match.Value}': Use hyphens for SQL Server dates (e.g., '2025-04-01')");
            }

            // Pattern 3: Dates with potential quote issues like '2025'04/01'
            var brokenDatePattern = @"'\d{4}'\d";
            if (Regex.IsMatch(criteriaText, brokenDatePattern))
            {
                validationIssues.Add("Broken date format detected: Check for improperly closed quotes in date values");
            }

            // Pattern 4: Unquoted date-like patterns
            var unquotedDatePatterns = new[]
            {
                @"\b\d{4}-\d{1,2}-\d{1,2}\b(?!')",     // 2025-04-01
                @"\b\d{4}/\d{1,2}/\d{1,2}\b(?!')",     // 2025/04/01
                @"\b\d{4}/\d{1,2}-\d{1,2}\b(?!')",     // 2025/04-01 (mixed)
                @"\b\d{4}-\d{1,2}/\d{1,2}\b(?!')"      // 2025-04/01 (mixed)
            };

            foreach (var pattern in unquotedDatePatterns)
            {
                var matches = Regex.Matches(criteriaText, pattern);
                foreach (Match match in matches)
                {
                    validationIssues.Add($"Unquoted date '{match.Value}': Date values should be enclosed in single quotes and use 'YYYY-MM-DD' format (e.g., '2025-04-01')");
                }
            }

            // Pattern 5: Check for common invalid date formats even when quoted
            var invalidQuotedDatePatterns = new[]
            {
                (@"'\d{4}\.\d{1,2}\.\d{1,2}'", "dots"), // 2025.04.01
                (@"'\d{1,2}/\d{1,2}/\d{4}'", "MM/DD/YYYY"), // 04/01/2025
                (@"'\d{1,2}-\d{1,2}-\d{4}'", "MM-DD-YYYY"), // 04-01-2025
            };

            foreach (var (pattern, formatName) in invalidQuotedDatePatterns)
            {
                var matches = Regex.Matches(criteriaText, pattern);
                foreach (Match match in matches)
                {
                    validationIssues.Add($"Invalid date format '{match.Value}' ({formatName}): Use 'YYYY-MM-DD' format (e.g., '2025-04-01')");
                }
            }

            // Pattern 6: Check for incomplete dates
            var incompleteDatePattern = @"'\d{4}[/-]?\d{0,2}[/-]?\d{0,2}'";
            var incompleteMatches = Regex.Matches(criteriaText, incompleteDatePattern);
            foreach (Match match in incompleteMatches)
            {
                var dateValue = match.Value.Trim('\'');
                // Count digits to see if it looks like an incomplete date
                var digitCount = dateValue.Count(char.IsDigit);
                var separatorCount = dateValue.Count(c => c == '/' || c == '-');
                
                if (digitCount < 8 && separatorCount > 0) // Less than YYYYMMDD and has separators
                {
                    validationIssues.Add($"Incomplete date '{match.Value}': Ensure full date format 'YYYY-MM-DD' (e.g., '2025-04-01')");
                }
            }
        }

        private void ValidateQuotes(string criteriaText, List<string> validationIssues)
        {
            // Count single quotes - should be even number
            int singleQuoteCount = criteriaText.Count(c => c == '\'');
            if (singleQuoteCount % 2 != 0)
            {
                validationIssues.Add("Unmatched single quotes detected - ensure all string values are properly quoted");
            }

            // Check for common quote escape issues
            if (criteriaText.Contains("''"))
            {
                // This might be intentional (escaped quote) but worth noting
                validationIssues.Add("Double quotes ('') detected - ensure this is intentional for escaping quotes in text");
            }

            // Check for Unicode-escaped quotes (like the user's original issue)
            if (criteriaText.Contains("\\u0027") || criteriaText.Contains("\\u002"))
            {
                validationIssues.Add("Unicode-escaped characters detected - use normal quotes and operators instead");
            }
        }

        private void ValidateGeneralSyntax(string criteriaText, List<string> validationIssues)
        {
            // Check for unquoted string values that should be quoted
            var unquotedStringPattern = @"=\s*[a-zA-Z][a-zA-Z0-9_]*\s*(?:AND|OR|$)";
            if (Regex.IsMatch(criteriaText, unquotedStringPattern, RegexOptions.IgnoreCase))
            {
                validationIssues.Add("Possible unquoted string value - text values should be enclosed in single quotes");
            }

            // Check for SQL injection patterns (basic detection)
            var suspiciousPatterns = new[] { "--", "/*", "*/", ";", "DROP", "DELETE FROM", "TRUNCATE", "INSERT INTO" };
            foreach (var pattern in suspiciousPatterns)
            {
                if (criteriaText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    validationIssues.Add($"Potentially unsafe SQL detected: '{pattern}' - ensure this is intentional");
                }
            }

            // Check for common operator mistakes
            if (criteriaText.Contains("=="))
            {
                validationIssues.Add("Use '=' instead of '==' for SQL equality comparison");
            }

            if (Regex.IsMatch(criteriaText, @"\b(true|false)\b", RegexOptions.IgnoreCase))
            {
                validationIssues.Add("Use 1/0 or 'true'/'false' (quoted) instead of unquoted boolean values");
            }
        }

        private void UpdateJsonPreview()
        {
            var criteria = new Dictionary<string, string>();

            foreach (var kvp in _criteriaTextBoxes)
            {
                var criteriaText = kvp.Value.Text?.Trim();
                if (!string.IsNullOrEmpty(criteriaText))
                {
                    criteria[kvp.Key] = criteriaText;
                }
            }

            if (criteria.Any())
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                JsonPreviewTextBox.Text = JsonSerializer.Serialize(criteria, options);
            }
            else
            {
                JsonPreviewTextBox.Text = "{\n  // No criteria defined yet\n}";
            }
        }

        private void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            var criteria = new Dictionary<string, string>();
            var hasValidationErrors = false;

            foreach (var kvp in _criteriaTextBoxes)
            {
                var criteriaText = kvp.Value.Text?.Trim();
                if (!string.IsNullOrEmpty(criteriaText))
                {
                    criteria[kvp.Key] = criteriaText;
                    
                    // Check if this textbox has validation errors
                    if (kvp.Value.BorderBrush == System.Windows.Media.Brushes.Red)
                    {
                        hasValidationErrors = true;
                    }
                }
            }

            if (!criteria.Any())
            {
                MessageBox.Show("Please define at least one criteria before saving.", "No Criteria", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (hasValidationErrors)
            {
                var result = MessageBox.Show(
                    "Some criteria have validation errors (red borders). Do you want to save anyway?\n\nClick 'Yes' to save with errors, 'No' to fix them first.",
                    "Validation Errors Found", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning);
                
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                FileName = "table_criteria.json",
                Title = "Save Table Criteria File"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    var json = JsonSerializer.Serialize(criteria, options);
                    File.WriteAllText(dialog.FileName, json);

                    SavedFilePath = dialog.FileName;
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save file: {ex.Message}", "Save Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}