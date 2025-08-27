using DatabaseMigrationTool.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DatabaseMigrationTool.Services
{
    public class OperationStateManager
    {
        private readonly string _stateDirectory;
        private const string STATE_FILE_EXTENSION = ".opstate";
        
        public OperationStateManager()
        {
            _stateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DatabaseMigrationTool",
                "OperationStates"
            );
            Directory.CreateDirectory(_stateDirectory);
        }
        
        public void SaveOperationState(OperationState state)
        {
            try
            {
                var fileName = $"{state.OperationId}{STATE_FILE_EXTENSION}";
                var filePath = Path.Combine(_stateDirectory, fileName);
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var json = JsonSerializer.Serialize(state, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save operation state: {ex.Message}");
            }
        }
        
        public OperationState? LoadOperationState(string operationId)
        {
            try
            {
                var fileName = $"{operationId}{STATE_FILE_EXTENSION}";
                var filePath = Path.Combine(_stateDirectory, fileName);
                
                if (!File.Exists(filePath))
                    return null;
                
                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                return JsonSerializer.Deserialize<OperationState>(json, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load operation state: {ex.Message}");
                return null;
            }
        }
        
        public List<OperationState> GetRecoverableOperations()
        {
            var recoverableStates = new List<OperationState>();
            
            try
            {
                var stateFiles = Directory.GetFiles(_stateDirectory, $"*{STATE_FILE_EXTENSION}");
                
                foreach (var filePath in stateFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };
                        
                        var state = JsonSerializer.Deserialize<OperationState>(json, options);
                        if (state != null && state.CanResume)
                        {
                            recoverableStates.Add(state);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load state file {filePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to enumerate operation states: {ex.Message}");
            }
            
            return recoverableStates.OrderByDescending(s => s.StartTime).ToList();
        }
        
        public void DeleteOperationState(string operationId)
        {
            try
            {
                var fileName = $"{operationId}{STATE_FILE_EXTENSION}";
                var filePath = Path.Combine(_stateDirectory, fileName);
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete operation state: {ex.Message}");
            }
        }
        
        public void CleanupOldStates(TimeSpan maxAge)
        {
            try
            {
                var cutoffDate = DateTime.Now - maxAge;
                var stateFiles = Directory.GetFiles(_stateDirectory, $"*{STATE_FILE_EXTENSION}");
                
                foreach (var filePath in stateFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };
                        
                        var state = JsonSerializer.Deserialize<OperationState>(json, options);
                        if (state != null && 
                            (state.EndTime ?? state.StartTime) < cutoffDate && 
                            (state.Status == "Completed" || state.Status == "Cancelled"))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to process state file {filePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to cleanup old states: {ex.Message}");
            }
        }
        
        public void UpdateOperationProgress(string operationId, Action<OperationState> updateAction)
        {
            var state = LoadOperationState(operationId);
            if (state != null)
            {
                updateAction(state);
                SaveOperationState(state);
            }
        }
        
        public OperationState CreateNewOperation(string operationType, string provider, string connectionString)
        {
            var state = new OperationState
            {
                OperationType = operationType,
                Provider = provider,
                ConnectionString = connectionString,
                Status = "InProgress"
            };
            
            SaveOperationState(state);
            return state;
        }
        
        public void CompleteOperation(string operationId, bool success = true)
        {
            UpdateOperationProgress(operationId, state =>
            {
                state.Status = success ? "Completed" : "Failed";
                state.EndTime = DateTime.Now;
            });
        }
        
        public void PauseOperation(string operationId)
        {
            UpdateOperationProgress(operationId, state =>
            {
                state.Status = "Paused";
            });
        }
        
        public void ResumeOperation(string operationId)
        {
            UpdateOperationProgress(operationId, state =>
            {
                state.Status = "InProgress";
            });
        }
        
        public void CancelOperation(string operationId)
        {
            UpdateOperationProgress(operationId, state =>
            {
                state.Status = "Cancelled";
                state.EndTime = DateTime.Now;
            });
        }
    }
}