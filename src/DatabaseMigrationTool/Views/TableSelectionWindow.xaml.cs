using DatabaseMigrationTool.Models;
using DatabaseMigrationTool.Providers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DatabaseMigrationTool.Views
{
    public partial class TableSelectionWindow : Window
    {
        private ObservableCollection<TableSelectionItem> _allTables;
        private CollectionViewSource _tablesViewSource;
        private IDatabaseProvider _provider;
        private string _connectionString;
        private List<TableSchema>? _cachedTables;

        public List<string> SelectedTableNames { get; private set; } = new List<string>();
        public List<TableSchema>? LoadedTables { get; private set; }

        public TableSelectionWindow(IDatabaseProvider provider, string connectionString, List<string>? preselectedTables = null, List<TableSchema>? cachedTables = null)
        {
            InitializeComponent();
            
            _provider = provider;
            _connectionString = connectionString;
            _cachedTables = cachedTables;
            _allTables = new ObservableCollection<TableSelectionItem>();
            _tablesViewSource = new CollectionViewSource { Source = _allTables };
            TablesItemsControl.ItemsSource = _tablesViewSource.View;
            
            // Set up filtering
            _tablesViewSource.View.Filter = FilterTable;
            
            // Load tables asynchronously (use cache if available)
            Loaded += async (s, e) => await LoadTablesAsync(preselectedTables);
        }

        private async Task LoadTablesAsync(List<string>? preselectedTables)
        {
            try
            {
                List<TableSchema> tables;

                // Use cached data if available
                if (_cachedTables != null && _cachedTables.Count > 0)
                {
                    StatusTextBlock.Text = "Loading tables from cache...";
                    tables = _cachedTables;
                    LoadedTables = _cachedTables; // Already loaded
                }
                else
                {
                    LoadingTextBlock.Visibility = Visibility.Visible;
                    StatusTextBlock.Text = "Connecting to database...";
                    
                    using var connection = _provider.CreateConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    StatusTextBlock.Text = "Loading tables from database...";
                    tables = await _provider.GetTablesAsync(connection);
                    LoadedTables = tables; // Store for caching
                }
                
                _allTables.Clear();
                foreach (var table in tables.OrderBy(t => t.Name))
                {
                    bool isSelected = preselectedTables?.Contains(table.Name) == true || 
                                    preselectedTables?.Contains(table.FullName) == true;
                    
                    _allTables.Add(new TableSelectionItem
                    {
                        TableName = table.Name,
                        FullName = table.FullName,
                        DisplayName = string.IsNullOrEmpty(table.Schema) ? table.Name : $"{table.Schema}.{table.Name}",
                        IsSelected = isSelected
                    });
                }
                
                LoadingTextBlock.Visibility = Visibility.Collapsed;
                UpdateCountDisplay();
                
                string sourceText = _cachedTables != null ? "cached" : "database";
                StatusTextBlock.Text = $"Found {_allTables.Count} tables (from {sourceText})";
            }
            catch (Exception ex)
            {
                LoadingTextBlock.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = $"Error loading tables: {ex.Message}";
                MessageBox.Show($"Failed to load tables: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool FilterTable(object item)
        {
            if (item is TableSelectionItem table && !string.IsNullOrWhiteSpace(FilterTextBox.Text))
            {
                return table.DisplayName.Contains(FilterTextBox.Text, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _tablesViewSource.View.Refresh();
            UpdateCountDisplay();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _tablesViewSource.View.Cast<TableSelectionItem>())
            {
                item.IsSelected = true;
            }
            UpdateCountDisplay();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _tablesViewSource.View.Cast<TableSelectionItem>())
            {
                item.IsSelected = false;
            }
            UpdateCountDisplay();
        }

        private void TableCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCountDisplay();
        }

        private void UpdateCountDisplay()
        {
            var visibleCount = _tablesViewSource.View.Cast<TableSelectionItem>().Count();
            var selectedCount = _allTables.Count(t => t.IsSelected);
            CountTextBlock.Text = $"({selectedCount} of {visibleCount} selected)";
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedTableNames = _allTables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class TableSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string TableName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}