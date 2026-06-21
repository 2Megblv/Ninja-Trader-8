using System;
using System.IO;
using Newtonsoft.Json;
using PropFirmATS.Engine.Risk;

namespace PropFirmATS.Engine.Persistence
{
    public class StateManager
    {
        private string _storageDirectory;

        public StateManager(string customBinPath)
        {
            _storageDirectory = Path.Combine(customBinPath, "PropFirmATS_States");
            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
            }
        }

        public void SaveRiskState(RiskState state)
        {
            try
            {
                string filePath = GetFilePath(state.AccountName);
                string json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                // In a real environment, log this exception
                Console.WriteLine($"Error saving state: {ex.Message}");
            }
        }

        public RiskState LoadRiskState(string accountName)
        {
            try
            {
                string filePath = GetFilePath(accountName);
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonConvert.DeserializeObject<RiskState>(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading state: {ex.Message}");
            }

            return new RiskState { AccountName = accountName }; // Return new state if not found or error
        }

        private string GetFilePath(string accountName)
        {
            // Sanitize account name for file system just in case
            string safeName = string.Join("_", accountName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_storageDirectory, $"{safeName}_state.json");
        }
    }
}
