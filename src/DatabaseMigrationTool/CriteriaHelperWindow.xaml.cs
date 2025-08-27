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
using System.Windows.Controls.Primitives;

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

                // Show available columns with type information
                if (_tableColumns.TryGetValue(table.Name, out var columns) && columns.Any())
                {
                    // Group columns by type for better display
                    var columnsByType = new Dictionary<string, List<string>>();
                    
                    foreach (var col in columns)
                    {
                        var dataType = col.DataType?.ToLower() ?? "unknown";
                        string typeCategory;
                        
                        if (IsDateTimeType(dataType))
                            typeCategory = "📅 Date/Time";
                        else if (IsNumericType(dataType))
                            typeCategory = "🔢 Numeric";
                        else if (IsStringType(dataType))
                            typeCategory = "📝 Text";
                        else if (IsBooleanType(dataType))
                            typeCategory = "☑️ Boolean";
                        else
                            typeCategory = "❓ Other";
                        
                        if (!columnsByType.ContainsKey(typeCategory))
                            columnsByType[typeCategory] = new List<string>();
                        
                        columnsByType[typeCategory].Add(col.Name);
                    }
                    
                    // Display columns grouped by type
                    foreach (var typeGroup in columnsByType.OrderBy(x => x.Key))
                    {
                        var typeText = new TextBlock
                        {
                            Text = $"{typeGroup.Key}: {string.Join(", ", typeGroup.Value)}",
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 10,
                            Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                            Margin = new Thickness(0, 0, 0, 2)
                        };
                        stackPanel.Children.Add(typeText);
                    }
                    
                    // Add separator
                    var separator = new TextBlock
                    {
                        Text = " ",
                        FontSize = 6,
                        Margin = new Thickness(0, 0, 0, 3)
                    };
                    stackPanel.Children.Add(separator);
                }

                // Criteria input
                var label = new TextBlock
                {
                    Text = "WHERE criteria:",
                    Margin = new Thickness(0, 0, 0, 2)
                };
                stackPanel.Children.Add(label);

                // Create container for text box and date picker buttons
                var textBoxContainer = new Grid();
                textBoxContainer.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                textBoxContainer.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

                var textBox = new TextBox
                {
                    Height = 60,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 0, 5, 5)
                };
                Grid.SetColumn(textBox, 0);
                textBoxContainer.Children.Add(textBox);

                // Add date picker button if table has date columns
                if (_tableColumns.TryGetValue(table.Name, out var cols) && cols.Any())
                {
                    var dateColumns = cols.Where(c => IsDateColumn(c)).ToList();
                    
                    // Debug output
                    System.Diagnostics.Debug.WriteLine($"Table {table.Name}: Found {cols.Count} columns, {dateColumns.Count} date columns");
                    foreach (var col in cols)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Column: {col.Name}, Type: {col.DataType}, IsDate: {IsDateColumn(col)}");
                    }
                    
                    if (dateColumns.Any())
                    {
                        var datePickerButton = new Button
                        {
                            Content = "📅",
                            Width = 30,
                            Height = 30,
                            ToolTip = $"Insert date for: {string.Join(", ", dateColumns.Select(c => c.Name))}",
                            Margin = new Thickness(0, 0, 0, 5),
                            VerticalAlignment = VerticalAlignment.Top
                        };
                        Grid.SetColumn(datePickerButton, 1);
                        
                        datePickerButton.Click += (s, e) => ShowDatePicker(textBox, dateColumns);
                        textBoxContainer.Children.Add(datePickerButton);
                    }
                }

                // Add some example placeholder text based on column types
                if (cols?.Any() == true)
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

                // Add autocomplete functionality
                if (cols?.Any() == true)
                {
                    AddAutoCompleteToTextBox(textBox, cols.Select(c => c.Name).ToList());
                }
                
                _criteriaTextBoxes[table.Name] = textBox;
                stackPanel.Children.Add(textBoxContainer);

                groupBox.Content = stackPanel;
                CriteriaPanel.Children.Add(groupBox);
            }
        }

        private string GenerateExampleCriteria(List<Models.ColumnDefinition> columns)
        {
            // Generate type-aware examples based on column data types and names
            var examples = new List<string>();

            // Look for ID columns (numeric)
            var idColumn = columns.FirstOrDefault(c => 
                c.Name.ToLower().Contains("id") && IsNumericType(c.DataType?.ToLower() ?? ""));
            if (idColumn != null)
            {
                examples.Add($"{idColumn.Name} > 0");
            }

            // Look for date columns
            var dateColumn = columns.FirstOrDefault(c => 
                IsDateTimeType(c.DataType?.ToLower() ?? "") ||
                c.Name.ToLower().Contains("date") || 
                c.Name.ToLower().Contains("created") ||
                c.Name.ToLower().Contains("modified"));
            
            if (dateColumn != null)
            {
                var currentYear = DateTime.Now.Year;
                examples.Add($"{dateColumn.Name} >= '{currentYear}-01-01'");
            }

            // Look for string columns
            var stringColumn = columns.FirstOrDefault(c => 
                IsStringType(c.DataType?.ToLower() ?? "") &&
                (c.Name.ToLower().Contains("name") ||
                 c.Name.ToLower().Contains("title") ||
                 c.Name.ToLower().Contains("description")));
            
            if (stringColumn != null)
            {
                examples.Add($"{stringColumn.Name} LIKE '%search%'");
            }

            // Look for boolean/bit columns
            var boolColumn = columns.FirstOrDefault(c => 
                IsBooleanType(c.DataType?.ToLower() ?? "") ||
                c.Name.ToLower().Contains("active") ||
                c.Name.ToLower().Contains("enabled") ||
                c.Name.ToLower().Contains("deleted"));
            
            if (boolColumn != null && examples.Count < 2)
            {
                var dataType = boolColumn.DataType?.ToLower() ?? "";
                if (dataType.Contains("bit"))
                {
                    examples.Add($"{boolColumn.Name} = 1");
                }
                else
                {
                    examples.Add($"{boolColumn.Name} = 'true'");
                }
            }

            // Look for status columns
            var statusColumn = columns.FirstOrDefault(c => 
                c.Name.ToLower().Contains("status") ||
                c.Name.ToLower().Contains("state") ||
                c.Name.ToLower().Contains("type"));
            
            if (statusColumn != null && examples.Count < 2)
            {
                if (IsStringType(statusColumn.DataType?.ToLower() ?? ""))
                {
                    examples.Add($"{statusColumn.Name} = 'Active'");
                }
                else if (IsNumericType(statusColumn.DataType?.ToLower() ?? ""))
                {
                    examples.Add($"{statusColumn.Name} IN (1, 2, 3)");
                }
            }

            return examples.Any() ? string.Join(" AND ", examples.Take(2)) : "column_name = 'value'";
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

            var availableColumns = columns.ToDictionary(c => c.Name.ToLower(), c => c);
            var validationIssues = new List<string>();

            // Enhanced validation - look for column-like patterns and validate against types
            var potentialColumnReferences = ExtractPotentialColumnNames(criteriaText);
            
            foreach (var columnRef in potentialColumnReferences)
            {
                var lowerColumnRef = columnRef.ToLower();
                if (!availableColumns.ContainsKey(lowerColumnRef))
                {
                    validationIssues.Add($"Column '{columnRef}' not found in table '{tableName}'");
                }
                else
                {
                    // Validate column usage against its data type
                    var column = availableColumns[lowerColumnRef];
                    ValidateColumnUsage(criteriaText, columnRef, column, validationIssues);
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

        private void ValidateColumnUsage(string criteriaText, string columnName, Models.ColumnDefinition column, List<string> validationIssues)
        {
            var dataType = column.DataType?.ToLower() ?? "";
            
            // Find all usages of this column in the criteria
            var columnPattern = $@"\b{Regex.Escape(columnName)}\b";
            var matches = Regex.Matches(criteriaText, columnPattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                // Get the context around the column usage
                var start = Math.Max(0, match.Index - 50);
                var length = Math.Min(criteriaText.Length - start, 100);
                var context = criteriaText.Substring(start, length);
                
                // Determine what type of operation is being performed
                ValidateColumnDataType(context, columnName, column, validationIssues);
                ValidateNullabilityUsage(context, columnName, column, validationIssues);
            }
        }

        private void ValidateColumnDataType(string context, string columnName, Models.ColumnDefinition column, List<string> validationIssues)
        {
            var dataType = column.DataType?.ToLower() ?? "";
            
            // Check for date/time columns
            if (IsDateTimeType(dataType))
            {
                // Look for unquoted date values
                var unquotedDatePattern = $@"{Regex.Escape(columnName)}\s*[=<>!]+\s*(\d{{4}}[-/]\d{{1,2}}[-/]\d{{1,2}})(?!')";
                if (Regex.IsMatch(context, unquotedDatePattern, RegexOptions.IgnoreCase))
                {
                    validationIssues.Add($"Date values for column '{columnName}' should be quoted (e.g., '{columnName} >= ''2025-01-01''')");
                }
                
                // Check for invalid date formats in quoted strings
                var quotedDatePattern = $@"{Regex.Escape(columnName)}\s*[=<>!]+\s*'([^']+)'";
                var dateMatches = Regex.Matches(context, quotedDatePattern, RegexOptions.IgnoreCase);
                foreach (Match match in dateMatches)
                {
                    var dateValue = match.Groups[1].Value;
                    if (!IsValidDateFormat(dateValue))
                    {
                        validationIssues.Add($"Invalid date format for column '{columnName}': '{dateValue}'. Use 'YYYY-MM-DD' format");
                    }
                }
            }
            
            // Check for numeric columns
            else if (IsNumericType(dataType))
            {
                // Look for quoted numeric values (should be unquoted)
                var quotedNumericPattern = $@"{Regex.Escape(columnName)}\s*[=<>!]+\s*'([^']*)'";
                var numericMatches = Regex.Matches(context, quotedNumericPattern, RegexOptions.IgnoreCase);
                foreach (Match match in numericMatches)
                {
                    var value = match.Groups[1].Value;
                    if (decimal.TryParse(value, out _))
                    {
                        validationIssues.Add($"Numeric values for column '{columnName}' should not be quoted (use {columnName} = {value} instead of {columnName} = '{value}')");
                    }
                }
                
                // Check for invalid numeric comparisons
                var numericValuePattern = $@"{Regex.Escape(columnName)}\s*[=<>!]+\s*([^\s']+)";
                var valueMatches = Regex.Matches(context, numericValuePattern, RegexOptions.IgnoreCase);
                foreach (Match match in valueMatches)
                {
                    var value = match.Groups[1].Value;
                    if (!decimal.TryParse(value, out _) && !IsReservedWord(value))
                    {
                        validationIssues.Add($"Invalid numeric value for column '{columnName}': '{value}'. Expected a number");
                    }
                }
            }
            
            // Check for string columns
            else if (IsStringType(dataType))
            {
                // Look for unquoted string values (except for LIKE patterns)
                var unquotedStringPattern = $@"{Regex.Escape(columnName)}\s*=\s*([a-zA-Z][a-zA-Z0-9_]*)(?!\s*(AND|OR|$))";
                if (Regex.IsMatch(context, unquotedStringPattern, RegexOptions.IgnoreCase))
                {
                    validationIssues.Add($"String values for column '{columnName}' should be quoted");
                }
            }
            
            // Check for boolean columns
            else if (IsBooleanType(dataType))
            {
                var booleanValuePattern = $@"{Regex.Escape(columnName)}\s*[=!]+\s*([^\s]+)";
                var boolMatches = Regex.Matches(context, booleanValuePattern, RegexOptions.IgnoreCase);
                foreach (Match match in boolMatches)
                {
                    var value = match.Groups[1].Value.Trim('\'', '"');
                    if (!IsBooleanValue(value))
                    {
                        validationIssues.Add($"Invalid boolean value for column '{columnName}': '{value}'. Use 1/0, true/false, or 'Y'/'N'");
                    }
                }
            }
        }

        private void ValidateNullabilityUsage(string context, string columnName, Models.ColumnDefinition column, List<string> validationIssues)
        {
            // Check for NULL comparisons on non-nullable columns
            if (!column.IsNullable)
            {
                var nullPattern = $@"{Regex.Escape(columnName)}\s+(IS\s+(NOT\s+)?NULL|=\s*NULL)";
                if (Regex.IsMatch(context, nullPattern, RegexOptions.IgnoreCase))
                {
                    validationIssues.Add($"Column '{columnName}' is NOT NULL, NULL comparisons are unnecessary");
                }
            }
        }

        private bool IsDateTimeType(string dataType)
        {
            return dataType.Contains("date") || dataType.Contains("time") || dataType.Contains("timestamp");
        }

        private bool IsNumericType(string dataType)
        {
            return dataType.Contains("int") || dataType.Contains("decimal") || dataType.Contains("numeric") || 
                   dataType.Contains("float") || dataType.Contains("double") || dataType.Contains("real") ||
                   dataType.Contains("money") || dataType.Contains("smallmoney");
        }

        private bool IsStringType(string dataType)
        {
            return dataType.Contains("char") || dataType.Contains("text") || dataType.Contains("varchar") ||
                   dataType.Contains("nvarchar") || dataType.Contains("nchar") || dataType.Contains("ntext");
        }

        private bool IsBooleanType(string dataType)
        {
            return dataType.Contains("bit") || dataType.Contains("boolean") || dataType.Contains("bool");
        }

        private bool IsValidDateFormat(string dateValue)
        {
            // Accept standard formats
            var validPatterns = new[]
            {
                @"^\d{4}-\d{2}-\d{2}$",                    // 2025-01-01
                @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", // 2025-01-01 12:30:45
                @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$"  // 2025-01-01T12:30:45
            };

            return validPatterns.Any(pattern => Regex.IsMatch(dateValue, pattern)) &&
                   DateTime.TryParse(dateValue, out _);
        }

        private bool IsBooleanValue(string value)
        {
            var validBooleans = new[] { "1", "0", "true", "false", "yes", "no", "y", "n" };
            return validBooleans.Contains(value.ToLower());
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

        private bool IsDateColumn(Models.ColumnDefinition column)
        {
            // Check if column is a date/time type
            var dataType = column.DataType?.ToLower() ?? "";
            
            // Common date/time type patterns
            var dateTimeTypes = new[]
            {
                "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset",
                "time", "timestamp", "timestamptz", 
                // SQL Server specific
                "sys.datetime", "sys.datetime2", "sys.smalldatetime", "sys.date", "sys.time", "sys.datetimeoffset",
                // MySQL specific  
                "year", "timestamp",
                // PostgreSQL specific
                "timestamptz", "timetz", 
                // Oracle specific
                "date", "timestamp",
                // Generic patterns
                "created", "modified", "updated"
            };

            // Check exact matches first
            if (dateTimeTypes.Any(dt => dataType == dt))
                return true;

            // Check if data type contains date/time keywords
            if (dataType.Contains("date") || dataType.Contains("time") || dataType.Contains("timestamp"))
                return true;

            // Check column name patterns (common date column naming)
            var columnName = column.Name?.ToLower() ?? "";
            var dateNamePatterns = new[]
            {
                "date", "created", "modified", "updated", "timestamp", "time",
                "saledate", "orderdate", "birthdate", "startdate", "enddate"
            };

            return dateNamePatterns.Any(pattern => columnName.Contains(pattern));
        }

        private void ShowDatePicker(TextBox textBox, List<Models.ColumnDefinition> dateColumns)
        {
            // Create a simple date picker dialog
            var datePickerWindow = new Window
            {
                Title = "Insert Date Criteria",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.Margin = new Thickness(15);

            // Instructions
            var instructionText = new TextBlock
            {
                Text = "Select dates to insert into your criteria:",
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.Bold
            };
            Grid.SetRow(instructionText, 0);
            mainGrid.Children.Add(instructionText);

            // Column selection
            var columnLabel = new TextBlock
            {
                Text = "Column:",
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(columnLabel, 1);
            mainGrid.Children.Add(columnLabel);

            var columnComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 10)
            };
            foreach (var col in dateColumns)
            {
                columnComboBox.Items.Add(col.Name);
            }
            columnComboBox.SelectedIndex = 0;
            Grid.SetRow(columnComboBox, 2);
            mainGrid.Children.Add(columnComboBox);

            // Date pickers
            var dateGrid = new Grid();
            dateGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            dateGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            dateGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            dateGrid.Margin = new Thickness(0, 0, 0, 15);

            var fromLabel = new TextBlock { Text = "From:", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(fromLabel, 0);
            dateGrid.Children.Add(fromLabel);

            var fromDatePicker = new DatePicker
            {
                SelectedDate = DateTime.Today.AddMonths(-1),
                Margin = new Thickness(0, 5, 10, 5)
            };
            Grid.SetColumn(fromDatePicker, 0);
            Grid.SetRow(fromDatePicker, 1);
            dateGrid.Children.Add(fromDatePicker);

            var toLabel = new TextBlock 
            { 
                Text = "To:", 
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(toLabel, 2);
            dateGrid.Children.Add(toLabel);

            var toDatePicker = new DatePicker
            {
                SelectedDate = DateTime.Today,
                Margin = new Thickness(10, 5, 0, 5)
            };
            Grid.SetColumn(toDatePicker, 2);
            Grid.SetRow(toDatePicker, 1);
            dateGrid.Children.Add(toDatePicker);

            dateGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dateGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(dateGrid, 3);
            mainGrid.Children.Add(dateGrid);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var insertButton = new Button
            {
                Content = "Insert Range",
                Margin = new Thickness(5, 5, 5, 5),
                Padding = new Thickness(15, 5, 15, 5),
                IsDefault = true
            };

            var insertSingleButton = new Button
            {
                Content = "Insert Single Date",
                Margin = new Thickness(5, 5, 5, 5),
                Padding = new Thickness(15, 5, 15, 5)
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Margin = new Thickness(5, 5, 5, 5),
                Padding = new Thickness(15, 5, 15, 5),
                IsCancel = true
            };

            insertButton.Click += (s, e) =>
            {
                if (fromDatePicker.SelectedDate.HasValue && toDatePicker.SelectedDate.HasValue && columnComboBox.SelectedItem != null)
                {
                    var columnName = columnComboBox.SelectedItem.ToString();
                    var fromDate = fromDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd");
                    var toDate = toDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd");
                    var criteria = $"{columnName} >= '{fromDate}' AND {columnName} <= '{toDate}'";
                    
                    InsertTextIntoTextBox(textBox, criteria);
                    datePickerWindow.DialogResult = true;
                    datePickerWindow.Close();
                }
            };

            insertSingleButton.Click += (s, e) =>
            {
                if (fromDatePicker.SelectedDate.HasValue && columnComboBox.SelectedItem != null)
                {
                    var columnName = columnComboBox.SelectedItem.ToString();
                    var date = fromDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd");
                    var criteria = $"{columnName} = '{date}'";
                    
                    InsertTextIntoTextBox(textBox, criteria);
                    datePickerWindow.DialogResult = true;
                    datePickerWindow.Close();
                }
            };

            cancelButton.Click += (s, e) =>
            {
                datePickerWindow.DialogResult = false;
                datePickerWindow.Close();
            };

            buttonPanel.Children.Add(insertSingleButton);
            buttonPanel.Children.Add(insertButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 4);
            mainGrid.Children.Add(buttonPanel);

            datePickerWindow.Content = mainGrid;
            datePickerWindow.ShowDialog();
        }

        private void InsertTextIntoTextBox(TextBox textBox, string criteria)
        {
            var currentText = textBox.Text ?? "";
            var cursorPosition = textBox.CaretIndex;

            // Smart insertion - add AND if there's existing text
            string textToInsert = criteria;
            if (!string.IsNullOrEmpty(currentText.Trim()))
            {
                // Check if we need to add AND
                var beforeCursor = currentText.Substring(0, cursorPosition).Trim();
                var afterCursor = currentText.Substring(cursorPosition).Trim();
                
                if (!string.IsNullOrEmpty(beforeCursor) && !beforeCursor.EndsWith("AND", StringComparison.OrdinalIgnoreCase) && !beforeCursor.EndsWith("OR", StringComparison.OrdinalIgnoreCase))
                {
                    textToInsert = " AND " + criteria;
                }
                else if (string.IsNullOrEmpty(beforeCursor) && !string.IsNullOrEmpty(afterCursor))
                {
                    textToInsert = criteria + " AND ";
                }
            }

            // Insert the text
            var newText = currentText.Insert(cursorPosition, textToInsert);
            textBox.Text = newText;
            textBox.CaretIndex = cursorPosition + textToInsert.Length;
            textBox.Focus();
        }

        private void AddAutoCompleteToTextBox(TextBox textBox, List<string> columnNames)
        {
            // Store autocomplete state in textbox tag or attached property
            var autoCompleteState = new AutoCompleteState();
            textBox.Tag = autoCompleteState;

            textBox.PreviewKeyDown += (sender, e) =>
            {
                var state = (sender as TextBox)?.Tag as AutoCompleteState;
                if (state?.Popup?.IsOpen == true && state.ListBox != null)
                {
                    switch (e.Key)
                    {
                        case System.Windows.Input.Key.Down:
                            if (state.ListBox.SelectedIndex < state.ListBox.Items.Count - 1)
                                state.ListBox.SelectedIndex++;
                            e.Handled = true;
                            break;
                        case System.Windows.Input.Key.Up:
                            if (state.ListBox.SelectedIndex > 0)
                                state.ListBox.SelectedIndex--;
                            e.Handled = true;
                            break;
                        case System.Windows.Input.Key.Enter:
                        case System.Windows.Input.Key.Tab:
                            if (state.ListBox.SelectedItem != null)
                            {
                                InsertAutoCompleteItem(textBox, state.ListBox.SelectedItem.ToString()!);
                                state.Popup.IsOpen = false;
                            }
                            e.Handled = e.Key == System.Windows.Input.Key.Enter;
                            break;
                        case System.Windows.Input.Key.Escape:
                            state.Popup.IsOpen = false;
                            e.Handled = true;
                            break;
                    }
                }
            };

            textBox.TextChanged += (sender, e) =>
            {
                var tb = sender as TextBox;
                var state = tb?.Tag as AutoCompleteState;
                if (tb != null && state != null)
                {
                    ShowAutoCompleteSuggestions(tb, columnNames, state);
                }
            };

            textBox.LostFocus += (sender, e) =>
            {
                var state = (sender as TextBox)?.Tag as AutoCompleteState;
                if (state?.Popup != null)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        new Action(() => state.Popup.IsOpen = false),
                        System.Windows.Threading.DispatcherPriority.Input);
                }
            };
        }

        private void ShowAutoCompleteSuggestions(TextBox textBox, List<string> columnNames, AutoCompleteState state)
        {
            var text = textBox.Text ?? "";
            var caretIndex = textBox.CaretIndex;

            // Find the word being typed at cursor position
            var currentWord = GetCurrentWordAtCursor(text, caretIndex);
            
            if (string.IsNullOrEmpty(currentWord) || currentWord.Length < 2)
            {
                state.Popup?.SetCurrentValue(Popup.IsOpenProperty, false);
                return;
            }

            // Find matching column names
            var matches = columnNames
                .Where(col => col.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase))
                .OrderBy(col => col)
                .Take(10)
                .ToList();

            if (!matches.Any())
            {
                state.Popup?.SetCurrentValue(Popup.IsOpenProperty, false);
                return;
            }

            // Create or update popup
            if (state.Popup == null)
            {
                state.ListBox = new ListBox
                {
                    MaxHeight = 150,
                    MinWidth = 200,
                    Background = System.Windows.Media.Brushes.White,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(1, 1, 1, 1)
                };

                state.ListBox.MouseDoubleClick += (s, e) =>
                {
                    if (state.ListBox.SelectedItem != null)
                    {
                        InsertAutoCompleteItem(textBox, state.ListBox.SelectedItem.ToString()!);
                        state.Popup!.IsOpen = false;
                    }
                };

                state.Popup = new Popup
                {
                    Child = state.ListBox,
                    PlacementTarget = textBox,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false,
                    AllowsTransparency = true
                };
            }

            // Update suggestions
            state.ListBox!.Items.Clear();
            foreach (var match in matches)
            {
                state.ListBox.Items.Add(match);
            }

            if (state.ListBox.Items.Count > 0)
            {
                state.ListBox.SelectedIndex = 0;
                
                // Position popup at cursor
                var rect = textBox.GetRectFromCharacterIndex(caretIndex);
                state.Popup.HorizontalOffset = rect.X;
                state.Popup.VerticalOffset = rect.Y + rect.Height;
                state.Popup.IsOpen = true;
            }
        }

        private string GetCurrentWordAtCursor(string text, int caretIndex)
        {
            if (string.IsNullOrEmpty(text) || caretIndex <= 0)
                return "";

            // Find word boundaries around cursor
            var wordStart = caretIndex;
            var wordEnd = caretIndex;

            // Go backwards to find word start
            while (wordStart > 0 && IsWordCharacter(text[wordStart - 1]))
                wordStart--;

            // Go forwards to find word end  
            while (wordEnd < text.Length && IsWordCharacter(text[wordEnd]))
                wordEnd++;

            if (wordEnd > wordStart)
                return text.Substring(wordStart, wordEnd - wordStart);

            return "";
        }

        private bool IsWordCharacter(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private void InsertAutoCompleteItem(TextBox textBox, string columnName)
        {
            var text = textBox.Text ?? "";
            var caretIndex = textBox.CaretIndex;
            
            // Find the current word to replace
            var wordStart = caretIndex;
            var wordEnd = caretIndex;

            // Go backwards to find word start
            while (wordStart > 0 && IsWordCharacter(text[wordStart - 1]))
                wordStart--;

            // Go forwards to find word end
            while (wordEnd < text.Length && IsWordCharacter(text[wordEnd]))
                wordEnd++;

            // Replace the current word with the selected column name
            var beforeWord = text.Substring(0, wordStart);
            var afterWord = text.Substring(wordEnd);
            var newText = beforeWord + columnName + afterWord;

            textBox.Text = newText;
            textBox.CaretIndex = wordStart + columnName.Length;
            textBox.Focus();
        }

        private class AutoCompleteState
        {
            public Popup? Popup { get; set; }
            public ListBox? ListBox { get; set; }
        }
    }
}