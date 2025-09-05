using DatabaseMigrationTool.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DatabaseMigrationTool.Views
{
    public partial class ImportTableSelectionWindow : Window
    {
        private ObservableCollection<ImportTableSelectionItem> _allTables;
        private CollectionViewSource _tablesViewSource;

        public List<string> SelectedTableNames { get; private set; } = new List<string>();

        public ImportTableSelectionWindow(List<TableSchema> tables, List<string>? preselectedTables = null)
        {
            InitializeComponent();
            
            _allTables = new ObservableCollection<ImportTableSelectionItem>();
            _tablesViewSource = new CollectionViewSource { Source = _allTables };
            TablesItemsControl.ItemsSource = _tablesViewSource.View;
            
            // Set up filtering
            _tablesViewSource.View.Filter = FilterTable;
            
            // Load tables and preselect if specified
            LoadTables(tables, preselectedTables);
            
            UpdateStatus();
        }

        private void LoadTables(List<TableSchema> tables, List<string>? preselectedTables)
        {
            var preselectedSet = preselectedTables?.Select(t => t.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase) 
                               ?? new HashSet<string>();

            foreach (var table in tables.OrderBy(t => t.FullName))
            {
                var item = new ImportTableSelectionItem
                {
                    Table = table,
                    IsSelected = preselectedSet.Contains(table.Name) || preselectedSet.Contains(table.FullName)
                };
                
                // Subscribe to property change for status updates
                item.PropertyChanged += (s, e) => UpdateStatus();
                
                _allTables.Add(item);
            }
        }

        private bool FilterTable(object item)
        {
            if (item is ImportTableSelectionItem tableItem)
            {
                string searchText = SearchTextBox.Text?.Trim().ToLowerInvariant() ?? "";
                if (string.IsNullOrEmpty(searchText))
                    return true;

                return tableItem.Table.Name.ToLowerInvariant().Contains(searchText) ||
                       tableItem.Table.FullName.ToLowerInvariant().Contains(searchText) ||
                       (tableItem.Table.Schema?.ToLowerInvariant().Contains(searchText) ?? false);
            }
            return false;
        }

        private void UpdateStatus()
        {
            var selectedCount = _allTables.Count(t => t.IsSelected);
            var totalCount = _allTables.Count;
            StatusTextBlock.Text = $"{selectedCount} of {totalCount} tables selected";
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _tablesViewSource.View.Refresh();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var table in _allTables)
            {
                table.IsSelected = true;
            }
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var table in _allTables)
            {
                table.IsSelected = false;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedTableNames = _allTables
                .Where(t => t.IsSelected)
                .Select(t => t.Table.FullName)
                .ToList();
            
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class ImportTableSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public TableSchema Table { get; set; } = new TableSchema();

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string ColumnsSummary
        {
            get
            {
                var columnCount = Table.Columns?.Count ?? 0;
                var primaryKeys = Table.Columns?.Count(c => c.IsPrimaryKey) ?? 0;
                var foreignKeys = Table.ForeignKeys?.Count ?? 0;

                return $"{columnCount} columns" +
                       (primaryKeys > 0 ? $", {primaryKeys} PK" : "") +
                       (foreignKeys > 0 ? $", {foreignKeys} FK" : "");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}